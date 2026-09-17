using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Perpetual screenshot rig for the DuckDuckMoose Overworld board.
/// Installed automatically in player builds via RuntimeInitializeOnLoadMethod
/// (no scene edits required).
///
/// Command-line usage (player):
///   -rigshot <name>        Capture Main Camera to RigShots/<name>.png (1920x1080)
///   -rigframes <n>         Frames to wait after scene load before capture (default 45)
///   -rigcam px py pz rx ry rz
///                          Set Main Camera position + euler rotation before capture
///   -rigarea <name>        Activate one overworld diorama by name:
///                          cozycove | dismaldesert | gentlewindgrasslands |
///                          meteormountains | crescentclouds
///                          (aliases: cove, desert, grasslands, mountains, clouds)
///                          When no -rigcam is given, the camera is re-framed onto
///                          the chosen area using the default view's offset.
///   -rigdolly <n>          Nudge camera along its view direction after framing
///                          (positive = toward what the camera aims at); logs
///                          the resulting world position.
///   -riglight <rx> <ry> <r> <g> <b> <intensity>
///                          First directional light: euler rotation, color, intensity.
///   -rigfog <density> <r> <g> <b>
///                          Exponential fog density + color.
///   -rignofog              Disable fog entirely.
///   -rigambient <r> <g> <b>
///                          Flat ambient color (switches ambient mode to Flat).
///   -rigbg <r> <g> <b>     Camera clear color (SolidColor), replacing the skybox.
///   -rignext <n>           Legacy/fallback: advance n dioramas forward (cycles order)
///   -rigquit               Application.Quit() after the capture (one-shot mode)
///
/// Idle (perpetual) mode: run with no -rigshot. The game just runs at
/// Application.targetFrameRate = 15 to stay cheap under software rendering;
/// use shot.sh to spawn one-shot captures against the idle instance.
/// Everything here is defensive: never throws, null-checks everywhere.
/// </summary>
public static class ScreenshotRig
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        try
        {
            var go = new GameObject("ScreenshotRig");
            go.AddComponent<ScreenshotRigBehaviour>();
            UnityEngine.Object.DontDestroyOnLoad(go);
            Debug.Log("ScreenshotRig: installed.");
        }
        catch (Exception e)
        {
            Debug.LogWarning("ScreenshotRig: failed to install: " + e.Message);
        }
    }

    private sealed class ScreenshotRigBehaviour : MonoBehaviour
    {
        void Start()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string shot = GetArgValue(args, "-rigshot");
                if (string.IsNullOrEmpty(shot))
                {
                    // Idle / perpetual mode: keep the game alive cheaply.
                    Application.targetFrameRate = 15;
                    Debug.Log("ScreenshotRig idle");
                    return;
                }

                int frames = 45;
                string framesArg = GetArgValue(args, "-rigframes");
                if (!string.IsNullOrEmpty(framesArg) && int.TryParse(framesArg, out int f) && f >= 0)
                    frames = f;

                StartCoroutine(CaptureRoutine(args, shot, frames));
            }
            catch (Exception e)
            {
                Debug.LogWarning("ScreenshotRig: Start failed: " + e.Message);
            }
        }

        private IEnumerator CaptureRoutine(string[] args, string shot, int frames)
        {
            // Let the scene settle: SlideArrows.Start() and friends run first.
            for (int i = 0; i < frames; i++)
                yield return null;

            Camera cam = null;
            try
            {
                cam = Camera.main;
                if (cam == null)
                {
                    Debug.LogWarning("ScreenshotRig: no Main Camera found, aborting capture.");
                    yield break;
                }

                string area = GetArgValue(args, "-rigarea");
                string next = GetArgValue(args, "-rignext");
                bool didArea = false;
                if (!string.IsNullOrEmpty(area))
                {
                    didArea = JumpToArea(area, cam);
                }
                else if (!string.IsNullOrEmpty(next) && int.TryParse(next, out int n) && n > 0)
                {
                    didArea = AdvanceAreas(n, cam);
                }

                string camArg = GetArgValue(args, "-rigcam");
                if (!string.IsNullOrEmpty(camArg))
                {
                    ApplyRigCam(cam, camArg);
                }
                else if (didArea)
                {
                    // Auto-frame onto the chosen area (JumpToArea handles this itself).
                }

                // Dolly: nudge the camera along its current view direction.
                // Positive moves toward whatever the camera is aimed at.
                // Applied after area framing / -rigcam so it composes with both.
                string dollyArg = GetArgValue(args, "-rigdolly");
                if (!string.IsNullOrEmpty(dollyArg))
                {
                    float dolly;
                    if (float.TryParse(dollyArg, out dolly))
                    {
                        cam.transform.position += cam.transform.forward * dolly;
                        Debug.Log("ScreenshotRig: dolly " + dolly);
                    }
                    else
                    {
                        Debug.LogWarning("ScreenshotRig: -rigdolly needs a number, got '" + dollyArg + "'.");
                    }
                }

                Debug.Log("ScreenshotRig: camera world pos " + cam.transform.position);

                ApplyLighting(args, cam);
            }
            catch (Exception e)
            {
                Debug.LogWarning("ScreenshotRig: capture failed: " + e.Message);
            }

            // One more frame so the camera move lands before the render.
            // (Kept outside the try: C# forbids yield inside try/catch.)
            yield return null;

            try
            {
                SavePng(cam, shot);
            }
            catch (Exception e)
            {
                Debug.LogWarning("ScreenshotRig: save failed: " + e.Message);
            }

            if (HasFlag(Environment.GetCommandLineArgs(), "-rigquit"))
            {
                Debug.Log("ScreenshotRig: quitting.");
                Application.Quit();
            }
        }

        // --- area navigation ------------------------------------------------

        // Order matches SlideArrows.levelOptions in the Overworld scene.
        private static readonly string[] AreaOrder =
        {
            "cozyCove",
            "dismalDesert",
            "MeteorMountains",
            "CrescentClouds",
            "GentlewindGrasslands",
        };

        private static readonly Dictionary<string, string> AreaAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "cozycove", "cozyCove" },
                { "cove", "cozyCove" },
                { "dismaldesert", "dismalDesert" },
                { "desert", "dismalDesert" },
                { "gentlewindgrasslands", "GentlewindGrasslands" },
                { "grasslands", "GentlewindGrasslands" },
                { "grass", "GentlewindGrasslands" },
                { "meteormountains", "MeteorMountains" },
                { "mountains", "MeteorMountains" },
                { "meteor", "MeteorMountains" },
                { "crescentclouds", "CrescentClouds" },
                { "clouds", "CrescentClouds" },
                { "crescent", "CrescentClouds" },
            };

        private static int areaCursor = 0; // tracks current diorama (0 = cozyCove default)

        private bool JumpToArea(string area, Camera cam)
        {
            try
            {
                string canon;
                if (!AreaAliases.TryGetValue(area.Trim(), out canon))
                {
                    // Also accept the exact scene object names case-insensitively.
                    canon = area.Trim();
                }

                GameObject levelsRoot = GameObject.Find("Levels");
                if (levelsRoot == null)
                {
                    Debug.LogWarning("ScreenshotRig: 'Levels' container not found.");
                    return TryLevelManagerJump(area);
                }

                Transform target = null;
                foreach (Transform child in levelsRoot.transform)
                {
                    if (string.Equals(child.name, canon, StringComparison.OrdinalIgnoreCase))
                        target = child;
                }
                if (target == null)
                {
                    Debug.LogWarning("ScreenshotRig: area '" + area + "' not found under Levels. " +
                                     "Known: " + string.Join(", ", AreaOrder));
                    return false;
                }

                // Capture the default camera framing relative to the current area center,
                // then swap dioramas and re-frame onto the new one.
                Vector3 prevCenter = DioramaCenter(levelsRoot.transform.GetChild(areaCursor).gameObject);
                Vector3 newCenter = DioramaCenter(target.gameObject);
                Vector3 offset = cam.transform.position - prevCenter;

                foreach (Transform child in levelsRoot.transform)
                    child.gameObject.SetActive(child == target);

                cam.transform.position = newCenter + offset;

                for (int i = 0; i < AreaOrder.Length; i++)
                {
                    if (string.Equals(AreaOrder[i], target.name, StringComparison.OrdinalIgnoreCase))
                        areaCursor = i;
                }

                Debug.Log("ScreenshotRig: area -> " + target.name);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("ScreenshotRig: JumpToArea failed: " + e.Message);
                return false;
            }
        }

        private bool AdvanceAreas(int n, Camera cam)
        {
            try
            {
                GameObject levelsRoot = GameObject.Find("Levels");
                if (levelsRoot == null)
                {
                    Debug.LogWarning("ScreenshotRig: 'Levels' container not found for -rignext.");
                    return false;
                }
                int idx = (areaCursor + n) % AreaOrder.Length;
                return JumpToArea(AreaOrder[idx], cam);
            }
            catch (Exception e)
            {
                Debug.LogWarning("ScreenshotRig: AdvanceAreas failed: " + e.Message);
                return false;
            }
        }

        // LevelManager exists in the project but is not wired into the Overworld
        // scene; this fallback keeps -rigspace usable if it ever is.
        private bool TryLevelManagerJump(string area)
        {
            try
            {
                var lm = UnityEngine.Object.FindFirstObjectByType<LevelManager>();
                if (lm == null)
                    return false;
                Type t = lm.GetType();
                FieldInfo lockField = t.GetField("isLocked", BindingFlags.NonPublic | BindingFlags.Instance);
                if (lockField != null) lockField.SetValue(lm, false);
                MethodInfo show = t.GetMethod("ShowCurrentBoardSpace", BindingFlags.NonPublic | BindingFlags.Instance);
                if (show != null) show.Invoke(lm, null);
                Debug.Log("ScreenshotRig: LevelManager jump (fallback) for '" + area + "'.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("ScreenshotRig: LevelManager fallback failed: " + e.Message);
                return false;
            }
        }

        private static Vector3 DioramaCenter(GameObject root)
        {
            try
            {
                var rends = root.GetComponentsInChildren<Renderer>(true);
                if (rends != null && rends.Length > 0)
                {
                    Bounds b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++)
                        b.Encapsulate(rends[i].bounds);
                    return b.center;
                }
            }
            catch { /* fall through to transform position */ }
            return root.transform.position;
        }

        // --- lighting -------------------------------------------------------

        // Lighting overrides for art-directed captures. Applied after camera
        // framing so each capture can carry its own lighting recipe.
        // Everything here is defensive: bad input just logs a warning.
        private void ApplyLighting(string[] args, Camera cam)
        {
            try
            {
                string lightArg = GetArgValue(args, "-riglight");
                if (!string.IsNullOrEmpty(lightArg))
                {
                    float[] v = ParseFloats(lightArg, 6);
                    if (v != null)
                    {
                        Light dir = FindDirectionalLight();
                        if (dir != null)
                        {
                            dir.transform.rotation = Quaternion.Euler(v[0], v[1], 0f);
                            dir.color = new Color(v[2], v[3], v[4]);
                            dir.intensity = v[5];
                            Debug.Log("ScreenshotRig: light rot(" + v[0] + "," + v[1] + ") " +
                                      "color(" + v[2] + "," + v[3] + "," + v[4] + ") int " + v[5]);
                        }
                        else
                        {
                            Debug.LogWarning("ScreenshotRig: -riglight found no directional light.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("ScreenshotRig: -riglight needs 6 numbers (rx ry r g b intensity).");
                    }
                }

                string fogArg = GetArgValue(args, "-rigfog");
                if (!string.IsNullOrEmpty(fogArg))
                {
                    float[] v = ParseFloats(fogArg, 4);
                    if (v != null)
                    {
                        RenderSettings.fog = true;
                        RenderSettings.fogDensity = v[0];
                        RenderSettings.fogColor = new Color(v[1], v[2], v[3]);
                        Debug.Log("ScreenshotRig: fog density " + v[0] +
                                  " color(" + v[1] + "," + v[2] + "," + v[3] + ")");
                    }
                    else
                    {
                        Debug.LogWarning("ScreenshotRig: -rigfog needs 4 numbers (density r g b).");
                    }
                }
                if (HasFlag(args, "-rignofog"))
                {
                    RenderSettings.fog = false;
                    Debug.Log("ScreenshotRig: fog off");
                }

                string ambArg = GetArgValue(args, "-rigambient");
                if (!string.IsNullOrEmpty(ambArg))
                {
                    float[] v = ParseFloats(ambArg, 3);
                    if (v != null)
                    {
                        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                        RenderSettings.ambientLight = new Color(v[0], v[1], v[2]);
                        Debug.Log("ScreenshotRig: ambient flat (" + v[0] + "," + v[1] + "," + v[2] + ")");
                    }
                    else
                    {
                        Debug.LogWarning("ScreenshotRig: -rigambient needs 3 numbers (r g b).");
                    }
                }

                string bgArg = GetArgValue(args, "-rigbg");
                if (!string.IsNullOrEmpty(bgArg) && cam != null)
                {
                    float[] v = ParseFloats(bgArg, 3);
                    if (v != null)
                    {
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.backgroundColor = new Color(v[0], v[1], v[2]);
                        Debug.Log("ScreenshotRig: bg (" + v[0] + "," + v[1] + "," + v[2] + ")");
                    }
                    else
                    {
                        Debug.LogWarning("ScreenshotRig: -rigbg needs 3 numbers (r g b).");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("ScreenshotRig: ApplyLighting failed: " + e.Message);
            }
        }

        private static float[] ParseFloats(string s, int want)
        {
            try
            {
                string[] parts = s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < want) return null;
                float[] v = new float[want];
                for (int i = 0; i < want; i++)
                    if (!float.TryParse(parts[i], out v[i])) return null;
                return v;
            }
            catch { return null; }
        }

        private static Light FindDirectionalLight()
        {
            try
            {
                var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                foreach (var l in lights)
                    if (l != null && l.type == LightType.Directional && l.gameObject.activeInHierarchy)
                        return l;
            }
            catch { /* fall through */ }
            return null;
        }

        // --- camera / capture -----------------------------------------------

        private void ApplyRigCam(Camera cam, string camArg)
        {
            try
            {
                string[] parts = camArg.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 6)
                {
                    Debug.LogWarning("ScreenshotRig: -rigcam needs 6 numbers (px py pz rx ry rz), got " + parts.Length);
                    return;
                }
                float[] v = new float[6];
                for (int i = 0; i < 6; i++)
                {
                    if (!float.TryParse(parts[i], out v[i]))
                    {
                        Debug.LogWarning("ScreenshotRig: -rigcam number parse failed at '" + parts[i] + "'.");
                        return;
                    }
                }
                cam.transform.position = new Vector3(v[0], v[1], v[2]);
                cam.transform.rotation = Quaternion.Euler(v[3], v[4], v[5]);
                Debug.Log("ScreenshotRig: camera set to " + camArg);
            }
            catch (Exception e)
            {
                Debug.LogWarning("ScreenshotRig: ApplyRigCam failed: " + e.Message);
            }
        }

        private void SavePng(Camera cam, string name)
        {
            const int w = 1920;
            const int h = 1080;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                cam.targetTexture = null;
                RenderTexture.active = null;

                string dir = Path.Combine(Directory.GetCurrentDirectory(), "RigShots");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, name + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log("ScreenshotRig: saved " + Path.GetFullPath(path));
            }
            finally
            {
                if (cam != null) cam.targetTexture = null;
                RenderTexture.active = null;
                if (rt != null) UnityEngine.Object.Destroy(rt);
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        // --- arg helpers ----------------------------------------------------

        private static string GetArgValue(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (string a in args)
            {
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
