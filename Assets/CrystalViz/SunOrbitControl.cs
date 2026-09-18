using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sun-orbit slider: a vertical slider pinned to the left edge of the screen.
/// Sliding UP rotates the sun clockwise around the crystal sphere; sliding DOWN
/// rotates it counter-clockwise. The whole UI is built in code (no prefabs) so
/// the scene file stays tiny and everything is version-controlled as C#.
/// </summary>
public class SunOrbitControl : MonoBehaviour
{
    [HideInInspector] public CrystalVizBootstrap bootstrap;

    Slider slider;
    Text angleLabel;
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
        });
        slider.value = targetAzimuth / 360f; // fires listener, sets initial sun pos
        PositionKnob();
        UpdateLabel();
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

        // Modern glassmorphism skin: procedural textures (dark glass track,
        // glowing cyan fill, glowing knob) generated once — no image assets.
        var trackSprite = MakeBarSprite(48, 64, 22,
            new Color(0.17f, 0.19f, 0.23f, 0.78f), new Color(0.07f, 0.09f, 0.13f, 0.78f),
            0, Color.clear);
        var fillSprite = MakeBarSprite(48, 64, 22,
            new Color(0.50f, 0.95f, 1.00f, 0.95f), new Color(0.05f, 0.72f, 0.95f, 0.95f),
            16, new Color(0.25f, 0.85f, 1.00f, 1f));
        var knobTex = MakeKnobTexture(96);

        // Slider root: vertical strip hugging the left edge.
        var root = new GameObject("SunSlider", typeof(RectTransform), typeof(Slider));
        root.transform.SetParent(canvasGo.transform, false);
        var rrt = root.GetComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0f, 0f);
        rrt.anchorMax = new Vector2(0f, 1f);
        rrt.offsetMin = new Vector2(20f, 150f);
        rrt.offsetMax = new Vector2(116f, -150f);

        slider = root.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.direction = Slider.Direction.BottomToTop;

        // Track: dark glass rounded bar.
        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(root.transform, false);
        var bgrt = bg.GetComponent<RectTransform>();
        bgrt.anchorMin = Vector2.zero; bgrt.anchorMax = Vector2.one;
        bgrt.offsetMin = new Vector2(24f, 0f); bgrt.offsetMax = new Vector2(-24f, 0f);
        var bgImg = bg.GetComponent<Image>();
        bgImg.sprite = trackSprite;
        bgImg.type = Image.Type.Sliced;

        // Fill area (wider than the track so the fill's glow bleeds over it).
        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(root.transform, false);
        var fart = fillArea.GetComponent<RectTransform>();
        fart.anchorMin = Vector2.zero; fart.anchorMax = Vector2.one;
        fart.offsetMin = new Vector2(6f, 0f); fart.offsetMax = new Vector2(-6f, 0f);
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        var fillImg = fill.GetComponent<Image>();
        fillImg.sprite = fillSprite;
        fillImg.type = Image.Type.Sliced;
        slider.fillRect = fill.GetComponent<RectTransform>();

        // Handle area + glowing knob. The knob is a RawImage (texture drawn
        // directly, no sprite mesh involved) positioned with fractional
        // anchors in PositionKnob(); slider.handleRect stays null so the
        // Slider never stretches the knob.
        var handleArea = new GameObject("Handle Area", typeof(RectTransform));
        handleArea.transform.SetParent(root.transform, false);
        var hart = handleArea.GetComponent<RectTransform>();
        hart.anchorMin = Vector2.zero; hart.anchorMax = Vector2.one;
        hart.offsetMin = Vector2.zero; hart.offsetMax = Vector2.zero;
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(RawImage));
        handle.transform.SetParent(handleArea.transform, false);
        var hrt = handle.GetComponent<RectTransform>();
        hrt.pivot = new Vector2(0.5f, 0.5f);
        hrt.sizeDelta = new Vector2(96f, 96f);
        knobRT = hrt;
        handle.GetComponent<RawImage>().texture = knobTex;

        // Angle readout under the slider.
        var labelGo = new GameObject("AngleLabel", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(canvasGo.transform, false);
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(0f, 0f);
        lrt.anchoredPosition = new Vector2(68f, 92f);
        lrt.sizeDelta = new Vector2(140f, 44f);
        var txt = labelGo.GetComponent<Text>();
        txt.font = GetDefaultFont(); // may be null; Text renders nothing without one
        txt.fontSize = 26;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = new Color(0.93f, 0.97f, 1f, 0.92f);
        angleLabel = txt;
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
    /// Glassy circular knob texture: bright disc, cyan rim, soft cyan aura.
    /// Used via RawImage so no sprite mesh is involved.
    /// </summary>
    static Texture2D MakeKnobTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.filterMode = FilterMode.Bilinear;
        float c = size / 2f;
        float cr = size * 0.23f;
        Color cyan = new Color(0.30f, 0.88f, 1.00f);
        Color glass = new Color(0.93f, 0.97f, 1.00f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f - c;
                float py = y + 0.5f - c;
                float d = Mathf.Sqrt(px * px + py * py);
                float aura = Mathf.Pow(Mathf.Clamp01(1f - d / c), 2.4f) * 0.55f;
                float disc = 1f - Mathf.SmoothStep(cr - 1.5f, cr + 1.5f, d);
                float rim = (1f - Mathf.SmoothStep(0f, 3f, Mathf.Abs(d - (cr - 2f)))) * disc;
                float sheen = disc * Mathf.Clamp01(0.5f - py / (2f * cr)) * 0.35f;
                float r = cyan.r * aura + glass.r * disc + rim * 0.6f + sheen;
                float g = cyan.g * aura + glass.g * disc + rim * 0.9f + sheen;
                float bch = cyan.b * aura + glass.b * disc + rim + sheen;
                float a = Mathf.Clamp01(aura * (1f - disc) + disc);
                tex.SetPixel(x, y, new Color(
                    Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(bch), a));
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
