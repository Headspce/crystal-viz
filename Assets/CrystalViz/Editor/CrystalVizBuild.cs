using System;
using System.IO;
using UnityEditor;
using UnityEngine;

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
    /// contains only CrystalViz.unity (an empty scene), so Unity would strip
    /// URP/Lit and every material would render magenta on device (this is
    /// exactly what v1.0.0 did). Pin the shader into Always Included Shaders
    /// via SerializedObject so no shader GUID hardcoding is needed.
    /// </summary>
    public static void EnsureLitShaderIncluded()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Debug.LogError("CrystalVizBuild: 'Universal Render Pipeline/Lit' not found in the editor; cannot pin it.");
            return;
        }
        var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
        if (gs == null)
        {
            Debug.LogError("CrystalVizBuild: GraphicsSettings.asset not found.");
            return;
        }
        var so = new SerializedObject(gs);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        for (int i = 0; i < arr.arraySize; i++)
        {
            if (arr.GetArrayElementAtIndex(i).objectReferenceValue == lit)
            {
                Debug.Log("CrystalVizBuild: URP/Lit already in Always Included Shaders.");
                return;
            }
        }
        arr.arraySize++;
        arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = lit;
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Debug.Log("CrystalVizBuild: pinned URP/Lit into Always Included Shaders.");
    }

    public static void BuildAndroid()
    {
        EnsureLitShaderIncluded();

        PlayerSettings.companyName = "Headspce";
        PlayerSettings.productName = "Crystal Viz";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.headspce.crystalviz");
        PlayerSettings.bundleVersion = "1.0.1";
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
