using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Sun-orbit slider: a vertical slider pinned to the right edge of the screen.
/// Sliding UP rotates the sun clockwise around the tree; sliding DOWN
/// rotates it counter-clockwise. v1.0.49: the slider runs 12:00 PM (bottom,
/// v=0) to 12:00 AM (top, v=1) — the direction Tyler asked for. v1.0.50: the
/// tick labels and the clock pill are gone (one clean solid slider again);
/// the panel starts HIDDEN and pops in/out via the corner menu's star button,
/// and a day/night toggle button sits at the top of the panel. The whole UI
/// is built in code (no prefabs) so the scene file stays tiny and everything
/// is version-controlled as C#.
/// </summary>
public class SunOrbitControl : MonoBehaviour
{
    [HideInInspector] public CrystalVizBootstrap bootstrap;

    Slider slider;
    RectTransform knobRT;
    RectTransform ringRT; // v1.0.47: gold sundial ring riding behind the sun thumb
    float currentAzimuth = 54f;
    float targetAzimuth = 54f;
    float lastSyncedV = -1f; // v1.0.48: last value pushed through SetTimeOfDay

    // v1.0.50: the lighting slider lives on its own panel so the corner
    // menu's star button can pop it in/out. The panel starts HIDDEN.
    GameObject panelGO;
    CanvasGroup panelCG; // v1.0.50: alpha fade for the pop-in/out
    bool panelVisible;
    float panelT;   // 0 = hidden, 1 = shown
    float panelDir; // +1 popping in, -1 popping out, 0 idle
    const float PanelPopDur = 0.28f;

    // v1.0.50: day/night toggle button at the top of the slider panel.
    RectTransform dayNightRT;
    float dayNightPunch;

    void Start()
    {
        BuildForScreenshot();
        // v1.0.48: every path that changes the time-of-day funnels through
        // SetTimeOfDay — the Slider's own drag events, the wide touch zone,
        // and the per-frame reconciliation in Update(). One funnel: the sun
        // azimuth, the knob, and the day/night lighting can never drift
        // apart again.
        slider.onValueChanged.AddListener(v => SetTimeOfDay(v));
        // v1.0.46: the slider initializes to the device's real local time
        // (player request) — opening the app on a real evening lands in
        // moonlight with zero interaction.
        SetTimeOfDay(DeviceTimeSliderValue());
    }

    /// <summary>
    /// v1.0.48: the single funnel for time-of-day changes. Drives the sun
    /// azimuth target, the knob position, and the day/night lighting together
    /// from one value. (v1.0.50: the clock pill is gone — no times on the
    /// slider anymore; the day/night button is the quick switch.)
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
        // the screenshot pins the slider to noon ("12:00 PM", full daylight)
        // instead of the device clock. v1.0.49: noon is the BOTTOM of the
        // slider now (v=0). The sun keeps its pleasant 54° 3/4 modeling
        // azimuth for continuity with earlier captures.
        // v1.0.48: funnel through SetTimeOfDay like every other path.
        SetTimeOfDay(0f);
        targetAzimuth = 54f;
        currentAzimuth = 54f;
        if (bootstrap != null) bootstrap.PlaceSun(54f);
    }

    /// <summary>
    /// v1.0.46: maps the device's real local time onto the slider (player
    /// request). v1.0.49: the slider runs noon (BOTTOM, v=0) to midnight
    /// (TOP, v=1) — the direction Tyler asked for. Morning hours clamp to
    /// noon — daylight, which is the honest answer for 9 AM on a 12-hour
    /// afternoon/evening scale.
    /// </summary>
    static float DeviceTimeSliderValue()
    {
        var now = System.DateTime.Now;
        float hour = now.Hour + now.Minute / 60f + now.Second / 3600f;
        if (hour < 12f) return 0f;
        return Mathf.Clamp01((hour - 12f) / 12f);
    }

    /// <summary>
    /// v1.0.49: HARD day/night switch (player request — no gradual fade).
    /// v=0 is 12:00 PM (noon, bottom), v=1 is 12:00 AM (midnight, top).
    /// Daylight runs noon until 7:00 PM; dark runs 7:00 PM until midnight.
    /// Stated generally: dark when hour >= 19 OR hour < 6, daylight
    /// otherwise — so a 6:00 AM sunrise holds if the range ever extends.
    /// </summary>
    public static float NightFactor(float v)
    {
        float hour24 = 12f + v * 12f; // 12 (noon) .. 24 (midnight)
        return (hour24 >= 19f || hour24 < 6f) ? 1f : 0f;
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
        // (v1.0.50: the pill is gone — no times on the slider anymore.)
    }

    /// <summary>
    /// v1.0.50: the lighting slider panel starts HIDDEN — the corner menu's
    /// star button pops it in/out (ToggleSliderPanel). In edit mode (the CI
    /// screenshot path runs no Update) the panel snaps instantly instead of
    /// animating.
    /// </summary>
    public bool IsSliderPanelVisible => panelVisible;

    public void ToggleSliderPanel() => SetSliderPanelVisible(!panelVisible);

    public void SetSliderPanelVisible(bool show)
    {
        if (show)
        {
            if (panelVisible && panelDir == 0f) return;
            panelVisible = true;
            if (panelGO != null)
            {
                panelGO.SetActive(true);
                // Start the pop-in from invisible: tiny scale + zero alpha,
                // so the first frame doesn't flash at full size.
                panelGO.transform.localScale = new Vector3(0.001f, 0.001f, 1f);
                if (panelCG != null)
                {
                    panelCG.alpha = 0f;
                    panelCG.blocksRaycasts = true;
                }
                if (Application.isPlaying) { panelT = 0f; panelDir = 1f; }
                else
                {
                    panelT = 1f; panelDir = 0f;
                    panelGO.transform.localScale = Vector3.one;
                    if (panelCG != null) panelCG.alpha = 1f;
                }
            }
        }
        else
        {
            if (!panelVisible && panelDir == 0f) return;
            panelVisible = false;
            if (panelGO != null)
            {
                if (panelCG != null) panelCG.blocksRaycasts = false;
                if (Application.isPlaying) panelDir = -1f;
                else
                {
                    panelT = 0f; panelDir = 0f;
                    if (panelCG != null) panelCG.alpha = 0f;
                    panelGO.SetActive(false);
                }
            }
        }
    }

    /// <summary>
    /// v1.0.50: the day/night button's action — jump the slider between noon
    /// (day) and 10 PM (night) when the current lighting is the other one.
    /// The hard 7 PM / 6 AM switch in NightFactor applies the lighting.
    /// Called by the slider touch zone (it owns every touch on the right
    /// edge and forwards taps landing on the day/night disc).
    /// </summary>
    public void OnDayNightTapped()
    {
        ToggleDayNight();
        dayNightPunch = 1f;
    }

    public void ToggleDayNight()
    {
        float v = slider != null ? slider.normalizedValue : lastSyncedV;
        if (NightFactor(v) > 0.5f) SetTimeOfDay(0f);       // night -> day (12:00 PM)
        else SetTimeOfDay(10f / 12f);                      // day -> night (10:00 PM)
    }

    /// <summary>
    /// v1.0.50: does this screen point land on the day/night disc? The touch
    /// zone calls this before scrubbing — a tap on the disc toggles
    /// day/night instead of moving the slider.
    /// </summary>
    public bool DayNightHit(Vector2 screenPos, Camera cam)
    {
        if (dayNightRT == null || !dayNightRT.gameObject.activeInHierarchy)
            return false;
        return RectTransformUtility.RectangleContainsScreenPoint(dayNightRT, screenPos, cam);
    }

    static float EaseOutBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
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

        // v1.0.50: slider-panel pop animation (quick and snappy, like the
        // corner menu). Pop-in overshoots; pop-out eases back and deactivates.
        // Scale + alpha fade together.
        if (panelDir != 0f && panelGO != null)
        {
            float dt = Time.deltaTime;
            panelT = Mathf.Clamp01(panelT + panelDir * dt / PanelPopDur);
            float e = panelDir > 0f
                ? EaseOutBack(panelT)
                : panelT * panelT * (3f - 2f * panelT);
            float s = Mathf.Max(0.001f, e);
            panelGO.transform.localScale = new Vector3(s, s, 1f);
            if (panelCG != null) panelCG.alpha = Mathf.Clamp01(panelT);
            if (panelT <= 0f)
            {
                panelDir = 0f;
                if (panelCG != null) panelCG.alpha = 0f;
                panelGO.SetActive(false);
            }
            else if (panelT >= 1f)
            {
                panelDir = 0f;
                panelGO.transform.localScale = Vector3.one;
                if (panelCG != null) panelCG.alpha = 1f;
            }
        }

        // v1.0.50: day/night button tap feedback.
        if (dayNightPunch > 0f)
        {
            dayNightPunch = Mathf.Max(0f, dayNightPunch - Time.deltaTime * 4f);
            float s = 1f + 0.18f * dayNightPunch;
            if (dayNightRT != null) dayNightRT.localScale = new Vector3(s, s, 1f);
        }
    }

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

        // v1.0.50: the slider panel — everything visible (the slider strip,
        // the day/night button, the touch zone) hangs under this wrapper so
        // the corner menu's star button can pop it in/out with one scale
        // animation. The panel spans the full canvas so the children's rects
        // need no conversion; the pivot sits at the right-edge center so the
        // pop originates beside the slider. It starts HIDDEN.
        var panelGO_ = new GameObject("SliderPanel", typeof(RectTransform), typeof(CanvasGroup));
        panelGO_.transform.SetParent(canvasGo.transform, false);
        var prt = panelGO_.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;
        prt.pivot = new Vector2(1f, 0.5f);
        panelGO = panelGO_;
        panelCG = panelGO_.GetComponent<CanvasGroup>();
        panelCG.alpha = 0f;
        panelCG.blocksRaycasts = false;
        panelGO.transform.localScale = new Vector3(0.001f, 0.001f, 1f);
        panelGO.SetActive(false);
        panelVisible = false;
        panelT = 0f;
        panelDir = 0f;

        // Slider root: slim vertical strip hugging the RIGHT edge.
        // v1.0.8: the whole bar is 75% smaller — rendered at quarter scale
        // about its right-center pivot so it stays glued to the edge.
        // v1.0.26: 25% wider (48px -> 60px) for easier touch targeting,
        // right edge stays glued at -28px.
        var root = new GameObject("SunSlider", typeof(RectTransform), typeof(Slider));
        root.transform.SetParent(panelGO.transform, false);
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

        // (v1.0.47: the old angle readout under the slider is gone for good.)

        // v1.0.50: the day/night toggle — a glass disc at the top of the
        // lighting slider wearing the SAME sun+moon symbol as the corner
        // menu's star button (player request). Taps jump the slider between
        // noon (day) and 10 PM (night); the hard 7 PM / 6 AM switch applies
        // the lighting. It replaces the old decorative sun icon (the thumb
        // already IS the sun PNG, so nothing is lost). Child of the slider
        // root: the root's 0.25 scale applies, so the 180px disc renders
        // ~45px, parked just above the track's top edge like the old icon.
        // NOTE: it is NOT a uGUI Button — the slider's invisible touch zone
        // sits above it in hit order and owns every touch on the right edge,
        // so the zone forwards taps landing on this disc to OnDayNightTapped
        // (see SliderTouchZone). Keeps one input owner, zero hit-order bugs.
        var dnGO = new GameObject("DayNightButton", typeof(RectTransform), typeof(Image));
        dnGO.transform.SetParent(root.transform, false);
        var drt = dnGO.GetComponent<RectTransform>();
        drt.anchorMin = new Vector2(0.5f, 1f);
        drt.anchorMax = new Vector2(0.5f, 1f);
        drt.pivot = new Vector2(0.5f, 0.5f);
        drt.anchoredPosition = new Vector2(0f, 90f);
        drt.sizeDelta = new Vector2(180f, 180f);
        drt.localScale = new Vector3(1f, 1f, 1f);
        var dnBg = dnGO.GetComponent<Image>();
        dnBg.sprite = MeadowGlassUI.MakeGlassDisc(160);
        dnBg.preserveAspect = true;
        var dnGlyphGO = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
        dnGlyphGO.transform.SetParent(dnGO.transform, false);
        var dgrt = dnGlyphGO.GetComponent<RectTransform>();
        dgrt.anchorMin = new Vector2(0.5f, 0.5f);
        dgrt.anchorMax = new Vector2(0.5f, 0.5f);
        dgrt.pivot = new Vector2(0.5f, 0.5f);
        dgrt.anchoredPosition = Vector2.zero;
        dgrt.sizeDelta = new Vector2(120f, 120f);
        var dnGlyphImg = dnGlyphGO.GetComponent<Image>();
        var dnTex = MeadowGlassUI.MakeSunMoonIcon(160);
        dnGlyphImg.sprite = Sprite.Create(dnTex,
            new Rect(0, 0, dnTex.width, dnTex.height),
            new Vector2(0.5f, 0.5f), 100f);
        dnGlyphImg.preserveAspect = true;
        dnGlyphImg.raycastTarget = false; // the zone owns the tap, forwards it
        dayNightRT = drt;

        // v1.0.50: the handle pill and its clock are GONE (player request —
        // no times on the slider anymore; the day/night button above is the
        // quick switch). The slider is one clean solid strip again.

        // v1.0.50: the hour tick labels are GONE too — back to one clean
        // solid slider, per the player request.

        // v1.0.48: the generous touch zone — the visible strip renders only
        // ~15px wide on screen (Tyler prefers the slim look), far too narrow
        // for a fingertip to hit reliably. This invisible 160px-wide catcher
        // sits over the slider column (and the sun icon) and maps vertical
        // drags to time-of-day, so the slider is easy to grab without
        // changing how it looks. Added last so it raycasts above the strip;
        // it drives everything directly through SetTimeOfDay.
        var zoneGO = new GameObject("SliderTouchZone", typeof(RectTransform), typeof(Image));
        zoneGO.transform.SetParent(panelGO.transform, false); // v1.0.50: under the panel so it hides with it
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
    /// Unity 6 removed the built-in Arial.ttf — note kept for history:
    /// v1.0.50 removed the slider's last Text users (pill + tick labels),
    /// so no font lookup is needed anymore.
    /// </summary>
}

/// <summary>
/// v1.0.48: invisible, generous touch catcher over the sun-slider column.
/// The visible strip renders only ~15px wide on screen (Tyler prefers the
/// slim look) — too narrow for a fingertip to hit reliably. This 160px-wide
/// zone maps vertical drags to time-of-day and funnels them through
/// SunOrbitControl.SetTimeOfDay, so the slider is easy to grab without
/// changing how it looks.
/// </summary>
public class SliderTouchZone : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [HideInInspector] public SunOrbitControl orbit;
    // v1.0.50: a press that starts on the day/night disc toggles day/night
    // instead of scrubbing — suppress the scrub until the finger lifts.
    bool suppressScrub;
    RectTransform rt;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (orbit != null && orbit.DayNightHit(eventData.position, eventData.pressEventCamera))
        {
            orbit.OnDayNightTapped();
            suppressScrub = true;
            return;
        }
        suppressScrub = false;
        DragTo(eventData);
    }
    public void OnDrag(PointerEventData eventData)
    {
        if (suppressScrub) return;
        DragTo(eventData);
    }
    public void OnPointerUp(PointerEventData eventData) { suppressScrub = false; }

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
