using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v1.0.62: owns the per-species environment. Builds the willow lakeside
/// props procedurally at startup (pond, reed clusters, drifting mist),
/// toggles them per species, and runs the fog-swell transition when the
/// player picks a new tree.
///
/// v1.0.63: five bespoke environments join the lakeside — oak's ancient
/// meadow (mushroom fairy ring + golden wildflower drifts), pine's alpine
/// clearing (peak silhouettes + falling snow), birch's grove (white trunks
/// + fern floor), cherry's sakura garden (stone lantern + still pond +
/// drifting petals), palm's tropical shore (turquoise shallows + wave lines
/// + shells). Every species' props are built once at startup into their own
/// container and toggled per profile. The transition is now Tyler's cloudy
/// fog: a full-cover billowing cloud bank (never a flat whiteout) that
/// rolls in, holds while the world swaps underneath, then parts.
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
    // Willow pond clear radius (legacy constant; the generalized per-
    // profile value is EnvironmentProfiles.PondClearRadius). The rim band
    // lets the shore fade out.
    public const float PondGrassClearRadius = 3.7f;
    public static float PondGrassClearRadiusSq =>
        PondGrassClearRadius * PondGrassClearRadius;

    CrystalVizBootstrap bootstrap;
    readonly Dictionary<ParametricTree.TreeSpecies, GameObject> propSets =
        new Dictionary<ParametricTree.TreeSpecies, GameObject>();
    Coroutine activeTransition;
    bool initialized;

    // v1.0.63: unified drifter system. Mist sways laterally (kind 0);
    // petals (kind 1) and snow (kind 2) fall and wrap, petals tumbling.
    // Positions derive from Time.time, so edit-mode captures (no Update)
    // show the build-time scatter.
    struct Drifter
    {
        public Transform tr;
        public Vector3 basePos;
        public float phase;
        public int kind;
        public float fallSpeed;
        public float swayAmp;
        public float swaySpeed;
        public float spinSpeed;
        public float topY;
        public float rangeY;
    }
    readonly List<Drifter> drifters = new List<Drifter>();

    // v1.0.63: cloudy transition overlay — a ScreenSpaceOverlay canvas at
    // sorting order 999 (above the bee counter at 100 and the inventory
    // menu at 110) with a full-screen Image wearing the TransitionFog
    // shader. The camera-child quad approach couldn't cover overlay UI;
    // this guarantees the whole screen — 3D and UI alike — is obscured at
    // full cover. Built lazily on the first play-mode transition; never
    // exists in the CI edit-mode path.
    GameObject transitionOverlay;
    Material transitionFogMat;

    public void Initialize(CrystalVizBootstrap boot)
    {
        if (initialized) return;
        initialized = true;
        Instance = this;
        bootstrap = boot;
        BuildAllPropSets();
        RefreshActiveProfile();
    }

    /// <summary>
    /// v1.0.63: re-applies the persisted species' profile. Called by
    /// Initialize and again by the bootstrap after BuildDiorama, because
    /// the ground/grass/wildflower materials only exist after the diorama
    /// builds — the second pass is what actually re-tints the surfaces.
    /// </summary>
    public void RefreshActiveProfile()
    {
        var species = (ParametricTree.TreeSpecies)Mathf.Clamp(
            PlayerPrefs.GetInt(TreeGrowthController.SpeciesKey, 0), 0, 5);
        ApplyProfileInstant(species);
    }

    /// <summary>
    /// Applies a species' profile instantly: fog, ambient, props, material
    /// tints, then re-runs the day/night lighting funnel at the current
    /// time of day.
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
        foreach (var kv in propSets) kv.Value.SetActive(kv.Key == s);
        if (bootstrap != null)
        {
            // Flatten the pond grass/flowers via the shader clip so a
            // species picked after launch still gets open water (the
            // build-time tuft skip only covers the persisted species).
            // v1.0.63: generalized — cherry's pond and palm's shallows use
            // the same path with their own center/radius.
            bootstrap.SetPondClip(p.pondEnabled);
            bootstrap.ApplyEnvironmentTint();
            var orbit = Object.FindFirstObjectByType<SunOrbitControl>();
            float v = orbit != null ? orbit.CurrentSnappedValue : 0f;
            bootstrap.ApplyTimeOfDayLighting(SunOrbitControl.NightFactor(v));
        }
    }

    /// <summary>
    /// Species change entry point. In play mode this runs the cloudy-fog
    /// transition: a dense billowing cloud bank rolls in to full cover, the
    /// environment swaps underneath while it holds, then the clouds part —
    /// ~2.6s total. In edit mode (CI screenshots) there is no Update loop,
    /// so the swap is instant and no overlay is ever built.
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
        activeTransition = StartCoroutine(CloudFogRoutine(s));
    }

    IEnumerator CloudFogRoutine(ParametricTree.TreeSpecies s)
    {
        EnsureTransitionOverlay();
        if (transitionFogMat == null)
        {
            // Shader missing: fall back to the instant swap (never magenta,
            // never stuck).
            ApplyProfileInstant(s);
            activeTransition = null;
            yield break;
        }

        // Tint the cloud bank to the world it's about to cover.
        transitionFogMat.SetColor("_FogColor", RenderSettings.fogColor);
        transitionOverlay.SetActive(true);

        // Phase 1: the cloud bank rolls in to full cover (~0.8s).
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.8f;
            transitionFogMat.SetFloat("_Cover", Smooth01(Mathf.Clamp01(t)));
            yield return null;
        }
        transitionFogMat.SetFloat("_Cover", 1f);

        // Phase 2: swap the world under full cover, then hold a beat while
        // the new environment settles (the swap itself is instant — props
        // are pre-built — the hold is the visual breath before the reveal).
        ApplyProfileInstant(s);
        // Re-tint the bank to the new world's fog so the reveal feels lit
        // by the environment it's uncovering.
        transitionFogMat.SetColor("_FogColor", RenderSettings.fogColor);
        yield return new WaitForSeconds(0.6f);

        // Phase 3: the clouds part and drift away (~1.2s).
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 1.2f;
            transitionFogMat.SetFloat("_Cover", 1f - Smooth01(Mathf.Clamp01(t)));
            yield return null;
        }
        transitionFogMat.SetFloat("_Cover", 0f);
        transitionOverlay.SetActive(false);
        activeTransition = null;
    }

    static float Smooth01(float x) { return x * x * (3f - 2f * x); }

    /// <summary>
    /// v1.0.63: builds (once) the transition overlay: a ScreenSpaceOverlay
    /// canvas at sorting order 999 with a full-rect Image wearing the
    /// TransitionFog shader. Covers the entire screen including overlay UI
    /// (menu 110, counter 100). raycastTarget is off so it never eats taps.
    /// </summary>
    void EnsureTransitionOverlay()
    {
        if (transitionOverlay != null) return;
        // Survive scene reloads: a previous instance may already persist.
        transitionOverlay = GameObject.Find("TransitionFogCanvas");
        if (transitionOverlay != null)
        {
            var img = transitionOverlay.GetComponentInChildren<UnityEngine.UI.Image>();
            if (img != null) transitionFogMat = img.material;
            return;
        }
        var fogShader = Shader.Find("CrystalViz/TransitionFog");
        if (fogShader == null)
        {
            Debug.LogWarning("EnvironmentManager: 'CrystalViz/TransitionFog' not found; " +
                             "species transitions will swap instantly.");
            return;
        }
        transitionFogMat = new Material(fogShader);
        transitionFogMat.SetFloat("_Cover", 0f);
        transitionFogMat.SetFloat("_Seed", 3.7f);

        transitionOverlay = new GameObject("TransitionFogCanvas");
        var canvas = transitionOverlay.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        // No GraphicRaycaster: the overlay never interacts.

        var imgGO = new GameObject("TransitionFogImage");
        imgGO.transform.SetParent(transitionOverlay.transform, false);
        var rt = imgGO.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = imgGO.AddComponent<UnityEngine.UI.Image>();
        img.material = transitionFogMat;
        img.raycastTarget = false;
        img.color = Color.white;

        transitionOverlay.SetActive(false);
        Object.DontDestroyOnLoad(transitionOverlay);
    }

    void Update()
    {
        if (drifters.Count == 0) return;
        float t = Time.time;
        foreach (var d in drifters)
        {
            if (d.tr == null || !d.tr.gameObject.activeInHierarchy) continue;
            var pos = d.basePos;
            if (d.kind == 0)
            {
                pos.x += Mathf.Sin(t * d.swaySpeed + d.phase) * d.swayAmp;
            }
            else
            {
                float fall = Mathf.Repeat(t * d.fallSpeed + d.phase * d.rangeY, d.rangeY);
                pos.y = d.topY - fall;
                pos.x += Mathf.Sin(t * d.swaySpeed + d.phase) * d.swayAmp;
                pos.z += Mathf.Cos(t * d.swaySpeed * 0.7f + d.phase * 1.3f) * d.swayAmp * 0.5f;
            }
            d.tr.position = pos;
            if (d.kind == 1)
            {
                d.tr.rotation = Quaternion.Euler(
                    t * d.spinSpeed + d.phase * 57f,
                    d.phase * 90f,
                    t * d.spinSpeed * 0.6f);
            }
        }
    }

    // ------------------------------------------------------------------
    // Prop-set construction (all procedural, built once, toggled).
    // ------------------------------------------------------------------

    void BuildAllPropSets()
    {
        var waterShader = Shader.Find("CrystalViz/LakesideWater");
        var grassShader = Shader.Find("CrystalViz/StylizedGrass");
        var litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (waterShader == null)
            Debug.LogWarning("EnvironmentManager: 'CrystalViz/LakesideWater' not found; " +
                             "ponds, mist, petals and snow will be skipped (never magenta).");
        if (grassShader == null)
            Debug.LogWarning("EnvironmentManager: 'CrystalViz/StylizedGrass' not found; " +
                             "reeds and ferns will be skipped.");
        if (litShader == null)
            Debug.LogWarning("EnvironmentManager: 'Universal Render Pipeline/Lit' not found; " +
                             "solid props (lantern, peaks, trunks, shells) will be skipped.");

        BuildWillowProps(waterShader, grassShader);
        BuildOakProps(litShader);
        BuildPineProps(waterShader, litShader);
        BuildBirchProps(grassShader, litShader);
        BuildCherryProps(waterShader, litShader);
        BuildPalmProps(waterShader, litShader);
    }

    GameObject NewPropSet(ParametricTree.TreeSpecies s, string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        propSets[s] = go;
        return go;
    }

    static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c != null)
        {
            if (Application.isPlaying) Object.Destroy(c);
            else Object.DestroyImmediate(c);
        }
    }

    static Material NewLit(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) return null;
        var m = new Material(sh);
        m.color = c;
        return m;
    }

    static GameObject Primitive(PrimitiveType type, GameObject parent, string name,
        Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        StripCollider(go);
        go.transform.SetParent(parent.transform, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        if (mat != null)
        {
            var mr = go.GetComponent<MeshRenderer>();
            mr.material = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
        return go;
    }

    /// <summary>
    /// v1.0.63: simple cone mesh (peaks, lantern roof, mushroom caps use
    /// spheres instead). Apex at +height/2, base at -height/2.
    /// </summary>
    static Mesh MakeCone(float radius, float height, int segments)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var idx = new List<int>();
        verts.Add(new Vector3(0f, height * 0.5f, 0f));
        uvs.Add(new Vector2(0.5f, 1f));
        for (int i = 0; i < segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            verts.Add(new Vector3(Mathf.Cos(a) * radius, -height * 0.5f, Mathf.Sin(a) * radius));
            uvs.Add(new Vector2((float)i / segments, 0f));
        }
        for (int i = 0; i < segments; i++)
        {
            idx.Add(0);
            idx.Add(1 + i);
            idx.Add(1 + (i + 1) % segments);
        }
        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(idx, 0);
        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>
    /// v1.0.63: flat water disc, generalized from the willow pond. UVs are
    /// radial so the LakesideWater shore fade melts the rim.
    /// </summary>
    static GameObject BuildWaterDisc(GameObject parent, Shader waterShader,
        Vector3 center, float radius, Color color, float alpha, string name)
    {
        const int segments = 28;
        var verts = new List<Vector3>(segments + 1);
        var uvs = new List<Vector2>(segments + 1);
        var idx = new List<int>(segments * 3);
        verts.Add(new Vector3(center.x, PondWaterY, center.z));
        uvs.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            float x = Mathf.Cos(a) * radius;
            float z = Mathf.Sin(a) * radius;
            verts.Add(new Vector3(center.x + x, PondWaterY, center.z + z));
            uvs.Add(new Vector2(0.5f + x / (radius * 2f), 0.5f + z / (radius * 2f)));
        }
        for (int i = 0; i < segments; i++)
        {
            idx.Add(0);
            idx.Add(1 + (i + 1) % segments);
            idx.Add(1 + i);
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(idx, 0);
        mesh.RecalculateNormals();

        var mat = new Material(waterShader);
        mat.SetColor("_Color", color);
        mat.SetFloat("_Alpha", alpha);
        mat.SetFloat("_Mode", 0f);

        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<MeshFilter>().mesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go;
    }

    /// <summary>
    /// v1.0.63: registers a soft billboard quad (mist/petal/snow via the
    /// LakesideWater mist mode) with the drifter system.
    /// kind: 0 = mist (lateral sway), 1 = petal (fall + tumble),
    /// 2 = snow (fall, gentle sway).
    /// </summary>
    void AddDrifter(GameObject parent, Shader waterShader, Vector3 pos, Vector2 size,
        Color color, float alpha, int kind, float seedPhase,
        float fallSpeed = 0f, float swayAmp = 0.5f, float swaySpeed = 0.12f,
        float topY = 0f, float rangeY = 1f)
    {
        var mat = new Material(waterShader);
        mat.SetColor("_Color", color);
        mat.SetFloat("_Alpha", alpha);
        mat.SetFloat("_Mode", 1f);

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Drifter" + kind + "_" + drifters.Count;
        StripCollider(go);
        go.transform.SetParent(parent.transform, false);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var mr = go.GetComponent<MeshRenderer>();
        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        drifters.Add(new Drifter
        {
            tr = go.transform,
            basePos = pos,
            phase = seedPhase * Mathf.PI * 2f,
            kind = kind,
            fallSpeed = fallSpeed,
            swayAmp = swayAmp,
            swaySpeed = swaySpeed,
            spinSpeed = 40f + seedPhase * 50f,
            topY = topY,
            rangeY = rangeY,
        });
    }

    // ------------------------------------------------------------------
    // Willow: lakeside (pond, reeds, mist). Unchanged since v1.0.62 except
    // the reed material no-op cleanup (v1.0.63): _WindFrequency,
    // _GustFrequency and _PhaseJitter were never real StylizedGrass
    // properties (the real ones are _WindSpeed and _GustFreq), so setting
    // them did nothing — removed.
    // ------------------------------------------------------------------

    void BuildWillowProps(Shader waterShader, Shader grassShader)
    {
        var set = NewPropSet(ParametricTree.TreeSpecies.Willow, "WillowLakeside");
        if (waterShader != null)
        {
            BuildWaterDisc(set, waterShader, PondCenter, PondRadius,
                new Color(0.16f, 0.35f, 0.45f, 1f), 0.82f, "Pond");
            BuildWillowMist(set, waterShader);
        }
        if (grassShader != null) BuildReeds(set, grassShader);
    }

    void BuildWillowMist(GameObject set, Shader waterShader)
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
            AddDrifter(set, waterShader, defs[i].pos, defs[i].size,
                mistColor, mistAlpha, 0, (float)rng.NextDouble(),
                swayAmp: 0.5f, swaySpeed: 0.12f);
        }
    }

    void BuildReeds(GameObject set, Shader grassShader)
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

        float[] clusterAngles = { 58f, 70f, 82f, 94f, 106f, 118f };
        int perCluster = Mathf.CeilToInt((float)tufts / clusterAngles.Length);
        foreach (float deg in clusterAngles)
        {
            float a = deg * Mathf.Deg2Rad;
            float cr = 3.15f + (float)rng.NextDouble() * 0.3f;
            float ccx = PondCenter.x + Mathf.Cos(a) * cr;
            float ccz = PondCenter.z + Mathf.Sin(a) * cr;
            for (int i = 0; i < perCluster; i++)
            {
                // Tight clumps with open water between them (not a wall).
                float ox = ((float)rng.NextDouble() - 0.5f) * 0.9f;
                float oz = ((float)rng.NextDouble() - 0.5f) * 0.9f;
                float bx = ccx + ox;
                float bz = ccz + oz;
                // Keep the trunk clear.
                if (bx * bx + bz * bz < 0.81f) continue;
                float yaw = (float)rng.NextDouble() * Mathf.PI * 2f;
                int blades = 3 + rng.Next(3);
                for (int b = 0; b < blades; b++)
                {
                    float h = 0.5f + (float)rng.NextDouble() * 0.45f;
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
        mat.SetColor("_RootColor", new Color(0.16f, 0.28f, 0.08f, 1f));
        mat.SetColor("_TipColor", new Color(0.45f, 0.55f, 0.20f, 1f));
        mat.SetFloat("_WindStrength", 0.045f);
        mat.SetFloat("_WindSpeed", 1.7f);
        mat.SetFloat("_GustStrength", 0.12f);
        mat.SetFloat("_GustSpeed", 1.8f);
        mat.SetFloat("_GustFreq", 0.18f);
        mat.SetFloat("_GustLighten", 0.28f);
        // v1.0.63: removed the no-op _WindFrequency / _GustFrequency /
        // _PhaseJitter sets — those were never StylizedGrass properties.

        var go = new GameObject("Reeds");
        go.transform.SetParent(set.transform, false);
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

    // ------------------------------------------------------------------
    // Oak: ancient meadow — mushroom fairy ring + golden wildflower drifts
    // in warm late-afternoon light.
    // ------------------------------------------------------------------

    void BuildOakProps(Shader litShader)
    {
        var set = NewPropSet(ParametricTree.TreeSpecies.Broadleaf, "OakMeadow");
        if (litShader == null) return;

        // Fairy ring of mushrooms at the meadow's edge: cream stems, warm
        // brown caps. Deterministic placement, kept clear of the trunk and
        // the camera corridor.
        var stemMat = NewLit(new Color(0.88f, 0.84f, 0.76f, 1f));
        var capMat = NewLit(new Color(0.58f, 0.34f, 0.18f, 1f));
        // Fairy ring of mushrooms: cream stems, warm brown caps. Explicit
        // on-frame arc in the mid-ground (all z<0, |x| inside the portrait
        // corridor at their depth) — the old radial ring put half the ring
        // behind the camera and the rest off-frame, so it never read.
        var shroomSpots = new (float x, float z)[]
        {
            (1.4f, -2.3f), (0.6f, -2.9f), (-0.4f, -2.6f), (-1.2f, -3.1f),
            (1.0f, -3.8f), (-0.6f, -4.0f), (0.2f, -4.6f), (-1.5f, -4.4f),
        };
        var rng = new System.Random(60606);
        for (int i = 0; i < shroomSpots.Length; i++)
        {
            float bx = shroomSpots[i].x, bz = shroomSpots[i].z;
            float h = 0.20f + (float)rng.NextDouble() * 0.12f;
            Primitive(PrimitiveType.Cylinder, set, "ShroomStem" + i,
                new Vector3(bx, h * 0.5f, bz), new Vector3(0.09f, h, 0.09f), stemMat);
            Primitive(PrimitiveType.Sphere, set, "ShroomCap" + i,
                new Vector3(bx, h + 0.03f, bz), new Vector3(0.30f, 0.16f, 0.30f), capMat);
        }

        BuildOakDrift(set);
    }

    /// <summary>
    /// v1.0.63: golden wildflower drifts for the oak meadow — three dense
    /// arc bands of warm-toned blossoms, built with the bootstrap's real
    /// wildflower geometry (blossom atlas included) so they match the
    /// meadow's flowers exactly.
    /// </summary>
    void BuildOakDrift(GameObject set)
    {
        var flowerShader = Shader.Find("CrystalViz/Wildflower");
        if (flowerShader == null) return;
        var mat = new Material(flowerShader);
        mat.SetColor("_RootColor", new Color(0.14f, 0.30f, 0.10f, 1f));
        mat.SetColor("_TipColor", new Color(0.40f, 0.62f, 0.20f, 1f));
        mat.SetTexture("_BlossomMap", CrystalVizBootstrap.MakeBlossomAtlas());
        mat.SetFloat("_WindStrength", 0.06f);
        mat.SetFloat("_WindSpeed", 1.7f);
        mat.SetFloat("_GustStrength", 0.12f);
        mat.SetFloat("_GustSpeed", 1.8f);
        mat.SetFloat("_GustFreq", 0.18f);
        mat.SetFloat("_GustLighten", 0.28f);

        var palette = new[]
        {
            new Color(0.99f, 0.80f, 0.30f, 1f), // gold
            new Color(0.99f, 0.66f, 0.25f, 1f), // amber
            new Color(0.97f, 0.93f, 0.70f, 1f), // cream
            new Color(0.95f, 0.55f, 0.35f, 1f), // poppy
        };
        var rng = new System.Random(70707);
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var uvs2 = new List<Vector2>();
        var colors = new List<Color>();
        var tris = new List<int>();

        float[] bandAngles = { 100f, 140f, 180f, 220f, 260f, 300f };
        foreach (float deg in bandAngles)
        {
            float a = deg * Mathf.Deg2Rad;
            for (int i = 0; i < 130; i++)
            {
                float rr = 3.0f + (float)rng.NextDouble() * 4.0f;
                float aa = a + ((float)rng.NextDouble() - 0.5f) * 0.35f;
                float px = Mathf.Cos(aa) * rr;
                float pz = Mathf.Sin(aa) * rr;
                if (px * px + pz * pz < 0.81f) continue;
                float cdx = px, cdz = pz - 7.4f;
                if (cdx * cdx + cdz * cdz < 9f) continue;
                var mtx = Matrix4x4.TRS(
                    new Vector3(px, 0f, pz),
                    Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                    Vector3.one * (0.9f + (float)rng.NextDouble() * 0.6f));
                var petal = palette[rng.Next(palette.Length)];
                float j = 0.88f + (float)rng.NextDouble() * 0.18f;
                petal = new Color(petal.r * j, petal.g * j, petal.b * j, 1f);
                CrystalVizBootstrap.AppendWildflower(verts, normals, uvs, uvs2,
                    colors, tris, mtx, rng, petal, rng.Next(4), out _);
            }
        }

        var mesh = new Mesh { name = "OakDrift" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uvs2);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        var go = new GameObject("OakDrift");
        go.transform.SetParent(set.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
    }

    // ------------------------------------------------------------------
    // Pine: alpine clearing — distant peak silhouettes + gentle snowfall.
    // ------------------------------------------------------------------

    void BuildPineProps(Shader waterShader, Shader litShader)
    {
        var set = NewPropSet(ParametricTree.TreeSpecies.Pine, "PineAlpine");
        var rng = new System.Random(80808);

        if (litShader != null)
        {
            // Distant peak silhouettes: big soft cones on the horizon ring,
            // blue-gray so the linear fog melts them into the distance.
            // Snow-capped: a smaller white cone rides each summit.
            var rockMat = NewLit(new Color(0.42f, 0.50f, 0.64f, 1f));
            var snowMat = NewLit(new Color(0.88f, 0.92f, 0.96f, 1f));
            // v1.0.63: explicit forward placement — every peak sits at z<0
            // and clear of x≈0 so none can loom beside/behind the camera as
            // the giant dark wedge the old radial ring produced (one peak
            // landed at small +x / far -z, i.e. huge and near frame-center).
            var peakDefs = new (float x, float z, float w, float h)[]
            {
                (-11f, -62f, 22f, 26f), (-5.5f, -72f, 18f, 31f),
                (3f, -60f, 20f, 24f),   (8.5f, -70f, 22f, 29f),
                (14f, -64f, 24f, 27f),  (-17f, -78f, 26f, 34f),
                (19f, -76f, 26f, 33f),
            };
            for (int i = 0; i < peakDefs.Length; i++)
            {
                float px = peakDefs[i].x, pz = peakDefs[i].z;
                float w = peakDefs[i].w, h = peakDefs[i].h;
                var peak = new GameObject("Peak" + i);
                peak.transform.SetParent(set.transform, false);
                peak.transform.position = new Vector3(px, h * 0.5f - 3f, pz);
                peak.AddComponent<MeshFilter>().mesh = MakeCone(w * 0.5f, h, 7);
                var pmr = peak.AddComponent<MeshRenderer>();
                pmr.material = rockMat;
                pmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                pmr.receiveShadows = false;

                var cap = new GameObject("PeakCap" + i);
                cap.transform.SetParent(set.transform, false);
                cap.transform.position = new Vector3(px, h - 3f - h * 0.16f, pz);
                cap.AddComponent<MeshFilter>().mesh = MakeCone(w * 0.5f * 0.42f, h * 0.34f, 7);
                var cmr = cap.AddComponent<MeshRenderer>();
                cmr.material = snowMat;
                cmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cmr.receiveShadows = false;
            }
        }

        if (waterShader != null)
        {
            // Gentle snowfall: soft white motes drifting down around the
            // clearing, wrapping seamlessly.
            var snowColor = new Color(0.95f, 0.97f, 1f, 1f);
            for (int i = 0; i < 55; i++)
            {
                float px = ((float)rng.NextDouble() - 0.5f) * 20f;
                float pz = -8f + (float)rng.NextDouble() * 14f;
                float py = (float)rng.NextDouble() * 8f;
                float s = 0.10f + (float)rng.NextDouble() * 0.08f;
                AddDrifter(set, waterShader, new Vector3(px, py, pz),
                    new Vector2(s, s), snowColor, 0.75f, 2,
                    (float)rng.NextDouble(),
                    fallSpeed: 0.5f + (float)rng.NextDouble() * 0.4f,
                    swayAmp: 0.25f, swaySpeed: 0.5f,
                    topY: 8f, rangeY: 8.5f);
            }
        }
    }

    // ------------------------------------------------------------------
    // Birch: slender white grove — pale trunks with dark lenticels, soft
    // yellow-green canopies, a fern floor.
    // ------------------------------------------------------------------

    void BuildBirchProps(Shader grassShader, Shader litShader)
    {
        var set = NewPropSet(ParametricTree.TreeSpecies.Birch, "BirchGrove");
        var rng = new System.Random(90909);

        if (litShader != null)
        {
            // Nine slender white trunks ringing the clearing — the two nearest
            // fully frame the portrait shot at the left/right edges (|x|>=1.9
            // keeps the corridor to the main tree open); the rest peek in
            // from the edges or fill wide screens. Screen-right is -x for
            // this camera's LookAt basis, so +x entries appear on the left.
            var trunkPos = new (float x, float z)[]
            {
                (1.9f, -6.5f), (-1.9f, -7.5f),
                (3.4f, -4.5f), (-3.4f, -5f),
                (5.5f, -8f), (-5.5f, -8.5f),
                (2.6f, -11f), (-2.6f, -11.5f),
            };
            var barkMat = NewLit(new Color(0.92f, 0.90f, 0.86f, 1f));
            var lenticelMat = NewLit(new Color(0.16f, 0.15f, 0.14f, 1f));
            var canopyMat = NewLit(new Color(0.55f, 0.68f, 0.30f, 1f));
            for (int i = 0; i < trunkPos.Length; i++)
            {
                float bx = trunkPos[i].x + ((float)rng.NextDouble() - 0.5f) * 1.2f;
                float bz = trunkPos[i].z + ((float)rng.NextDouble() - 0.5f) * 1.2f;
                float h = 4f + (float)rng.NextDouble() * 1.5f;
                float tr = 0.10f + (float)rng.NextDouble() * 0.04f;
                Primitive(PrimitiveType.Cylinder, set, "BirchTrunk" + i,
                    new Vector3(bx, h * 0.5f, bz), new Vector3(tr * 2f, h, tr * 2f), barkMat);
                // Dark lenticel bands: thin dark rings up the white bark.
                int bands = 4 + rng.Next(3);
                for (int b = 0; b < bands; b++)
                {
                    float by = h * (0.15f + 0.75f * (float)rng.NextDouble());
                    Primitive(PrimitiveType.Cylinder, set, $"BirchBand{i}_{b}",
                        new Vector3(bx, by, bz),
                        new Vector3(tr * 2f + 0.012f, 0.035f, tr * 2f + 0.012f), lenticelMat);
                }
                // Soft canopy blob: a squashed ellipsoid in yellow-green,
                // kept modest so the trunks read as trunks, not bushes.
                float cw = 0.8f + (float)rng.NextDouble() * 0.5f;
                Primitive(PrimitiveType.Sphere, set, "BirchCanopy" + i,
                    new Vector3(bx, h + 0.4f, bz),
                    new Vector3(cw * 2f, cw * 1.15f, cw * 2f), canopyMat);
            }
        }

        if (grassShader != null)
        {
            // Fern floor: low drooping tufts in deep green, clustered near
            // the background trunks.
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var idx = new List<int>();
            var frng = new System.Random(91919);
            for (int i = 0; i < 70; i++)
            {
                float a = (float)frng.NextDouble() * Mathf.PI * 2f;
                float rr = 4f + (float)frng.NextDouble() * 9f;
                float bx = Mathf.Cos(a) * rr;
                float bz = Mathf.Sin(a) * rr;
                if (bx * bx + bz * bz < 1.2f) continue;
                float cdx = bx, cdz = bz - 7.4f;
                if (cdx * cdx + cdz * cdz < 9f) continue;
                float yaw = (float)frng.NextDouble() * Mathf.PI * 2f;
                int blades = 4 + frng.Next(3);
                for (int b = 0; b < blades; b++)
                {
                    float h = 0.22f + (float)frng.NextDouble() * 0.18f;
                    float w = 0.030f + (float)frng.NextDouble() * 0.014f;
                    float lean = ((float)frng.NextDouble() - 0.5f) * 0.9f;
                    AppendReedBlade(verts, uvs, idx,
                        new Vector3(bx, 0f, bz), yaw + lean * 0.3f, h, w);
                }
            }
            var mesh = new Mesh { name = "BirchFerns" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var mat = new Material(grassShader);
            mat.SetColor("_RootColor", new Color(0.10f, 0.25f, 0.08f, 1f));
            mat.SetColor("_TipColor", new Color(0.30f, 0.50f, 0.16f, 1f));
            mat.SetFloat("_WindStrength", 0.03f);
            mat.SetFloat("_WindSpeed", 1.4f);
            mat.SetFloat("_GustStrength", 0.08f);
            mat.SetFloat("_GustSpeed", 1.8f);
            mat.SetFloat("_GustFreq", 0.18f);
            mat.SetFloat("_GustLighten", 0.28f);

            var go = new GameObject("Ferns");
            go.transform.SetParent(set.transform, false);
            go.AddComponent<MeshFilter>().mesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.material = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }

    // ------------------------------------------------------------------
    // Cherry: sakura garden — stone lantern, still pond, stepping stones,
    // petals always on the breeze.
    // ------------------------------------------------------------------

    void BuildCherryProps(Shader waterShader, Shader litShader)
    {
        var set = NewPropSet(ParametricTree.TreeSpecies.Cherry, "CherryGarden");
        var prof = EnvironmentProfiles.ForSpecies(ParametricTree.TreeSpecies.Cherry);
        var rng = new System.Random(11111);

        if (waterShader != null)
        {
            // Still garden pond: darker and calmer-reading than the willow
            // lake (deep reflective tint, high alpha).
            BuildWaterDisc(set, waterShader, prof.pondCenter, prof.pondRadius,
                new Color(0.14f, 0.22f, 0.30f, 1f), 0.88f, "GardenPond");

            // Drifting sakura petals: soft pink-white motes tumbling down
            // through the whole garden air.
            var petalColor = new Color(0.97f, 0.78f, 0.84f, 1f);
            for (int i = 0; i < 70; i++)
            {
                float px = ((float)rng.NextDouble() - 0.5f) * 16f;
                float pz = -7f + (float)rng.NextDouble() * 12f;
                float py = (float)rng.NextDouble() * 6.5f;
                float s = 0.06f + (float)rng.NextDouble() * 0.05f;
                AddDrifter(set, waterShader, new Vector3(px, py, pz),
                    new Vector2(s, s), petalColor, 0.9f, 1,
                    (float)rng.NextDouble(),
                    fallSpeed: 0.35f + (float)rng.NextDouble() * 0.25f,
                    swayAmp: 0.4f + (float)rng.NextDouble() * 0.4f,
                    swaySpeed: 0.9f + (float)rng.NextDouble() * 0.5f,
                    topY: 6.5f, rangeY: 7f);
            }
        }

        if (litShader == null) return;

        var stoneMat = NewLit(new Color(0.55f, 0.55f, 0.58f, 1f));

        // Stone lantern (tōrō), facing the camera. Screen-right is -x for
        // this camera's LookAt basis, so +x puts it on the frame's left,
        // balancing the pond on the right.
        var lx = 1.0f; var lz = -2.6f;
        Primitive(PrimitiveType.Cube, set, "LanternBase",
            new Vector3(lx, 0.09f, lz), new Vector3(0.55f, 0.18f, 0.55f), stoneMat);
        Primitive(PrimitiveType.Cylinder, set, "LanternPillar",
            new Vector3(lx, 0.45f, lz), new Vector3(0.20f, 0.55f, 0.20f), stoneMat);
        Primitive(PrimitiveType.Cube, set, "LanternPlatform",
            new Vector3(lx, 0.77f, lz), new Vector3(0.45f, 0.10f, 0.45f), stoneMat);
        Primitive(PrimitiveType.Cube, set, "LanternFirebox",
            new Vector3(lx, 0.98f, lz), new Vector3(0.36f, 0.32f, 0.36f), stoneMat);
        // Warm glowing windows on the firebox faces.
        var glowMat = NewLit(new Color(1f, 0.72f, 0.42f, 1f));
        var glow = glowMat;
        if (glow != null && glow.HasProperty("_EmissionColor"))
        {
            glow = new Material(glow);
            glow.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.25f, 1f) * 1.6f);
            glow.EnableKeyword("_EMISSION");
        }
        Primitive(PrimitiveType.Quad, set, "LanternGlowF",
            new Vector3(lx, 0.98f, lz + 0.185f), new Vector3(0.16f, 0.16f, 1f), glow);
        Primitive(PrimitiveType.Quad, set, "LanternGlowB",
            new Vector3(lx, 0.98f, lz - 0.185f), new Vector3(0.16f, 0.16f, 1f), glow)
            .transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        // Pyramid roof + jewel.
        var roof = new GameObject("LanternRoof");
        roof.transform.SetParent(set.transform, false);
        roof.transform.position = new Vector3(lx, 1.27f, lz);
        roof.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
        roof.AddComponent<MeshFilter>().mesh = MakeCone(0.34f, 0.28f, 4);
        var rmr = roof.AddComponent<MeshRenderer>();
        rmr.material = stoneMat;
        rmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rmr.receiveShadows = false;
        Primitive(PrimitiveType.Sphere, set, "LanternJewel",
            new Vector3(lx, 1.45f, lz), new Vector3(0.13f, 0.13f, 0.13f), stoneMat);

        // Stepping stones curving from the foreground toward the pond
        // (screen-right is -x, so -x leads right toward the water's edge).
        // Smaller and lighter than v1.0.63a — the old ones read as black
        // holes in the grass.
        var stepMat = NewLit(new Color(0.50f, 0.50f, 0.54f, 1f));
        var stonePath = new (float x, float z)[]
        {
            (-0.35f, 1.0f), (-0.75f, -0.1f), (-1.05f, -1.1f), (-1.25f, -2.0f),
        };
        for (int i = 0; i < stonePath.Length; i++)
        {
            Primitive(PrimitiveType.Cylinder, set, "StepStone" + i,
                new Vector3(stonePath[i].x, 0.035f, stonePath[i].z),
                new Vector3(0.44f, 0.07f, 0.44f), stepMat)
                .transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 60f, 0f);
        }
    }

    // ------------------------------------------------------------------
    // Palm: tropical shore — turquoise shallows, wave lines, shells.
    // ------------------------------------------------------------------

    void BuildPalmProps(Shader waterShader, Shader litShader)
    {
        var set = NewPropSet(ParametricTree.TreeSpecies.Palm, "PalmShore");
        var prof = EnvironmentProfiles.ForSpecies(ParametricTree.TreeSpecies.Palm);
        var rng = new System.Random(22222);

        if (waterShader != null)
        {
            // Turquoise shallows lapping in from the frame's edge (+x reads
            // on the frame's left for this camera's LookAt basis).
            BuildWaterDisc(set, waterShader, prof.pondCenter, prof.pondRadius,
                new Color(0.18f, 0.62f, 0.66f, 1f), 0.80f, "Shallows");

            // Gentle wave lines: long soft white billboards along the
            // shallows' near edge, breathing almost imperceptibly.
            var waveColor = new Color(0.95f, 0.98f, 1f, 1f);
            var waveDefs = new (Vector3 pos, float len)[]
            {
                (new Vector3(1.6f, 0.06f, -1.4f), 4.2f),
                (new Vector3(2.6f, 0.06f, -2.6f), 5.2f),
                (new Vector3(3.4f, 0.06f, -3.9f), 5.8f),
            };
            for (int i = 0; i < waveDefs.Length; i++)
            {
                var mat = new Material(waterShader);
                mat.SetColor("_Color", waveColor);
                mat.SetFloat("_Alpha", 0.30f);
                mat.SetFloat("_Mode", 1f);
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "WaveLine" + i;
                StripCollider(go);
                go.transform.SetParent(set.transform, false);
                go.transform.position = waveDefs[i].pos;
                go.transform.rotation = Quaternion.Euler(65f, 0f, -18f - i * 6f);
                go.transform.localScale = new Vector3(waveDefs[i].len, 0.16f, 1f);
                var mr = go.GetComponent<MeshRenderer>();
                mr.material = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        if (litShader == null) return;

        // Scattered shells on the sand near the waterline.
        var shellMatA = NewLit(new Color(0.95f, 0.90f, 0.86f, 1f));
        var shellMatB = NewLit(new Color(0.93f, 0.78f, 0.74f, 1f));
        for (int i = 0; i < 6; i++)
        {
            float a = (200f + i * 22f) * Mathf.Deg2Rad;
            float rr = prof.pondRadius - 0.9f + (float)rng.NextDouble() * 0.7f;
            float sx = prof.pondCenter.x + Mathf.Cos(a) * rr;
            float sz = prof.pondCenter.z + Mathf.Sin(a) * rr;
            float sr = 0.06f + (float)rng.NextDouble() * 0.035f;
            Primitive(PrimitiveType.Sphere, set, "Shell" + i,
                new Vector3(sx, sr * 0.4f, sz),
                new Vector3(sr * 2f, sr * 0.9f, sr * 2f),
                i % 2 == 0 ? shellMatA : shellMatB);
        }
    }
}
