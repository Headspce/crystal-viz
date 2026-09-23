using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Circular stage avatar pinned to the top-center of the screen (profile-pic
/// style): a thin sky-blue ring border, the current stage sprite clipped to a
/// circle inside it, and a "12 / 50" tap counter on a dark pill underneath.
///
/// Stage sprites are loaded as Texture2D from Resources/StageIcons and turned
/// into Sprites at runtime, so any texture import type works. Subscribes to
/// TreeGrowthController.onStageChanged for the swap + scale-pop transition.
/// </summary>
public class StageIndicatorUI : MonoBehaviour
{
    public TreeGrowthController controller;

    static readonly string[] StagePaths =
    {
        "StageIcons/stage-sprout",
        "StageIcons/stage-sapling",
        "StageIcons/stage-youngtree",
        "StageIcons/stage-maturetree",
    };

    readonly Sprite[] stageSprites = new Sprite[4];
    Image stageImage;
    Text tapText;
    Text beeText;
    Image beeIconImg;
    BeeController beeController;
    int lastBeeCount = -1;
    RectTransform popRect;
    RectTransform beePillRect;
    Coroutine popRoutine;
    Coroutine rejectRoutine;

    bool initialized;

    /// <summary>
    /// Test hook for CI: when set, BuildUI uses this rect instead of
    /// Screen.safeArea, letting the screenshot path simulate a phone camera
    /// cutout (Screen.safeArea is full-bleed in CI, so the inset would
    /// otherwise never be exercised). Public (not internal) so the
    /// Editor-assembly screenshot tool can set it. Never set in player
    /// builds.
    /// </summary>
    public static Rect? TestSafeAreaOverride = null;

    void Awake()
    {
        Initialize();
    }

    /// <summary>
    /// Loads stage sprites, builds the overlay canvas, and sets the initial
    /// avatar + tap counter. Called from Awake() in play mode; the CI
    /// screenshot path builds the scene in edit mode, where AddComponent does
    /// NOT fire Awake(), so CrystalVizBootstrap calls this explicitly after
    /// AddComponent. Idempotent: safe to call twice.
    /// </summary>
    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        controller = GetComponent<TreeGrowthController>();
        beeController = FindObjectOfType<BeeController>();
        for (int i = 0; i < StagePaths.Length; i++)
        {
            var tex = Resources.Load<Texture2D>(StagePaths[i]);
            if (tex != null)
            {
                stageSprites[i] = Sprite.Create(
                    tex, new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), 100f);
            }
            else
            {
                Debug.LogWarning($"StageIndicatorUI: texture '{StagePaths[i]}' not found in Resources.");
            }
        }
        BuildUI();
        // Start()/Update() never run in edit-mode screenshot captures, so set
        // the initial sprite and counter text here instead of waiting for them.
        if (controller != null) Refresh(controller.CurrentStage);
    }

    void Start()
    {
        if (controller == null)
        {
            Debug.LogError("StageIndicatorUI: no TreeGrowthController on this GameObject.");
            return;
        }
        controller.onStageChanged.AddListener(OnStageChanged);
        controller.onTapRejected.AddListener(OnTapRejected);
        Refresh(controller.CurrentStage);
    }

    void Update()
    {
        if (controller != null && tapText != null)
        {
            // v1.0.37: show banked bee-pop tap credits next to progress, e.g.
            // "12 / 50 (+3)". No suffix when there is nothing banked.
            int credits = controller.tapCredits;
            tapText.text = credits > 0
                ? $"{controller.currentTaps} / {controller.totalTaps} (+{credits})"
                : $"{controller.currentTaps} / {controller.totalTaps}";
        }
        // Live-bee counter: 3 -> 0 as bees pop, refilling on respawn.
        // Cached so the text (and icon dim) only updates on change.
        // Lazy-resolve: BuildDiorama() builds the tree (and this UI) BEFORE
        // the BeeController GameObject exists, so the Initialize()-time
        // lookup always missed and the counter was stuck. Resolving here
        // self-heals regardless of build order.
        if (beeController == null) beeController = FindObjectOfType<BeeController>();
        if (beeController != null && beeText != null)
        {
            int n = beeController.FlyingBeeCount;
            if (n != lastBeeCount)
            {
                lastBeeCount = n;
                beeText.text = n.ToString();
                LayoutBeeIcon(n);
                if (beeIconImg != null)
                    beeIconImg.color = n > 0
                        ? Color.white
                        : new Color(0.45f, 0.45f, 0.45f, 0.55f);
            }
        }
    }

    /// <summary>
    /// v1.0.39: hug the bee icon to the number — a single digit pulls the bee
    /// in tight, double digits push it back out to make room for the second
    /// digit.
    /// </summary>
    void LayoutBeeIcon(int n)
    {
        if (beeIconImg == null) return;
        var rt = beeIconImg.rectTransform;
        var p = rt.anchoredPosition;
        p.x = n < 10 ? 22f : 6f;
        rt.anchoredPosition = p;
    }

    void OnStageChanged(int stage)
    {
        Refresh(stage);
        if (popRoutine != null) StopCoroutine(popRoutine);
        popRoutine = StartCoroutine(PopRoutine());
    }

    /// <summary>
    /// A tree tap with no banked bee-pop credits: punch the bee pill so the
    /// player sees where to go earn taps.
    /// </summary>
    void OnTapRejected()
    {
        if (rejectRoutine != null) StopCoroutine(rejectRoutine);
        rejectRoutine = StartCoroutine(RejectPunch());
    }

    IEnumerator RejectPunch()
    {
        const float dur = 0.28f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float s = 1f + 0.16f * Mathf.Sin(Mathf.PI * k);
            if (beePillRect != null) beePillRect.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        if (beePillRect != null) beePillRect.localScale = Vector3.one;
    }

    void Refresh(int stage)
    {
        if (stageImage != null && stage >= 0 && stage < stageSprites.Length
            && stageSprites[stage] != null)
        {
            stageImage.sprite = stageSprites[stage];
            // Stage-specific scaling: the sapling artwork is very tall and
            // narrow; scale it down to 72% so it sits comfortably inside the
            // circular indicator without touching the ring. Other stages
            // fill the circle naturally.
            float scale = (stage == 1) ? 0.72f : 1.0f;
            stageImage.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
    }

    IEnumerator PopRoutine()
    {
        const float dur = 0.35f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float s = 1f + 0.28f * Mathf.Sin(Mathf.PI * k);
            if (popRect != null) popRect.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        if (popRect != null) popRect.localScale = Vector3.one;
    }

    // ------------------------------------------------------------------ UI build

    void BuildUI()
    {
        var canvasGO = new GameObject("StageCanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // Root: top-center of the screen.
        var root = NewRect("StageRoot", canvasGO.transform);
        var rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 1f);
        rootRt.anchorMax = new Vector2(0.5f, 1f);
        rootRt.pivot = new Vector2(0.5f, 1f);
        // Camera-cutout clearance: start the indicator below Screen.safeArea
        // so it never hides under a notch or punch-hole camera (Tyler's
        // Motorola has a top-center cutout; other phones vary). The inset is
        // converted to reference units for the CanvasScaler (1080x1920).
        // TestSafeAreaOverride lets CI simulate a cutout; in the CI
        // screenshot path the real safe area is the full game view, so
        // without the override this is a no-op and captures stay
        // pixel-identical.
        float topInsetRef = 0f;
        Rect safe = TestSafeAreaOverride ?? Screen.safeArea;
        if (Screen.height > 0f && safe.yMax < Screen.height)
            topInsetRef = (Screen.height - safe.yMax) / Screen.height * 1920f;
        rootRt.anchoredPosition = new Vector2(0f, -36f - topInsetRef);
        rootRt.sizeDelta = new Vector2(400f, 280f);

        Sprite circle = MakeCircleSprite(256);

        // Black outer outline ring around the whole avatar (player request).
        var outlineGO = NewRect("AvatarOutline", root.transform);
        var outlineRt = outlineGO.GetComponent<RectTransform>();
        outlineRt.anchorMin = new Vector2(0.5f, 1f);
        outlineRt.anchorMax = new Vector2(0.5f, 1f);
        outlineRt.pivot = new Vector2(0.5f, 1f);
        outlineRt.anchoredPosition = Vector2.zero;
        outlineRt.sizeDelta = new Vector2(172f, 172f);
        var outlineImg = outlineGO.AddComponent<Image>();
        outlineImg.sprite = circle;
        outlineImg.color = new Color(0f, 0f, 0f, 1f);

        // Avatar wrapper (this is what pops on stage change).
        var avatar = NewRect("Avatar", root.transform);
        var avatarRt = avatar.GetComponent<RectTransform>();
        avatarRt.anchorMin = new Vector2(0.5f, 1f);
        avatarRt.anchorMax = new Vector2(0.5f, 1f);
        avatarRt.pivot = new Vector2(0.5f, 1f);
        avatarRt.anchoredPosition = Vector2.zero;
        avatarRt.sizeDelta = new Vector2(162f, 162f);
        popRect = avatarRt;

        // Sky-blue ring border: Vintage Meadow palette (#5CA8FF).
        var border = NewRect("Border", avatar.transform);
        Stretch(border.GetComponent<RectTransform>());
        var borderImg = border.AddComponent<Image>();
        borderImg.sprite = circle;
        // Sky-blue ring, ~62% opaque so the sky shows through a little
        // (player request), with the black outline ring sitting just outside
        // it for definition.
        borderImg.color = new Color(0.361f, 0.659f, 1.0f, 0.62f);

        // Circular mask holding the stage sprite.
        var maskGO = NewRect("CircleMask", avatar.transform);
        var maskRt = maskGO.GetComponent<RectTransform>();
        maskRt.anchorMin = new Vector2(0.5f, 0.5f);
        maskRt.anchorMax = new Vector2(0.5f, 0.5f);
        maskRt.pivot = new Vector2(0.5f, 0.5f);
        maskRt.anchoredPosition = Vector2.zero;
        maskRt.sizeDelta = new Vector2(150f, 150f);
        var maskImg = maskGO.AddComponent<Image>();
        maskImg.sprite = circle;
        maskImg.color = Color.white;
        var mask = maskGO.AddComponent<Mask>();
        mask.showMaskGraphic = false; // ring only; the mask graphic itself stays invisible

        // Stage sprite, clipped to the circle.
        var stageGO = NewRect("StageImage", maskGO.transform);
        Stretch(stageGO.GetComponent<RectTransform>());
        stageImage = stageGO.AddComponent<Image>();
        stageImage.preserveAspect = true;

        // HUD row under the avatar: live-bee counter pill on the left, tap
        // counter pill on the right. The bee pill shows the game's bee sprite
        // plus how many bees are currently flying unpopped (3 -> 0 as they
        // pop, refilling on respawn). The tap pill keeps the "12 / 50"
        // fractional readout — it shows progress toward the goal at a glance,
        // where a bare number would make the player remember the target.
        var rowGO = NewRect("HudRow", root.transform);
        var rowRt = rowGO.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0.5f, 1f);
        rowRt.anchorMax = new Vector2(0.5f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, -186f);
        rowRt.sizeDelta = new Vector2(448f, 60f);

        Sprite pill = MakePillSprite(256, 72);
        Font hudFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (hudFont == null)
            Debug.LogWarning("StageIndicatorUI: built-in LegacyRuntime font not found; HUD counters may not render.");

        // Bee counter pill (left).
        var beePillGO = NewRect("BeePill", rowGO.transform);
        var beePillRt = beePillGO.GetComponent<RectTransform>();
        beePillRt.anchorMin = new Vector2(0f, 0.5f);
        beePillRt.anchorMax = new Vector2(0f, 0.5f);
        beePillRt.pivot = new Vector2(0f, 0.5f);
        beePillRt.anchoredPosition = new Vector2(0f, 0f);
        beePillRt.sizeDelta = new Vector2(168f, 58f);
        beePillRect = beePillRt;
        var beePillImg = beePillGO.AddComponent<Image>();
        beePillImg.sprite = pill;
        beePillImg.color = new Color(0.05f, 0.10f, 0.22f, 0.55f);

        var iconGO = NewRect("BeeIcon", beePillGO.transform);
        var iconRt = iconGO.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0f, 0.5f);
        iconRt.anchorMax = new Vector2(0f, 0.5f);
        iconRt.pivot = new Vector2(0f, 0.5f);
        // Seed tight: the pill opens showing "3" (single digit), and
        // Update() calls LayoutBeeIcon() on every count change after that.
        iconRt.anchoredPosition = new Vector2(22f, 0f);
        iconRt.sizeDelta = new Vector2(50f, 50f);
        beeIconImg = iconGO.AddComponent<Image>();
        var beeTex = BeeController.BeeIconTexture;
        if (beeTex != null)
        {
            beeIconImg.sprite = Sprite.Create(beeTex,
                new Rect(0f, 0f, beeTex.width, beeTex.height),
                new Vector2(0.5f, 0.5f), 100f);
            beeIconImg.preserveAspect = true;
        }

        var beeTextGO = NewRect("BeeCounter", beePillGO.transform);
        var beeTextRt = beeTextGO.GetComponent<RectTransform>();
        beeTextRt.anchorMin = new Vector2(0f, 0.5f);
        beeTextRt.anchorMax = new Vector2(1f, 0.5f);
        beeTextRt.pivot = new Vector2(0.5f, 0.5f);
        beeTextRt.anchoredPosition = new Vector2(28f, 0f);
        beeTextRt.sizeDelta = new Vector2(112f, 58f);
        beeText = beeTextGO.AddComponent<Text>();
        beeText.font = hudFont;
        beeText.fontSize = 40;
        beeText.alignment = TextAnchor.MiddleCenter;
        beeText.color = Color.white;
        var beeOutline = beeTextGO.AddComponent<Outline>();
        beeOutline.effectColor = new Color(0.04f, 0.08f, 0.18f, 0.95f);
        beeOutline.effectDistance = new Vector2(2.5f, -2.5f);
        beeText.text = "3";

        // Tap counter pill (right): white text on a dark translucent
        // pill so the numbers stay readable against the bright sky, plus a
        // navy outline for extra bite.
        var pillGO = NewRect("CounterPill", rowGO.transform);
        var pillRt = pillGO.GetComponent<RectTransform>();
        pillRt.anchorMin = new Vector2(1f, 0.5f);
        pillRt.anchorMax = new Vector2(1f, 0.5f);
        pillRt.pivot = new Vector2(1f, 0.5f);
        pillRt.anchoredPosition = new Vector2(0f, 0f);
        pillRt.sizeDelta = new Vector2(264f, 58f);
        var pillImg = pillGO.AddComponent<Image>();
        pillImg.sprite = pill;
        pillImg.color = new Color(0.05f, 0.10f, 0.22f, 0.55f);

        var textGO = NewRect("TapCounter", pillGO.transform);
        var textRt = textGO.GetComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0f, 0.5f);
        textRt.anchorMax = new Vector2(1f, 0.5f);
        textRt.pivot = new Vector2(0.5f, 0.5f);
        textRt.anchoredPosition = Vector2.zero;
        textRt.sizeDelta = new Vector2(264f, 58f);
        tapText = textGO.AddComponent<Text>();
        tapText.font = hudFont;
        tapText.fontSize = 36;
        tapText.alignment = TextAnchor.MiddleCenter;
        tapText.color = Color.white;
        var outline = textGO.AddComponent<Outline>();
        outline.effectColor = new Color(0.04f, 0.08f, 0.18f, 0.95f);
        outline.effectDistance = new Vector2(2.5f, -2.5f);
        // Update() never runs in edit-mode screenshot captures, so seed the
        // counter text now; Update() keeps it fresh in play mode.
        if (controller != null)
            tapText.text = $"{controller.currentTaps} / {controller.totalTaps}";
    }

    static GameObject NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>Generates a soft-edged white circle sprite for the ring + mask.</summary>
    static Sprite MakeCircleSprite(int size)
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
                float a = Mathf.Clamp01((r - d) / 1.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>Generates a soft-edged rounded-rect (pill) sprite for the tap-counter backing.</summary>
    static Sprite MakePillSprite(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color[w * h];
        float r = h / 2f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Distance to the pill's inner segment (horizontal capsule).
                float cx = Mathf.Clamp(x + 0.5f, r, w - r);
                float dx = (x + 0.5f) - cx;
                float dy = (y + 0.5f) - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((r - d) / 1.5f);
                pixels[y * w + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
    }
}
