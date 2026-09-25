using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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
    Text timeText;   // v1.0.48: big numerals inside the handle pill ("12:00")
    Text periodText; // v1.0.48: quiet AM/PM suffix beside the numerals
    RectTransform knobRT;
    RectTransform ringRT; // v1.0.47: gold sundial ring riding behind the sun thumb
    float currentAzimuth = 54f;
    float targetAzimuth = 54f;
    float lastSyncedV = -1f; // v1.0.48: last value pushed through SetTimeOfDay

    void Start()
    {
        BuildForScreenshot();
        // v1.0.48: every path that changes the time-of-day funnels through
        // SetTimeOfDay — the Slider's own drag events, the wide touch zone,
        // and the per-frame reconciliation in Update(). One funnel: the sun
        // azimuth, the knob + handle pill, the clock text, and the day/night
        // lighting can never drift apart again.
        slider.onValueChanged.AddListener(v => SetTimeOfDay(v));
        // v1.0.46: the slider initializes to the device's real local time
        // (player request) — opening the app on a real evening lands in
        // moonlight with zero interaction.
        SetTimeOfDay(DeviceTimeSliderValue());
    }

    /// <summary>
    /// v1.0.48: the single funnel for time-of-day changes. Drives the sun
    /// azimuth target, the knob position, the handle-pill clock text, and
    /// the day/night lighting together from one value.
    /// </summary>
    public void SetTimeOfDay(float v)
    {
        v = Mathf.Clamp01(v);
        lastSyncedV = v;
        targetAzimuth = v * 360f;
        // Push the value into the Slider without re-firing its callback
        // (we are the callback path); the fill visual still follows.
        if (slider != null && !Mathf.Approximately(slider.value, v))
            slider.SetValueWithoutNotify(v);
        PositionKnob();
        UpdateTimeLabel(v);
        ApplyTimeOfDay(v);
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
        // v1.0.46: captures must be deterministic — CI runs at any hour, so
        // the screenshot pins the slider to noon (top, "12:00 PM", full
        // daylight) instead of the device clock. The sun keeps its pleasant
        // 54° 3/4 modeling azimuth for continuity with earlier captures.
        // v1.0.48: funnel through SetTimeOfDay like every other path.
        SetTimeOfDay(1f);
        targetAzimuth = 54f;
        currentAzimuth = 54f;
        if (bootstrap != null) bootstrap.PlaceSun(54f);
    }

    /// <summary>
    /// v1.0.46: maps the device's real local time onto the slider (player
    /// request). The slider spans noon (top, v=1) to midnight (bottom, v=0);
    /// morning hours clamp to noon — daylight, which is the honest answer
    /// for 9 AM on a 12-hour afternoon/evening scale.
    /// </summary>
    static float DeviceTimeSliderValue()
    {
        var now = System.DateTime.Now;
        float hour = now.Hour + now.Minute / 60f + now.Second / 3600f;
        if (hour < 12f) return 1f;
        return Mathf.Clamp01((24f - hour) / 12f);
    }

    /// <summary>
    /// v1.0.46: day/night factor from the slider's time-of-day. v=1 is
    /// 12:00 PM (noon), v=0 is 12:00 AM (midnight). Daylight runs noon–7 PM,
    /// moonlight 7 PM–midnight, with a smooth 1-hour crossfade centered on
    /// 7 PM (SmoothStep => no pop at the boundary).
    /// </summary>
    public static float NightFactor(float v)
    {
        float hour24 = 12f + (1f - v) * 12f; // 12 (noon) .. 24 (midnight)
        return Mathf.SmoothStep(18.5f, 19.5f, hour24);
    }

    /// <summary>
    /// v1.0.46: pushes the slider's time-of-day into the scene lighting.
    /// </summary>
    void ApplyTimeOfDay(float v)
    {
        if (bootstrap != null) bootstrap.ApplyTimeOfDayLighting(NightFactor(v));
    }

    /// <summary>
    /// Centers the glowing knob (and its gold sundial ring) on the fill
    /// line. The Slider's own handleRect driving stretches the knob across
    /// the track, so we leave slider.handleRect null and place the knob with
    /// fractional anchors.
    /// </summary>
    void PositionKnob()
    {
        if (slider == null || knobRT == null) return;
        float v = slider.normalizedValue;
        knobRT.anchorMin = new Vector2(0.5f, v);
        knobRT.anchorMax = new Vector2(0.5f, v);
        knobRT.anchoredPosition = Vector2.zero;
        if (ringRT != null)
        {
            ringRT.anchorMin = new Vector2(0.5f, v);
            ringRT.anchorMax = new Vector2(0.5f, v);
            ringRT.anchoredPosition = Vector2.zero;
        }
        // v1.0.48 fix: the clock pill is a CHILD OF THE KNOB — it inherits
        // the thumb's anchors structurally, so no per-frame pill math here.
    }

    void Update()
    {
        if (bootstrap == null || bootstrap.sun == null) return;
        // v1.0.48: reconciliation backstop — if the slider's value changed
        // through ANY path, the sun, clock, and lighting follow within one
        // frame. This is what guarantees the drag->lighting sync no matter
        // which Unity event path delivered the touch.
        if (slider != null && Mathf.Abs(slider.normalizedValue - lastSyncedV) > 0.0004f)
            SetTimeOfDay(slider.normalizedValue);
        // Smooth-damped follow so the sun glides instead of snapping.
        currentAzimuth = Mathf.LerpAngle(currentAzimuth, targetAzimuth,
            1f - Mathf.Exp(-8f * Time.deltaTime));
        bootstrap.PlaceSun(currentAzimuth);
        PositionKnob();
    }

    /// <summary>
    /// v1.0.47: the time pill — big warm-white numerals ("12:00") with a
    /// quiet sage AM/PM beside them, on the meadow-glass badge. The slider
    /// value t in [0,1] maps linearly to h = 12*t, rounded to the hour: top
    /// reads 12:00 PM, bottom reads 12:00 AM. Updates live while dragging
    /// (called from the value-changed listener). The old floating text and
    /// the tiny angle readout are gone — the clock now carries the slider's
    /// whole meaning.
    /// </summary>
    void UpdateTimeLabel(float v)
    {
        int hr = Mathf.Clamp(Mathf.RoundToInt(v * 12f), 0, 12);
        if (timeText != null)
            timeText.text = hr == 12 ? "12:00" : hr == 0 ? "12:00" : $"{hr}:00";
        if (periodText != null)
            periodText.text = hr == 12 ? "PM" : "AM";
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
        // v1.0.47: the sundial ring — a gold hairline halo riding behind the
        // sun thumb (added first so it draws underneath). PositionKnob()
        // moves it in lockstep with the knob.
        var ringGO = new GameObject("SundialRing", typeof(RectTransform), typeof(Image));
        ringGO.transform.SetParent(handleArea.transform, false);
        ringRT = ringGO.GetComponent<RectTransform>();
        ringRT.pivot = new Vector2(0.5f, 0.5f);
        ringRT.sizeDelta = new Vector2(96f, 96f);
        var ringImg = ringGO.GetComponent<Image>();
        ringImg.sprite = MeadowGlassUI.MakeGoldRing(96, 6);
        ringImg.color = new Color(1f, 0.78f, 0.22f, 0.85f);
        ringImg.preserveAspect = true;
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

        // (v1.0.47: the old angle readout under the slider is gone — the
        // clock pill above now carries the slider's whole meaning.)

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

        // v1.0.48: the handle pill — the clock now RIDES the sun thumb
        // (player request: "attach it to the button so it drags with us")
        // instead of floating at the top of the screen. The pill is a CHILD
        // OF THE KNOB: it inherits the thumb's anchors structurally, so it
        // travels with every drag with zero per-frame math and stays glued
        // in every canvas render mode. (d71adeb parked it by absolute
        // RectTransform.position, which the CI screenshot harness silently
        // invalidated when it flips the canvas to ScreenSpaceCamera after
        // BuildForScreenshot — the pill rendered top-center while the
        // anchor-driven knob stayed correct. This cannot drift: same anchors
        // as the thumb, on a real phone or in the harness.)
        // raycastTarget is off on the pill and its texts so touches fall
        // through to the slider's touch zone.
        var pillGO = new GameObject("HandleTimePill", typeof(RectTransform), typeof(Image));
        pillGO.transform.SetParent(knobRT.transform, false);
        var pillRT = pillGO.GetComponent<RectTransform>();
        pillRT.anchorMin = new Vector2(0.5f, 0.5f);
        pillRT.anchorMax = new Vector2(0.5f, 0.5f);
        pillRT.pivot = new Vector2(1f, 0.5f);
        // The knob lives under the slider root's 0.25x scale: counter-scale
        // the pill so the numerals render full-size and legible, and express
        // the 110px thumb offset in knob-local units (110 / 0.25 = 440).
        float pillCounter = 1f / rrt.localScale.x;
        pillRT.localScale = new Vector3(pillCounter, pillCounter, 1f);
        pillRT.anchoredPosition = new Vector2(-110f * pillCounter, 0f);
        pillRT.sizeDelta = new Vector2(300f, 84f);
        var pillImg = pillGO.GetComponent<Image>();
        pillImg.sprite = MeadowGlassUI.MakeGlassPill(256, 96);
        // v1.0.48 fix: Simple, not Sliced. The sprite is generated at
        // near-display size (256x96 -> 300x84 rect), and Sliced with
        // half-height borders produced a zero-height center strip that
        // rendered nothing — the glass never drew. Simple stretches the
        // whole baked capsule (~17% wider) and the gold ring survives.
        pillImg.type = Image.Type.Simple;
        pillImg.preserveAspect = false;
        pillImg.raycastTarget = false;

        var timeGO = new GameObject("TimeText", typeof(RectTransform), typeof(Text));
        timeGO.transform.SetParent(pillGO.transform, false);
        var ttrt = timeGO.GetComponent<RectTransform>();
        // v1.0.48 fix: the numerals live INSIDE the glass pill, right-aligned
        // (the old negative offsets parked them outside the pill, floating
        // detached to its left).
        ttrt.anchorMin = new Vector2(1f, 0.5f);
        ttrt.anchorMax = new Vector2(1f, 0.5f);
        ttrt.pivot = new Vector2(1f, 0.5f);
        ttrt.anchoredPosition = new Vector2(-88f, 2f);
        ttrt.sizeDelta = new Vector2(196f, 84f);
        timeText = timeGO.GetComponent<Text>();
        timeText.font = GetDefaultFont();
        timeText.fontSize = 44;
        timeText.alignment = TextAnchor.MiddleRight;
        timeText.color = MeadowGlassUI.WarmWhite;
        timeText.raycastTarget = false;

        var periodGO = new GameObject("PeriodText", typeof(RectTransform), typeof(Text));
        periodGO.transform.SetParent(pillGO.transform, false);
        var perRt = periodGO.GetComponent<RectTransform>();
        // v1.0.48 fix: the AM/PM sits inside the pill just right of the
        // numerals ("12:00 PM" reads left to right).
        perRt.anchorMin = new Vector2(1f, 0.5f);
        perRt.anchorMax = new Vector2(1f, 0.5f);
        perRt.pivot = new Vector2(0f, 0.5f);
        perRt.anchoredPosition = new Vector2(-80f, 4f);
        perRt.sizeDelta = new Vector2(64f, 84f);
        periodText = periodGO.GetComponent<Text>();
        periodText.font = GetDefaultFont();
        periodText.fontSize = 24;
        periodText.alignment = TextAnchor.MiddleLeft;
        periodText.color = MeadowGlassUI.Sage;
        periodText.raycastTarget = false;

        // v1.0.48: the generous touch zone — the visible strip renders only
        // ~15px wide on screen (Tyler prefers the slim look), far too narrow
        // for a fingertip to hit reliably. This invisible 160px-wide catcher
        // sits over the slider column (and the sun icon) and maps vertical
        // drags to time-of-day, so the slider is easy to grab without
        // changing how it looks. Added last so it raycasts above the strip;
        // it drives everything directly through SetTimeOfDay.
        var zoneGO = new GameObject("SliderTouchZone", typeof(RectTransform), typeof(Image));
        zoneGO.transform.SetParent(canvasGo.transform, false);
        var zrt = zoneGO.GetComponent<RectTransform>();
        zrt.anchorMin = new Vector2(1f, 0f);
        zrt.anchorMax = new Vector2(1f, 1f);
        zrt.pivot = new Vector2(0.5f, 0.5f);
        zrt.offsetMin = new Vector2(-168f, 100f);
        zrt.offsetMax = new Vector2(-8f, -100f);
        var zoneImg = zoneGO.GetComponent<Image>();
        zoneImg.color = new Color(0f, 0f, 0f, 0f); // invisible but raycastable
        var zone = zoneGO.AddComponent<SliderTouchZone>();
        zone.orbit = this;
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

/// <summary>
/// v1.0.48: invisible, generous touch catcher over the sun-slider column.
/// The visible strip renders only ~15px wide on screen (Tyler prefers the
/// slim look) — too narrow for a fingertip to hit reliably. This 160px-wide
/// zone maps vertical drags to time-of-day and funnels them through
/// SunOrbitControl.SetTimeOfDay, so the slider is easy to grab without
/// changing how it looks.
/// </summary>
public class SliderTouchZone : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    [HideInInspector] public SunOrbitControl orbit;
    RectTransform rt;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
    }

    public void OnPointerDown(PointerEventData eventData) { DragTo(eventData); }
    public void OnDrag(PointerEventData eventData) { DragTo(eventData); }

    void DragTo(PointerEventData eventData)
    {
        if (orbit == null || rt == null) return;
        Vector2 local;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rt, eventData.position, eventData.pressEventCamera, out local))
        {
            // BottomToTop: touch at the zone's bottom edge -> 0, top -> 1.
            float v = Mathf.Clamp01((local.y - rt.rect.yMin) / rt.rect.height);
            orbit.SetTimeOfDay(v);
        }
    }
}
