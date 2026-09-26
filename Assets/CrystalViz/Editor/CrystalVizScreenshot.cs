using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// CI visual verification for CrystalViz: opens the scene, runs the
/// runtime bootstrap in edit mode (Awake never runs in edit mode), and
/// renders the main camera to Screenshots/crystalviz.png.
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
        // Simulate a phone camera cutout: the CI game view is full-bleed,
        // so Screen.safeArea never insets there. A 90px top cutout on the
        // 720x1600 capture is representative of a punch-hole camera +
        // status bar (Tyler's Motorola). This visibly proves the stage
        // indicator clears the cutout instead of hiding under it.
        StageIndicatorUI.TestSafeAreaOverride = new Rect(0f, 0f, 720f, 1600f - 90f);
        bootstrap.BuildScene(); // edit-mode equivalent of Awake

        // SunOrbitControl IS attached by the bootstrap (time-of-day
        // engine), and BuildScene() resolves it explicitly in edit mode
        // (Start never runs in edit mode).

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
        // v1.0.55: the lighting slider is GONE (player request — removed
        // from the right edge entirely). The corner menu's day/night button
        // is the only time control now; SetTimeOfDay drives the same engine.
        // The menu snaps open so the sapling + day/night buttons show.
        // The day/night capture pins to night via SetTimeOfDay(0.8f).
        var orbit = Object.FindFirstObjectByType<SunOrbitControl>();
        var menu = Object.FindFirstObjectByType<TreeResetButton>();
        if (menu != null) menu.SnapExpandedForScreenshot();
        CaptureFrame(cam, w, h, Path.Combine("Screenshots", "crystalviz.png"));
        if (orbit != null)
        {
            orbit.SetTimeOfDay(0.8f); // ~9:36 PM -> hard night switch
            Debug.Log("CrystalVizScreenshot: night-state frame at slider 0.8 (~9:36 PM).");
        }
        // v1.0.55: the day/night button's glyph is state-aware — re-sync it
        // now that the time is at night (edit mode runs no Update).
        var menu2 = Object.FindFirstObjectByType<TreeResetButton>();
        if (menu2 != null) menu2.SyncDayNightGlyphForScreenshot();
        // v1.0.51: the BeeController's Start never runs in edit mode — build
        // the firefly preview squad so the night capture shows the
        // bee->firefly transformation (glowing orbs + ground light pools).
        var beeCtl = Object.FindFirstObjectByType<BeeController>();
        if (beeCtl != null) beeCtl.BuildPreview();
        CaptureFrame(cam, w, h, Path.Combine("Screenshots", "crystalviz-night.png"));
        // v1.0.55: inventory prototype proof — snap it open and capture the
        // frosted overlay + six empty slots.
        var menu3 = Object.FindFirstObjectByType<TreeResetButton>();
        if (menu3 != null) menu3.SnapInventoryOpenForScreenshot();
        CaptureFrame(cam, w, h, Path.Combine("Screenshots", "crystalviz-inventory.png"));
        Debug.Log("CrystalVizScreenshot: saved Screenshots/crystalviz.png + crystalviz-night.png + crystalviz-inventory.png");
    }

    /// <summary>
    /// Renders the camera to a PNG at the given path.
    /// </summary>
    static void CaptureFrame(Camera cam, int w, int h, string path)
    {
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

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
