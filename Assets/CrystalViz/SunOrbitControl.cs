using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sun-orbit slider: a vertical slider pinned to the right edge of the screen.
/// Sliding UP rotates the sun clockwise around the tree; sliding DOWN
/// rotates it counter-clockwise. The whole UI is built in code (no prefabs) so
/// the scene file stays tiny and everything is version-controlled as C#.
/// </summary>
public class SunOrbitControl : MonoBehaviour
{
    [HideInInspector] public CrystalVizBootstrap bootstrap;

    Slider slider;
    Text angleLabel;
    Text timeLabel; // v1.0.45: clock readout above the slider (12:00 PM at top -> 12:00 AM at bottom)
    RectTransform knobRT;
    float currentAzimuth = 54f;
    float targetAzimuth = 54f;

    void Start()
    {
        BuildForScreenshot();
        slider.onValueChanged.AddListener(v =>
        {
            targetAzimuth = v * 360f;
            PositionKnob();
            UpdateLabel();
            UpdateTimeLabel(v);
        });
        slider.value = targetAzimuth / 360f; // fires listener, sets initial sun pos
        PositionKnob();
        UpdateLabel();
        UpdateTimeLabel(slider.value);
    }

    /// <summary>
    /// Builds the slider UI and sets its initial value. Called from Start at
    /// runtime; the CI screenshot tool calls it directly in edit mode (Start
    /// never runs in edit mode).
    /// </summary>
    public void BuildForScreenshot()
    {
        if (slider != null) return; // already built
        if (bootstrap == null) bootstrap = FindObjectOfType<CrystalVizBootstrap>();
        BuildUI();
        slider.value = targetAzimuth / 360f;
        PositionKnob();
        UpdateLabel();
        UpdateTimeLabel(slider.value);
    }

    /// <summary>
    /// Centers the glowing knob on the fill line. The Slider's own
    /// handleRect driving stretches the knob across the track, so we leave
    /// slider.handleRect null and place the knob with fractional anchors.
    /// </summary>
    void PositionKnob()
    {
        if (slider == null || knobRT == null) return;
        float v = slider.normalizedValue;
        knobRT.anchorMin = new Vector2(0.5f, v);
        knobRT.anchorMax = new Vector2(0.5f, v);
        knobRT.anchoredPosition = Vector2.zero;
    }

    void Update()
    {
        if (bootstrap == null || bootstrap.sun == null) return;
        // Smooth-damped follow so the sun glides instead of snapping.
        currentAzimuth = Mathf.LerpAngle(currentAzimuth, targetAzimuth,
            1f - Mathf.Exp(-8f * Time.deltaTime));
        bootstrap.PlaceSun(currentAzimuth);
        PositionKnob();
    }

    void UpdateLabel()
    {
        if (angleLabel != null)
            angleLabel.text = $"{Mathf.RoundToInt(targetAzimuth % 360f)}°";
    }

    /// <summary>
    /// v1.0.45: clock readout above the slider (player request). The slider
    /// value t in [0,1] maps linearly to h = 12*t, shown 12-hour style and
    /// rounded to the hour: top reads "12:00 PM", bottom reads "12:00 AM",
    /// "11:00 AM", "10:00 AM", ... on the way down. Updates live while
    /// dragging (called from the value-changed listener).
    /// </summary>
    void UpdateTimeLabel(float v)
    {
        if (timeLabel == null) return;
        int hr = Mathf.Clamp(Mathf.RoundToInt(v * 12f), 0, 12);
        timeLabel.text = hr == 12 ? "12:00 PM"
            : hr == 0 ? "12:00 AM"
            : $"{hr}:00 AM";
    }

    // ------------------------------------------------------------------ UI build

    // ------------------------------------------------------------------ UI build

    public void BuildUI()
    {
        // EventSystem is required for touch/click on UI.
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
            es.name = "EventSystem";
        }

        var canvasGo = new GameObject("SunSliderCanvas", typeof(Canvas));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        // Transparent glass skin: procedural textures (dark glass track,
        // translucent fill, transparent glass knob) generated once — no image assets.
        // Vintage Meadow palette: sky blue (#5CA8FF) track, golden sunlight
        // (#D3D925) fill glow.
        var trackSprite = MakeBarSprite(24, 64, 11,
            new Color(0.361f, 0.659f, 1.00f, 0.78f), new Color(0.17f, 0.32f, 0.55f, 0.78f),
            0, Color.clear);
        var fillSprite = MakeBarSprite(24, 64, 11,
            new Color(0.827f, 0.851f, 0.145f, 0.28f), new Color(0.867f, 0.490f, 0.153f, 0.28f),
            8, new Color(0.827f, 0.851f, 0.145f, 0.35f));
        // (knob texture is created lazily in the handle section below)

        // Slider root: slim vertical strip hugging the RIGHT edge.
        // v1.0.8: the whole bar is 75% smaller — rendered at quarter scale
        // about its right-center pivot so it stays glued to the edge.
        // v1.0.26: 25% wider (48px -> 60px) for easier touch targeting,
        // right edge stays glued at -28px.
        var root = new GameObject("SunSlider", typeof(RectTransform), typeof(Slider));
        root.transform.SetParent(canvasGo.transform, false);
        var rrt = root.GetComponent<RectTransform>();
        rrt.anchorMin = new Vector2(1f, 0f);
        rrt.anchorMax = new Vector2(1f, 1f);
        // Shifted ~2 screen px inward from the right edge (player request
        // 2026-09-21): a fingertip on the strip no longer collides with the
        // screen border before the knob reaches the end of its travel.
        rrt.offsetMin = new Vector2(-88f, 150f);
        rrt.offsetMax = new Vector2(-28f, -150f);
        // v1.0.33: back to the ORIGINAL size (player request) — 0.25.
        // v1.0.32 briefly made it 3x larger; Tyler preferred the original.
        rrt.pivot = new Vector2(1f, 0.5f);
        rrt.localScale = new Vector3(0.25f, 0.25f, 1f);

        slider = root.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.direction = Slider.Direction.BottomToTop;

        // Track: dark glass rounded bar.
        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(root.transform, false);
        var bgrt = bg.GetComponent<RectTransform>();
        bgrt.anchorMin = Vector2.zero; bgrt.anchorMax = Vector2.one;
        bgrt.offsetMin = new Vector2(12f, 0f); bgrt.offsetMax = new Vector2(-12f, 0f);
        var bgImg = bg.GetComponent<Image>();
        bgImg.sprite = trackSprite;
        bgImg.type = Image.Type.Sliced;

        // Fill area (wider than the track so the fill's glow bleeds over it).
        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(root.transform, false);
        var fart = fillArea.GetComponent<RectTransform>();
        fart.anchorMin = Vector2.zero; fart.anchorMax = Vector2.one;
        fart.offsetMin = new Vector2(3f, 0f); fart.offsetMax = new Vector2(-3f, 0f);
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        var fillImg = fill.GetComponent<Image>();
        fillImg.sprite = fillSprite;
        fillImg.type = Image.Type.Sliced;
        slider.fillRect = fill.GetComponent<RectTransform>();

        // Handle area + glowing knob. The knob is an Image fed by
        // Sprite.Create(knobTex) with default (Tight) mesh, positioned with
        // fractional anchors in PositionKnob(). (RawImage + this texture
        // silently fails to draw; Image+sprite renders fine.)
        var handleArea = new GameObject("Handle Area", typeof(RectTransform));
        handleArea.transform.SetParent(root.transform, false);
        var hart = handleArea.GetComponent<RectTransform>();
        hart.anchorMin = Vector2.zero; hart.anchorMax = Vector2.one;
        hart.offsetMin = Vector2.zero; hart.offsetMax = Vector2.zero;
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        var hrt = handle.GetComponent<RectTransform>();
        hrt.pivot = new Vector2(0.5f, 0.5f);
        // v1.0.27: the slider thumb IS the sun PNG icon (player request) —
        // slightly larger than the old glass knob so the sun reads at the
        // 0.25x slider scale. Falls back to the glass knob if missing.
        hrt.sizeDelta = new Vector2(64f, 64f);
        knobRT = hrt;
        var sunThumbTex = Resources.Load<Texture2D>("sun-icon");
        Sprite thumbSprite;
        if (sunThumbTex != null)
        {
            thumbSprite = Sprite.Create(sunThumbTex,
                new Rect(0, 0, sunThumbTex.width, sunThumbTex.height),
                new Vector2(0.5f, 0.5f), 100f);
        }
        else
        {
            var knobTex = MakeKnobTexture(96);
            thumbSprite = Sprite.Create(knobTex,
                new Rect(0, 0, knobTex.width, knobTex.height),
                new Vector2(0.5f, 0.5f));
        }
        var knobImg = handle.GetComponent<Image>();
        knobImg.sprite = thumbSprite;
        knobImg.preserveAspect = true;
        knobImg.type = Image.Type.Simple;

        // Angle readout under the mini slider: centered beneath it (the bar's
        // rendered center sits 26px left of the edge), scaled to match.
        var labelGo = new GameObject("AngleLabel", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(canvasGo.transform, false);
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(1f, 0.355f); lrt.anchorMax = new Vector2(1f, 0.355f);
        // v1.0.33: back to original size/position with the slider (0.25).
        lrt.anchoredPosition = new Vector2(-26f, -8f);
        lrt.sizeDelta = new Vector2(140f, 44f);
        lrt.localScale = new Vector3(0.25f, 0.25f, 1f);
        var txt = labelGo.GetComponent<Text>();
        txt.font = GetDefaultFont(); // may be null; Text renders nothing without one
        txt.fontSize = 26;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = new Color(0.93f, 0.97f, 1f, 0.92f);
        angleLabel = txt;

        // Sun icon: child of the slider root so it rides with the slider and
        // sits stuck to the top of the track (v1.0.29: was a free-floating
        // canvas child at the top-right corner). Anchored to the slider's
        // top-center; the slider root's 0.25 scale applies, so localScale
        // stays 1 and the 126px size renders at the same 31.5px as before.
        var sunTex = Resources.Load<Texture2D>("sun-icon");
        if (sunTex != null)
        {
            var sunGo = new GameObject("SunIcon", typeof(RectTransform), typeof(Image));
            sunGo.transform.SetParent(root.transform, false);
            var srt = sunGo.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.5f, 1f);
            srt.anchorMax = new Vector2(0.5f, 1f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            // Center 75px above the slider's top edge: icon half-height (63)
            // + 12px gap, so it sits snug against the track.
            srt.anchoredPosition = new Vector2(0f, 75f);
            srt.sizeDelta = new Vector2(126f, 126f); // v1.0.28: 75% bigger (was 72)
            srt.localScale = new Vector3(1f, 1f, 1f);
            var sunImg = sunGo.GetComponent<Image>();
            sunImg.sprite = Sprite.Create(sunTex,
                new Rect(0, 0, sunTex.width, sunTex.height),
                new Vector2(0.5f, 0.5f), 100f);
            sunImg.preserveAspect = true;
        }

        // v1.0.45: clock readout floating just above the sun icon, so the
        // time sits "directly above the slider" (player request). A canvas
        // child at full scale (NOT under the slider's 0.25x root) so the
        // time stays legible: white text with the HUD's navy outline, same
        // treatment as the tap counter. Positioned over the slider's screen
        // column: the slider root spans x -88..-28 from the right edge
        // (center -58), and the sun icon's top lands ~34 canvas px above the
        // root's top edge (1770), so the label centers at y ~1840. Centered
        // at x=-80 (inside the track column) so the text never clips the edge.
        var timeGO = new GameObject("TimeLabel", typeof(RectTransform), typeof(Text));
        timeGO.transform.SetParent(canvasGo.transform, false);
        var trt = timeGO.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(1f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(-80f, -80f);
        trt.sizeDelta = new Vector2(200f, 48f);
        var ttxt = timeGO.GetComponent<Text>();
        ttxt.font = GetDefaultFont();
        ttxt.fontSize = 30;
        ttxt.alignment = TextAnchor.MiddleCenter;
        ttxt.color = new Color(0.93f, 0.97f, 1f, 0.95f);
        var tout = timeGO.AddComponent<Outline>();
        tout.effectColor = new Color(0.04f, 0.08f, 0.18f, 0.95f);
        tout.effectDistance = new Vector2(2.5f, -2.5f);
        timeLabel = ttxt;
    }

    /// <summary>
    /// Rounded vertical bar texture with vertical gradient and optional baked
    /// outer glow. 9-sliced (borders cover the rounded ends + glow) so it
    /// stretches cleanly to any height.
    /// </summary>
    static Sprite MakeBarSprite(int w, int h, int radius, Color top, Color bottom,
        int glowPad, Color glowColor)
    {
        int W = w + glowPad * 2, H = h + glowPad * 2;
        var tex = new Texture2D(W, H, TextureFormat.ARGB32, false);
        tex.filterMode = FilterMode.Bilinear;
        float cx = W / 2f, cy = H / 2f;
        float bx = w / 2f, by = h / 2f;
        float r = radius;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float px = x + 0.5f - cx;
                float py = y + 0.5f - cy;
                float qx = Mathf.Abs(px) - bx + r;
                float qy = Mathf.Abs(py) - by + r;
                float ax = Mathf.Max(qx, 0f), ay = Mathf.Max(qy, 0f);
                float d = Mathf.Min(Mathf.Max(qx, qy), 0f) + Mathf.Sqrt(ax * ax + ay * ay) - r;
                float cover = 1f - Mathf.SmoothStep(0f, 1.5f, d);
                float t = Mathf.Clamp01((py + by) / (2f * by));
                Color c = Color.Lerp(bottom, top, t);
                float glowA = 0f;
                if (d > 0f && glowPad > 0)
                    glowA = Mathf.Pow(Mathf.Clamp01(1f - d / glowPad), 2f);
                float inv = 1f - cover;
                float fr = c.r * cover + glowColor.r * glowA * inv;
                float fg = c.g * cover + glowColor.g * glowA * inv;
                float fb = c.b * cover + glowColor.b * glowA * inv;
                float fa = Mathf.Clamp01(cover * c.a + glowColor.a * glowA * inv);
                tex.SetPixel(x, y, new Color(
                    Mathf.Clamp01(fr), Mathf.Clamp01(fg), Mathf.Clamp01(fb), fa));
            }
        }
        tex.Apply();
        float b = glowPad + radius;
        return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f),
            100f, 0u, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
    }

    /// <summary>
    /// Transparent glass knob texture: faint disc, soft rim, top sheen — the
    /// scene shows through it. Rendered via Image + Sprite.Create (Tight mesh).
    /// </summary>
    static Texture2D MakeKnobTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.filterMode = FilterMode.Bilinear;
        float c = size / 2f;
        float cr = size * 0.23f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f - c;
                float py = y + 0.5f - c;
                float d = Mathf.Sqrt(px * px + py * py);
                float disc = 1f - Mathf.SmoothStep(cr - 1.5f, cr + 1.5f, d);
                float rim = (1f - Mathf.SmoothStep(0f, 2.5f, Mathf.Abs(d - (cr - 2f)))) * disc;
                float sheen = disc * Mathf.Clamp01(0.5f - py / (2f * cr)) * 0.30f;
                float a = disc * 0.10f + rim * 0.60f + sheen * 0.55f;
                tex.SetPixel(x, y, new Color(0.82f, 0.93f, 1.00f, Mathf.Clamp01(a)));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// Unity 6 removed the built-in Arial.ttf — GetBuiltinResource now THROWS
    /// for it instead of returning null. LegacyRuntime.ttf is the bundled
    /// replacement. A missing font must never crash UI construction.
    /// </summary>
    static Font GetDefaultFont()
    {
        foreach (var name in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
        {
            try
            {
                var f = Resources.GetBuiltinResource<Font>(name);
                if (f != null) return f;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"SunOrbitControl: built-in font '{name}' unavailable ({e.GetType().Name}).");
            }
        }
        return null;
    }
}
