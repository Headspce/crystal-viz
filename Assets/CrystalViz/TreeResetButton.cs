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
        buttonRect.sizeDelta = new Vector2(75f, 75f); // half the old 150px: a quiet mark, not a button
        // Glowing infinity mark on transparency: no box, no border — just the
        // symbol. Loaded from Resources and turned into a sprite at runtime
        // (same proven pattern as the stage avatars), so texture import
        // settings can't break it.
        var img = btnGO.AddComponent<Image>();
        var infTex = Resources.Load<Texture2D>("infinity-reset");
        if (infTex != null)
        {
            img.sprite = Sprite.Create(infTex,
                new Rect(0f, 0f, infTex.width, infTex.height),
                new Vector2(0.5f, 0.5f), 100f);
            img.color = Color.white; // keep the painted glow as-is
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
}
