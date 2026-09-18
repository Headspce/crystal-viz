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
    float currentAzimuth = 54f;
    float targetAzimuth = 54f;

    void Start()
    {
        BuildForScreenshot();
        slider.onValueChanged.AddListener(v =>
        {
            targetAzimuth = v * 360f;
            UpdateLabel();
        });
        slider.value = targetAzimuth / 360f; // fires listener, sets initial sun pos
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
        UpdateLabel();
    }

    void Update()
    {
        if (bootstrap == null || bootstrap.sun == null) return;
        // Smooth-damped follow so the sun glides instead of snapping.
        currentAzimuth = Mathf.LerpAngle(currentAzimuth, targetAzimuth,
            1f - Mathf.Exp(-8f * Time.deltaTime));
        bootstrap.PlaceSun(currentAzimuth);
    }

    void UpdateLabel()
    {
        if (angleLabel != null)
            angleLabel.text = $"{Mathf.RoundToInt(targetAzimuth % 360f)}°";
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

        // Slider root: vertical strip hugging the left edge.
        var root = new GameObject("SunSlider", typeof(RectTransform), typeof(Slider));
        root.transform.SetParent(canvasGo.transform, false);
        var rrt = root.GetComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0f, 0f);
        rrt.anchorMax = new Vector2(0f, 1f);
        rrt.offsetMin = new Vector2(28f, 130f);
        rrt.offsetMax = new Vector2(104f, -130f);

        slider = root.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.direction = Slider.Direction.BottomToTop;

        // Track background.
        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(root.transform, false);
        var bgrt = bg.GetComponent<RectTransform>();
        bgrt.anchorMin = Vector2.zero; bgrt.anchorMax = Vector2.one;
        bgrt.offsetMin = new Vector2(26f, 0f); bgrt.offsetMax = new Vector2(-26f, 0f);
        bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.30f);

        // Fill area + fill (the warm amber progress).
        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(root.transform, false);
        var fart = fillArea.GetComponent<RectTransform>();
        fart.anchorMin = Vector2.zero; fart.anchorMax = Vector2.one;
        fart.offsetMin = new Vector2(26f, 0f); fart.offsetMax = new Vector2(-26f, 0f);
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        fill.GetComponent<Image>().color = new Color(1f, 0.72f, 0.35f, 0.90f);
        slider.fillRect = fill.GetComponent<RectTransform>();

        // Handle area + knob.
        var handleArea = new GameObject("Handle Area", typeof(RectTransform));
        handleArea.transform.SetParent(root.transform, false);
        var hart = handleArea.GetComponent<RectTransform>();
        hart.anchorMin = Vector2.zero; hart.anchorMax = Vector2.one;
        hart.offsetMin = new Vector2(8f, -28f); hart.offsetMax = new Vector2(-8f, 28f);
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        var hrt = handle.GetComponent<RectTransform>();
        hrt.sizeDelta = new Vector2(56f, 56f);
        handle.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.95f);
        slider.handleRect = hrt;
        slider.targetGraphic = handle.GetComponent<Image>();

        // Angle readout under the slider.
        var labelGo = new GameObject("AngleLabel", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(canvasGo.transform, false);
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(0f, 0f);
        lrt.anchoredPosition = new Vector2(66f, 78f);
        lrt.sizeDelta = new Vector2(120f, 40f);
        var txt = labelGo.GetComponent<Text>();
        var builtin = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (builtin != null) txt.font = builtin;
        txt.fontSize = 30;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = new Color(0.25f, 0.22f, 0.20f, 0.9f);
        angleLabel = txt;
    }
}
