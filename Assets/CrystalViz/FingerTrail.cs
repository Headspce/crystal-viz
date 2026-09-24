using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v1.0.48: fingertip feedback — a short trail of soft gold/dewdrop dots
/// that follows the player's finger while touching the screen and fades in
/// ~0.3–0.5s, "like the meadow notices your finger" (player request).
/// Pure feel, zero gameplay: a fixed pool of Images (no per-frame allocs,
/// no runtime Instantiate/Destroy), low count, subtle alpha. The overlay
/// canvas never blocks input (blocksRaycasts = false, dots raycastTarget =
/// false) and mouse input is supported too so it can be previewed in the
/// editor. Initialized via FingerTrail.Ensure() from CrystalVizBootstrap.
/// </summary>
public class FingerTrail : MonoBehaviour
{
    static FingerTrail instance;

    const int PoolSize = 56;
    const float MinSpawnGapPx = 10f; // one dot per ~10px of travel

    Canvas canvas;
    RectTransform canvasRT;
    Sprite dotSprite;

    struct Dot
    {
        public Image img;
        public RectTransform rt;
        public float age;
        public float life;
        public float startSize;
        public bool active;
    }

    Dot[] pool;
    int cursor;
    Vector2 lastSpawnPos;
    bool hasLastSpawn;

    /// <summary>
    /// Play-mode-only initializer, mirroring MeadowToast.Ensure().
    /// No-ops in edit mode so CI screenshots stay deterministic.
    /// </summary>
    public static FingerTrail Ensure()
    {
        if (!Application.isPlaying) return null;
        if (instance == null)
        {
            var go = new GameObject("FingerTrail");
            instance = go.AddComponent<FingerTrail>();
            instance.Build();
        }
        return instance;
    }

    void Build()
    {
        var canvasGO = new GameObject("FingerTrailCanvas");
        canvasGO.transform.SetParent(transform, false);
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150; // above the slider, below the toast layer
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();
        var group = canvasGO.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false; // never eats touches
        group.interactable = false;

        canvasRT = canvasGO.GetComponent<RectTransform>();
        dotSprite = MakeDotSprite(64);

        pool = new Dot[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var go = new GameObject("TrailDot", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvasGO.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-9999f, -9999f); // parked off-screen
            var img = go.GetComponent<Image>();
            img.sprite = dotSprite;
            img.raycastTarget = false;
            var c = img.color;
            c.a = 0f;
            img.color = c;
            pool[i] = new Dot { img = img, rt = rt, active = false };
        }
    }

    void Update()
    {
        bool touching = false;

        // Touch input: a dot on touch-down, then one per ~10px of travel.
        for (int i = 0; i < Input.touchCount; i++)
        {
            var t = Input.GetTouch(i);
            if (t.phase == TouchPhase.Began)
            {
                Spawn(t.position);
                touching = true;
            }
            else if (t.phase == TouchPhase.Moved)
            {
                if (!hasLastSpawn || Vector2.Distance(t.position, lastSpawnPos) >= MinSpawnGapPx)
                    Spawn(t.position);
                touching = true;
            }
        }

        // Mouse fallback (editor preview + the CI-less local run).
        if (Input.touchCount == 0 && Input.GetMouseButton(0))
        {
            Vector2 mp = Input.mousePosition;
            if (!hasLastSpawn || Vector2.Distance(mp, lastSpawnPos) >= MinSpawnGapPx)
                Spawn(mp);
            touching = true;
        }
        if (!touching) hasLastSpawn = false;

        // Age the pool: ease-out fade + gentle shrink, then park off-screen.
        float dt = Time.deltaTime;
        for (int i = 0; i < PoolSize; i++)
        {
            if (!pool[i].active) continue;
            pool[i].age += dt;
            float k = pool[i].age / pool[i].life;
            if (k >= 1f)
            {
                pool[i].active = false;
                var c0 = pool[i].img.color;
                c0.a = 0f;
                pool[i].img.color = c0;
                pool[i].rt.anchoredPosition = new Vector2(-9999f, -9999f);
                continue;
            }
            float fade = 1f - k * k;
            var c = pool[i].img.color;
            c.a = 0.55f * fade;
            pool[i].img.color = c;
            float s = pool[i].startSize * (1f - 0.45f * k);
            pool[i].rt.sizeDelta = new Vector2(s, s);
        }
    }

    void Spawn(Vector2 screenPos)
    {
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRT, screenPos, null, out local))
            return;
        int idx = cursor;
        cursor = (cursor + 1) % PoolSize;
        Dot d = pool[idx];
        d.rt.anchoredPosition = local;
        d.age = 0f;
        d.life = 0.32f + Random.value * 0.18f; // 0.32–0.5s fade
        d.startSize = 16f + Random.value * 18f; // 16–34 canvas px
        d.active = true;
        // Mostly soft gold, sometimes a pale dewdrop glint.
        d.img.color = Random.value < 0.75f
            ? new Color(1f, 0.80f, 0.30f, 0.55f)
            : new Color(0.82f, 0.93f, 1f, 0.50f);
        pool[idx] = d;
        lastSpawnPos = screenPos;
        hasLastSpawn = true;
    }

    /// <summary>
    /// Soft radial dot: bright warm core falling off to transparent —
    /// reads as a dewdrop catching gold light.
    /// </summary>
    static Sprite MakeDotSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.filterMode = FilterMode.Bilinear;
        float c = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f - c;
                float py = y + 0.5f - c;
                float d = Mathf.Sqrt(px * px + py * py) / c; // 0 center .. 1 edge
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a); // smoothstep falloff
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
