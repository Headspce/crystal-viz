using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Command-line build entry point for CI.
/// Invoked from GitHub Actions via:
///   -executeMethod CrystalVizBuild.BuildAndroid
/// Builds ONLY the CrystalViz scene into Builds/Android/CrystalViz.apk.
/// All Android player settings are applied here in code so no ProjectSettings
/// YAML surgery is needed. Signing comes from the ANDROID_KEYSTORE_* env vars
/// (same pattern as the duck-walk repo).
/// </summary>
public static class CrystalVizBuild
{
    /// <summary>
    /// The whole scene is generated at runtime via Shader.Find, and the build
    /// contains only CrystalViz.unity (a near-empty scene), so Unity would strip
    /// URP/Lit and every material would render magenta on device (this is
    /// exactly what v1.0.0 did).
    ///
    /// The first fix attempt pinned URP/Lit into Always Included Shaders, but
    /// that forces Unity to compile EVERY surviving variant of the shader
    /// (~590k for the ForwardLit pass under the PC pipeline asset) and the
    /// build hung for 6+ hours compiling them.
    ///
    /// This instead writes a ShaderVariantCollection containing a generous
    /// superset of the keyword combinations the runtime diorama can actually
    /// hit: opaque + transparent surfaces, main-light shadows (with/without
    /// cascades and screen-space), additional lights, linear/exp fog, DBuffer
    /// decals on/off, across every pass type. Collection entries that match no
    /// real variant cost nothing; the matched set is a few dozen variants, so
    /// the build finishes in minutes and nothing renders magenta.
    /// </summary>
    public static void EnsureVariantCollection()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Debug.LogError("CrystalVizBuild: 'Universal Render Pipeline/Lit' not found in the editor; cannot build variant collection.");
            return;
        }

        // Defensive: remove any Always-Included pin left by the old fix — it
        // explodes compile time and is superseded by the variant collection.
        var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
        if (gs != null)
        {
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            bool removed = false;
            for (int i = arr.arraySize - 1; i >= 0; i--)
            {
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == lit)
                {
                    arr.DeleteArrayElementAtIndex(i);
                    removed = true;
                }
            }
            if (removed)
            {
                so.ApplyModifiedProperties();
                Debug.Log("CrystalVizBuild: removed URP/Lit from Always Included Shaders (superseded by variant collection).");
            }
        }

        string[] keywords =
        {
            "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_MAIN_LIGHT_SHADOWS_SCREEN",
            "_ADDITIONAL_LIGHTS", "_ADDITIONAL_LIGHTS_VERTEX", "_ADDITIONAL_LIGHT_SHADOWS",
            "FOG_LINEAR", "FOG_EXP", "FOG_EXP2",
            "_SURFACE_TYPE_TRANSPARENT", "_ALPHATEST_ON", "_EMISSION",
        };
        string[] dbuffer = { "_DBUFFER_MRT1", "_DBUFFER_MRT2", "_DBUFFER_MRT3" };
        var passTypes = (PassType[])Enum.GetValues(typeof(PassType));

        var svc = ScriptableObject.CreateInstance<ShaderVariantCollection>();
        int added = 0;
        int n = keywords.Length;
        var combo = new List<string>(n + 3);
        for (int mask = 0; mask < (1 << n); mask++)
        {
            combo.Clear();
            for (int b = 0; b < n; b++)
                if ((mask & (1 << b)) != 0) combo.Add(keywords[b]);
            // Two DBuffer flavors per combo: decals off, and decals on.
            for (int db = 0; db < 2; db++)
            {
                int baseCount = combo.Count;
                if (db == 1) combo.AddRange(dbuffer);
                var kws = combo.ToArray();
                foreach (var pt in passTypes)
                {
                    svc.Add(new ShaderVariant(lit, pt, kws));
                    added++;
                }
                if (db == 1) combo.RemoveRange(baseCount, dbuffer.Length);
            }
        }

        const string dir = "Assets/CrystalViz/Resources";
        Directory.CreateDirectory(dir);
        string path = dir + "/CrystalVizVariants.shadervariants";
        if (AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(path) != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(svc, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"CrystalVizBuild: wrote {added} variant entries to {path}; URP/Lit variants pinned surgically.");
    }

    public static void BuildAndroid()
    {
        EnsureVariantCollection();

        PlayerSettings.companyName = "Headspce";
        PlayerSettings.productName = "Crystal Viz";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.headspce.crystalviz");
        PlayerSettings.bundleVersion = "1.0.2";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

        var runNumber = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
        if (int.TryParse(runNumber, out var versionCode) && versionCode > 0)
        {
            PlayerSettings.Android.bundleVersionCode = versionCode;
            Debug.Log($"CrystalVizBuild: bundleVersionCode set to {versionCode}");
        }

        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        var keystorePath = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PATH");
        var keystorePass = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PASSWORD");
        var keyPass = Environment.GetEnvironmentVariable("ANDROID_KEY_PASSWORD");
        if (!string.IsNullOrEmpty(keystorePath) && File.Exists(keystorePath)
            && !string.IsNullOrEmpty(keystorePass) && !string.IsNullOrEmpty(keyPass))
        {
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keystorePass = keystorePass;
            PlayerSettings.Android.keyaliasName = "crystalviz";
            PlayerSettings.Android.keyaliasPass = keyPass;
            Debug.Log("CrystalVizBuild: persistent signing keystore configured.");
        }
        else
        {
            Debug.LogWarning("CrystalVizBuild: no signing keystore provided; falling back to Unity debug key.");
        }

        var scenes = new[] { "Assets/CrystalViz/CrystalViz.unity" };
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "Android");
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, "CrystalViz.apk");

        Debug.Log($"CrystalVizBuild: building {scenes.Length} scene(s) to {outputPath}");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        });
        var result = report.summary.result;
        Debug.Log($"CrystalVizBuild: build finished with result {result}");
        if (result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.LogError("CrystalVizBuild: build FAILED.");
            EditorApplication.Exit(1);
        }
    }
}
