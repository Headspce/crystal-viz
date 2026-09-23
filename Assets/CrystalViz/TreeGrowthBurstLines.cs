using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cartoonish "growth feedback" burst: three golden trapezoid emphasis lines
/// radiating from the top of the tree crown every time a tree tap is accepted
/// (a bee-pop credit is spent and the tree grows). Anime surprise-line style,
/// per Tyler's reference image (v1.0.38): chunky, slightly tapered bars that
/// pop in above the crown, drift outward, and fade.
/// </summary>
public class TreeGrowthBurstLines : MonoBehaviour
{
    const float LineLife = 0.75f;   // total burst lifetime (seconds)
    const float PopTime = 0.12f;    // scale pop-in duration
    const float FadeStart = 0.40f;  // alpha fade starts
    const float Stagger = 0.05f;    // per-line start delay
    const float BaseGap = 0.35f;    // gap between crown top and line bases
    const float DriftOut = 0.18f;   // outward drift over the burst
    const float LineWidth = 0.11f;  // trapezoid base width

    // Golden yellow, anime emphasis-line style. Tyler said to color-code it
    // as I saw fit: gold complements both the blue sky and the green canopy,
    // so the lines pop against either background.
    static readonly Color LineColor = new Color(1.0f, 0.78f, 0.22f, 1.0f);

    static readonly float[] BaseAngles = { -34f, 0f, 34f };
    static readonly float[] BaseLengths = { 0.46f, 0.64f, 0.52f };

    bool initialized;
    TreeGrowthController growth;
    ParametricTree tree;
    Camera mainCam;
    Shader lineShader;
    Mesh lineMesh; // unit trapezoid: base width 1 at y=0, tip width 0.55 at y=1
    readonly List<Burst> bursts = new List<Burst>();

    class Line
    {
        public Transform t;
        public Material mat;
        public float delay;
        public float dirX, dirY; // unit direction in the burst's billboard plane
        public float length;
    }

    class Burst
    {
        public GameObject root;
        public List<Line> lines = new List<Line>();
        public float age;
    }

    void Awake() => Initialize();

    /// <summary>
    /// Idempotent: called explicitly from the bootstrap (edit-mode screenshot
    /// path never runs Awake) and again from Awake in play mode.
    /// </summary>
    public void Initialize()
    {
        if (initialized) return;
        initialized = true;

        growth = GetComponent<TreeGrowthController>();
        if (growth == null) growth = FindObjectOfType<TreeGrowthController>();
        tree = GetComponent<ParametricTree>();
        if (tree == null) tree = FindObjectOfType<ParametricTree>();
        mainCam = Camera.main;

        // Same recipe as the bee confetti (proven on device): URP/Lit,
        // transparent, no culling. Emission keeps the lines flat-bright like
        // flat anime color regardless of the sun angle.
        lineShader = Shader.Find("Universal Render Pipeline/Lit");

        lineMesh = new Mesh { name = "GrowthBurstLine" };
        lineMesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, 0f, 0f),
            new Vector3(0.5f, 0f, 0f),
            new Vector3(0.275f, 1f, 0f),
            new Vector3(-0.275f, 1f, 0f),
        };
        lineMesh.uv = new Vector2[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f),
        };
        lineMesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };

        if (growth != null)
            growth.onTapAccepted.AddListener(FireBurst);
    }

    void OnDestroy()
    {
        if (growth != null) growth.onTapAccepted.RemoveListener(FireBurst);
    }

    static Material MakeLineMaterial(Color c, Shader shader)
    {
        if (shader == null) return null;
        var mat = new Material(shader);
        mat.SetFloat("_Surface", 1f); // transparent
        mat.SetFloat("_Cull", 0f);
        mat.SetColor("_EmissionColor", new Color(c.r, c.g, c.b, 1f));
        mat.EnableKeyword("_EMISSION");
        mat.color = c;
        return mat;
    }

    /// <summary>Spawn one burst at the top of the crown.</summary>
    public void FireBurst()
    {
        // Bursts are play-mode-only FX; never spawn from the edit-mode
        // screenshot path.
        if (!Application.isPlaying) return;
        if (tree == null || growth == null || lineMesh == null) return;

        Vector3 anchor = tree.CrownTop;
        // Scale the burst with the tree so the sprout isn't dwarfed by its
        // own emphasis lines.
        float s = 0.35f + 0.65f * growth.Growth01;

        // v1.0.39: a fast follow-up tap restarts the burst instead of
        // stacking — kill any in-flight lines so only ever three animate
        // at once.
        for (int b = bursts.Count - 1; b >= 0; b--)
            KillBurst(bursts[b]);
        bursts.Clear();

        var burst = new Burst();
        var root = new GameObject("GrowthBurst");
        root.transform.position = anchor;
        burst.root = root;

        for (int i = 0; i < 3; i++)
        {
            float angle = (BaseAngles[i] + Random.Range(-7f, 7f)) * Mathf.Deg2Rad;
            float dirX = Mathf.Sin(angle);
            float dirY = Mathf.Cos(angle);
            float length = BaseLengths[i] * s * Random.Range(0.9f, 1.1f);

            var go = new GameObject("BurstLine" + i);
            go.transform.SetParent(root.transform, false);
            go.transform.position = anchor;
            go.transform.rotation = Quaternion.Euler(0f, 0f, -BaseAngles[i]);
            go.transform.localScale = Vector3.zero;

            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = lineMesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = MakeLineMaterial(LineColor, lineShader);
            if (mat == null) { Destroy(go); continue; }
            mr.material = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var line = new Line
            {
                t = go.transform,
                mat = mat,
                delay = i * Stagger,
                dirX = dirX, dirY = dirY,
                length = length,
            };
            burst.lines.Add(line);
        }

        burst.age = 0f;
        bursts.Add(burst);
    }

    void Update()
    {
        if (bursts.Count == 0) return;
        if (mainCam == null) mainCam = Camera.main;
        float dt = Time.deltaTime;

        for (int b = bursts.Count - 1; b >= 0; b--)
        {
            var burst = bursts[b];
            burst.age += dt;

            if (mainCam != null)
                burst.root.transform.rotation = mainCam.transform.rotation;

            bool allDone = true;
            foreach (var line in burst.lines)
            {
                float localAge = burst.age - line.delay;
                if (localAge < 0f) { allDone = false; continue; }

                // Pop-in with a little overshoot.
                float pop = Mathf.Clamp01(localAge / PopTime);
                float scale = 1f + 0.35f * Mathf.Sin(pop * Mathf.PI) * (1f - pop);
                float lifeT = Mathf.Clamp01(localAge / LineLife);
                float alpha = 1f - Mathf.Clamp01((localAge - FadeStart) / (LineLife - FadeStart));
                float radius = BaseGap * (0.35f + 0.65f * growth.Growth01) + DriftOut * lifeT;

                line.t.localPosition = new Vector3(line.dirX * radius, line.dirY * radius, 0f);
                line.t.localScale = new Vector3(LineWidth * (0.35f + 0.65f * growth.Growth01) * scale, line.length * scale, 1f);

                var c = line.mat.color;
                c.a = alpha;
                line.mat.color = c;

                if (localAge < LineLife) allDone = false;
            }

            if (allDone && burst.age > LineLife + 2 * Stagger)
            {
                KillBurst(burst);
                bursts.RemoveAt(b);
            }
        }
    }

    /// <summary>Destroy a burst's GameObject and its line materials.</summary>
    static void KillBurst(Burst burst)
    {
        if (burst == null) return;
        foreach (var line in burst.lines)
            if (line.mat != null) Destroy(line.mat);
        if (burst.root != null) Destroy(burst.root);
    }
}
