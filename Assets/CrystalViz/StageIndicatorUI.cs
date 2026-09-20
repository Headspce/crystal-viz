using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Circular stage avatar pinned to the top-center of the screen (profile-pic
/// style): a thin white ring border, the current stage sprite clipped to a
/// circle inside it, and a "12 / 50" tap counter underneath.
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
    RectTransform popRect;
    Coroutine popRoutine;

    bool initialized;

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
        Refresh(controller.CurrentStage);
    }

    void Update()
    {
        if (controller != null && tapText != null)
            tapText.text = $"{controller.currentTaps} / {controller.totalTaps}";
    }

    void OnStageChanged(int stage)
    {
        Refresh(stage);
        if (popRoutine != null) StopCoroutine(popRoutine);
        popRoutine = StartCoroutine(PopRoutine());
    }

    void Refresh(int stage)
    {
        if (stageImage != null && stage >= 0 && stage < stageSprites.Length
            && stageSprites[stage] != null)
        {
            stageImage.sprite = stageSprites[stage];
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
        // In the CI screenshot path the safe area is the full game view, so
        // this is a no-op there and captures stay pixel-identical.
        float topInsetRef = 0f;
        Rect safe = Screen.safeArea;
        if (Screen.height > 0f && safe.yMax < Screen.height)
            topInsetRef = (Screen.height - safe.yMax) / Screen.height * 1920f;
        rootRt.anchoredPosition = new Vector2(0f, -36f - topInsetRef);
        rootRt.sizeDelta = new Vector2(240f, 280f);

        Sprite circle = MakeCircleSprite(256);

        // Avatar wrapper (this is what pops on stage change).
        var avatar = NewRect("Avatar", root.transform);
        var avatarRt = avatar.GetComponent<RectTransform>();
        avatarRt.anchorMin = new Vector2(0.5f, 1f);
        avatarRt.anchorMax = new Vector2(0.5f, 1f);
        avatarRt.pivot = new Vector2(0.5f, 1f);
        avatarRt.anchoredPosition = Vector2.zero;
        avatarRt.sizeDelta = new Vector2(162f, 162f);
        popRect = avatarRt;

        // White ring border.
        var border = NewRect("Border", avatar.transform);
        Stretch(border.GetComponent<RectTransform>());
        var borderImg = border.AddComponent<Image>();
        borderImg.sprite = circle;
        borderImg.color = Color.white;

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

        // Tap counter under the avatar.
        var textGO = NewRect("TapCounter", root.transform);
        var textRt = textGO.GetComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0.5f, 1f);
        textRt.anchorMax = new Vector2(0.5f, 1f);
        textRt.pivot = new Vector2(0.5f, 1f);
        textRt.anchoredPosition = new Vector2(0f, -186f);
        textRt.sizeDelta = new Vector2(240f, 60f);
        tapText = textGO.AddComponent<Text>();
        tapText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (tapText.font == null)
            Debug.LogWarning("StageIndicatorUI: built-in LegacyRuntime font not found; tap counter may not render.");
        tapText.fontSize = 40;
        tapText.alignment = TextAnchor.MiddleCenter;
        tapText.color = Color.white;
        var shadow = textGO.AddComponent<Shadow>();
        shadow.effectDistance = new Vector2(2f, -2f);
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
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
}
