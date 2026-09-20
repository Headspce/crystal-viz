using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// CI visual verification for CrystalViz: opens the scene, runs the
/// runtime bootstrap in edit mode (Awake never runs in edit mode), builds the
/// slider UI, and renders the main camera to Screenshots/crystalviz.png.
/// Invoked from GitHub Actions via:
///   -executeMethod CrystalVizScreenshot.Capture
/// Must run WITHOUT -nographics (needs a GL context; the workflow wraps it
/// in xvfb-run with Mesa software rendering, same as the duck-walk repo).
/// </summary>
public static class CrystalVizScreenshot
{
    public static void Capture()
    {
        EditorSceneManager.OpenScene("Assets/CrystalViz/CrystalViz.unity");

        // Optional tap override for staged screenshots:
        //   -executeMethod CrystalVizScreenshot.Capture -cvizTaps 50
        // Sets the saved tap count before the scene builds so the 50-tap
        // mature tree can be verified without touching the playtest default.
        int tapsOverride = -1;
        var cli = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < cli.Length - 1; i++)
        {
            if (cli[i] == "-cvizTaps" && int.TryParse(cli[i + 1], out int t))
                tapsOverride = Mathf.Clamp(t, 0, 50);
        }
        // -cvizReset simulates pressing the reset button: true zero-tap
        // sprout at 0 taps. Seeded via PlayerPrefs BEFORE the scene builds
        // so every component (tree, avatar, counter) initializes from that
        // state, exactly as the real button press persists it.
        bool resetToSprout = false;
        for (int i = 0; i < cli.Length; i++)
        {
            if (cli[i] == "-cvizReset") { resetToSprout = true; break; }
        }
        if (resetToSprout)
        {
            // Same keys TreeGrowthController reads ("CrystalViz_TreeTaps");
            // the sapling-start flag is cleared, matching ResetToSprout().
            PlayerPrefs.SetInt("CrystalViz_TreeTaps", 0);
            PlayerPrefs.SetInt("CrystalViz_TreeTaps_SaplingStart", 0);
            PlayerPrefs.Save();
            Debug.Log("CrystalVizScreenshot: reset-button state (sprout, 0 taps).");
        }
        else if (tapsOverride >= 0)
        {
            // Same key TreeGrowthController reads ("CrystalViz_TreeTaps").
            PlayerPrefs.SetInt("CrystalViz_TreeTaps", tapsOverride);
            PlayerPrefs.Save();
            Debug.Log($"CrystalVizScreenshot: tap override {tapsOverride}.");
        }

        var bootstrap = Object.FindFirstObjectByType<CrystalVizBootstrap>();
        if (bootstrap == null)
        {
            Debug.LogError("CrystalVizScreenshot: no CrystalVizBootstrap in scene.");
            EditorApplication.Exit(1);
            return;
        }
        bootstrap.BuildScene(); // edit-mode equivalent of Awake

        // SunOrbitControl IS attached by the bootstrap (player light slider),
        // but its UI is built in Start(), which never runs in edit mode —
        // so the slider stays out of captures while player builds get it.

        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("CrystalVizScreenshot: no Main Camera.");
            EditorApplication.Exit(1);
            return;
        }

        // ScreenSpaceOverlay canvases don't render into a camera target
        // texture, so point every canvas at the camera just for capture.
        // (There can be more than one: the sun slider canvas plus the
        // tap-to-grow stage avatar canvas.)
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (var canvas in canvases)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            Debug.Log($"CrystalVizScreenshot: canvas '{canvas.name}' -> ScreenSpaceCamera " +
                      $"(sorting {canvas.sortingOrder}, children {canvas.transform.childCount}).");
        }

        // Portrait, close to Tyler's phone aspect.
        const int w = 720;
        const int h = 1600;
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
        var path = Path.Combine("Screenshots", "crystalviz.png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Debug.Log("CrystalVizScreenshot: saved " + path);
    }
}
