using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// v1.0.49: fingertip feedback — ONE continuous fading ribbon (player
/// request: "instead of dots make it one continuous fading line, a bit
/// longer"). Replaces the v1.0.48 dot pool. A ring buffer of recent pointer
/// samples (screen px) feeds a single TrailRibbon UI Graphic that rebuilds
/// its mesh each frame: one draw call, no per-frame allocations after the
/// lists warm up, ~0.8s fade along the ribbon's length, tail-tapered width,
/// soft alpha edges, round head cap at the fingertip. Pure feel, zero
/// gameplay. The overlay canvas never blocks input (blocksRaycasts = false,
/// ribbon raycastTarget = false) and mouse input is supported too so it can
/// be previewed in the editor. Initialized via FingerTrail.Ensure() from
/// CrystalVizBootstrap. Ensure() no-ops in edit mode so CI screenshots stay
/// deterministic.
/// </summary>
public class FingerTrail : MonoBehaviour
{
    static FingerTrail instance;

    const float Lifetime = 0.8f; // v1.0.49: a bit longer than the old 0.3–0.5s dots
    const int MaxSamples = 72;
    const float MinSpawnGapPx = 8f; // one sample per ~8px of travel

    readonly Vector2[] samplePos = new Vector2[MaxSamples];
    readonly float[] sampleTime = new float[MaxSamples];
    int sampleHead; // index of the newest sample
    int sampleCount;

    TrailRibbon ribbon;
    RectTransform canvasRT;

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
        var canvas = canvasGO.AddComponent<Canvas>();
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

        var ribbonGO = new GameObject("TrailRibbon", typeof(RectTransform));
        ribbonGO.transform.SetParent(canvasGO.transform, false);
        var rrt = ribbonGO.GetComponent<RectTransform>();
        rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
        rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
        ribbon = ribbonGO.AddComponent<TrailRibbon>();
        ribbon.raycastTarget = false;
    }

    void Update()
    {
        // Touch input: a sample on touch-down, then one per ~8px of travel.
        for (int i = 0; i < Input.touchCount; i++)
        {
            var t = Input.GetTouch(i);
            if (t.phase == TouchPhase.Began || t.phase == TouchPhase.Moved)
                AddSample(t.position);
        }

        // Mouse fallback (editor preview + the CI-less local run).
        if (Input.touchCount == 0 && Input.GetMouseButton(0))
            AddSample(Input.mousePosition);

        float now = Time.time;
        // Prune samples older than the trail lifetime, dropping from the tail.
        while (sampleCount > 0)
        {
            int tail = (sampleHead - sampleCount + 1 + MaxSamples * 2) % MaxSamples;
            if (now - sampleTime[tail] < Lifetime) break;
            sampleCount--;
        }

        if (sampleCount == 0)
        {
            if (ribbon.pointCount > 0) ribbon.Clear();
            return;
        }

        // Feed the ribbon oldest -> newest, alpha fading with age.
        ribbon.BeginPoints(sampleCount);
        for (int i = 0; i < sampleCount; i++)
        {
            int idx = (sampleHead - sampleCount + 1 + i + MaxSamples * 2) % MaxSamples;
            float age = now - sampleTime[idx];
            float a = Mathf.Clamp01(1f - age / Lifetime);
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRT, samplePos[idx], null, out local))
                ribbon.AddPoint(local, a * a); // ease-out: the fade lingers
        }
        ribbon.EndPoints();
    }

    void AddSample(Vector2 screenPos)
    {
        if (sampleCount > 0)
        {
            Vector2 prev = samplePos[sampleHead];
            if (Vector2.Distance(screenPos, prev) < MinSpawnGapPx) return;
        }
        sampleHead = (sampleHead + 1) % MaxSamples;
        samplePos[sampleHead] = screenPos;
        sampleTime[sampleHead] = Time.time;
        if (sampleCount < MaxSamples) sampleCount++;
    }
}

/// <summary>
/// v1.0.49: the single ribbon mesh — one MaskableGraphic = one draw call.
/// Points arrive oldest → newest in the graphic's local space; the mesh is a
/// triangle strip tapering toward the tail (25% width at the tail), with
/// per-point vertex alpha (head bright, tail fading to zero) and a baked
/// soft-edge texture across the ribbon for feathery sides. A semicircle fan
/// rounds the head cap at the fingertip. The UI/Default shader culls nothing
/// (Cull Off), so winding order is not a concern.
/// </summary>
public class TrailRibbon : MaskableGraphic
{
    const int CapSteps = 8;

    readonly List<Vector2> points = new List<Vector2>(MaxSamples);
    readonly List<float> alphas = new List<float>(MaxSamples);
    const int MaxSamples = 72;

    static Texture2D softTex;

    public float width = 30f; // ribbon width in canvas px at the head
    public Color tint = new Color(1f, 0.80f, 0.30f, 1f); // warm gold
    public float headAlpha = 0.7f;

    public override Texture mainTexture => softTex != null ? softTex : base.mainTexture;

    public int pointCount => points.Count;

    public void BeginPoints(int capacity)
    {
        points.Clear();
        alphas.Clear();
        if (points.Capacity < capacity)
        {
            points.Capacity = capacity;
            alphas.Capacity = capacity;
        }
    }

    public void AddPoint(Vector2 local, float alpha)
    {
        points.Add(local);
        alphas.Add(alpha);
    }

    public void EndPoints()
    {
        SetVerticesDirty();
    }

    public void Clear()
    {
        points.Clear();
        alphas.Clear();
        SetVerticesDirty();
    }

    protected override void Awake()
    {
        base.Awake();
        if (softTex == null) softTex = MakeSoftRibbonTexture();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        int n = points.Count;
        if (n < 2) return;

        var vt = new UIVertex();
        vt.normal = Vector3.back;
        vt.tangent = new Vector4(1f, 0f, 0f, -1f);

        // Triangle strip: two verts per point, width tapering to the tail.
        for (int i = 0; i < n; i++)
        {
            Vector2 p = points[i];
            Vector2 dir;
            if (i == 0) dir = points[1] - points[0];
            else if (i == n - 1) dir = points[n - 1] - points[n - 2];
            else dir = points[i + 1] - points[i - 1];
            if (dir.sqrMagnitude < 1e-8f) dir = Vector2.right;
            else dir.Normalize();
            Vector2 nrm = new Vector2(-dir.y, dir.x);
            float t = (float)i / (n - 1); // 0 = tail .. 1 = head
            float w = width * 0.5f * Mathf.Lerp(0.25f, 1f, t);
            Color c = tint;
            c.a = headAlpha * alphas[i];
            vt.position = p + nrm * w;
            vt.color = c;
            vt.uv0 = new Vector2(0f, t);
            vh.AddVert(vt);
            vt.position = p - nrm * w;
            vt.uv0 = new Vector2(1f, t);
            vh.AddVert(vt);
        }
        for (int i = 0; i < n - 1; i++)
        {
            int b = i * 2;
            vh.AddTriangle(b, b + 2, b + 1);
            vh.AddTriangle(b + 1, b + 2, b + 3);
        }

        // Round head cap: semicircle fan at the newest point (the fingertip).
        Vector2 hp = points[n - 1];
        Vector2 hd = points[n - 1] - points[n - 2];
        if (hd.sqrMagnitude < 1e-8f) hd = Vector2.right;
        else hd.Normalize();
        Vector2 hn = new Vector2(-hd.y, hd.x);
        float hw = width * 0.5f;
        Color hc = tint;
        hc.a = headAlpha * alphas[n - 1];
        int center = vh.currentVertCount;
        vt.position = hp;
        vt.color = hc;
        vt.uv0 = new Vector2(0.5f, 1f);
        vh.AddVert(vt);
        for (int k = 0; k <= CapSteps; k++)
        {
            float ang = Mathf.PI * k / CapSteps; // 0..PI: +hn around hd to -hn
            Vector2 d = hn * Mathf.Cos(ang) + hd * Mathf.Sin(ang);
            vt.position = hp + d * hw;
            vt.color = hc;
            vt.uv0 = new Vector2(0.5f, 1f);
            vh.AddVert(vt);
        }
        for (int k = 0; k < CapSteps; k++)
            vh.AddTriangle(center, center + 1 + k, center + 2 + k);
    }

    /// <summary>
    /// White texture with a soft sin^0.7 alpha falloff across X (the ribbon
    /// width): feathery ribbon edges without extra geometry or shaders.
    /// </summary>
    static Texture2D MakeSoftRibbonTexture()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false);
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)(S - 1); // 0..1 across the ribbon
                float a = Mathf.Pow(Mathf.Sin(Mathf.PI * u), 0.7f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }
}
