using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-left corner menu (v1.0.45, player request): a small BLACK arrow on
/// a BLUE disc (the top stage-avatar blue, #5CA8FF). The arrow points
/// diagonally toward the screen center (north-east). Tapping it slides three
/// buttons out from behind it toward the center — the infinity reset button
/// (keeps its reset function: ResetToSprout) plus two placeholder buttons
/// (star, dots — no function yet) — and tapping it again collapses everything
/// back. The slide is staggered with an overshoot ease.
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

    // The three slide-out buttons: 0 = infinity (reset), 1-2 = placeholders.
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

    static readonly Color MenuBlue = new Color(0.361f, 0.659f, 1.0f, 0.85f); // #5CA8FF, top-button blue

    const float ArrowSize = 120f;
    const float SubSize = 100f;
    const float SlideDur = 0.32f;
    const float Stagger = 0.07f;

    RectTransform arrowRect;
    MenuButton[] subButtons;
    bool expanded;
    bool initialized;
    float arrowPunch;

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
            // Null camera is correct: the menu lives on a ScreenSpaceOverlay canvas.
            if (arrowRect != null &&
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
                            if (controller != null) controller.ResetToSprout();
                            ToggleMenu(); // reset done: collapse
                        }
                        else
                        {
                            // Placeholders: acknowledge the tap, no function yet.
                            b.punch = 1f;
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
    }

    void ToggleMenu()
    {
        expanded = !expanded;
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
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90; // below the stage avatar canvas (100); different corner anyway
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // Arrow button: bottom-left, black arrow on the blue disc, pointing
        // north-east (toward the screen center).
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
        discGO.GetComponent<Image>().sprite = MakeDiscSprite(160, MenuBlue, Color.black, 5);

        var arrowGlyphGO = new GameObject("ArrowGlyph", typeof(RectTransform), typeof(Image));
        arrowGlyphGO.transform.SetParent(arrowGO.transform, false);
        var glyphRt = arrowGlyphGO.GetComponent<RectTransform>();
        glyphRt.anchorMin = new Vector2(0.5f, 0.5f);
        glyphRt.anchorMax = new Vector2(0.5f, 0.5f);
        glyphRt.pivot = new Vector2(0.5f, 0.5f);
        glyphRt.anchoredPosition = Vector2.zero;
        glyphRt.sizeDelta = new Vector2(72f, 72f);
        var glyphImg = arrowGlyphGO.GetComponent<Image>();
        var arrowTex = DrawArrowTexture();
        glyphImg.sprite = Sprite.Create(arrowTex,
            new Rect(0f, 0f, arrowTex.width, arrowTex.height),
            new Vector2(0.5f, 0.5f), 100f);

        // Sub-buttons, parked at the arrow center at scale 0 (invisible).
        Vector2 arrowCenter = arrowRect.anchoredPosition;
        subButtons = new MenuButton[3];
        // Fan toward the screen center: infinity leads at 45 deg, the two
        // placeholders flank it.
        float[] angles = { 45f, 22f, 68f };
        float[] dists = { 200f, 195f, 195f };
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
            bDisc.GetComponent<Image>().sprite = MakeDiscSprite(160, MenuBlue, Color.black, 5);

            var bGlyph = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            bGlyph.transform.SetParent(btnGO.transform, false);
            var bGlyphRt = bGlyph.GetComponent<RectTransform>();
            bGlyphRt.anchorMin = new Vector2(0.5f, 0.5f);
            bGlyphRt.anchorMax = new Vector2(0.5f, 0.5f);
            bGlyphRt.pivot = new Vector2(0.5f, 0.5f);
            bGlyphRt.anchoredPosition = Vector2.zero;
            bGlyphRt.sizeDelta = new Vector2(64f, 64f);
            var bImg = bGlyph.GetComponent<Image>();
            Texture2D glyphTex = i == 0 ? DrawInfinityTexture()
                : i == 1 ? DrawStarTexture() : DrawDotsTexture();
            bImg.sprite = Sprite.Create(glyphTex,
                new Rect(0f, 0f, glyphTex.width, glyphTex.height),
                new Vector2(0.5f, 0.5f), 100f);
            if (i == 0) bImg.color = new Color(1f, 1f, 1f, 0.75f); // infinity: 25% lighter, as before

            subButtons[i] = b;
        }
        Debug.Log($"TreeResetButton: corner menu built (arrow at {arrowCenter}, 3 sub-buttons).");
    }

    /// <summary>
    /// Black arrow pointing north-east (toward the screen center), drawn
    /// directly in texture space: thick shaft + triangular head.
    /// </summary>
    static Texture2D DrawArrowTexture()
    {
        const int S = 160;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var black = Color.black;
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
                tex.SetPixel(x, y, new Color(black.r, black.g, black.b, a));
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

    /// <summary>White infinity mark on transparency (the reset glyph, as before).</summary>
    static Texture2D DrawInfinityTexture()
    {
        const int S = 160;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        // Lemniscate of Bernoulli, scaled into the canvas.
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2((x + 0.5f - S / 2f) / (S * 0.32f),
                                        (y + 0.5f - S / 2f) / (S * 0.32f));
                // Distance to the lemniscate curve, approximated by sampling.
                float best = float.MaxValue;
                for (int s = 0; s <= 64; s++)
                {
                    float t = s / 64f * Mathf.PI * 2f;
                    float denom = 1f + Mathf.Sin(t) * Mathf.Sin(t);
                    Vector2 q = new Vector2(Mathf.Cos(t) / denom, Mathf.Sin(t) * Mathf.Cos(t) / denom);
                    float d = Vector2.Distance(p, q);
                    if (d < best) best = d;
                }
                float a = Mathf.Clamp01((0.16f - best) / 0.06f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>Golden 5-point star (placeholder glyph 1).</summary>
    static Texture2D DrawStarTexture()
    {
        const int S = 160;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Vector2 c = new Vector2(S / 2f, S / 2f);
        const int spikes = 5;
        var pts = new Vector2[spikes * 2];
        for (int i = 0; i < spikes * 2; i++)
        {
            float r = (i % 2 == 0) ? 62f : 27f;
            float a = (i / (float)(spikes * 2)) * Mathf.PI * 2f - Mathf.PI / 2f;
            pts[i] = c + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }
        var outline = new Color(0.23f, 0.10f, 0.02f);
        var gold = new Color(1.00f, 0.80f, 0.16f);
        var big = new Vector2[spikes * 2]; // outline star: slightly larger
        for (int i = 0; i < big.Length; i++)
            big[i] = c + (pts[i] - c) * 1.14f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                bool inside = PointInPolygon(p, pts);
                bool inOutline = PointInPolygon(p, big);
                Color col;
                if (inside) col = gold;
                else if (inOutline) col = outline;
                else col = new Color(0f, 0f, 0f, 0f);
                if (col.a > 0f && !inside)
                    col.a *= 0.5f; // feather the outer edge
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        return tex;
    }

    static bool PointInPolygon(Vector2 p, Vector2[] pts)
    {
        bool inside = false;
        int n = pts.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector2 pi = pts[i], pj = pts[j];
            if (((pi.y > p.y) != (pj.y > p.y)) &&
                (p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y) + pi.x))
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Three white dots (placeholder glyph 2 — "more to come").</summary>
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

    /// <summary>
    /// Soft-edged filled disc sprite with a crisp outer border ring (colors
    /// baked in so the Image stays plain white). Same recipe as before.
    /// </summary>
    static Sprite MakeDiscSprite(int size, Color innerColor, Color borderColor, float borderPx)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        float r = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - r + 0.5f;
                float dy = y - r + 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float outer = Mathf.Clamp01((r - d) / 2f);
                float border = Mathf.Clamp01((r - borderPx - d) / 1.5f);
                Color c = Color.Lerp(innerColor, borderColor, 1f - border);
                c.a *= outer;
                pixels[y * size + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
