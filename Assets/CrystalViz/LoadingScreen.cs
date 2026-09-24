using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Boot overlay (v1.0.45): Unity splash (automatic) -> this quote screen ->
/// a 2D diagonal screen wipe -> the main scene, fully loaded and immediately
/// visible. The quote fades in; then a dark panel with a soft glowing gold
/// leading edge sweeps diagonally across the screen (bottom-right to
/// top-left), wiping the quote screen away to reveal the finished diorama.
/// No staged loading, no dots, no WorldReveal — the v1.0.40-44 reveal
/// sequence (grid sweep, world arrival, tree pop, block dissolve) is DISABLED
/// per Tyler 2026-09-24 ("ignore that for now"); its code is kept untouched
/// for later. When the wipe clears, all assets are simply there.
/// Play-mode only — the CI edit-mode screenshot path never creates it.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    static readonly Color BgColor = new Color(0.02f, 0.035f, 0.09f, 1f);
    static readonly Color Gold = new Color(1.0f, 0.78f, 0.22f, 1f);
    static readonly Color SoftWhite = new Color(0.85f, 0.87f, 0.93f, 1f);

    // Wipe geometry (canvas reference units, 1080x1920): an oversized dark
    // square rotated 25 degrees. It slides up-left; its local bottom edge
    // (a true diagonal at 25 deg) is the wipe frontier, with the glow bars
    // riding on it. 2600px covers the 2203px screen diagonal with margin.
    const float WipeAngleDeg = 25f;
    const float WipeTravel = 2500f;
    const float WipeDuration = 1.1f;
    const float QuoteFadeIn = 0.9f;
    const float QuoteFadeOut = 0.3f;

    Text quoteText;
    RectTransform wipePanel;
    bool wiping;

    /// <summary>Build and show the quote overlay. Returns the instance.</summary>
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
        canvasGO.AddComponent<CanvasGroup>();

        // The wipe panel: oversized, rotated, centered. Its bottom edge is
        // the diagonal wipe frontier.
        var panelGO = new GameObject("WipePanel", typeof(RectTransform), typeof(Image));
        panelGO.transform.SetParent(canvasGO.transform, false);
        wipePanel = panelGO.GetComponent<RectTransform>();
        wipePanel.anchorMin = new Vector2(0.5f, 0.5f);
        wipePanel.anchorMax = new Vector2(0.5f, 0.5f);
        wipePanel.pivot = new Vector2(0.5f, 0.5f);
        wipePanel.anchoredPosition = Vector2.zero;
        wipePanel.sizeDelta = new Vector2(2600f, 2600f);
        wipePanel.localRotation = Quaternion.Euler(0f, 0f, WipeAngleDeg);
        panelGO.GetComponent<Image>().color = BgColor;

        // Soft glowing leading edge: a bright gold core bar plus a wider
        // faint halo, both riding the panel's bottom edge (the frontier).
        var coreGO = new GameObject("WipeEdgeCore", typeof(RectTransform), typeof(Image));
        coreGO.transform.SetParent(panelGO.transform, false);
        var coreRt = coreGO.GetComponent<RectTransform>();
        coreRt.anchorMin = new Vector2(0.5f, 0.5f);
        coreRt.anchorMax = new Vector2(0.5f, 0.5f);
        coreRt.pivot = new Vector2(0.5f, 0.5f);
        coreRt.anchoredPosition = new Vector2(0f, -1295f);
        coreRt.sizeDelta = new Vector2(2600f, 10f);
        coreGO.GetComponent<Image>().color = new Color(Gold.r, Gold.g, Gold.b, 0.95f);

        var haloGO = new GameObject("WipeEdgeHalo", typeof(RectTransform), typeof(Image));
        haloGO.transform.SetParent(panelGO.transform, false);
        var haloRt = haloGO.GetComponent<RectTransform>();
        haloRt.anchorMin = new Vector2(0.5f, 0.5f);
        haloRt.anchorMax = new Vector2(0.5f, 0.5f);
        haloRt.pivot = new Vector2(0.5f, 0.5f);
        haloRt.anchoredPosition = new Vector2(0f, -1276f);
        haloRt.sizeDelta = new Vector2(2600f, 48f);
        haloGO.GetComponent<Image>().color = new Color(Gold.r, Gold.g, Gold.b, 0.30f);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var titleGO = new GameObject("Quote", typeof(RectTransform), typeof(Text));
        titleGO.transform.SetParent(canvasGO.transform, false);
        var titleRt = titleGO.GetComponent<RectTransform>();
        titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 0.5f);
        titleRt.pivot = new Vector2(0.5f, 0.5f);
        titleRt.anchoredPosition = new Vector2(0f, 20f);
        titleRt.sizeDelta = new Vector2(1000f, 80f);
        quoteText = titleGO.GetComponent<Text>();
        quoteText.font = font;
        quoteText.fontSize = 46;
        quoteText.alignment = TextAnchor.MiddleCenter;
        quoteText.color = new Color(SoftWhite.r, SoftWhite.g, SoftWhite.b, 0f); // fades in
        quoteText.text = "sow bees, reap shade.";
    }

    /// <summary>
    /// Fades the quote in. The bootstrap yields this BEFORE the blocking
    /// scene build so the fade is actually visible (nothing animates during
    /// the build itself).
    /// </summary>
    public IEnumerator FadeInQuote()
    {
        float t = 0f;
        while (t < QuoteFadeIn)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / QuoteFadeIn);
            if (quoteText != null)
                quoteText.color = new Color(SoftWhite.r, SoftWhite.g, SoftWhite.b, a);
            yield return null;
        }
        if (quoteText != null)
            quoteText.color = SoftWhite;
    }

    /// <summary>
    /// Quote fades out, then the diagonal wipe sweeps the panel (and its
    /// glowing edge) off-screen, revealing the finished scene. Destroys the
    /// overlay when done.
    /// </summary>
    public void WipeAway()
    {
        if (wiping) return;
        wiping = true;
        StartCoroutine(WipeRoutine());
    }

    IEnumerator WipeRoutine()
    {
        // Quote out first (quick), the panel starts moving almost at once.
        float t = 0f;
        while (t < QuoteFadeOut)
        {
            t += Time.deltaTime;
            float a = 1f - Mathf.Clamp01(t / QuoteFadeOut);
            if (quoteText != null)
                quoteText.color = new Color(SoftWhite.r, SoftWhite.g, SoftWhite.b, a);
            yield return null;
        }

        // Diagonal sweep: up-left, smootherstep easing. The panel's rotated
        // bottom edge (with its glow bars) is the wipe frontier.
        Vector2 dir = new Vector2(
            Mathf.Cos(Mathf.Deg2Rad * (90f + WipeAngleDeg)),
            Mathf.Sin(Mathf.Deg2Rad * (90f + WipeAngleDeg)));
        t = 0f;
        while (t < WipeDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / WipeDuration);
            k = k * k * (3f - 2f * k); // smootherstep
            if (wipePanel != null)
                wipePanel.anchoredPosition = dir * (WipeTravel * k);
            yield return null;
        }
        Destroy(gameObject);
    }
}
