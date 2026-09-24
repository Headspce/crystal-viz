using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cartoonish "growth feedback" burst: three anime sweat drops popping in
/// above the tree crown every time a tree tap is accepted (a bee-pop credit
/// is spent and the tree grows). v1.0.41: Tyler replaced the golden trapezoid
/// emphasis lines with his anime sweat-drop reference — same timing,
/// stagger, billboard, and rapid-tap restart behavior (never more than
/// three drops at once), now as textured sprites.
/// </summary>
public class TreeGrowthBurstLines : MonoBehaviour
{
    const float LineLife = 0.75f;   // total burst lifetime (seconds)
    const float PopTime = 0.12f;    // scale pop-in duration
    const float FadeStart = 0.40f;  // alpha fade starts
    const float Stagger = 0.05f;    // per-drop start delay
    const float BaseGap = 0.35f;    // gap between crown top and drop bases
    const float DriftOut = 0.18f;   // outward drift over the burst
    const float DropHeight = 0.34f; // sweat-drop sprite height (world units)
    const float DropAspect = 160f / 224f; // sweat-drop.png width/height

    // Ice-blue anime sweat drop, per Tyler's reference. Color of the burst
    // comes from the texture itself; the tint stays white.
    static readonly Color DropTint = Color.white;

    static readonly float[] BaseAngles = { -34f, 0f, 34f };
    static readonly float[] BaseLengths = { 0.46f, 0.64f, 0.52f };

    bool initialized;
    TreeGrowthController growth;
    ParametricTree tree;
    Camera mainCam;
    Shader spriteShader;
    Texture2D sweatTex;
    readonly List<Burst> bursts = new List<Burst>();

    class Drop
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
        public List<Drop> drops = new List<Drop>();
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

        // Same recipe as the paper bees (proven on device): the keyword-free
        // CrystalViz/SpritePaper unlit alpha-blend shader, pinned in the
        // variant collection so Shader.Find resolves on the phone.
        spriteShader = Shader.Find("CrystalViz/SpritePaper");
        if (spriteShader == null)
            spriteShader = Shader.Find("Universal Render Pipeline/Lit");
        sweatTex = Resources.Load<Texture2D>("sweat-drop");

        if (growth != null)
            growth.onTapAccepted.AddListener(FireBurst);
    }

    void OnDestroy()
    {
        if (growth != null) growth.onTapAccepted.RemoveListener(FireBurst);
    }

    Material MakeDropMaterial(Texture2D tex, Shader shader)
    {
        if (shader == null || tex == null) return null;
        var mat = new Material(shader);
        mat.mainTexture = tex;
        mat.color = DropTint;
        return mat;
    }

    /// <summary>Spawn one burst at the top of the crown.</summary>
    public void FireBurst()
    {
        // Bursts are play-mode-only FX; never spawn from the edit-mode
        // screenshot path.
        if (!Application.isPlaying) return;
        if (tree == null || growth == null || sweatTex == null) return;

        Vector3 anchor = tree.CrownTop;
        // Scale the burst with the tree so the sprout isn't dwarfed by its
        // own sweat drops.
        float s = 0.35f + 0.65f * growth.Growth01;

        // A fast follow-up tap restarts the burst instead of stacking — kill
        // any in-flight drops so only ever three animate at once.
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

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            go.transform.position = anchor;
            // Lean the drop outward along its drift direction, like the old
            // emphasis lines.
            // v1.0.43: flipped 180° — the drops used to read as diving INTO
            // the crown; now the round bulb leads and they flick outward,
            // like sweat flying off the tree.
            go.transform.rotation = Quaternion.Euler(0f, 0f, -BaseAngles[i] + 180f);
            go.transform.localScale = Vector3.zero;

            var mat = MakeDropMaterial(sweatTex, spriteShader);
            if (mat == null) { Destroy(go); continue; }
            go.GetComponent<MeshRenderer>().material = mat;
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var drop = new Drop
            {
                t = go.transform,
                mat = mat,
                delay = i * Stagger,
                dirX = dirX, dirY = dirY,
                length = length,
            };
            burst.drops.Add(drop);
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
            foreach (var drop in burst.drops)
            {
                float localAge = burst.age - drop.delay;
                if (localAge < 0f) { allDone = false; continue; }

                // Pop-in with a little overshoot.
                float pop = Mathf.Clamp01(localAge / PopTime);
                float scale = 1f + 0.35f * Mathf.Sin(pop * Mathf.PI) * (1f - pop);
                float lifeT = Mathf.Clamp01(localAge / LineLife);
                float alpha = 1f - Mathf.Clamp01((localAge - FadeStart) / (LineLife - FadeStart));
                float radius = BaseGap * (0.35f + 0.65f * growth.Growth01) + DriftOut * lifeT;

                drop.t.localPosition = new Vector3(drop.dirX * radius, drop.dirY * radius, 0f);
                float h = DropHeight * (0.35f + 0.65f * growth.Growth01) * scale;
                drop.t.localScale = new Vector3(h * DropAspect, h, 1f);

                var c = drop.mat.color;
                c.a = alpha;
                drop.mat.color = c;

                if (localAge < LineLife) allDone = false;
            }

            if (allDone && burst.age > LineLife + 2 * Stagger)
            {
                KillBurst(burst);
                bursts.RemoveAt(b);
            }
        }
    }

    /// <summary>Destroy a burst's GameObject and its drop materials.</summary>
    static void KillBurst(Burst burst)
    {
        if (burst == null) return;
        foreach (var drop in burst.drops)
            if (drop.mat != null) Destroy(drop.mat);
        if (burst.root != null) Destroy(burst.root);
    }
}
