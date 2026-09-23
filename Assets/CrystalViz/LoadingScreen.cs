using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Branded loading screen: the first thing painted after the Unity splash,
/// so the procedurally-built diorama doesn't pop in jarringly. Just a dark
/// screen with the game's quote and an animated dot row while the bootstrap
/// builds the scene behind it, then fades away. (The app wordmark stays off
/// for now — the name may change.)
/// Play-mode only — the CI edit-mode screenshot path never creates it.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    static readonly Color BgColor = new Color(0.02f, 0.035f, 0.09f, 1f);
    static readonly Color Gold = new Color(1.0f, 0.78f, 0.22f, 1f);
    static readonly Color SoftWhite = new Color(0.85f, 0.87f, 0.93f, 1f);

    CanvasGroup canvasGroup;
    RectTransform[] dots = new RectTransform[3];
    bool dismissing;

    /// <summary>Build and show the loading overlay. Returns the instance.</summary>
    public static LoadingScreen Show()
    {
        var go = new GameObject("LoadingScreen");
        var screen = go.AddComponent<LoadingScreen>();
        screen.Build();
        return screen;
    }

    void Build()
    {
        var canvasGO = new GameObject("LoadingCanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGroup = canvasGO.AddComponent<CanvasGroup>();

        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bgRt = bgGO.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.sizeDelta = Vector2.zero;
        var bg = bgGO.AddComponent<Image>();
        bg.color = BgColor;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(canvasGO.transform, false);
        var titleRt = titleGO.AddComponent<RectTransform>();
        titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 0.5f);
        titleRt.pivot = new Vector2(0.5f, 0.5f);
        titleRt.anchoredPosition = new Vector2(0f, 20f);
        titleRt.sizeDelta = new Vector2(1000f, 80f);
        var title = titleGO.AddComponent<Text>();
        title.font = font;
        title.fontSize = 46;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = SoftWhite;
        title.text = "sow bees, reap shade.";

        for (int i = 0; i < 3; i++)
        {
            var dotGO = new GameObject("Dot" + i);
            dotGO.transform.SetParent(canvasGO.transform, false);
            var dotRt = dotGO.AddComponent<RectTransform>();
            dotRt.anchorMin = dotRt.anchorMax = new Vector2(0.5f, 0.5f);
            dotRt.pivot = new Vector2(0.5f, 0.5f);
            dotRt.anchoredPosition = new Vector2((i - 1) * 80f, -110f);
            dotRt.sizeDelta = new Vector2(60f, 60f);
            var dot = dotGO.AddComponent<Text>();
            dot.font = font;
            dot.fontSize = 64;
            dot.alignment = TextAnchor.MiddleCenter;
            dot.color = Gold;
            dot.text = "●";
            dots[i] = dotRt;
        }
    }

    void Update()
    {
        if (dismissing) return;
        // Staggered bounce on the dot row while the scene builds.
        for (int i = 0; i < dots.Length; i++)
        {
            float s = 1f + 0.35f * Mathf.Sin(Time.time * 4f - i * 0.9f);
            s = Mathf.Max(0.6f, s);
            dots[i].localScale = new Vector3(s, s, 1f);
        }
    }

    /// <summary>Fade out over ~0.6s, then destroy.</summary>
    public void Dismiss()
    {
        if (dismissing) return;
        dismissing = true;
        StartCoroutine(FadeOut());
    }

    IEnumerator FadeOut()
    {
        float t = 0f;
        while (t < 0.6f)
        {
            t += Time.deltaTime;
            if (canvasGroup != null)
                canvasGroup.alpha = 1f - Mathf.Clamp01(t / 0.6f);
            yield return null;
        }
        Destroy(gameObject);
    }
}
