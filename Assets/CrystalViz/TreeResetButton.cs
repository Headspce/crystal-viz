using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Test-loop reset button: a small glowing infinity mark pinned to the
/// bottom-left of the screen. Pressing it calls
/// TreeGrowthController.ResetToSprout(), which drops the tree back to the
/// true zero-tap sprout with the tap counter at zero, so the full
/// sprout -> mature growth can be re-tapped over and over during playtesting.
///
/// Raw Input is used (not uGUI Button + EventSystem) to match the tree's
/// tap detection, which also reads Input directly: a press inside the button
/// rect resets, and TreeGrowthController swallows that same press so it never
/// also counts as a growth tap.
/// </summary>
public class TreeResetButton : MonoBehaviour
{
    public TreeGrowthController controller;

    RectTransform buttonRect;
    bool initialized;

    void Awake()
    {
        Initialize();
    }

    /// <summary>
    /// Builds the button and hands its rect to the controller so growth taps
    /// landing on it are ignored. Called from Awake() in play mode; the CI
    /// screenshot path builds the scene in edit mode, where AddComponent
    /// does NOT fire Awake(), so CrystalVizBootstrap calls this explicitly
    /// after AddComponent. Idempotent: safe to call twice.
    /// </summary>
    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        controller = GetComponent<TreeGrowthController>();
        BuildUI();
        if (controller != null) controller.resetButtonRect = buttonRect;
    }

    void Update()
    {
        if (buttonRect == null || controller == null) return;
        bool pressed = Input.GetMouseButtonDown(0);
        Vector2 pos = Input.mousePosition;
        if (!pressed && Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            pressed = true;
            pos = Input.GetTouch(0).position;
        }
        // Null camera is correct here: the button lives on a
        // ScreenSpaceOverlay canvas.
        if (pressed && RectTransformUtility.RectangleContainsScreenPoint(buttonRect, pos, null))
            controller.ResetToSprout();
    }

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

        var btnGO = new GameObject("ResetButton", typeof(RectTransform));
        btnGO.transform.SetParent(canvasGO.transform, false);
        buttonRect = btnGO.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0f, 0f);
        buttonRect.anchorMax = new Vector2(0f, 0f);
        buttonRect.pivot = new Vector2(0f, 0f);
        buttonRect.anchoredPosition = new Vector2(48f, 48f);
        buttonRect.sizeDelta = new Vector2(96f, 96f);
        // Color scheme: the glowing infinity mark at 75% opacity (25% lighter
        // than before) floating on a soft dark-navy disc, so it reads cleanly
        // against both the bright grass and the dark soil. No box, no border.
        var discGO = new GameObject("ResetDisc", typeof(RectTransform));
        discGO.transform.SetParent(btnGO.transform, false);
        var discRt = discGO.GetComponent<RectTransform>();
        discRt.anchorMin = new Vector2(0.5f, 0.5f);
        discRt.anchorMax = new Vector2(0.5f, 0.5f);
        discRt.pivot = new Vector2(0.5f, 0.5f);
        discRt.anchoredPosition = Vector2.zero;
        discRt.sizeDelta = new Vector2(96f, 96f);
        var discImg = discGO.AddComponent<Image>();
        discImg.sprite = MakeDiscSprite(128);
        discImg.color = new Color(0.06f, 0.11f, 0.24f, 0.55f);

        var markGO = new GameObject("InfinityMark", typeof(RectTransform));
        markGO.transform.SetParent(btnGO.transform, false);
        var markRt = markGO.GetComponent<RectTransform>();
        markRt.anchorMin = new Vector2(0.5f, 0.5f);
        markRt.anchorMax = new Vector2(0.5f, 0.5f);
        markRt.pivot = new Vector2(0.5f, 0.5f);
        markRt.anchoredPosition = Vector2.zero;
        markRt.sizeDelta = new Vector2(75f, 75f);
        // Glowing infinity mark on transparency: no box, no border — just the
        // symbol. Loaded from Resources and turned into a sprite at runtime
        // (same proven pattern as the stage avatars), so texture import
        // settings can't break it.
        var img = markGO.AddComponent<Image>();
        var infTex = Resources.Load<Texture2D>("infinity-reset");
        if (infTex != null)
        {
            img.sprite = Sprite.Create(infTex,
                new Rect(0f, 0f, infTex.width, infTex.height),
                new Vector2(0.5f, 0.5f), 100f);
            img.color = new Color(1f, 1f, 1f, 0.75f); // 25% lighter
        }
        else
        {
            Debug.LogWarning("TreeResetButton: 'infinity-reset' texture not found in Resources; falling back to plain mark.");
            var whiteTex = Texture2D.whiteTexture;
            img.sprite = Sprite.Create(whiteTex, new Rect(0f, 0f, whiteTex.width, whiteTex.height),
                new Vector2(0.5f, 0.5f));
            img.color = new Color(1f, 1f, 1f, 0.35f);
        }
        Debug.Log($"TreeResetButton: built {buttonRect.rect} on {canvasGO.name}.");
    }

    /// <summary>Generates a soft-edged filled disc sprite for the reset backing.</summary>
    static Sprite MakeDiscSprite(int size)
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
                float a = Mathf.Clamp01((r - d) / 2f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
