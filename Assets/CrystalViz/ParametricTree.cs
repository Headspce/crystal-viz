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

    float lastBuiltG = -1f;

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

    bool initialized;

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
            var trunkMat = new Material(lit);
            float[] barkHeight;
            var barkTex = MakeBarkTexture(out barkHeight, BarkRidgeColumns);
            trunkMat.SetTexture("_BaseMap", barkTex);
            var barkBump = MakeBumpTexture(barkHeight, BarkTexSize);
            trunkMat.SetTexture("_BumpMap", barkBump);
            trunkMat.SetFloat("_BumpScale", 0.6f);
            trunkMat.SetFloat("_Smoothness", 0.8f); // roughness ~0.2: soft sheen, not chalk
            trunkMat.SetFloat("_Metallic", 0f);
            trunkMat.color = Color.white;
            // Branches (submesh 1) wear the same dark-brown ridged bark with
            // chunkier ridges so the texture reads on thin tubes.
            var branchMat = new Material(lit);
            float[] branchHeight;
            var branchTex = MakeBarkTexture(out branchHeight, BranchRidgeColumns);
            branchMat.SetTexture("_BaseMap", branchTex);
            var branchBump = MakeBumpTexture(branchHeight, BarkTexSize);
            branchMat.SetTexture("_BumpMap", branchBump);
            branchMat.SetFloat("_BumpScale", 0.6f);
            branchMat.SetFloat("_Smoothness", 0.8f);
            branchMat.SetFloat("_Metallic", 0f);
            branchMat.color = Color.white;
            trunkRenderer.materials = new Material[] { trunkMat, branchMat };
        }
        else
        {
            Debug.LogError("ParametricTree: no shader resolved; trunk will use the renderer's default material.");
        }

        var leafGO = new GameObject("Leaves");
        leafGO.transform.SetParent(transform, false);
        leafFilter = leafGO.AddComponent<MeshFilter>();
        leafRenderer = leafGO.AddComponent<MeshRenderer>();
        if (lit != null)
        {
            var leafMat = new Material(lit);
            // Textured leaf: pointed-oval silhouette with a center vein on a
            // transparent background, cut out by alpha test. Near-white
            // albedo so the per-leaf green vertex colors define the hue.
            leafMat.SetTexture("_BaseMap", MakeLeafTexture());
            leafMat.color = Color.white;
            leafMat.SetFloat("_AlphaClip", 1f); // URP/Lit alpha-test cutout
            leafMat.SetFloat("_Cutoff", 0.5f);
            // Setting the _AlphaClip float is not enough: the URP/Lit shader
            // only runs the alpha-test branch with the _ALPHATEST_ON keyword.
            leafMat.EnableKeyword("_ALPHATEST_ON");
            leafMat.SetFloat("_Cull", 0f); // double-sided: leaf quads are visible from both sides
            leafRenderer.material = leafMat;
        }

        trunkMesh = new Mesh { name = "ParametricTrunk" };
        trunkMesh.MarkDynamic();
        trunkFilter.mesh = trunkMesh;
        leafMesh = new Mesh { name = "ParametricLeaves" };
        leafMesh.MarkDynamic();
        leafFilter.mesh = leafMesh;
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
    /// center vein and a slightly darker rim, near-white so the per-leaf
    /// green vertex colors keep defining the hue. Transparent background for
    /// alpha-test cutout (no more flat square quads).
    /// </summary>
    static Texture2D MakeLeafTexture()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
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
                float shade = 1f - 0.18f * rim - 0.10f * vein;
                tex.SetPixel(x, y, new Color(shade, shade, shade * 0.96f, 1f));
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

    /// <summary>
    /// Rebuilds the tree at growth g in [0,1]. Cheap no-op when g barely
    /// changed since the last build (the growth controller animates smoothly
    /// and calls this every frame during the animation).
    /// </summary>
    public void SetGrowth(float g)
    {
        g = Mathf.Clamp01(g);
        if (trunkMesh == null)
        {
            Debug.LogWarning("ParametricTree.SetGrowth: trunkMesh is null — Awake did not complete; cannot build tree.");
            return;
        }
        if (Mathf.Abs(g - lastBuiltG) < RebuildEpsilon) return;
        lastBuiltG = g;

        float trunkLen = Mathf.Lerp(0.3f, 4.6f, g);
        float trunkRad = Mathf.Lerp(0.03f, 0.28f, g);
        int maxLevel = Mathf.FloorToInt(g * 3.99f); // 0..3 tiers of branches above the trunk

        var rng = new System.Random(Seed);
        Vector3 lean = new Vector3(
            ((float)rng.NextDouble() - 0.5f) * 0.12f, 1f,
            ((float)rng.NextDouble() - 0.5f) * 0.12f).normalized;

        BuildTrunkMesh(trunkLen, trunkRad, maxLevel, g, lean, rng);
        BuildLeafMesh(g, new System.Random(Seed + 1));
    }

    // ------------------------------------------------------------ trunk mesh

    void BuildTrunkMesh(float trunkLen, float trunkRad, int maxLevel, float g,
                        Vector3 lean, System.Random rng)
    {
        vList.Clear(); nList.Clear(); uvList.Clear(); cList.Clear(); tList.Clear();
        bvList.Clear(); bnList.Clear(); buvList.Clear(); bcList.Clear(); btList.Clear();
        tips.Clear();
        GrowBranch(Vector3.zero, lean, trunkLen, trunkRad, 0, maxLevel, g, rng, rootFlare: true);
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
                    int level, int maxLevel, float g, System.Random rng, bool rootFlare = false)
    {
        dir.Normalize();

        // Curved control points: gentle random wobble plus an upward bend so
        // tips reach skyward like real phototropic growth.
        Vector3 up = Vector3.up;
        Vector3 side = Vector3.Cross(dir, up);
        if (side.sqrMagnitude < 1e-6f) side = Vector3.right; else side.Normalize();
        Vector3 side2 = Vector3.Cross(dir, side).normalized;

        float wob = length * 0.07f;
        Vector3 p0 = origin;
        Vector3 p1 = origin + dir * (length * 0.33f) + RandPerp(rng, side, side2, wob) + up * (length * 0.05f);
        Vector3 p2 = origin + dir * (length * 0.66f) + RandPerp(rng, side, side2, wob) + up * (length * 0.14f);
        Vector3 p3 = origin + dir * length + up * (length * 0.30f);
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
            Vector3 childDir = (tan * Mathf.Cos(elev)
                + (refA * Mathf.Cos(az) + refB * Mathf.Sin(az)) * Mathf.Sin(elev)).normalized;

            float parentR = Mathf.Lerp(baseRadius, tipRadius, t);
            float childLen = length * (0.55f + (float)rng.NextDouble() * 0.2f) * (level == 0 ? 1f : 0.85f);
            GrowBranch(pos, childDir, childLen, parentR * 0.55f, level + 1, maxLevel, g, rng);
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

    void BuildLeafMesh(float g, System.Random rng)
    {
        vList.Clear(); nList.Clear(); uvList.Clear(); cList.Clear(); tList.Clear();

        // Leaves only appear once the trunk is established (g > 0.25).
        // 12,000 at full growth (15x the old 800 — the same jump the grass
        // field made from 10k to 150k tufts), each leaf a third of its old
        // size, so the canopy reads as one dense lush mass.
        float f = Smooth01((g - 0.25f) / 0.75f);
        int total = Mathf.RoundToInt(12000f * f);
        if (total > 0 && tips.Count > 0)
        {
            int per = total / tips.Count;
            int rem = total % tips.Count;
            for (int i = 0; i < tips.Count; i++)
            {
                int count = per + (i < rem ? 1 : 0);
                for (int k = 0; k < count; k++)
                    AppendLeaf(tips[i], rng);
            }
        }
        AssignMesh(leafMesh, vList, nList, uvList, cList, tList);
    }

    void AppendLeaf(Vector3 tip, System.Random rng)
    {
        Vector3 center = tip + RandomInSphere(rng, 0.5f);
        // A third of the old 0.12-0.25 size: small leaves, many of them.
        float s = 0.04f + (float)rng.NextDouble() * 0.043f;
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
        Color gc = new Color(0.16f * v, 0.42f * v, 0.12f * v, 1f);
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
