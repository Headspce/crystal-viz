using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Procedural parametric tree for CrystalViz's tap-to-grow feature.
///
/// The trunk and all recursive branches are merged into ONE mesh; leaf quads
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
    readonly List<Vector3> tips = new List<Vector3>(); // outermost branch tips (leaf anchors)

    void Awake()
    {
        trunkFilter = GetComponent<MeshFilter>();
        trunkRenderer = GetComponent<MeshRenderer>();

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

        // Bark texture if the OldTree assets are still in Resources; otherwise
        // fall back to a flat bark-brown albedo (vertex colors add variation).
        var bark = Resources.Load<Texture2D>("Models/OldTree/bark04");
        if (lit != null)
        {
            var trunkMat = new Material(lit);
            if (bark != null)
            {
                trunkMat.SetTexture("_BaseMap", bark);
                trunkMat.color = Color.white;
            }
            else
            {
                trunkMat.color = new Color(0.36f, 0.23f, 0.13f);
            }
            trunkRenderer.material = trunkMat;
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
            leafMat.color = Color.white;
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

        float trunkLen = Mathf.Lerp(0.3f, 7f, g);
        float trunkRad = Mathf.Lerp(0.03f, 0.35f, g);
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
        tips.Clear();
        GrowBranch(Vector3.zero, lean, trunkLen, trunkRad, 0, maxLevel, g, rng, rootFlare: true);
        AssignMesh(trunkMesh, vList, nList, uvList, cList, tList);
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
        AppendTube(pts, baseRadius, tipRadius, BranchTint(rng), rootFlare);

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
    /// (tangent, normal, binormal) frames.
    /// </summary>
    void AppendTube(Vector3[] pts, float r0, float r1, Color tint, bool rootFlare)
    {
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
        int baseIdx = vList.Count;
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
                vList.Add(pts[i] + radial * r);
                nList.Add(radial);
                uvList.Add(new Vector2((float)j / RadialSegments, vAcc * 0.5f));
                cList.Add(c);
            }
        }
        for (int i = 0; i < n - 1; i++)
        {
            for (int j = 0; j < RadialSegments; j++)
            {
                int a0 = baseIdx + i * ringSize + j;
                int b0 = a0 + ringSize;
                tList.Add(a0); tList.Add(a0 + 1); tList.Add(b0);
                tList.Add(a0 + 1); tList.Add(b0 + 1); tList.Add(b0);
            }
        }
    }

    // ------------------------------------------------------------- leaf mesh

    void BuildLeafMesh(float g, System.Random rng)
    {
        vList.Clear(); nList.Clear(); uvList.Clear(); cList.Clear(); tList.Clear();

        // Leaves only appear once the trunk is established (g > 0.25).
        float f = Smooth01((g - 0.25f) / 0.75f);
        int total = Mathf.RoundToInt(800f * f);
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
        Vector3 center = tip + RandomInSphere(rng, 0.35f);
        float s = 0.12f + (float)rng.NextDouble() * 0.13f;
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
