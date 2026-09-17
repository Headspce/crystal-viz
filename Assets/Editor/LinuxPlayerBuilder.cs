using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// CI builder for the headless Linux player used by the perpetual screenshot rig.
/// Invoked from GitHub Actions via:
///   -executeMethod LinuxPlayerBuilder.Build
///
/// Forces Mono2x scripting backend to avoid IL2CPP toolchain requirements on the
/// runner. Builds all enabled scenes in EditorBuildSettings to
/// Builds/Linux/DuckDuckMoose.x86_64 (StandaloneLinux64).
/// </summary>
public static class LinuxPlayerBuilder
{
    public static void Build()
    {
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);

        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();
        Debug.Log("LinuxPlayerBuilder: building scenes: " + string.Join(", ", scenes));
        if (scenes.Length == 0)
            throw new Exception("LinuxPlayerBuilder: no enabled scenes in EditorBuildSettings.");

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "Builds/Linux/DuckDuckMoose.x86_64",
            target = BuildTarget.StandaloneLinux64,
            options = BuildOptions.None,
        });

        Debug.Log("LinuxPlayerBuilder: result=" + report.summary.result +
                  ", totalErrors=" + report.summary.totalErrors);
        if (report.summary.result == BuildResult.Failed)
            throw new Exception("LinuxPlayerBuilder: build failed (" +
                                report.summary.totalErrors + " errors). See log.");
    }
}
