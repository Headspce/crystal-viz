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
    /// decals on/off, across every pass type the Lit shader implements.
    /// The custom CrystalViz shaders (ScrollingClouds, StylizedGrass,
    /// HorizonHaze) are pinned too: ScrollingClouds has a single variant, while StylizedGrass
    /// declares shadow/additional-light/fog multi_compiles, so it goes through
    /// the same keyword-combo probing — invalid combos throw and are skipped.
    /// Note (Unity 6 API): ShaderVariantCollection now derives from Object, not
    /// ScriptableObject (use `new`, not CreateInstance), ShaderVariant is the
    /// nested struct ShaderVariantCollection.ShaderVariant, and its constructor
    /// throws ArgumentException for combos that don't exist — so invalid
    /// combos are probed and skipped. The matched set is a few dozen variants,
    /// so the build finishes in minutes and nothing renders magenta.
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

        var svc = new ShaderVariantCollection();
        int added = 0, skipped = 0, passTypesHit = 0;
        int n = keywords.Length;
        var combo = new List<string>(n + 3);

        // Probe every keyword combo against a shader; invalid combos throw
        // ArgumentException and are skipped. Shared by URP/Lit and the grass
        // shader below.
        void PinVariants(Shader sh)
        {
            foreach (var pt in passTypes)
            {
                // Probe: skip pass types the shader doesn't implement at all.
                try { var probe = new ShaderVariantCollection.ShaderVariant(sh, pt, new string[0]); }
                catch (ArgumentException) { continue; }
                passTypesHit++;

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
                        try
                        {
                            var v = new ShaderVariantCollection.ShaderVariant(sh, pt, kws);
                            if (svc.Add(v)) added++;
                        }
                        catch (ArgumentException) { skipped++; }
                        if (db == 1) combo.RemoveRange(baseCount, dbuffer.Length);
                    }
                }
            }
        }

        PinVariants(lit);

        // The stylized-grass shader is also created at runtime via
        // Shader.Find; pin its variants (shadow/additional-light/fog
        // multi_compiles) so they survive stripping. A missing shader here
        // is fine — the bootstrap skips the grass field.
        var grassShader = Shader.Find("CrystalViz/StylizedGrass");
        if (grassShader != null)
        {
            PinVariants(grassShader);
        }
        else
        {
            Debug.LogWarning("CrystalVizBuild: 'CrystalViz/StylizedGrass' not found; grass field will be skipped at runtime.");
        }

        // The scrolling-cloud backdrop shader is also created at runtime via
        // Shader.Find; pin its (single) variant so it survives stripping.
        // A missing shader here is fine — the bootstrap skips the backdrop.
        var cloudShader = Shader.Find("CrystalViz/ScrollingClouds");
        if (cloudShader != null)
        {
            foreach (var pt in passTypes)
            {
                try
                {
                    var v = new ShaderVariantCollection.ShaderVariant(cloudShader, pt, new string[0]);
                    if (svc.Add(v)) added++;
                }
                catch (ArgumentException) { skipped++; }
            }
        }
        else
        {
            Debug.LogWarning("CrystalVizBuild: 'CrystalViz/ScrollingClouds' not found; cloud backdrop will be skipped at runtime.");
        }

        // The horizon-haze shader is also created at runtime via
        // Shader.Find; pin its (single) variant so it survives stripping.
        // A missing shader here is fine — the bootstrap skips the haze.
        var hazeShader = Shader.Find("CrystalViz/HorizonHaze");
        if (hazeShader != null)
        {
            foreach (var pt in passTypes)
            {
                try
                {
                    var v = new ShaderVariantCollection.ShaderVariant(hazeShader, pt, new string[0]);
                    if (svc.Add(v)) added++;
                }
                catch (ArgumentException) { skipped++; }
            }
        }
        else
        {
            Debug.LogWarning("CrystalVizBuild: 'CrystalViz/HorizonHaze' not found; horizon haze will be skipped at runtime.");
        }

        const string dir = "Assets/CrystalViz/Resources";
        Directory.CreateDirectory(dir);
        string path = dir + "/CrystalVizVariants.shadervariants";
        if (AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(path) != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(svc, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"CrystalVizBuild: wrote {added} real variants ({skipped} invalid combos skipped, {passTypesHit} pass types) to {path}.");
    }

    public static void BuildAndroid()
    {
        EnsureVariantCollection();

        PlayerSettings.companyName = "Headspce";
        PlayerSettings.productName = "Crystal Viz";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.headspce.crystalviz");
        PlayerSettings.bundleVersion = "1.0.10";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

        var runNumber = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
        if (int.TryParse(runNumber, out var versionCode) && versionCode > 0)
        {
            PlayerSettings.Android.bundleVersionCode = versionCode;
            Debug.Log($"CrystalVizBuild: bundleVersionCode set to {versionCode}");
        }

        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
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
