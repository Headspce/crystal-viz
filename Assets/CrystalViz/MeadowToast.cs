using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// v1.0.47: the plain-language feedback voice of CrystalViz (research
/// principles 5, 6, 12). A single meadow-glass toast — bottom-center by
/// default, screen-center when asked (v1.0.52) — that confirms actions in
/// short English: resets, placeholder taps, and two one-time first-run
/// hints (corner menu + sun slider) shown after the boot wipe clears.
/// Play-mode only: the CI edit-mode screenshot path never creates it, so
/// captures stay deterministic.
/// </summary>
public class MeadowToast : MonoBehaviour
{
    static MeadowToast instance;

    // v1.0.52: toasts can ride bottom-center (default) or screen-center.
    // The queue carries the placement with the message so a centered toast
    // never leaks its position onto the next queued one.
    struct ToastMsg
    {
        public string text;
        public bool centered;
    }
    readonly Queue<ToastMsg> queue = new Queue<ToastMsg>();

    Text toastText;
    CanvasGroup group;
    RectTransform pillRt;
    float baseY;

    int state; // 0 idle, 1 fading in, 2 holding, 3 fading out
    float stateT;
    const float InDur = 0.25f;
    const float HoldDur = 2.2f;
    const float OutDur = 0.30f;

    // One-time first-run hints, armed by BeginHints() after the boot wipe.
    bool hintsArmed;
    float hintClock;
    int hintStage;

    /// <summary>Creates the toast overlay if needed. Null outside play mode.</summary>
    public static MeadowToast Ensure()
    {
        if (!Application.isPlaying) return null;
        if (instance == null)
        {
            var go = new GameObject("MeadowToast");
            instance = go.AddComponent<MeadowToast>();
            instance.Build();
        }
        return instance;
    }

    /// <summary>
    /// Queues a short plain-English confirmation. Safe to call anytime.
    /// v1.0.52: centered=true pins the toast to the middle of the screen
    /// instead of the bottom edge — used by the one-time lighting-slider
    /// hint, which the expanded corner menu used to overlap.
    /// </summary>
    public static void Show(string message, bool centered = false)
    {
        var t = Ensure();
        if (t == null || string.IsNullOrEmpty(message)) return;
        t.queue.Enqueue(new ToastMsg { text = message, centered = centered });
    }

    /// <summary>
    /// Called when the boot wipe finishes: starts the one-time hint timers.
    /// Each hint shows once ever (PlayerPrefs), so returning players are
    /// never nagged.
    /// </summary>
    public static void BeginHints()
    {
        var t = Ensure();
        if (t == null) return;
        bool menuSeen = PlayerPrefs.GetInt("cv_hint_menu_seen", 0) == 1;
        bool sunSeen = PlayerPrefs.GetInt("cv_hint_sun_seen", 0) == 1;
        if (!menuSeen || !sunSeen)
        {
            t.hintsArmed = true;
            t.hintClock = 0f;
            t.hintStage = 0;
        }
    }

    void Build()
    {
        var canvasGO = new GameObject("MeadowToastCanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200; // above HUD, below the boot overlay
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        group = canvasGO.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false; // toasts never eat taps
        group.interactable = false;

        var pillGO = new GameObject("ToastPill", typeof(RectTransform), typeof(Image));
        pillGO.transform.SetParent(canvasGO.transform, false);
        pillRt = pillGO.GetComponent<RectTransform>();
        pillRt.anchorMin = new Vector2(0.5f, 0f);
        pillRt.anchorMax = new Vector2(0.5f, 0f);
        pillRt.pivot = new Vector2(0.5f, 0.5f);
        baseY = 210f;
        pillRt.anchoredPosition = new Vector2(0f, baseY);
        pillRt.sizeDelta = new Vector2(860f, 100f);
        var pillImg = pillGO.GetComponent<Image>();
        pillImg.sprite = MeadowGlassUI.MakeGlassPill(256, 100);
        pillImg.type = Image.Type.Sliced;

        var textGO = new GameObject("ToastText", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(pillGO.transform, false);
        var trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.offsetMin = new Vector2(36f, 8f);
        trt.offsetMax = new Vector2(-36f, -8f);
        toastText = textGO.GetComponent<Text>();
        toastText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        toastText.fontSize = 34;
        toastText.alignment = TextAnchor.MiddleCenter;
        toastText.color = MeadowGlassUI.WarmWhite;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // First-run hints, staggered after the boot wipe.
        if (hintsArmed)
        {
            hintClock += dt;
            if (hintStage == 0 && hintClock >= 1.2f)
            {
                if (PlayerPrefs.GetInt("cv_hint_menu_seen", 0) == 0)
                {
                    Show("Tap the arrow, bottom-left, for tools.");
                    PlayerPrefs.SetInt("cv_hint_menu_seen", 1);
                    PlayerPrefs.Save();
                }
                hintStage = 1;
            }
            else if (hintStage == 1 && hintClock >= 4.6f)
            {
                if (PlayerPrefs.GetInt("cv_hint_sun_seen", 0) == 0)
                {
                    Show("Drag the sun tab on the right edge to change the time.");
                    PlayerPrefs.SetInt("cv_hint_sun_seen", 1);
                    PlayerPrefs.Save();
                }
                hintsArmed = false;
            }
        }

        // Queue pump.
        if (state == 0 && queue.Count > 0)
        {
            var msg = queue.Dequeue();
            toastText.text = msg.text;
            // v1.0.52: centered toasts pin to mid-screen (clear of the
            // expanded corner menu); everything else stays bottom-center.
            pillRt.anchorMin = new Vector2(0.5f, msg.centered ? 0.5f : 0f);
            pillRt.anchorMax = new Vector2(0.5f, msg.centered ? 0.5f : 0f);
            baseY = msg.centered ? 0f : 210f;
            state = 1;
            stateT = 0f;
        }

        if (state == 1) // fade in, pill rises slightly
        {
            stateT += dt;
            float k = Mathf.Clamp01(stateT / InDur);
            float e = k * k * (3f - 2f * k);
            group.alpha = e;
            pillRt.anchoredPosition = new Vector2(0f, baseY - 14f * (1f - e));
            if (k >= 1f) { state = 2; stateT = 0f; }
        }
        else if (state == 2) // hold
        {
            stateT += dt;
            if (stateT >= HoldDur) { state = 3; stateT = 0f; }
        }
        else if (state == 3) // fade out
        {
            stateT += dt;
            float k = Mathf.Clamp01(stateT / OutDur);
            group.alpha = 1f - k;
            if (k >= 1f) { state = 0; group.alpha = 0f; }
        }
    }
}
