using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// CI visual check: opens the first overworld level (Assets/Scenes/Overworld.unity),
/// renders its Main Camera to Screenshots/overworld.png.
/// Invoked from GitHub Actions via:
///   -executeMethod OverworldScreenshot.Capture
/// Runs in edit mode (no Play Mode) under xvfb-run with Mesa software rendering.
/// </summary>
public static class OverworldScreenshot
{
    public static void Capture()
    {
        const string scenePath = "Assets/Scenes/Overworld.unity";
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        Debug.Log("OverworldScreenshot: opened scene " + scene.path);

        GameObject camGo = GameObject.Find("Main Camera");
        if (camGo == null)
        {
            Debug.LogError("OverworldScreenshot: 'Main Camera' not found in Overworld scene.");
            EditorApplication.Exit(1);
            return;
        }
        Camera cam = camGo.GetComponent<Camera>();
        if (cam == null)
        {
            Debug.LogError("OverworldScreenshot: 'Main Camera' has no Camera component.");
            EditorApplication.Exit(1);
            return;
        }

        const int w = 1920;
        const int h = 1080;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);

        Directory.CreateDirectory("Screenshots");
        string path = Path.Combine("Screenshots", "overworld.png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Debug.Log("OverworldScreenshot: saved " + path);
    }
}
