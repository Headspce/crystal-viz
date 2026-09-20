using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Test-loop reset button: an opaque black button pinned to the bottom-left
/// of the screen. Pressing it calls TreeGrowthController.ResetToSapling(),
/// which drops the tree back to the sapling stage with the tap counter at
/// zero, so the full sapling -> mature growth can be re-tapped over and over
/// during playtesting.
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
            controller.ResetToSapling();
    }

    void BuildUI()
    {
        var canvasGO = new GameObject("ResetButtonCanvas");
        canvasGO.transform.SetParent(transform, false);
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
        buttonRect.sizeDelta = new Vector2(150f, 150f);
        // Opaque black, no label: a plain test-loop button.
        var img = btnGO.AddComponent<Image>();
        img.color = Color.black;
    }
}
