using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-left corner menu (v1.0.45, player request): a warm-white arrow on a
/// meadow-glass "dewdrop" disc (v1.0.47: deep forest-green translucent glass
/// with a baked gold hairline ring, replacing the old blue). v1.0.46: the
/// arrow's direction IS the menu state — collapsed (idle) it points
/// diagonally south-west, away from the screen center; the moment it's tapped
/// it flips 180° (quick rotation animation) to point north-east at the
/// fanning icons, and flips back when collapsed. v1.0.47: every toggle fires
/// a small feedback ceremony — a gold ring pulses out from the disc — and
/// taps answer back in plain-English toasts. Tapping the arrow slides three
/// buttons out from behind it toward the center — the sapling button
/// (v1.0.55: opens the inventory prototype — a frosted overlay with six
/// empty rounded slots), the day/night button (v1.0.55: a sun-and-moon
/// glyph on the star that snaps the time engine to 12 PM / 12 AM, flipping
/// the lighting and the bee<->firefly transformation through the existing
/// hour mechanism), and one placeholder (dots, no function yet) — and
/// tapping the arrow again collapses everything back. The slide is
/// staggered with an overshoot ease; the three buttons fan out on even 25°
/// steps along a common radius so the gaps between them are uniform.
///
/// v1.0.55: the old infinity reset mark (ResetToSprout) is gone, and so is
/// the lighting slider (player request) — button 0 is now the
/// sapling/inventory button. The day/night glyph re-renders whenever the
/// lighting state flips so the button always reads the current state
/// (bright sun by day, bright moon by night).
///
/// Raw Input is used (not uGUI Button + EventSystem) to match the tree's tap
/// detection, which also reads Input directly. TreeGrowthController swallows
/// presses that land on the menu (via MenuHitTest) so they never also count
/// as growth taps.
///
/// The CI screenshot path builds the scene in edit mode, where AddComponent
/// does NOT fire Awake(), so CrystalVizBootstrap calls Initialize()
/// explicitly after AddComponent. Idempotent: safe to call twice.
/// </summary>
public class TreeResetButton : MonoBehaviour
{
    public TreeGrowthController controller;

    // The three slide-out buttons: 0 = sapling (opens the inventory
    // prototype), 1 = day/night (snaps the time engine to 12 PM / 12 AM),
    // 2 = placeholder.
    class MenuButton
    {
        public RectTransform rt;
        public Vector2 homePos;  // parked: arrow center, scale 0
        public Vector2 outPos;   // fanned out toward the screen center
        public float t;          // 0 = in, 1 = out
        public float dir;        // +1 expanding, -1 collapsing, 0 idle
        public float delay;      // stagger before motion starts
        public float punch;      // 1 -> 0 feedback pop on placeholder tap
    }

    const float ArrowSize = 120f;
    const float SubSize = 100f;
    const float SlideDur = 0.32f;
    const float Stagger = 0.07f;

    RectTransform arrowRect;
    RectTransform glyphRt; // v1.0.46: the arrow glyph rotates to show menu state
    RectTransform pulseRt; // v1.0.47: gold feedback ring, pulses on toggle
    Image pulseImg;
    float pulseT;
    MenuButton[] subButtons;
    bool expanded;
    bool initialized;
    float arrowPunch;
    float arrowAngle = 180f; // v1.0.46: collapsed = SW (180°); expanded = NE (0°)
    GameObject menuCanvasGO; // v1.0.55: stored so BuildInventory can parent to it

    // v1.0.55: the day/night button's glyph re-renders on state flips so it
    // always reads the current lighting (bright sun by day, bright moon by
    // night). The Image + its live texture are cached here; the bool seeds
    // from the slider at build time and refreshes in Update().
    Image dayNightGlyphImg;
    Texture2D dayNightGlyphTex;
    bool dayNightIsNight;

    // v1.0.55: inventory prototype (sapling button). A frosted full-screen
    // overlay with a small centered panel holding six empty rounded slots.
    // Taps outside the panel close it; while open, MenuHitTest swallows
    // every tap so the tree never grows underneath it.
    public static bool InventoryOpen { get; private set; }
    GameObject inventoryRoot;
    RectTransform inventoryPanelRt;
    Image inventoryFrostImg;
    float inventoryT;   // 0 = closed, 1 = open
    float inventoryDir; // +1 opening, -1 closing, 0 idle
    const float InventoryPopDur = 0.26f;

    void Awake()
    {
        Initialize();
    }

    /// <summary>
    /// Builds the menu and hands the arrow rect to the controller as the
    /// reset-button backstop (expanded sub-buttons are covered by
    /// MenuHitTest). Called from Awake() in play mode and explicitly by the
    /// bootstrap in edit mode. Idempotent.
    /// </summary>
    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        controller = GetComponent<TreeGrowthController>();
        BuildUI();
        if (controller != null) controller.resetButtonRect = arrowRect;
    }

    /// <summary>
    /// True when the screen point hits the arrow or any expanded sub-button.
    /// TreeGrowthController calls this at touchdown so menu presses never
    /// grow the tree.
    /// </summary>
    public bool MenuHitTest(Vector2 screenPos)
    {
        // v1.0.55: while the inventory prototype is open (or animating),
        // EVERY tap counts as menu UI — the tree must never grow under the
        // frosted overlay.
        if (InventoryOpen || inventoryDir != 0f) return true;
        if (arrowRect != null &&
            RectTransformUtility.RectangleContainsScreenPoint(arrowRect, screenPos, null))
            return true;
        if (expanded && subButtons != null)
        {
            foreach (var b in subButtons)
            {
                if (b.t > 0.5f &&
                    RectTransformUtility.RectangleContainsScreenPoint(b.rt, screenPos, null))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// v1.0.50: CI screenshot helper — snaps the corner menu open instantly
    /// (edit mode runs no Update, so the stagger animation never plays).
    /// </summary>
    public void SnapExpandedForScreenshot()
    {
        if (!initialized) Initialize();
        expanded = true;
        pulseT = 0f;
        if (subButtons != null)
        {
            foreach (var b in subButtons)
            {
                b.t = 1f; b.dir = 0f; b.delay = 0f; b.punch = 0f;
                b.rt.anchoredPosition = b.outPos;
                b.rt.localScale = Vector3.one;
            }
        }
        arrowAngle = 0f;
        if (glyphRt != null) glyphRt.localRotation = Quaternion.Euler(0f, 0f, 0f);
    }

    /// <summary>
    /// v1.0.55: CI screenshot helper — re-syncs the day/night glyph to the
    /// slider's current lighting state. Edit mode runs no Update, so the
    /// per-frame sync never fires there; the screenshot tool calls this
    /// after flipping to night so the capture shows the moon-bright glyph.
    /// </summary>
    public void SyncDayNightGlyphForScreenshot()
    {
        var o = Orbit;
        if (o == null) return;
        dayNightIsNight = SunOrbitControl.NightFactor(o.CurrentSnappedValue) > 0.5f;
        RefreshDayNightGlyph();
    }

    /// <summary>
    /// v1.0.50: lazy handle on the time-of-day engine — the corner menu's
    /// day/night snap (v1.0.55: the star button) drives it. Resolved on
    /// first tap so menu/engine build order never matters.
    /// </summary>
    SunOrbitControl orbitRef;
    SunOrbitControl Orbit
    {
        get
        {
            if (orbitRef == null) orbitRef = FindObjectOfType<SunOrbitControl>();
            return orbitRef;
        }
    }

    /// <summary>
    /// v1.0.55: rebuilds the day/night button's glyph for the current
    /// lighting state (bright sun by day, bright moon by night) and swaps
    /// it onto the button. The old texture is destroyed to avoid leaking
    /// one 160px texture per flip.
    /// </summary>
    void RefreshDayNightGlyph()
    {
        if (dayNightGlyphImg == null) return;
        var old = dayNightGlyphTex;
        dayNightGlyphTex = MeadowGlassUI.MakeDayNightIcon(160, dayNightIsNight);
        dayNightGlyphImg.sprite = Sprite.Create(dayNightGlyphTex,
            new Rect(0f, 0f, dayNightGlyphTex.width, dayNightGlyphTex.height),
            new Vector2(0.5f, 0.5f), 100f);
        if (old != null)
        {
            if (Application.isPlaying) Destroy(old);
            else DestroyImmediate(old);
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;

        bool pressed = Input.GetMouseButtonDown(0);
        Vector2 pos = Input.mousePosition;
        if (!pressed && Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            pressed = true;
            pos = Input.GetTouch(0).position;
        }
        if (pressed)
        {
            // v1.0.55: while the inventory prototype is open it owns every
            // tap — a tap outside the panel closes it, taps on the panel do
            // nothing (the slots are empty for now).
            if (InventoryOpen)
            {
                if (inventoryPanelRt == null ||
                    !RectTransformUtility.RectangleContainsScreenPoint(
                        inventoryPanelRt, pos, null))
                {
                    SetInventoryOpen(false);
                }
            }
            // Null camera is correct: the menu lives on a ScreenSpaceOverlay canvas.
            else if (arrowRect != null &&
                RectTransformUtility.RectangleContainsScreenPoint(arrowRect, pos, null))
            {
                ToggleMenu();
                arrowPunch = 1f;
            }
            else if (expanded && subButtons != null)
            {
                for (int i = 0; i < subButtons.Length; i++)
                {
                    var b = subButtons[i];
                    if (b.t > 0.5f &&
                        RectTransformUtility.RectangleContainsScreenPoint(b.rt, pos, null))
                    {
                        if (i == 0)
                        {
                            // v1.0.55: the sapling button opens the
                            // inventory prototype — a frosted overlay with
                            // six empty rounded slots. Tap outside the
                            // panel to close it.
                            b.punch = 1f;
                            SetInventoryOpen(true);
                            MeadowToast.Show("Inventory sprouted — six empty slots, for now.");
                            Debug.Log("TreeResetButton: sapling button opened the inventory prototype.");
                        }
                        else if (i == 1)
                        {
                            // v1.0.55: the star button is the day/night
                            // toggle — it snaps the time engine to 12 PM
                            // (day) or 12 AM (night) based on the current
                            // lighting state, so the hard 7 PM / 6 AM
                            // lighting switch and the bee<->firefly
                            // transformation stay consistent through the
                            // existing hour mechanism. (The lighting slider
                            // is gone — this button is the only time control.)
                            b.punch = 1f;
                            var o = Orbit;
                            if (o != null)
                            {
                                bool isNight = SunOrbitControl.NightFactor(
                                    o.CurrentSnappedValue) > 0.5f;
                                o.SetTimeOfDay(isNight ? 0f : 1f);
                                dayNightIsNight = !isNight;
                                RefreshDayNightGlyph();
                                MeadowToast.Show(isNight
                                    ? "Daybreak — the bees are back out."
                                    : "Nightfall — the fireflies are out.");
                            }
                            else
                            {
                                MeadowToast.Show("Still growing — this one's coming soon.");
                            }
                            Debug.Log("TreeResetButton: day/night button snapped the time engine.");
                        }
                        else
                        {
                            // Placeholder: acknowledge the tap, no function yet.
                            b.punch = 1f;
                            MeadowToast.Show("More tools are on the way.");
                            Debug.Log($"TreeResetButton: placeholder button {i} tapped (no function yet).");
                        }
                        break;
                    }
                }
            }
        }

        // Slide animation (staggered, overshoot on expand).
        if (subButtons != null)
        {
            foreach (var b in subButtons)
            {
                if (b.delay > 0f) { b.delay -= dt; continue; }
                if (b.dir != 0f)
                {
                    b.t = Mathf.Clamp01(b.t + b.dir * dt / SlideDur);
                    float e = b.t * b.t * (3f - 2f * b.t); // smootherstep
                    b.rt.anchoredPosition = Vector2.Lerp(b.homePos, b.outPos, e);
                    float s = b.dir > 0f ? EaseOutBack(b.t) : e;
                    float ps = 1f + 0.22f * b.punch;
                    s = Mathf.Max(0.001f, s * ps);
                    b.rt.localScale = new Vector3(s, s, 1f);
                    if (b.t <= 0f || b.t >= 1f) b.dir = 0f;
                }
                else if (b.punch > 0f)
                {
                    // Idle punch (placeholder tapped while fully out).
                    float ps = 1f + 0.22f * b.punch;
                    b.rt.localScale = new Vector3(ps, ps, 1f);
                }
                if (b.punch > 0f) b.punch = Mathf.Max(0f, b.punch - dt * 3.5f);
            }
        }

        if (arrowPunch > 0f)
        {
            arrowPunch = Mathf.Max(0f, arrowPunch - dt * 4f);
            float s = 1f + 0.18f * arrowPunch;
            if (arrowRect != null) arrowRect.localScale = new Vector3(s, s, 1f);
        }

        // v1.0.47: gold feedback pulse — a hairline ring blooms out from the
        // disc and dissolves, so every toggle feels answered.
        if (pulseT > 0f && pulseRt != null)
        {
            pulseT = Mathf.Max(0f, pulseT - dt * 2.6f);
            float s = 1f + 0.55f * (1f - pulseT);
            pulseRt.localScale = new Vector3(s, s, 1f);
            if (pulseImg != null)
                pulseImg.color = new Color(1f, 0.78f, 0.22f, 0.85f * pulseT);
        }

        // v1.0.46: arrow flip — direction is menu state. Collapsed the glyph
        // rests at 180° (pointing south-west, away from center); on expand it
        // swings to 0° (north-east, toward the fanned-out icons) and back on
        // collapse. Fast exponential settle => a quick, snappy flip.
        float targetAngle = expanded ? 0f : 180f;
        arrowAngle = Mathf.LerpAngle(arrowAngle, targetAngle,
            1f - Mathf.Exp(-10f * dt));
        if (glyphRt != null)
            glyphRt.localRotation = Quaternion.Euler(0f, 0f, arrowAngle);

        // v1.0.55: inventory pop animation — the panel overshoots in like
        // the corner menu, and the frosted veil fades with it.
        if (inventoryDir != 0f && inventoryRoot != null)
        {
            float idt = Time.deltaTime;
            inventoryT = Mathf.Clamp01(inventoryT + inventoryDir * idt / InventoryPopDur);
            float ie = inventoryDir > 0f
                ? EaseOutBack(inventoryT)
                : inventoryT * inventoryT * (3f - 2f * inventoryT);
            float s = Mathf.Max(0.001f, ie);
            if (inventoryPanelRt != null)
                inventoryPanelRt.localScale = new Vector3(s, s, 1f);
            if (inventoryFrostImg != null)
            {
                var fc = inventoryFrostImg.color;
                fc.a = 0.55f * Mathf.Clamp01(inventoryT);
                inventoryFrostImg.color = fc;
            }
            if (inventoryT <= 0f)
            {
                inventoryDir = 0f;
                if (inventoryFrostImg != null)
                {
                    var fc = inventoryFrostImg.color;
                    fc.a = 0f;
                    inventoryFrostImg.color = fc;
                    inventoryFrostImg.raycastTarget = false;
                }
            }
            else if (inventoryT >= 1f)
            {
                inventoryDir = 0f;
                if (inventoryPanelRt != null)
                    inventoryPanelRt.localScale = Vector3.one;
                if (inventoryFrostImg != null)
                {
                    var fc = inventoryFrostImg.color;
                    fc.a = 0.55f;
                    inventoryFrostImg.color = fc;
                }
            }
        }

        // v1.0.55: keep the day/night glyph honest — re-render the button's
        // icon whenever the lighting state flips outside the button's own
        // tap path.
        var orbit = Orbit;
        if (orbit != null && dayNightGlyphImg != null)
        {
            bool isNight = SunOrbitControl.NightFactor(
                orbit.CurrentSnappedValue) > 0.5f;
            if (isNight != dayNightIsNight)
            {
                dayNightIsNight = isNight;
                RefreshDayNightGlyph();
            }
        }
    }

    void ToggleMenu()
    {
        expanded = !expanded;
        pulseT = 1f; // v1.0.47: every toggle fires the gold feedback pulse
        for (int i = 0; i < subButtons.Length; i++)
        {
            var b = subButtons[i];
            b.dir = expanded ? 1f : -1f;
            // Expand: infinity leads. Collapse: reverse order.
            b.delay = (expanded ? i : (subButtons.Length - 1 - i)) * Stagger;
        }
    }

    static float EaseOutBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    // ------------------------------------------------------------------ UI build

    void BuildUI()
    {
        // Root-level canvas: NOT parented to the (0.26-scaled) tree object.
        // A ScreenSpaceOverlay canvas ignores parent transforms in play mode,
        // but the CI screenshot path re-points canvases at the camera
        // (ScreenSpaceCamera), where inherited 3D scales can affect the UI.
        var canvasGO = new GameObject("ResetButtonCanvas");
        menuCanvasGO = canvasGO;
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90; // below the stage avatar canvas (100); different corner anyway
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // Arrow button: bottom-left, warm-white arrow on the meadow-glass
        // dewdrop disc (v1.0.47: deep green glass + gold hairline ring). The
        // glyph is drawn pointing north-east; v1.0.46 keeps it rotated 180°
        // (SW) while collapsed and flips it to NE on expand — direction = state.
        var arrowGO = new GameObject("MenuArrow", typeof(RectTransform));
        arrowGO.transform.SetParent(canvasGO.transform, false);
        arrowRect = arrowGO.GetComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(0f, 0f);
        arrowRect.anchorMax = new Vector2(0f, 0f);
        arrowRect.pivot = new Vector2(0.5f, 0.5f);
        arrowRect.anchoredPosition = new Vector2(48f + ArrowSize * 0.5f, 48f + ArrowSize * 0.5f);
        arrowRect.sizeDelta = new Vector2(ArrowSize, ArrowSize);

        var discGO = new GameObject("ArrowDisc", typeof(RectTransform), typeof(Image));
        discGO.transform.SetParent(arrowGO.transform, false);
        var discRt = discGO.GetComponent<RectTransform>();
        discRt.anchorMin = new Vector2(0.5f, 0.5f);
        discRt.anchorMax = new Vector2(0.5f, 0.5f);
        discRt.pivot = new Vector2(0.5f, 0.5f);
        discRt.anchoredPosition = Vector2.zero;
        discRt.sizeDelta = new Vector2(ArrowSize, ArrowSize);
        discGO.GetComponent<Image>().sprite = MeadowGlassUI.MakeGlassDisc(160);

        // v1.0.47: the feedback pulse ring, parked invisible until a toggle.
        var pulseGO = new GameObject("PulseRing", typeof(RectTransform), typeof(Image));
        pulseGO.transform.SetParent(arrowGO.transform, false);
        pulseRt = pulseGO.GetComponent<RectTransform>();
        pulseRt.anchorMin = new Vector2(0.5f, 0.5f);
        pulseRt.anchorMax = new Vector2(0.5f, 0.5f);
        pulseRt.pivot = new Vector2(0.5f, 0.5f);
        pulseRt.anchoredPosition = Vector2.zero;
        pulseRt.sizeDelta = new Vector2(150f, 150f);
        pulseImg = pulseGO.GetComponent<Image>();
        pulseImg.sprite = MeadowGlassUI.MakeGoldRing(150, 7);
        pulseImg.color = new Color(1f, 0.78f, 0.22f, 0f);

        var arrowGlyphGO = new GameObject("ArrowGlyph", typeof(RectTransform), typeof(Image));
        arrowGlyphGO.transform.SetParent(arrowGO.transform, false);
        glyphRt = arrowGlyphGO.GetComponent<RectTransform>();
        glyphRt.anchorMin = new Vector2(0.5f, 0.5f);
        glyphRt.anchorMax = new Vector2(0.5f, 0.5f);
        glyphRt.pivot = new Vector2(0.5f, 0.5f);
        glyphRt.anchoredPosition = Vector2.zero;
        glyphRt.sizeDelta = new Vector2(72f, 72f);
        // v1.0.46: start flipped (collapsed = SW) so the first frame is right.
        glyphRt.localRotation = Quaternion.Euler(0f, 0f, 180f);
        var glyphImg = arrowGlyphGO.GetComponent<Image>();
        var arrowTex = DrawArrowTexture();
        glyphImg.sprite = Sprite.Create(arrowTex,
            new Rect(0f, 0f, arrowTex.width, arrowTex.height),
            new Vector2(0.5f, 0.5f), 100f);

        // Sub-buttons, parked at the arrow center at scale 0 (invisible).
        Vector2 arrowCenter = arrowRect.anchoredPosition;
        subButtons = new MenuButton[3];
        // v1.0.46: fan toward the screen center on EVEN spacing — equal 25°
        // steps (20/45/70) on a common 280px radius. Adjacent buttons sit
        // ~122px apart center-to-center, so the 100px discs keep a uniform
        // ~22px gap instead of touching. The sapling leads at 45°.
        float[] angles = { 20f, 45f, 70f };
        float[] dists = { 280f, 280f, 280f };
        for (int i = 0; i < 3; i++)
        {
            var b = new MenuButton();
            var btnGO = new GameObject(i == 0 ? "SubReset" : "SubPlaceholder" + i,
                typeof(RectTransform));
            btnGO.transform.SetParent(canvasGO.transform, false);
            b.rt = btnGO.GetComponent<RectTransform>();
            b.rt.anchorMin = new Vector2(0f, 0f);
            b.rt.anchorMax = new Vector2(0f, 0f);
            b.rt.pivot = new Vector2(0.5f, 0.5f);
            b.homePos = arrowCenter;
            float rad = angles[i] * Mathf.Deg2Rad;
            b.outPos = arrowCenter + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * dists[i];
            b.rt.anchoredPosition = b.homePos;
            b.rt.sizeDelta = new Vector2(SubSize, SubSize);
            b.rt.localScale = new Vector3(0.001f, 0.001f, 1f);

            var bDisc = new GameObject("Disc", typeof(RectTransform), typeof(Image));
            bDisc.transform.SetParent(btnGO.transform, false);
            var bDiscRt = bDisc.GetComponent<RectTransform>();
            bDiscRt.anchorMin = new Vector2(0.5f, 0.5f);
            bDiscRt.anchorMax = new Vector2(0.5f, 0.5f);
            bDiscRt.pivot = new Vector2(0.5f, 0.5f);
            bDiscRt.anchoredPosition = Vector2.zero;
            bDiscRt.sizeDelta = new Vector2(SubSize, SubSize);
            // v1.0.50: the day/night toggle (i==1) is a STAR-shaped button
            // (not a round disc). v1.0.55: the bulb (i==0) is a round disc
            // again; the infinity reset mark is gone.
            bDisc.GetComponent<Image>().sprite = i == 1
                ? MeadowGlassUI.MakeStarDisc(160)
                : MeadowGlassUI.MakeGlassDisc(160);

            var bGlyph = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            bGlyph.transform.SetParent(btnGO.transform, false);
            var bGlyphRt = bGlyph.GetComponent<RectTransform>();
            bGlyphRt.anchorMin = new Vector2(0.5f, 0.5f);
            bGlyphRt.anchorMax = new Vector2(0.5f, 0.5f);
            bGlyphRt.pivot = new Vector2(0.5f, 0.5f);
            bGlyphRt.anchoredPosition = Vector2.zero;
            bGlyphRt.sizeDelta = new Vector2(64f, 64f);
            var bImg = bGlyph.GetComponent<Image>();
            // v1.0.55: button 0 wears the sapling glyph (inventory
            // prototype); button 1 wears the state-aware day/night glyph —
            // its icon is cached and re-rendered on every lighting flip.
            Texture2D glyphTex;
            if (i == 0)
            {
                glyphTex = MeadowGlassUI.MakeSaplingIcon(160);
            }
            else if (i == 1)
            {
                // Seed the state from the slider when it's already built;
                // at menu build time it usually isn't yet (the bootstrap
                // adds the slider after the tree), so this defaults to day
                // and Update() corrects it on the first frame. The CI
                // screenshot path pins the slider to noon = day anyway.
                var o0 = Orbit;
                dayNightIsNight = o0 != null &&
                    SunOrbitControl.NightFactor(o0.CurrentSnappedValue) > 0.5f;
                glyphTex = MeadowGlassUI.MakeDayNightIcon(160, dayNightIsNight);
                dayNightGlyphTex = glyphTex;
            }
            else
            {
                glyphTex = DrawDotsTexture();
            }
            bImg.sprite = Sprite.Create(glyphTex,
                new Rect(0f, 0f, glyphTex.width, glyphTex.height),
                new Vector2(0.5f, 0.5f), 100f);
            if (i == 1) dayNightGlyphImg = bImg;

            subButtons[i] = b;
        }
        BuildInventory();
        Debug.Log($"TreeResetButton: corner menu built (arrow at {arrowCenter}, 3 sub-buttons).");
    }

    // ------------------------------------------------- inventory prototype

    /// <summary>
    /// v1.0.55: builds the inventory prototype overlay — a frosted
    /// full-screen veil (prototype stand-in for a real blur) with a small
    /// centered meadow-glass panel holding six empty rounded slots in a
    /// 3x2 grid. Starts closed.
    /// </summary>
    void BuildInventory()
    {
        if (inventoryRoot != null || menuCanvasGO == null) return;
        inventoryRoot = new GameObject("InventoryOverlay", typeof(RectTransform));
        inventoryRoot.transform.SetParent(menuCanvasGO.transform, false);
        var rootRt = inventoryRoot.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        // v1.0.55: no CanvasGroup — visibility is driven directly (panel
        // scale + frost alpha + frost raycastTarget). Always active.

        var frostGO = new GameObject("Frost", typeof(RectTransform), typeof(Image));
        frostGO.transform.SetParent(inventoryRoot.transform, false);
        var frostRt = frostGO.GetComponent<RectTransform>();
        frostRt.anchorMin = Vector2.zero;
        frostRt.anchorMax = Vector2.one;
        frostRt.offsetMin = Vector2.zero;
        frostRt.offsetMax = Vector2.zero;
        inventoryFrostImg = frostGO.GetComponent<Image>();
        inventoryFrostImg.color = new Color(0.03f, 0.06f, 0.04f, 0f);
        inventoryFrostImg.raycastTarget = false;

        var panelGO = new GameObject("InventoryPanel", typeof(RectTransform), typeof(Image));
        panelGO.transform.SetParent(inventoryRoot.transform, false);
        inventoryPanelRt = panelGO.GetComponent<RectTransform>();
        inventoryPanelRt.anchorMin = new Vector2(0.5f, 0.5f);
        inventoryPanelRt.anchorMax = new Vector2(0.5f, 0.5f);
        inventoryPanelRt.pivot = new Vector2(0.5f, 0.5f);
        inventoryPanelRt.anchoredPosition = Vector2.zero;
        inventoryPanelRt.sizeDelta = new Vector2(680f, 620f);
        var panelImg = panelGO.GetComponent<Image>();
        // v1.0.55 diagnostic: plain color (no sprite) to isolate render issue.
        panelImg.color = new Color(0.9f, 0.2f, 0.2f, 0.9f);

        // Six empty slots, 3 columns x 2 rows.
        var slotSprite = MeadowGlassUI.MakeRoundedSquare(160, 30f);
        for (int r = 0; r < 2; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                var slotGO = new GameObject($"Slot_{r}_{c}",
                    typeof(RectTransform), typeof(Image));
                slotGO.transform.SetParent(panelGO.transform, false);
                var srt = slotGO.GetComponent<RectTransform>();
                srt.anchorMin = new Vector2(0.5f, 0.5f);
                srt.anchorMax = new Vector2(0.5f, 0.5f);
                srt.pivot = new Vector2(0.5f, 0.5f);
                srt.anchoredPosition = new Vector2((c - 1) * 190f, (0.5f - r) * 190f);
                srt.sizeDelta = new Vector2(150f, 150f);
                var simg = slotGO.GetComponent<Image>();
                simg.sprite = slotSprite;
                simg.preserveAspect = true;
            }
        }

        inventoryRoot.SetActive(true);
        // v1.0.55: parked hidden via scale-0 + frost alpha-0 (never
        // deactivated — the sub-buttons prove always-active UI renders
        // reliably).
        inventoryPanelRt.localScale = new Vector3(0.001f, 0.001f, 1f);
        Debug.Log("TreeResetButton: inventory prototype built (6 empty slots).");
    }

    /// <summary>
    /// v1.0.55: opens/closes the inventory prototype with a pop. While open
    /// (or animating), MenuHitTest swallows every tap so the tree never
    /// grows under the overlay.
    /// </summary>
    void SetInventoryOpen(bool open)
    {
        if (inventoryRoot == null) BuildInventory();
        if (inventoryRoot == null) return;
        if (open)
        {
            if (InventoryOpen && inventoryDir == 0f) return;
            InventoryOpen = true;
            if (inventoryFrostImg != null) inventoryFrostImg.raycastTarget = true;
            inventoryPanelRt.localScale = new Vector3(0.001f, 0.001f, 1f);
            inventoryT = 0f;
            inventoryDir = 1f;
        }
        else
        {
            if (!InventoryOpen && inventoryDir == 0f) return;
            InventoryOpen = false;
            inventoryDir = -1f;
        }
    }

    /// <summary>
    /// v1.0.55: CI screenshot helper — snaps the inventory prototype open
    /// instantly (edit mode runs no Update, so the pop animation never plays).
    /// </summary>
    public void SnapInventoryOpenForScreenshot()
    {
        if (!initialized) Initialize();
        if (inventoryRoot == null) BuildInventory();
        if (inventoryRoot == null) return;
        InventoryOpen = true;
        inventoryT = 1f;
        inventoryDir = 0f;
        inventoryPanelRt.localScale = Vector3.one;
        if (inventoryFrostImg != null)
        {
            var fc = inventoryFrostImg.color;
            fc.a = 0.55f;
            inventoryFrostImg.color = fc;
            inventoryFrostImg.raycastTarget = true;
        }
        // v1.0.55 diagnostics: prove the overlay is really live for the capture.
        var prt = inventoryPanelRt;
        var pimg = prt.GetComponent<Image>();
        Debug.Log($"TreeResetButton: inventory snap — root active={inventoryRoot.activeSelf}, " +
            $"hierarchy={inventoryRoot.activeInHierarchy}, " +
            $"panelScale={prt.localScale}, panelRect={prt.rect}, " +
            $"panelChildren={prt.childCount}, panelSprite={(pimg != null && pimg.sprite != null)}, " +
            $"frostAlpha={(inventoryFrostImg != null ? inventoryFrostImg.color.a : -1f)}, " +
            $"canvas={menuCanvasGO != null}");
    }

    /// <summary>
    /// Warm-white arrow pointing north-east (toward the screen center),
    /// drawn directly in texture space: thick shaft + triangular head.
    /// v1.0.47: warm white so it reads on the dark meadow-glass disc.
    /// </summary>
    static Texture2D DrawArrowTexture()
    {
        const int S = 160;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var ink = new Color(0.96f, 0.94f, 0.86f, 1f);
        // Shaft: from (44,44) to (100,100), half-width 10. Head: triangle
        // with tip (124,124), base corners (86,110) and (110,86).
        Vector2 p0 = new Vector2(44f, 44f), p1 = new Vector2(100f, 100f);
        Vector2 hTip = new Vector2(124f, 124f);
        Vector2 hA = new Vector2(86f, 110f), hB = new Vector2(110f, 86f);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float dShaft = SegmentDistance(p, p0, p1);
                bool inHead = PointInTriangle(p, hTip, hA, hB);
                float a = 0f;
                if (inHead) a = 1f;
                else a = Mathf.Clamp01((10f - dShaft) / 2f);
                tex.SetPixel(x, y, new Color(ink.r, ink.g, ink.b, a));
            }
        }
        tex.Apply();
        return tex;
    }

    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return Vector2.Distance(p, a + ab * t);
    }

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
        bool neg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
        bool pos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);
        return !(neg && pos);
    }

    static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    /// <summary>Three white dots (placeholder glyph — "more to come").</summary>
    static Texture2D DrawDotsTexture()
    {
        const int S = 160;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = 0f;
                for (int i = 0; i < 3; i++)
                {
                    Vector2 dc = new Vector2(44f + i * 36f, 80f);
                    float d = Vector2.Distance(p, dc);
                    a = Mathf.Max(a, Mathf.Clamp01((15f - d) / 2.5f));
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }

    // (MakeDiscSprite removed in v1.0.47: discs now come from
    // MeadowGlassUI.MakeGlassDisc, the shared meadow-glass material.)
}
