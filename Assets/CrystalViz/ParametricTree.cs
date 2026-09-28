using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Procedural parametric tree for CrystalViz's tap-to-grow feature.
///
/// The trunk and all recursive branches are merged into ONE mesh with two
/// submeshes: submesh 0 is the trunk (level 0) wearing the fine 14-ridge
/// bark, submesh 1 is every branch (level >= 1) wearing a chunkier 8-ridge
/// bark so the ridges stay readable on thin tubes. Leaf quads
/// clustered at the branch tips form a second merged mesh. Call SetGrowth(g)
/// with g in [0,1] to rebuild the tree at any growth stage.
///
/// Branches follow curved Catmull-Rom splines with an upward (phototropic)
/// bend and tapered radii, so the tree reads as grown rather than assembled
/// from cylinders. All randomness comes from a seeded System.Random (12345),
/// so the tree is identical on every run and every rebuild at the same g.
///
/// Tube frames use parallel-transport to avoid twisting; branch children use
/// golden-angle phyllotaxis around the parent for a natural look.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ParametricTree : MonoBehaviour
{
    /// <summary>
    /// v1.0.57: selectable tree species. Broadleaf is the original
    /// oak-style tree; Pine is a procedural conifer (straight trunk,
    /// whorled tier branches, needle foliage) grown by the same engine.
    /// v1.0.61: six selectable species — the inventory rows map 1:1 to these
    /// values (Oak=0 … Palm=5), so keep this order in sync with the row labels.
    /// </summary>
    public enum TreeSpecies { Broadleaf = 0, Pine = 1, Birch = 2, Willow = 3, Cherry = 4, Palm = 5 }
    public TreeSpecies species = TreeSpecies.Broadleaf;

    const int Seed = 12345;
    const int RadialSegments = 7;   // vertices per tube ring
    const float RebuildEpsilon = 0.004f; // skip rebuilds for tiny g changes
    const float GoldenAngle = 2.39996f;   // radians, ~137.5 deg

    MeshFilter trunkFilter;
    MeshRenderer trunkRenderer;
    MeshFilter leafFilter;
    MeshRenderer leafRenderer;
    Mesh trunkMesh;
    Mesh leafMesh;

    // v1.0.57: both foliage textures are built once; SetSpecies swaps the
    // leaf material's _BaseMap between the broadleaf and the needle.
    Material leafMat;
    Texture2D broadleafTex;
    Texture2D needleTex;

    float lastBuiltG = -1f;
    float currentG; // last growth value fed to SetGrowth (for species rebuilds)

    // Scratch buffers reused across rebuilds to avoid GC churn.
    readonly List<Vector3> vList = new List<Vector3>();
    readonly List<Vector3> nList = new List<Vector3>();
    readonly List<Vector2> uvList = new List<Vector2>();
    readonly List<Color> cList = new List<Color>();
    readonly List<int> tList = new List<int>();
    // Branch scratch buffers: branches (level >= 1) accumulate here so the
    // trunk mesh can be built with two submeshes (trunk bark / branch bark).
    readonly List<Vector3> bvList = new List<Vector3>();
    readonly List<Vector3> bnList = new List<Vector3>();
    readonly List<Vector2> buvList = new List<Vector2>();
    readonly List<Color> bcList = new List<Color>();
    readonly List<int> btList = new List<int>();
    readonly List<Vector3> tips = new List<Vector3>(); // leaf anchors: branch tips + points along the outer branches

    // v1.0.61: palm fronds live on their own mesh (opaque wind-blown ribbon,
    // not the alpha-cutout leaf), with dedicated scratch buffers.
    MeshFilter frondFilter;
    MeshRenderer frondRenderer;
    Mesh frondMesh;
    Material frondMat;
    readonly List<Vector3> fvList = new List<Vector3>();
    readonly List<Vector3> fnList = new List<Vector3>();
    readonly List<Vector2> fuvList = new List<Vector2>();
    readonly List<Color> fcList = new List<Color>();
    readonly List<int> ftList = new List<int>();

    // v1.0.61: bark materials promoted to fields so SyncBarkMaterials can
    // swap the birch's pale bark in/out when the species changes.
    Material trunkMat;
    Material branchMat;
    Material birchTrunkMat;
    Material birchBranchMat;

    bool initialized;

    /// <summary>
    /// v1.0.61: per-species recipe for the broadleaf-family engine (oak,
    /// birch, willow, cherry). Pine and palm have bespoke builders.
    /// </summary>
    struct SpeciesRecipe
    {
        public float trunkLen;   // full-growth trunk length
        public float trunkRad;   // full-growth trunk radius
        public Color leafColor;  // base foliage tint (graded by per-leaf variation)
        public float leafCount;  // full-growth leaf quad count
        public float leafSize;   // base quad half-size
        public float leafSpread; // random sphere radius around each anchor
        public float droop;      // 0 = phototropic upward; 1 = fully pendulous
    }

    static SpeciesRecipe GetRecipe(TreeSpecies s)
    {
        switch (s)
        {
            case TreeSpecies.Birch: // tall, slim, paper-white bark, airy light canopy
                return new SpeciesRecipe { trunkLen = 5.4f, trunkRad = 0.20f,
                    leafColor = new Color(0.38f, 0.56f, 0.20f, 1f), leafCount = 9000f,
                    leafSize = 0.036f, leafSpread = 0.55f, droop = 0f };
            case TreeSpecies.Willow: // stout trunk, weeping curtains
                return new SpeciesRecipe { trunkLen = 4.2f, trunkRad = 0.30f,
                    leafColor = new Color(0.32f, 0.52f, 0.16f, 1f), leafCount = 14000f,
                    leafSize = 0.032f, leafSpread = 0.65f, droop = 1f };
            case TreeSpecies.Cherry: // blossom canopy, gentle outward drape
                return new SpeciesRecipe { trunkLen = 4.0f, trunkRad = 0.26f,
                    leafColor = new Color(0.95f, 0.55f, 0.66f, 1f), leafCount = 11000f,
                    leafSize = 0.042f, leafSpread = 0.55f, droop = 0.15f };
            default: // Broadleaf (oak): the original recipe, unchanged
                return new SpeciesRecipe { trunkLen = 4.6f, trunkRad = 0.28f,
                    leafColor = new Color(0.16f, 0.42f, 0.12f, 1f), leafCount = 12000f,
                    leafSize = 0.04f, leafSpread = 0.5f, droop = 0f };
        }
    }

    void Awake()
    {
        Initialize();
    }

    /// <summary>
    /// Builds materials and the trunk/leaf meshes. Called from Awake() in
    /// play mode; the CI screenshot path builds the scene in edit mode, where
    /// AddComponent does NOT fire Awake(), so CrystalVizBootstrap calls this
    /// explicitly after AddComponent. Idempotent: safe to call twice.
    /// </summary>
    public void Initialize()
    {
        if (initialized) return;
        initialized = true;

        // RequireComponent adds these at AddComponent time, but belt-and-braces
        // in case the edit-mode path ever skips that.
        trunkFilter = GetComponent<MeshFilter>();
        if (trunkFilter == null) trunkFilter = gameObject.AddComponent<MeshFilter>();
        trunkRenderer = GetComponent<MeshRenderer>();
        if (trunkRenderer == null) trunkRenderer = gameObject.AddComponent<MeshRenderer>();

        // Resolve a shader with fallbacks. We NEVER abort here: even a magenta
        // error-shader material is better than an invisible tree, because it
        // proves the geometry is building (a missing URP/Lit in CI otherwise
        // fails silently and the tree just never appears).
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Debug.LogWarning("ParametricTree: 'Universal Render Pipeline/Lit' not found; trying 'Standard'.");
            lit = Shader.Find("Standard");
        }
        if (lit == null)
        {
            Debug.LogWarning("ParametricTree: 'Standard' not found either; using 'Hidden/InternalErrorShader' " +
                "(magenta) so the geometry is at least visible.");
            lit = Shader.Find("Hidden/InternalErrorShader");
        }
        if (lit == null)
        {
            // Should be impossible: InternalErrorShader ships with every Unity build.
            Debug.LogError("ParametricTree: no shader found at all. Tree geometry will still build; " +
                "materials may render magenta.");
        }

        // Bark: procedural tileable ridged texture (valleys + ridges like
        // real bark, dark brown), generated in code so no texture asset can
        // go missing or render black in CI. Lower roughness (higher
        // smoothness) so the trunk catches a soft sheen instead of reading
        // chalky, with the ridge height as a bump map.

        if (lit != null)
        {
            // Bark: Poly Haven pine_bark (CC0) with official normal map:
            // deeply furrowed, chunky plates like the reference photos
            // (v1.0.28: replaced the finer-grained bark_brown_02).
            // Falls back to procedural ridged bark if the textures are missing.
            var polyBark = Resources.Load<Texture2D>("Textures/pine_bark_1k");
            var polyBarkNormal = Resources.Load<Texture2D>("Textures/pine_bark_1k_nor_gl");

            trunkMat = new Material(lit);
            if (polyBark != null)
            {
                trunkMat.SetTexture("_BaseMap", polyBark);
                if (polyBarkNormal != null)
                {
                    trunkMat.SetTexture("_BumpMap", polyBarkNormal);
                    trunkMat.SetFloat("_BumpScale", 0.6f);
                }
            }
            else
            {
                // Fallback: procedural tileable ridged bark
                float[] barkHeight;
                var barkTex = MakeBarkTexture(out barkHeight, BarkRidgeColumns);
                trunkMat.SetTexture("_BaseMap", barkTex);
                var barkBump = MakeBumpTexture(barkHeight, BarkTexSize);
                trunkMat.SetTexture("_BumpMap", barkBump);
                trunkMat.SetFloat("_BumpScale", 0.6f);
            }
            trunkMat.SetFloat("_Smoothness", 0.8f); // roughness ~0.2: soft sheen, not chalk
            trunkMat.SetFloat("_Metallic", 0f);
            trunkMat.color = Color.white;
            // Branches (submesh 1) wear the same bark with chunkier ridges so
            // the texture reads on thin tubes. Poly Haven bark works for both;
            // procedural fallback uses the chunkier branch ridge count.
            branchMat = new Material(lit);
            if (polyBark != null)
            {
                branchMat.SetTexture("_BaseMap", polyBark);
                if (polyBarkNormal != null)
                {
                    branchMat.SetTexture("_BumpMap", polyBarkNormal);
                    branchMat.SetFloat("_BumpScale", 0.6f);
                }
            }
            else
            {
                float[] branchHeight;
                var branchTex = MakeBarkTexture(out branchHeight, BranchRidgeColumns);
                branchMat.SetTexture("_BaseMap", branchTex);
                var branchBump = MakeBumpTexture(branchHeight, BarkTexSize);
                branchMat.SetTexture("_BumpMap", branchBump);
                branchMat.SetFloat("_BumpScale", 0.6f);
            }
            branchMat.SetFloat("_Smoothness", 0.8f);
            branchMat.SetFloat("_Metallic", 0f);
            branchMat.color = Color.white;
            // v1.0.61: birch wears pale paper bark with dark lenticel dashes
            // (procedural — no texture asset to go missing in CI).
            float[] birchHeight;
            var birchTex = MakeBirchBarkTexture(out birchHeight);
            var birchBump = MakeBumpTexture(birchHeight, BarkTexSize);
            birchTrunkMat = new Material(lit);
            birchTrunkMat.SetTexture("_BaseMap", birchTex);
            birchTrunkMat.SetTexture("_BumpMap", birchBump);
            birchTrunkMat.SetFloat("_BumpScale", 0.4f);
            birchTrunkMat.SetFloat("_Smoothness", 0.7f);
            birchTrunkMat.SetFloat("_Metallic", 0f);
            birchTrunkMat.color = Color.white;
            birchBranchMat = new Material(lit);
            birchBranchMat.SetTexture("_BaseMap", birchTex);
            birchBranchMat.SetTexture("_BumpMap", birchBump);
            birchBranchMat.SetFloat("_BumpScale", 0.4f);
            birchBranchMat.SetFloat("_Smoothness", 0.7f);
            birchBranchMat.SetFloat("_Metallic", 0f);
            birchBranchMat.color = Color.white;
            SyncBarkMaterials();
        }
        else
        {
            Debug.LogError("ParametricTree: no shader resolved; trunk will use the renderer's default material.");
        }

        var leafGO = new GameObject("Leaves");
        leafGO.transform.SetParent(transform, false);
        leafFilter = leafGO.AddComponent<MeshFilter>();
        leafRenderer = leafGO.AddComponent<MeshRenderer>();
        var leafShader = Shader.Find("CrystalViz/LeafWind");
        if (leafShader != null)
        {
            var leafMat = new Material(leafShader);
            // Textured leaf: realistic broad-leaf PNG (texture.ninja,
            // foliage_47, CC0) with a center vein on a transparent
            // background, cut out by alpha test in the shader. The PNG is
            // normalized toward the old procedural leaf green so the
            // per-leaf green vertex colors grade it exactly as before.
            // Wind uniforms mirror the meadow grass so the canopy
            // shivers and catches the same traveling gust fronts.
            // v1.0.57: keep both foliage textures; the pine species swaps
            // in the procedural needle (thin silhouette, deep blue-green).
            broadleafTex = Resources.Load<Texture2D>("Textures/foliage_47_leaf");
            if (broadleafTex == null) broadleafTex = MakeLeafTexture();
            needleTex = MakeNeedleTexture();
            this.leafMat = leafMat;
            SyncLeafTexture();
            leafMat.SetFloat("_WindStrength", 0.025f);
            leafMat.SetFloat("_WindSpeed", 1.7f);
            leafMat.SetFloat("_GustStrength", 0.06f);
            leafMat.SetFloat("_GustSpeed", 1.8f);
            leafMat.SetFloat("_GustFreq", 0.18f);
            leafMat.SetFloat("_GustLighten", 0.28f);
            leafRenderer.material = leafMat;
            // The custom leaf shader has no shadow-caster pass; leaves are
            // small and dappled by the gust light-wave instead.
            leafRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        else
        {
            Debug.LogWarning("ParametricTree: 'CrystalViz/LeafWind' not found; leaves will use the default material.");
        }

        // v1.0.61: palm fronds — their own mesh so they can use an opaque
        // wind-blown material instead of the alpha-cutout leaf. Shares the
        // leaf shader's gust uniforms so the crown sways with the canopy.
        var frondGO = new GameObject("Fronds");
        frondGO.transform.SetParent(transform, false);
        frondFilter = frondGO.AddComponent<MeshFilter>();
        frondRenderer = frondGO.AddComponent<MeshRenderer>();
        frondMesh = new Mesh { name = "PalmFronds" };
        frondMesh.MarkDynamic();
        frondFilter.mesh = frondMesh;
        if (leafShader != null)
        {
            var whiteTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var whitePx = new Color[16];
            for (int i = 0; i < 16; i++) whitePx[i] = Color.white;
            whiteTex.SetPixels(whitePx);
            whiteTex.Apply();
            frondMat = new Material(leafShader);
            frondMat.SetTexture("_BaseMap", whiteTex);
            frondMat.SetFloat("_WindStrength", 0.035f);
            frondMat.SetFloat("_WindSpeed", 1.7f);
            frondMat.SetFloat("_GustStrength", 0.08f);
            frondMat.SetFloat("_GustSpeed", 1.8f);
            frondMat.SetFloat("_GustFreq", 0.18f);
            frondMat.SetFloat("_GustLighten", 0.28f);
            frondMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            frondRenderer.material = frondMat;
        }

        trunkMesh = new Mesh { name = "ParametricTrunk" };
        trunkMesh.MarkDynamic();
        trunkFilter.mesh = trunkMesh;
        leafMesh = new Mesh { name = "ParametricLeaves" };
        leafMesh.MarkDynamic();
        leafFilter.mesh = leafMesh;
    }

    /// <summary>
    /// v1.0.57: points the leaf material at the species' foliage texture.
    /// No-op until Initialize has built the material (species set earlier
    /// is still honored — Initialize calls this at the end).
    /// </summary>
    void SyncLeafTexture()
    {
        if (leafMat == null) return;
        leafMat.SetTexture("_BaseMap",
            species == TreeSpecies.Pine ? needleTex : broadleafTex);
    }

    /// <summary>
    /// v1.0.61: points the bark materials at the species' bark — birch gets
    /// its pale paper bark, everyone else the standard furrowed bark.
    /// No-op until Initialize has built the materials.
    /// </summary>
    void SyncBarkMaterials()
    {
        if (trunkRenderer == null) return;
        bool birch = species == TreeSpecies.Birch;
        if (birch && birchTrunkMat != null && birchBranchMat != null)
            trunkRenderer.materials = new Material[] { birchTrunkMat, birchBranchMat };
        else if (trunkMat != null && branchMat != null)
            trunkRenderer.materials = new Material[] { trunkMat, branchMat };
    }

    /// <summary>
    /// v1.0.61: switches the grown species and rebuilds the tree at the
    /// current growth value. Safe to call before Initialize (the species
    /// field is read when the meshes build).
    /// </summary>
    public void SetSpecies(TreeSpecies s)
    {
        species = s;
        SyncLeafTexture();
        SyncBarkMaterials();
        lastBuiltG = -1f; // force a rebuild even at the same g
        if (trunkMesh != null) SetGrowth(currentG);
    }

    // ------------------------------------------------------- bark texture

    const int BarkTexSize = 256;
    const int BarkRidgeColumns = 14; // ridges around the trunk (u tiles seamlessly)
    const int BranchRidgeColumns = 8; // ridges around branches: chunkier so they read on thin tubes

    /// <summary>
    /// Builds a tileable dark-brown bark albedo: vertical ridges (ridged
    /// multifractal noise, stretched along the trunk) running from dark
    /// valleys to lighter ridge tops. Tileable in u (around the trunk) via
    /// a periodic lattice; v tiles freely with Repeat wrap.
    /// Returns the albedo texture and outputs the raw height field for the bump map.
    /// </summary>
    static Texture2D MakeBarkTexture(out float[] height, int ridgeColumns)
    {
        int S = BarkTexSize;
        height = new float[S * S];
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        var valley = new Color(0.10f, 0.062f, 0.036f, 1f); // deep crevice brown
        var ridge = new Color(0.30f, 0.185f, 0.105f, 1f);  // sunlit ridge top

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = (float)x / S;
                float v = (float)y / S;
                // Ridges run along the trunk (v): high frequency around (u),
                // stretched vertically.
                float n = BarkFbm(u * ridgeColumns, v * ridgeColumns * 0.28f, ridgeColumns);
                float r = 1f - Mathf.Abs(2f * n - 1f); // ridged multifractal
                r = r * r;
                // Fine grain so large flat areas don't read as plastic.
                float grain = Hash2(x * 7 + 13, y * 7 + 71, 999) * 0.14f;
                float h = Mathf.Clamp01(r * 0.92f + grain * 0.35f);
                height[y * S + x] = h;
                // Crevices sink darker for extra depth.
                float crevice = Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 0.55f, h));
                tex.SetPixel(x, y, (Color.Lerp(valley, ridge, h) * crevice));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// v1.0.61: birch paper bark — a pale, near-white base with soft vertical
    /// tonal variation and the dark horizontal lenticel dashes that make a
    /// birch read as a birch. Tileable in u via the same periodic lattice.
    /// </summary>
    static Texture2D MakeBirchBarkTexture(out float[] height)
    {
        int S = BarkTexSize;
        height = new float[S * S];
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        var paper = new Color(0.87f, 0.85f, 0.80f, 1f); // sunlit paper-white
        var shade = new Color(0.70f, 0.68f, 0.63f, 1f); // shaded paper
        var dash = new Color(0.15f, 0.12f, 0.10f, 1f);  // dark lenticel dash

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = (float)x / S;
                float v = (float)y / S;
                // Soft tonal variation stretched along the trunk.
                float n = BarkFbm(u * 6f, v * 40f, 6);
                // Lenticel dashes: high frequency around the trunk, low along
                // it, thresholded into short horizontal streaks.
                float d = BarkFbm(u * 60f, v * 9f, 60);
                float dashMask = Mathf.SmoothStep(0.60f, 0.72f, d)
                               * Mathf.SmoothStep(0.25f, 0.45f, n);
                float h = Mathf.Clamp01(n * 0.6f + dashMask * 0.5f);
                height[y * S + x] = h;
                Color c = Color.Lerp(paper, shade, n * 0.85f);
                c = Color.Lerp(c, dash, dashMask);
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// v1.0.57: a tuft of pine needles — vertical streaks across the
    /// FULL quad (no transparency). The first version used a thin sliver
    /// on a transparent background, but at needle-quad render sizes the
    /// sliver went sub-pixel and the shader's alpha test discarded it,
    /// leaving bare branches in the proof shot. A full-quad tuft survives
    /// filtering at any distance and still reads as needles.
    /// NOTE: kept BRIGHT on purpose — the leaf shader multiplies the
    /// texture by the per-leaf vertex color (the broadleaf PNG follows
    /// the same convention), so a dark texture here would render
    /// nearly black. The vertex color grades this down to the intended
    /// deep blue-green.
    /// </summary>
    static Texture2D MakeNeedleTexture()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = (float)x / (S - 1);
                float v = (float)y / (S - 1); // 0 base .. 1 tip
                // Five vertical needle streaks across the quad.
                float streak = 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 5f + 1.3f);
                float shade = 0.82f + 0.18f * streak;
                // Slight taper: tips a touch lighter, like sunlit needles.
                float tipLight = 0.92f + 0.08f * v;
                // Soft vertical edge fade so quads don't show hard borders.
                float edge = Mathf.SmoothStep(0f, 0.08f, u)
                           * Mathf.SmoothStep(1f, 0.92f, u);
                float a = 0.55f + 0.45f * edge;
                Vector3 c = new Vector3(0.72f, 0.95f, 0.70f)
                          * shade * tipLight;
                tex.SetPixel(x, y, new Color(c.x, c.y, c.z, a));
            }
        }
        tex.Apply();
        return tex;
    }

    static Texture2D MakeBumpTexture(float[] height, int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        for (int i = 0; i < height.Length; i++)
        {
            float h = height[i];
            tex.SetPixel(i % size, i / size, new Color(h, h, h, 1f));
        }
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// Builds a small leaf albedo: a pointed-oval leaf silhouette with a
    /// lighter center vein and a darker rim, in leaf green. Transparent
    /// background for alpha-test cutout (no more flat square quads).
    /// NOTE: the green is baked into the texture because URP/Lit ignores
    /// mesh vertex colors — the old 1x1 green texture worked the same way.
    /// </summary>
    static Texture2D MakeLeafTexture()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        // Leaf green, matching the old 1x1 base color (0.25, 0.55, 0.18).
        Color baseGreen = new Color(0.27f, 0.56f, 0.19f);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = (float)x / (S - 1); // 0..1 across
                float v = (float)y / (S - 1); // 0 stem .. 1 tip
                // Pointed-oval width profile: narrow at stem and tip,
                // widest about a third of the way up.
                float w = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(1f - v, 0.75f)), 0.8f) * 0.5f;
                float d = Mathf.Abs(u - 0.5f);
                if (d >= w)
                {
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                    continue;
                }
                float vein = 1f - Mathf.SmoothStep(0f, 0.035f + (1f - v) * 0.02f, d);
                float rim = Mathf.SmoothStep(w * 0.72f, w, d);
                // Lighter yellow-green vein, darker rim for shape.
                Color c = baseGreen * (1f - 0.22f * rim) + new Color(0.10f, 0.12f, 0.02f) * vein;
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, 1f));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>fBm over a lattice that wraps every <paramref name="xPeriod"/> cells in x.</summary>
    static float BarkFbm(float x, float y, int xPeriod)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int o = 0; o < 4; o++)
        {
            float f = 1 << o;
            sum += amp * ValueNoise(x * f, y * f, xPeriod * (1 << o), 1000 + o * 77);
            norm += amp;
            amp *= 0.5f;
        }
        return sum / norm;
    }

    static float ValueNoise(float x, float y, int xPeriod, int seed)
    {
        int xi = Mathf.FloorToInt(x);
        float xf = x - xi;
        int yi = Mathf.FloorToInt(y);
        float yf = y - yi;
        float u = xf * xf * (3f - 2f * xf);
        float v = yf * yf * (3f - 2f * yf);
        int x0 = ((xi % xPeriod) + xPeriod) % xPeriod;
        int x1 = (x0 + 1) % xPeriod;
        float a = Hash2(x0, yi, seed);
        float b = Hash2(x1, yi, seed);
        float c = Hash2(x0, yi + 1, seed);
        float d = Hash2(x1, yi + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    static float Hash2(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 974634);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
    }

    /// <summary>World-space top of the tree crown (trunk tips + foliage).</summary>
    public Vector3 CrownTop
    {
        get
        {
            float topY = 0f;
            if (trunkMesh != null) topY = Mathf.Max(topY, trunkMesh.bounds.max.y);
            if (leafMesh != null) topY = Mathf.Max(topY, leafMesh.bounds.max.y);
            if (frondMesh != null) topY = Mathf.Max(topY, frondMesh.bounds.max.y);
            return transform.TransformPoint(new Vector3(0f, topY, 0f));
        }
    }

    /// <summary>
    /// Rebuilds the tree at growth g in [0,1]. Cheap no-op when g barely
    /// changed since the last build (the growth controller animates smoothly
    /// and calls this every frame during the animation).
    /// </summary>
    public void SetGrowth(float g)
    {
        g = Mathf.Clamp01(g);
        currentG = g;
        if (trunkMesh == null)
        {
            Debug.LogWarning("ParametricTree.SetGrowth: trunkMesh is null — Awake did not complete; cannot build tree.");
            return;
        }
        if (Mathf.Abs(g - lastBuiltG) < RebuildEpsilon) return;
        lastBuiltG = g;

        var rng = new System.Random(Seed);
        // v1.0.61: fronds only exist on the palm — clear any crown left over
        // from a previous palm before another species builds.
        ClearFrondMesh();
        if (species == TreeSpecies.Pine)
        {
            // v1.0.57: conifer path — straight trunk, whorled tiers,
            // needle foliage. Same engine, different recipe.
            float pineLen = Mathf.Lerp(0.3f, 5.2f, g);
            float pineRad = Mathf.Lerp(0.03f, 0.22f, g);
            BuildPineMeshes(pineLen, pineRad, g, rng);
            return;
        }
        // v1.0.61: palm path — curving trunk with a crown of arching fronds.
        if (species == TreeSpecies.Palm)
        {
            float palmLen = Mathf.Lerp(0.3f, 4.4f, g);
            float palmRad = Mathf.Lerp(0.03f, 0.24f, g);
            BuildPalmMeshes(palmLen, palmRad, g, rng);
            return;
        }

        // Broadleaf family (oak, birch, willow, cherry): the same engine,
        // driven by the species recipe (size, foliage, droop).
        SpeciesRecipe recipe = GetRecipe(species);
        float trunkLen = Mathf.Lerp(0.3f, recipe.trunkLen, g);
        float trunkRad = Mathf.Lerp(0.03f, recipe.trunkRad, g);
        int maxLevel = Mathf.FloorToInt(g * 3.99f); // 0..3 tiers of branches above the trunk

        Vector3 lean = new Vector3(
            ((float)rng.NextDouble() - 0.5f) * 0.12f, 1f,
            ((float)rng.NextDouble() - 0.5f) * 0.12f).normalized;

        BuildTrunkMesh(trunkLen, trunkRad, maxLevel, g, lean, rng, recipe.droop);
        BuildLeafMesh(g, new System.Random(Seed + 1), species);
    }

    // ------------------------------------------------------------ pine mesh

    /// <summary>
    /// v1.0.57: builds the conifer — a straight trunk with whorled tiers
    /// of branches. Lower tiers are longer and carry more branches, so the
    /// silhouette reads as a classic pine cone. Needle anchors run along
    /// each branch (not just the tips) so the foliage clothes the limbs.
    /// </summary>
    void BuildPineMeshes(float trunkLen, float trunkRad, float g, System.Random rng)
    {
        vList.Clear(); nList.Clear(); uvList.Clear(); cList.Clear(); tList.Clear();
        bvList.Clear(); bnList.Clear(); buvList.Clear(); bcList.Clear(); btList.Clear();
        tips.Clear();

        // Trunk: straight up, near-zero wobble (conifers don't lean).
        Vector3 top = new Vector3(0f, trunkLen, 0f);
        Vector3[] tctrl =
        {
            Vector3.zero, Vector3.zero,
            new Vector3(0.02f, trunkLen * 0.33f, -0.015f),
            new Vector3(-0.015f, trunkLen * 0.66f, 0.02f),
            top, top
        };
        int rings = Mathf.Max(6, Mathf.RoundToInt(trunkLen * 5f));
        Vector3[] tpts = new Vector3[rings];
        for (int i = 0; i < rings; i++)
            tpts[i] = SampleCurve(tctrl, (float)i / (rings - 1));
        AppendTube(tpts, trunkRad, trunkRad * 0.25f, BranchTint(rng), true, true);

        // Whorled tiers: 3 at sprout, up to 9 at full growth.
        int tiers = 3 + Mathf.FloorToInt(g * 6.99f);
        float azimBase = (float)rng.NextDouble() * Mathf.PI * 2f;
        for (int i = 0; i < tiers; i++)
        {
            float hFrac = tiers == 1 ? 0.6f : 0.30f + 0.62f * ((float)i / (tiers - 1));
            Vector3 tierOrigin = new Vector3(0f, trunkLen * hFrac, 0f);
            float tierLen = 2.3f * (1f - hFrac * 0.78f) * (0.2f + 0.8f * g);
            if (tierLen < 0.15f) continue;
            int count = Mathf.Max(3, 6 - (i * 4) / tiers);
            for (int b = 0; b < count; b++)
            {
                float az = azimBase + i * 0.7f + b * GoldenAngle;
                Vector3 dir = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
                GrowPineBranch(tierOrigin, dir, tierLen, trunkRad, rng);
            }
        }
        AssignTrunkMesh();
        BuildLeafMesh(g, new System.Random(Seed + 1), species);
    }

    /// <summary>
    /// One pine limb: outward with a slight droop, tip sweeping skyward
    /// like a real conifer branch. Needle anchors clothe the whole limb.
    /// </summary>
    void GrowPineBranch(Vector3 origin, Vector3 dir, float length,
                        float trunkRad, System.Random rng)
    {
        dir.Normalize();
        Vector3 up = Vector3.up;
        Vector3 p0 = origin;
        Vector3 p1 = origin + dir * (length * 0.45f) - up * (length * 0.07f);
        Vector3 p2 = origin + dir * (length * 0.80f) - up * (length * 0.02f);
        Vector3 p3 = origin + dir * length + up * (length * 0.20f);
        Vector3[] ctrl = { p0, p0, p1, p2, p3, p3 };

        int n = Mathf.Max(4, Mathf.RoundToInt(length * 6f));
        Vector3[] pts = new Vector3[n];
        for (int i = 0; i < n; i++)
            pts[i] = SampleCurve(ctrl, (float)i / (n - 1));

        float r0 = Mathf.Max(0.015f, trunkRad * 0.35f * (length / 2.3f + 0.3f));
        AppendTube(pts, r0, r0 * 0.3f, BranchTint(rng), false, false);

        // v1.0.57: needle anchors clothe the whole limb — six puffs per
        // branch so the tiers read dense, not bare.
        tips.Add(SampleCurve(ctrl, 0.20f));
        tips.Add(SampleCurve(ctrl, 0.38f));
        tips.Add(SampleCurve(ctrl, 0.56f));
        tips.Add(SampleCurve(ctrl, 0.74f));
        tips.Add(SampleCurve(ctrl, 0.90f));
        tips.Add(p3);
    }

    // ------------------------------------------------------------ palm mesh

    /// <summary>
    /// v1.0.61: builds the palm — a gently curving trunk with a crown of
    /// arching fronds. The leaf mesh stays empty for palms; the fronds live
    /// on their own opaque, wind-blown mesh (the Fronds child).
    /// </summary>
    void BuildPalmMeshes(float trunkLen, float trunkRad, float g, System.Random rng)
    {
        vList.Clear(); nList.Clear(); uvList.Clear(); cList.Clear(); tList.Clear();
        bvList.Clear(); bnList.Clear(); buvList.Clear(); bcList.Clear(); btList.Clear();
        tips.Clear();

        // Trunk: gentle S-curve with a fixed lean direction (palms lean).
        float leanAmt = 0.35f + (float)rng.NextDouble() * 0.2f;
        float leanAz = (float)rng.NextDouble() * Mathf.PI * 2f;
        Vector3 leanDir = new Vector3(Mathf.Cos(leanAz), 0f, Mathf.Sin(leanAz));
        Vector3 top = leanDir * (trunkLen * leanAmt * 0.5f) + new Vector3(0f, trunkLen, 0f);
        Vector3[] tctrl =
        {
            Vector3.zero, Vector3.zero,
            leanDir * (trunkLen * leanAmt * 0.12f) + new Vector3(0f, trunkLen * 0.33f, 0f),
            leanDir * (trunkLen * leanAmt * 0.35f) + new Vector3(0f, trunkLen * 0.66f, 0f),
            top, top
        };
        int rings = Mathf.Max(6, Mathf.RoundToInt(trunkLen * 5f));
        Vector3[] tpts = new Vector3[rings];
        for (int i = 0; i < rings; i++)
            tpts[i] = SampleCurve(tctrl, (float)i / (rings - 1));
        AppendTube(tpts, trunkRad, trunkRad * 0.45f, BranchTint(rng), true, true);
        AssignTrunkMesh();

        // Empty leaf mesh (no broadleaf foliage on a palm)…
        vList.Clear(); nList.Clear(); uvList.Clear(); cList.Clear(); tList.Clear();
        AssignMesh(leafMesh, vList, nList, uvList, cList, tList);
        // …and the frond crown on top.
        BuildFrondMesh(g, top, new System.Random(Seed + 2));
    }

    /// <summary>
    /// v1.0.61: empties the frond mesh (used when a non-palm species builds).
    /// </summary>
    void ClearFrondMesh()
    {
        if (frondMesh == null) return;
        fvList.Clear(); fnList.Clear(); fuvList.Clear(); fcList.Clear(); ftList.Clear();
        AssignMesh(frondMesh, fvList, fnList, fuvList, fcList, ftList);
    }

    /// <summary>
    /// v1.0.61: the palm crown — fronds unfurl once the trunk is established,
    /// from 4 stubby leaves at sprout to a full 12-frond crown at maturity.
    /// </summary>
    void BuildFrondMesh(float g, Vector3 crown, System.Random rng)
    {
        fvList.Clear(); fnList.Clear(); fuvList.Clear(); fcList.Clear(); ftList.Clear();
        if (frondMesh == null) return;
        float f = Smooth01((g - 0.3f) / 0.7f);
        int fronds = Mathf.RoundToInt(Mathf.Lerp(4f, 12f, f));
        float frondLen = Mathf.Lerp(0.4f, 2.3f, g);
        float azimBase = (float)rng.NextDouble() * Mathf.PI * 2f;
        for (int i = 0; i < fronds; i++)
        {
            float az = azimBase + i * GoldenAngle;
            Vector3 dir = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
            AppendFrond(crown, dir, frondLen * (0.85f + (float)rng.NextDouble() * 0.3f), rng);
        }
        AssignMesh(frondMesh, fvList, fnList, fuvList, fcList, ftList);
    }

    /// <summary>
    /// v1.0.61: one arching frond — rises off the crown, crests, then droops,
    /// built as a tapered ribbon so it reads as a palm leaf rather than
    /// scattered quads. Normals face up (the ribbon is near-horizontal) and
    /// the material is double-sided so the drooping tips read from below.
    /// </summary>
    void AppendFrond(Vector3 crown, Vector3 dir, float len, System.Random rng)
    {
        const int segs = 9;
        Vector3 side = new Vector3(-dir.z, 0f, dir.x).normalized;
        float rise = len * 0.35f;
        float droopAmt = len * 0.75f;
        float width = len * 0.16f;
        float v = 0.85f + (float)rng.NextDouble() * 0.3f; // per-frond green variation
        Color frondGreen = new Color(0.14f * v, 0.38f * v, 0.12f * v, 1f);
        int baseIdx = fvList.Count;
        for (int sIdx = 0; sIdx <= segs; sIdx++)
        {
            float t = (float)sIdx / segs;
            Vector3 center = crown
                + dir * (t * len)
                + Vector3.up * (Mathf.Sin(t * Mathf.PI * 0.62f) * rise - t * t * droopAmt);
            // Narrow at the stem, widest just past the middle, pointed tip.
            float w = width * Mathf.Sin(Mathf.PI * Mathf.Clamp01(0.12f + t * 0.88f));
            fvList.Add(center - side * w * 0.5f);
            fvList.Add(center + side * w * 0.5f);
            fnList.Add(Vector3.up);
            fnList.Add(Vector3.up);
            fuvList.Add(new Vector2(t, 0f));
            fuvList.Add(new Vector2(t, 1f));
            fcList.Add(frondGreen);
            fcList.Add(frondGreen);
        }
        for (int sIdx = 0; sIdx < segs; sIdx++)
        {
            int a = baseIdx + sIdx * 2;
            ftList.Add(a); ftList.Add(a + 1); ftList.Add(a + 2);
            ftList.Add(a + 1); ftList.Add(a + 3); ftList.Add(a + 2);
        }
    }

    // ------------------------------------------------------------ trunk mesh

    void BuildTrunkMesh(float trunkLen, float trunkRad, int maxLevel, float g,
                        Vector3 lean, System.Random rng, float droop = 0f)
    {
        vList.Clear(); nList.Clear(); uvList.Clear(); cList.Clear(); tList.Clear();
        bvList.Clear(); bnList.Clear(); buvList.Clear(); bcList.Clear(); btList.Clear();
        tips.Clear();
        GrowBranch(Vector3.zero, lean, trunkLen, trunkRad, 0, maxLevel, g, rng, rootFlare: true, droop: droop);
        AssignTrunkMesh();
    }

    /// <summary>
    /// Merges the trunk and branch streams into one vertex buffer and assigns
    /// two submeshes: 0 = trunk (fine bark), 1 = branches (chunky bark).
    /// Branch triangle indices rebase past the trunk vertices.
    /// </summary>
    void AssignTrunkMesh()
    {
        int tv = vList.Count;
        var V = new List<Vector3>(tv + bvList.Count);
        V.AddRange(vList); V.AddRange(bvList);
        var N = new List<Vector3>(tv + bnList.Count);
        N.AddRange(nList); N.AddRange(bnList);
        var U = new List<Vector2>(tv + buvList.Count);
        U.AddRange(uvList); U.AddRange(buvList);
        var C = new List<Color>(tv + bcList.Count);
        C.AddRange(cList); C.AddRange(bcList);
        var BT = new List<int>(btList.Count);
        for (int i = 0; i < btList.Count; i++) BT.Add(btList[i] + tv);

        trunkMesh.Clear();
        trunkMesh.subMeshCount = 2;
        trunkMesh.SetVertices(V);
        trunkMesh.SetNormals(N);
        trunkMesh.SetUVs(0, U);
        trunkMesh.SetColors(C);
        trunkMesh.SetTriangles(tList, 0);
        trunkMesh.SetTriangles(BT, 1);
        trunkMesh.RecalculateBounds(); // correct frustum culling for procedural geometry
    }

    /// <summary>
    /// Recursively grows one curved, tapered branch tube and spawns children
    /// along its outer portion. Branches with no children record their tip
    /// for leaf placement.
    /// </summary>
    void GrowBranch(Vector3 origin, Vector3 dir, float length, float baseRadius,
                    int level, int maxLevel, float g, System.Random rng, bool rootFlare = false,
                    float droop = 0f)
    {
        dir.Normalize();

        // Curved control points: gentle random wobble plus an upward bend so
        // tips reach skyward like real phototropic growth.
        // v1.0.61: droop flips the lift downward for weeping species
        // (willow) — 0 = tips reach skyward, 1 = branches arc down in curtains.
        Vector3 up = Vector3.up;
        Vector3 side = Vector3.Cross(dir, up);
        if (side.sqrMagnitude < 1e-6f) side = Vector3.right; else side.Normalize();
        Vector3 side2 = Vector3.Cross(dir, side).normalized;

        float wob = length * 0.07f;
        float lift0 = Mathf.Lerp(0.05f, -0.12f, droop);
        float lift1 = Mathf.Lerp(0.14f, -0.32f, droop);
        float lift2 = Mathf.Lerp(0.30f, -0.60f, droop);
        Vector3 p0 = origin;
        Vector3 p1 = origin + dir * (length * 0.33f) + RandPerp(rng, side, side2, wob) + up * (length * lift0);
        Vector3 p2 = origin + dir * (length * 0.66f) + RandPerp(rng, side, side2, wob) + up * (length * lift1);
        Vector3 p3 = origin + dir * length + up * (length * lift2);
        // Doubled endpoints make the Catmull-Rom spline pass through p0/p3.
        Vector3[] ctrl = { p0, p0, p1, p2, p3, p3 };

        int rings = Mathf.Max(5, Mathf.RoundToInt(length * 5f));
        Vector3[] pts = new Vector3[rings];
        for (int i = 0; i < rings; i++)
            pts[i] = SampleCurve(ctrl, (float)i / (rings - 1));

        float tipRadius = baseRadius * 0.32f;
        AppendTube(pts, baseRadius, tipRadius, BranchTint(rng), rootFlare, level == 0);

        // Leaf anchors along the outer part of every branch (not just the
        // tips) so the canopy fills in as one lush mass instead of puffs
        // floating at the branch ends.
        if (level == 0)
        {
            tips.Add(SampleCurve(ctrl, 0.80f));
            tips.Add(SampleCurve(ctrl, 0.93f));
        }
        else
        {
            tips.Add(SampleCurve(ctrl, 0.55f));
            tips.Add(SampleCurve(ctrl, 0.85f));
        }

        if (level >= maxLevel)
        {
            tips.Add(p3);
            return;
        }

        // Children along the outer half of the branch, golden-angle spaced.
        int childCount = level == 0 ? 2 + Mathf.RoundToInt(g * 5f)
                       : level == 1 ? 1 + Mathf.RoundToInt(g * 3f)
                       : 1 + Mathf.RoundToInt(g * 2f);
        float azim0 = (float)rng.NextDouble() * Mathf.PI * 2f;
        for (int c = 0; c < childCount; c++)
        {
            float t = childCount == 1 ? 0.72f : 0.5f + 0.45f * ((float)c / (childCount - 1));
            Vector3 pos = SampleCurve(ctrl, t);
            Vector3 tan = CurveTangent(ctrl, t).normalized;

            // Child direction: tilt outward from the parent tangent by
            // 31-54 degrees, at a golden-angle azimuth around it.
            Vector3 refA = Vector3.Cross(tan, Vector3.up);
            if (refA.sqrMagnitude < 1e-6f) refA = Vector3.right; else refA.Normalize();
            Vector3 refB = Vector3.Cross(tan, refA).normalized;
            float az = azim0 + c * GoldenAngle;
            float elev = 0.55f + (float)rng.NextDouble() * 0.4f;
            // v1.0.61: weeping children tilt outward-down instead of
            // outward-up, so the curtains compound down the generations.
            elev = Mathf.Lerp(elev, 1.15f + (float)rng.NextDouble() * 0.35f, droop);
            Vector3 childDir = (tan * Mathf.Cos(elev)
                + (refA * Mathf.Cos(az) + refB * Mathf.Sin(az)) * Mathf.Sin(elev)).normalized;

            float parentR = Mathf.Lerp(baseRadius, tipRadius, t);
            float childLen = length * (0.55f + (float)rng.NextDouble() * 0.2f) * (level == 0 ? 1f : 0.85f);
            GrowBranch(pos, childDir, childLen, parentR * 0.55f, level + 1, maxLevel, g, rng, droop: droop);
        }
    }

    /// <summary>
    /// Appends a tapered tube along pts using parallel-transport frames
    /// (twist-free). Triangle winding is outward-facing for right-handed
    /// (tangent, normal, binormal) frames. Trunk tubes (isTrunk) accumulate
    /// into the trunk streams; branches accumulate into the branch streams so
    /// they can wear the chunkier branch bark as a second submesh.
    /// </summary>
    void AppendTube(Vector3[] pts, float r0, float r1, Color tint, bool rootFlare, bool isTrunk)
    {
        List<Vector3> V = isTrunk ? vList : bvList;
        List<Vector3> Nr = isTrunk ? nList : bnList;
        List<Vector2> Uv = isTrunk ? uvList : buvList;
        List<Color> Cl = isTrunk ? cList : bcList;
        List<int> Tr = isTrunk ? tList : btList;

        int n = pts.Length;
        Vector3[] T = new Vector3[n], N = new Vector3[n], B = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 a = pts[Mathf.Max(0, i - 1)];
            Vector3 b = pts[Mathf.Min(n - 1, i + 1)];
            T[i] = (b - a).normalized;
        }
        Vector3 ref0 = Mathf.Abs(T[0].y) > 0.94f ? Vector3.right : Vector3.up;
        N[0] = Vector3.Cross(T[0], ref0).normalized;
        B[0] = Vector3.Cross(T[0], N[0]).normalized;
        for (int i = 1; i < n; i++)
        {
            // Parallel transport: drop the tangential component, renormalize.
            N[i] = (N[i - 1] - T[i] * Vector3.Dot(N[i - 1], T[i])).normalized;
            B[i] = Vector3.Cross(T[i], N[i]).normalized;
        }

        int ringSize = RadialSegments + 1;
        int baseIdx = V.Count;
        float vAcc = 0f;
        for (int i = 0; i < n; i++)
        {
            if (i > 0) vAcc += Vector3.Distance(pts[i], pts[i - 1]);
            float f = (float)i / (n - 1);
            float r = Mathf.Lerp(r0, r1, f);
            if (rootFlare) r *= 1f + 0.55f * Mathf.Exp(-f * 9f); // root flare at the base
            Color c = tint * Mathf.Lerp(0.7f, 1.04f, f);         // darker at the base
            for (int j = 0; j <= RadialSegments; j++)
            {
                float ang = (float)j / RadialSegments * Mathf.PI * 2f;
                Vector3 radial = N[i] * Mathf.Cos(ang) + B[i] * Mathf.Sin(ang);
                V.Add(pts[i] + radial * r);
                Nr.Add(radial);
                Uv.Add(new Vector2((float)j / RadialSegments, vAcc * 2.0f)); // dense tiling: bark ridges stay crisp along the trunk
                Cl.Add(c);
            }
        }
        for (int i = 0; i < n - 1; i++)
        {
            for (int j = 0; j < RadialSegments; j++)
            {
                int a0 = baseIdx + i * ringSize + j;
                int b0 = a0 + ringSize;
                Tr.Add(a0); Tr.Add(a0 + 1); Tr.Add(b0);
                Tr.Add(a0 + 1); Tr.Add(b0 + 1); Tr.Add(b0);
            }
        }
    }

    // ------------------------------------------------------------- leaf mesh

    void BuildLeafMesh(float g, System.Random rng, TreeSpecies sp)
    {
        vList.Clear(); nList.Clear(); uvList.Clear(); cList.Clear(); tList.Clear();

        // Leaves only appear once the trunk is established (g > 0.25).
        // 12,000 at full growth (15x the old 800 — the same jump the grass
        // field made from 10k to 150k tufts), each leaf a third of its old
        // size, so the canopy reads as one dense lush mass.
        // v1.0.57: pines get 14,000 smaller needles in deep blue-green.
        // v1.0.61: density comes from the species recipe (willow densest,
        // birch airiest).
        bool pine = sp == TreeSpecies.Pine;
        float f = Smooth01((g - 0.25f) / 0.75f);
        float fullCount = pine ? 14000f : GetRecipe(sp).leafCount;
        int total = Mathf.RoundToInt(fullCount * f);
        if (total > 0 && tips.Count > 0)
        {
            int per = total / tips.Count;
            int rem = total % tips.Count;
            for (int i = 0; i < tips.Count; i++)
            {
                int count = per + (i < rem ? 1 : 0);
                for (int k = 0; k < count; k++)
                    AppendLeaf(tips[i], rng, sp);
            }
        }
        AssignMesh(leafMesh, vList, nList, uvList, cList, tList);
    }

    void AppendLeaf(Vector3 tip, System.Random rng, TreeSpecies sp)
    {
        // v1.0.57: needles cluster tighter and run smaller than broad leaves.
        // v1.0.61: size, spread, and tint come from the species recipe —
        // cherry blooms pink, birch glows light, willow hangs fine and bright.
        bool pine = sp == TreeSpecies.Pine;
        SpeciesRecipe recipe = GetRecipe(sp);
        Vector3 center = tip + RandomInSphere(rng, pine ? 0.50f : recipe.leafSpread);
        float s = pine ? 0.028f + (float)rng.NextDouble() * 0.024f
                       : recipe.leafSize + (float)rng.NextDouble() * (recipe.leafSize * 1.07f);
        Quaternion q = RandomQuat(rng);
        Vector3 n = q * Vector3.forward;
        Vector3[] corners =
        {
            new Vector3(-s, -s, 0f), new Vector3(s, -s, 0f),
            new Vector3(s, s, 0f), new Vector3(-s, s, 0f),
        };
        int b = vList.Count;
        for (int i = 0; i < 4; i++)
        {
            vList.Add(center + q * corners[i]);
            nList.Add(n);
        }
        uvList.Add(new Vector2(0f, 0f)); uvList.Add(new Vector2(1f, 0f));
        uvList.Add(new Vector2(1f, 1f)); uvList.Add(new Vector2(0f, 1f));
        float v = 0.8f + (float)rng.NextDouble() * 0.5f; // per-leaf green variation
        // v1.0.57: pine needles grade deep blue-green; broad leaves keep
        // the original green. v1.0.61: the recipe tints each species.
        Color tintBase = pine ? new Color(0.10f, 0.30f, 0.17f, 1f) : recipe.leafColor;
        Color gc = new Color(tintBase.r * v, tintBase.g * v, tintBase.b * v, 1f);
        for (int i = 0; i < 4; i++) cList.Add(gc);
        tList.Add(b); tList.Add(b + 1); tList.Add(b + 2);
        tList.Add(b); tList.Add(b + 2); tList.Add(b + 3);
    }

    // ---------------------------------------------------------------- helpers

    static void AssignMesh(Mesh mesh, List<Vector3> verts, List<Vector3> norms,
                           List<Vector2> uvs, List<Color> colors, List<int> tris)
    {
        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds(); // correct frustum culling for procedural geometry
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>Sample a 6-point curve with doubled endpoints; t in [0,1].</summary>
    static Vector3 SampleCurve(Vector3[] ctrl, float t)
    {
        float ft = Mathf.Clamp01(t) * 3f;
        int seg = Mathf.Min(2, Mathf.FloorToInt(ft));
        float lt = ft - seg;
        return CatmullRom(ctrl[seg], ctrl[seg + 1], ctrl[seg + 2], ctrl[seg + 3], lt);
    }

    static Vector3 CurveTangent(Vector3[] ctrl, float t)
    {
        const float eps = 0.01f;
        return SampleCurve(ctrl, Mathf.Min(1f, t + eps)) - SampleCurve(ctrl, Mathf.Max(0f, t - eps));
    }

    static Vector3 RandPerp(System.Random rng, Vector3 a, Vector3 b, float scale)
    {
        return (a * ((float)rng.NextDouble() - 0.5f) + b * ((float)rng.NextDouble() - 0.5f)) * scale;
    }

    static Color BranchTint(System.Random rng)
    {
        float v = 0.92f + (float)rng.NextDouble() * 0.16f;
        return new Color(v, v * (0.97f + (float)rng.NextDouble() * 0.05f), v, 1f);
    }

    /// <summary>Uniform random unit quaternion (Shoemake).</summary>
    static Quaternion RandomQuat(System.Random rng)
    {
        float u1 = (float)rng.NextDouble();
        float u2 = (float)rng.NextDouble();
        float u3 = (float)rng.NextDouble();
        float a = Mathf.Sqrt(1f - u1);
        float b = Mathf.Sqrt(u1);
        float t2 = Mathf.PI * 2f * u2;
        float t3 = Mathf.PI * 2f * u3;
        return new Quaternion(
            a * Mathf.Sin(t2), a * Mathf.Cos(t2),
            b * Mathf.Sin(t3), b * Mathf.Cos(t3));
    }

    static Vector3 RandomInSphere(System.Random rng, float radius)
    {
        double u = rng.NextDouble() * 2.0 - 1.0;
        double phi = rng.NextDouble() * System.Math.PI * 2.0;
        double rr = radius * System.Math.Pow(rng.NextDouble(), 1.0 / 3.0);
        double s = System.Math.Sqrt(1.0 - u * u);
        return new Vector3(
            (float)(rr * s * System.Math.Cos(phi)),
            (float)(rr * u),
            (float)(rr * s * System.Math.Sin(phi)));
    }

    static float Smooth01(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));
}
