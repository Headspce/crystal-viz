using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v1.0.62: owns the per-species environment. Builds the willow lakeside
/// props procedurally at startup (pond, reed clusters, drifting mist),
/// toggles them per species, and runs the fog-swell transition when the
/// player picks a new tree.
///
/// The active profile is static so the day/night lighting funnel
/// (CrystalVizBootstrap.ApplyTimeOfDayLighting) can read it without a
/// reference chain. Defaults to the grassland profile so anything that
/// reads it before Initialize runs gets the legacy look.
/// </summary>
public class EnvironmentManager : MonoBehaviour
{
    public static EnvironmentManager Instance { get; private set; }

    public static EnvironmentProfile ActiveProfile { get; private set; } =
        EnvironmentProfiles.ForSpecies(ParametricTree.TreeSpecies.Broadleaf);

    // The willow pond: a shallow disc of water behind the tree, ringed with
    // reeds and low drifting mist. Kept well inside the camera's visible
    // corridor so it reads in the portrait frame.
    public static readonly Vector3 PondCenter = new Vector3(0f, 0f, -4.5f);
    public const float PondRadius = 3.5f;
    public const float PondWaterY = 0.04f;
    // Grass/flower blades are suppressed inside this radius for willow only
    // (see CrystalVizBootstrap); the rim band lets the shore fade out.
    public const float PondGrassClearRadius = 3.7f;
    public static float PondGrassClearRadiusSq =>
        PondGrassClearRadius * PondGrassClearRadius;

    CrystalVizBootstrap bootstrap;
    GameObject willowProps;
    Coroutine activeTransition;
    bool initialized;

    // Mist drift state (play mode only).
    readonly List<Transform> mistQuads = new List<Transform>();
    readonly List<float> mistBaseX = new List<float>();
    readonly List<float> mistPhase = new List<float>();

    public void Initialize(CrystalVizBootstrap boot)
    {
        if (initialized) return;
        initialized = true;
        Instance = this;
        bootstrap = boot;
        BuildWillowProps();
        var species = (ParametricTree.TreeSpecies)Mathf.Clamp(
            PlayerPrefs.GetInt(TreeGrowthController.SpeciesKey, 0), 0, 5);
        ApplyProfileInstant(species);
    }

    /// <summary>
    /// Applies a species' profile instantly: fog, ambient, props, then
    /// re-runs the day/night lighting funnel at the current time of day.
    /// </summary>
    public void ApplyProfileInstant(ParametricTree.TreeSpecies s)
    {
        var p = EnvironmentProfiles.ForSpecies(s);
        ActiveProfile = p;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = p.fogStart;
        RenderSettings.fogEndDistance = p.fogEnd;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        if (willowProps != null) willowProps.SetActive(p.waterEnabled);
        if (bootstrap != null)
        {
            var orbit = Object.FindFirstObjectByType<SunOrbitControl>();
            float v = orbit != null ? orbit.CurrentSnappedValue : 0f;
            bootstrap.ApplyTimeOfDayLighting(SunOrbitControl.NightFactor(v));
        }
    }

    /// <summary>
    /// Species change entry point. In play mode this runs the fog-swell:
    /// the fog rolls in thick, the environment swaps underneath it, then
    /// the fog lifts — ~1.5s total. In edit mode (CI screenshots) there is
    /// no Update loop, so the swap is instant.
    /// </summary>
    public void TransitionTo(ParametricTree.TreeSpecies s)
    {
        if (!initialized) return;
        if (!Application.isPlaying)
        {
            ApplyProfileInstant(s);
            return;
        }
        if (activeTransition != null) StopCoroutine(activeTransition);
        activeTransition = StartCoroutine(FogSwellRoutine(s));
    }

    IEnumerator FogSwellRoutine(ParametricTree.TreeSpecies s)
    {
        var target = EnvironmentProfiles.ForSpecies(s);
        float startStart = RenderSettings.fogStartDistance;
        float startEnd = RenderSettings.fogEndDistance;

        // Phase 1: the fog swells in (~0.6s).
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.6f;
            float k = Smooth01(Mathf.Clamp01(t));
            RenderSettings.fogStartDistance = Mathf.Lerp(startStart, 0f, k);
            RenderSettings.fogEndDistance = Mathf.Lerp(startEnd, 6f, k);
            yield return null;
        }

        // Phase 2: swap the world under the white-out.
        ApplyProfileInstant(s);

        // Phase 3: the fog lifts onto the new profile (~0.9s).
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.9f;
            float k = Smooth01(Mathf.Clamp01(t));
            RenderSettings.fogStartDistance = Mathf.Lerp(0f, target.fogStart, k);
            RenderSettings.fogEndDistance = Mathf.Lerp(6f, target.fogEnd, k);
            yield return null;
        }
        RenderSettings.fogStartDistance = target.fogStart;
        RenderSettings.fogEndDistance = target.fogEnd;
        activeTransition = null;
    }

    static float Smooth01(float x) { return x * x * (3f - 2f * x); }

    void Update()
    {
        // Mist drifts only while the lakeside is live, in play mode.
        if (willowProps == null || !willowProps.activeSelf) return;
        if (mistQuads.Count == 0) return;
        float t = Time.time;
        for (int i = 0; i < mistQuads.Count; i++)
        {
            var tr = mistQuads[i];
            if (tr == null) continue;
            var pos = tr.position;
            pos.x = mistBaseX[i] + Mathf.Sin(t * 0.12f + mistPhase[i]) * 0.5f;
            tr.position = pos;
        }
    }

    // ------------------------------------------------------------------
    // Willow prop construction (all procedural, built once, toggled).
    // ------------------------------------------------------------------

    void BuildWillowProps()
    {
        willowProps = new GameObject("WillowLakeside");
        willowProps.SetActive(false);

        var waterShader = Shader.Find("CrystalViz/LakesideWater");
        if (waterShader == null)
        {
            Debug.LogWarning("EnvironmentManager: 'CrystalViz/LakesideWater' not found; " +
                             "willow pond and mist will be skipped (never magenta).");
        }
        else
        {
            BuildPond(waterShader);
            BuildMist(waterShader);
        }

        var grassShader = Shader.Find("CrystalViz/StylizedGrass");
        if (grassShader == null)
        {
            Debug.LogWarning("EnvironmentManager: 'CrystalViz/StylizedGrass' not found; " +
                             "willow reeds will be skipped.");
        }
        else
        {
            BuildReeds(grassShader);
        }
    }

    void BuildPond(Shader waterShader)
    {
        const int segments = 28;
        var verts = new List<Vector3>(segments + 1);
        var uvs = new List<Vector2>(segments + 1);
        var idx = new List<int>(segments * 3);
        verts.Add(new Vector3(PondCenter.x, PondWaterY, PondCenter.z));
        uvs.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            float x = Mathf.Cos(a) * PondRadius;
            float z = Mathf.Sin(a) * PondRadius;
            verts.Add(new Vector3(PondCenter.x + x, PondWaterY, PondCenter.z + z));
            uvs.Add(new Vector2(0.5f + x / (PondRadius * 2f), 0.5f + z / (PondRadius * 2f)));
        }
        for (int i = 0; i < segments; i++)
        {
            idx.Add(0);
            idx.Add(1 + (i + 1) % segments);
            idx.Add(1 + i);
        }
        var mesh = new Mesh { name = "WillowPond" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(idx, 0);
        mesh.RecalculateNormals();

        var mat = new Material(waterShader);
        mat.SetColor("_Color", new Color(0.16f, 0.35f, 0.45f, 1f));
        mat.SetFloat("_Alpha", 0.82f);
        mat.SetFloat("_Mode", 0f);

        var go = new GameObject("Pond");
        go.transform.SetParent(willowProps.transform, false);
        go.AddComponent<MeshFilter>().mesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    void BuildMist(Shader waterShader)
    {
        var defs = new (Vector3 pos, Vector2 size)[]
        {
            (new Vector3(0f, 0.42f, -3.0f), new Vector2(3.2f, 1.5f)),
            (new Vector3(1.1f, 0.50f, -4.4f), new Vector2(2.6f, 1.3f)),
            (new Vector3(-1.1f, 0.48f, -4.2f), new Vector2(2.6f, 1.3f)),
            (new Vector3(0.2f, 0.58f, -6.2f), new Vector2(3.6f, 1.6f)),
        };
        var mistColor = new Color(0.75f, 0.82f, 0.90f, 1f);
        float mistAlpha = EnvironmentProfiles
            .ForSpecies(ParametricTree.TreeSpecies.Willow).mistAlpha;
        var rng = new System.Random(777);
        for (int i = 0; i < defs.Length; i++)
        {
            var mat = new Material(waterShader);
            mat.SetColor("_Color", mistColor);
            mat.SetFloat("_Alpha", mistAlpha);
            mat.SetFloat("_Mode", 1f);

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Mist" + i;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(willowProps.transform, false);
            go.transform.position = defs[i].pos;
            go.transform.localScale = new Vector3(defs[i].size.x, defs[i].size.y, 1f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.material = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            mistQuads.Add(go.transform);
            mistBaseX.Add(defs[i].pos.x);
            mistPhase.Add((float)rng.NextDouble() * Mathf.PI * 2f);
        }
    }

    void BuildReeds(Shader grassShader)
    {
        // Reed clusters along the near-shore arc facing the tree/camera,
        // standing in the shallows. Tapered blades like the meadow grass
        // but taller and reed-toned.
        int tufts = EnvironmentProfiles
            .ForSpecies(ParametricTree.TreeSpecies.Willow).reedTufts;
        tufts = Mathf.Max(tufts, 1);

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var idx = new List<int>();
        var rng = new System.Random(4242);

        float[] clusterAngles = { 62f, 74f, 86f, 98f, 110f, 122f };
        int perCluster = Mathf.CeilToInt((float)tufts / clusterAngles.Length);
        foreach (float deg in clusterAngles)
        {
            float a = deg * Mathf.Deg2Rad;
            float cr = 3.15f + (float)rng.NextDouble() * 0.3f;
            float ccx = PondCenter.x + Mathf.Cos(a) * cr;
            float ccz = PondCenter.z + Mathf.Sin(a) * cr;
            for (int i = 0; i < perCluster; i++)
            {
                float ox = ((float)rng.NextDouble() - 0.5f) * 1.1f;
                float oz = ((float)rng.NextDouble() - 0.5f) * 1.1f;
                float bx = ccx + ox;
                float bz = ccz + oz;
                // Keep the trunk clear.
                if (bx * bx + bz * bz < 0.81f) continue;
                float yaw = (float)rng.NextDouble() * Mathf.PI * 2f;
                int blades = 4 + rng.Next(3);
                for (int b = 0; b < blades; b++)
                {
                    float h = 0.7f + (float)rng.NextDouble() * 0.45f;
                    float w = 0.022f + (float)rng.NextDouble() * 0.012f;
                    float lean = ((float)rng.NextDouble() - 0.5f) * 0.3f;
                    AppendReedBlade(verts, uvs, idx,
                        new Vector3(bx, 0f, bz), yaw + lean, h, w);
                }
            }
        }

        var mesh = new Mesh { name = "WillowReeds" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(idx, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var mat = new Material(grassShader);
        mat.SetColor("_RootColor", new Color(0.23f, 0.20f, 0.09f, 1f));
        mat.SetColor("_TipColor", new Color(0.58f, 0.52f, 0.26f, 1f));
        mat.SetFloat("_WindStrength", 0.045f);
        mat.SetFloat("_WindFrequency", 1.7f);
        mat.SetFloat("_GustStrength", 0.12f);
        mat.SetFloat("_GustFrequency", 1.8f);
        mat.SetFloat("_GustSpeed", 0.18f);
        mat.SetFloat("_PhaseJitter", 0.28f);

        var go = new GameObject("Reeds");
        go.transform.SetParent(willowProps.transform, false);
        go.AddComponent<MeshFilter>().mesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    static void AppendReedBlade(List<Vector3> verts, List<Vector2> uvs, List<int> idx,
        Vector3 basePos, float yaw, float height, float width)
    {
        float dx = Mathf.Cos(yaw), dz = Mathf.Sin(yaw);
        float hw = width * 0.5f;
        int s = verts.Count;
        verts.Add(basePos + new Vector3(-dz * hw, 0f, dx * hw));
        verts.Add(basePos + new Vector3(dz * hw, 0f, -dx * hw));
        verts.Add(basePos + new Vector3(-dz * hw * 0.7f + dx * 0.02f, height * 0.55f, dx * hw * 0.7f + dz * 0.02f));
        verts.Add(basePos + new Vector3(dz * hw * 0.7f + dx * 0.02f, height * 0.55f, -dx * hw * 0.7f + dz * 0.02f));
        verts.Add(basePos + new Vector3(dx * 0.05f, height, dz * 0.05f));
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(0f, 0.55f));
        uvs.Add(new Vector2(1f, 0.55f));
        uvs.Add(new Vector2(0.5f, 1f));
        idx.Add(s); idx.Add(s + 1); idx.Add(s + 2);
        idx.Add(s + 1); idx.Add(s + 3); idx.Add(s + 2);
        idx.Add(s + 2); idx.Add(s + 3); idx.Add(s + 4);
    }
}
