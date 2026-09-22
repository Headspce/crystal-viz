using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Crystal Viz bootstrap: builds the entire diorama at runtime so the shipped
/// scene file stays tiny. The scene is a tap-to-grow parametric tree on a
/// stylized grass field dotted with wind-blown wildflowers, under a
/// procedural anime sky dome, with a single sun that orbits the tree under
/// slider control (see SunOrbitControl).
/// Attach to an empty GameObject in CrystalViz.unity; it finds MainCamera itself.
/// </summary>
public class CrystalVizBootstrap : MonoBehaviour
{
    public static readonly Vector3 FocusPoint = new Vector3(0f, 1.3f, 0f);

    [HideInInspector] public Light sun;
    [HideInInspector] public float sunElevationDeg = 35f;
    [HideInInspector] public float sunDistance = 14f;

    // v1.0.29: flower blossom-head world positions, recorded during
    // BuildWildflowers so the butterfly prototype has targets to visit.
    [HideInInspector] public System.Collections.Generic.List<Vector3> flowerHeads =
        new System.Collections.Generic.List<Vector3>();

    void Awake() => BuildScene();

    /// <summary>
    /// Builds the whole diorama. Called from Awake at runtime; the CI
    /// screenshot tool calls it directly in edit mode (Awake never runs in
    /// edit mode).
    /// </summary>
    public void BuildScene()
    {
        Random.InitState(1234); // deterministic branches every run
        BuildCamera();
        BuildEnvironment();
        BuildSkyDome();
        BuildHorizonHaze();
        BuildHorizonHills();
        BuildSun();
        BuildDiorama();
        // Player light control: the right-edge sun slider is back by player
        // request (Tyler missed the light adjustment bar). SunOrbitControl
        // builds its UI in Start(), which never runs in the CI screenshot
        // path (edit mode), so captures stay clean while player builds get
        // the working slider.
        var sunCtrl = gameObject.AddComponent<SunOrbitControl>();
        sunCtrl.bootstrap = this;
    }

    /// <summary>
    /// URP/Lit is created at runtime via Shader.Find. If it was stripped from
    /// the build this returns null (and logs loudly) instead of producing a
    /// magenta material or crashing halfway through the diorama.
    /// </summary>
    static Material NewLitMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("CrystalViz: 'Universal Render Pipeline/Lit' not found in this build. " +
                "CI builds a ShaderVariantCollection for it automatically.");
            return null;
        }
        return new Material(shader);
    }

    /// <summary>
    /// Destroy() is illegal in edit mode (the CI screenshot tool builds the
    /// scene in edit mode); use DestroyImmediate there instead.
    /// </summary>
    static void DestroyNow(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Object.Destroy(obj);
        else Object.DestroyImmediate(obj);
    }

    // ------------------------------------------------------------------ camera

    void BuildCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera", typeof(Camera));
            go.tag = "MainCamera";
            cam = go.GetComponent<Camera>();
        }
        cam.transform.position = new Vector3(0f, 2.5f, 7.4f);
        cam.transform.LookAt(new Vector3(0f, 1.7f, 0f)); // frame tree + grass
        cam.fieldOfView = 40f;
        cam.clearFlags = CameraClearFlags.SolidColor; // sky is a mesh dome; solid clear is the proven path
        // Periwinkle sampled from the bottom edge of clouds.jpg: the sky,
        // the distance fog, and the clear color all meet at this one hue so
        // the ground melts into the horizon with no seam.
        cam.backgroundColor = new Color(0.611f, 0.672f, 0.824f, 1f);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 250f;
    }

    // -------------------------------------------------------------- environment

    void BuildEnvironment()
    {
        // Endless-meadow horizon: the ground runs far past the sky plane and
        // melts into a periwinkle distance fog sampled from the bottom edge
        // of clouds.jpg. Near field (tree + grass) stays crisp; the far
        // ground fades to exactly the fog/clear color, so there is no seam
        // where the world ends and the sky begins.
        var horizon = new Color(0.611f, 0.672f, 0.824f, 1f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = horizon;
        RenderSettings.fogStartDistance = 40f;
        RenderSettings.fogEndDistance = 80f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.43f, 0.46f, 1f);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "GrassGround";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(20f, 1f, 20f); // 200x200 world units, past the sky plane
        // MeadowGround shader: dark-green base tint preserved exactly, with
        // Poly Haven dirt detail and wind-aligned moving cloud shadows.
        var meadowShader = Shader.Find("CrystalViz/MeadowGround");
        if (meadowShader != null)
        {
            var gmat = new Material(meadowShader);
            gmat.color = new Color(0.24f, 0.45f, 0.17f, 1f); // dark shadowed moss: gaps read as depth under the grass, not neon
            var dirtTex = Resources.Load<Texture2D>("Textures/brown_mud_leaves_01_1k");
            if (dirtTex != null) gmat.SetTexture("_DirtTex", dirtTex);
            ground.GetComponent<Renderer>().material = gmat;
        }
        else
        {
            // Fallback: plain lit material if the shader was stripped.
            var gmat = NewLitMaterial();
            if (gmat != null)
            {
                gmat.color = new Color(0.24f, 0.45f, 0.17f, 1f);
                gmat.SetFloat("_Smoothness", 0f);
                ground.GetComponent<Renderer>().material = gmat;
            }
        }

        // Faint cool fill so shadow sides of the tree don't go pitch black.
        var fill = new GameObject("FillLight").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.70f, 0.80f, 1.0f, 1f);
        fill.intensity = 0.35f;
        fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.Euler(50f, -130f, 0f);
    }

    // ------------------------------------------------------- sky dome

    Material skyDomeMat;

    /// <summary>
    /// Sky backdrop: a giant inverted sphere (radius 200, inside the 250 far
    /// plane) wearing the authentic "FREE - SkyBox Anime Sky" panorama by
    /// Paul (@paul_paul_paul), CC-BY 4.0 — the clean original texture from
    /// the authenticated Sketchfab download (2026-09-21), downscaled to
    /// 4096x2048. Sampled equirectangular by view direction on the sphere,
    /// so there is no UV seam anywhere; the dome turns ultra-slowly (one
    /// revolution ~20 minutes) via SkyRotation. Falls back to the procedural
    /// CrystalViz/AnimeSkybox shader if the texture or textured shader is
    /// missing: never a crash, never a blank sky. A mesh dome instead of
    /// RenderSettings.skybox: the CI screenshot renders the camera directly
    /// in edit mode, where the skybox pass does not draw (verified: sky
    /// rendered as flat clear color), while plain meshes render reliably on
    /// that path. The dome turns ultra-slowly (one revolution ~20 minutes)
    /// via SkyRotation so the painted clouds drift across the sky.
    /// </summary>
    void BuildSkyDome()
    {
        var tex = Resources.Load<Texture2D>("anime-sky");
        var texShader = Shader.Find("CrystalViz/AnimeSkyTextured");
        if (tex != null && texShader != null)
        {
            skyDomeMat = new Material(texShader);
            skyDomeMat.mainTexture = tex;
            var dome = BuildDomeMesh();
            dome.GetComponent<Renderer>().material = skyDomeMat;
            dome.AddComponent<SkyRotation>();
            Debug.Log("CrystalViz: textured anime sky dome installed (r=200).");
            return;
        }
        if (tex == null) Debug.LogWarning("CrystalViz: 'anime-sky' texture not found in Resources; trying procedural sky.");
        if (texShader == null) Debug.LogWarning("CrystalViz: 'CrystalViz/AnimeSkyTextured' shader not found; trying procedural sky.");
        BuildProceduralSkyDome();
    }

    /// <summary>
    /// Builds the inverted sky sphere centered on the camera. Shared by the
    /// textured and procedural sky paths.
    /// </summary>
    static GameObject BuildDomeMesh()
    {
        // Inverted sphere: Cull Front in the shader shows the interior.
        // Centered on the camera so painted directions match view directions.
        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "SkyDome";
        DestroyNow(dome.GetComponent<Collider>());
        Camera mainCam = Camera.main;
        dome.transform.position = mainCam != null ? mainCam.transform.position : Vector3.zero;
        dome.transform.localScale = new Vector3(400f, 400f, 400f); // radius 200 < far plane 250
        var rend = dome.GetComponent<Renderer>();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        return dome;
    }

    /// <summary>
    /// Fallback sky: the fully procedural CrystalViz/AnimeSkybox shader —
    /// deep-blue gradient zenith, bright warm horizon, dramatic cel-shaded
    /// cumulus billows and thin cirrus wisps drifting ultra-slowly. Pure math
    /// => no texture seam anywhere, no matter how long you stare at it. The
    /// horizon color equals the fog color so the ground melts into the sky
    /// with no visible line, preserving the praised horizon blend.
    /// Missing shader => quietly skipped, never a crash.
    /// </summary>
    void BuildProceduralSkyDome()
    {
        var shader = Shader.Find("CrystalViz/AnimeSkybox");
        if (shader == null)
        {
            Debug.LogWarning("CrystalViz: 'CrystalViz/AnimeSkybox' shader not found; skipping sky dome.");
            return;
        }
        skyDomeMat = new Material(shader);
        // Must equal the fog color in BuildEnvironment: one hue for sky,
        // fog, haze band, and clear color => seamless horizon.
        skyDomeMat.SetColor("_HorizonColor", new Color(0.611f, 0.672f, 0.824f, 1f));
        skyDomeMat.SetColor("_MidColor", new Color(0.45f, 0.65f, 0.93f, 1f));
        skyDomeMat.SetColor("_ZenithColor", new Color(0.15f, 0.36f, 0.78f, 1f));
        skyDomeMat.SetColor("_CloudShadow", new Color(0.70f, 0.73f, 0.87f, 1f));
        skyDomeMat.SetColor("_CloudMid", new Color(0.93f, 0.94f, 0.99f, 1f));
        skyDomeMat.SetColor("_CloudLight", new Color(1f, 1f, 1f, 1f));
        skyDomeMat.SetColor("_SunColor", new Color(1f, 0.93f, 0.78f, 1f));
        skyDomeMat.SetFloat("_CloudScale", 1.2f);
        skyDomeMat.SetFloat("_Coverage", 0.72f);
        // Ultra-slow drift: a full cloud cycle takes many minutes.
        skyDomeMat.SetFloat("_WindSpeed", 0.004f);

        var dome = BuildDomeMesh();
        dome.GetComponent<Renderer>().material = skyDomeMat;
        dome.AddComponent<SkyRotation>();
        SyncSkySun();
        Debug.Log("CrystalViz: procedural anime sky dome installed (r=200).");
    }

    /// <summary>
    /// Points the dome's painted sun at the real directional light so the
    /// disc follows it as it orbits. Safe to call before either exists.
    /// </summary>
    void SyncSkySun()
    {
        if (skyDomeMat == null || sun == null) return;
        Vector3 dir = (sun.transform.position - FocusPoint).normalized;
        skyDomeMat.SetVector("_SunDir", new Vector4(dir.x, dir.y, dir.z, 0f));
    }

    /// <summary>
    /// Horizon haze: a wide, short transparent quad standing just in front
    /// of the sky plane, painted with a vertical gradient of the fog color.
    /// It blurs the line where the grass field meets the sky into a soft
    /// painted smudge instead of a hard edge. Its bottom dips below the
    /// ground plane so the opaque ground clips it exactly at the horizon.
    /// Missing shader => quietly skipped, never a crash.
    /// </summary>
    void BuildHorizonHaze()
    {
        var shader = Shader.Find("CrystalViz/HorizonHaze");
        if (shader == null)
        {
            Debug.LogWarning("CrystalViz: 'CrystalViz/HorizonHaze' shader not found; skipping horizon haze.");
            return;
        }
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "HorizonHaze";
        DestroyNow(quad.GetComponent<Collider>());
        // In front of the sky plane (z=-60), straddling the horizon line:
        // 300 wide, 12 tall, centered at y=5 so it spans y=-1..11.
        quad.transform.position = new Vector3(0f, 5f, -55f);
        quad.transform.localScale = new Vector3(300f, 12f, 1f);
        var mat = new Material(shader);
        mat.SetColor("_HazeColor", new Color(0.611f, 0.672f, 0.824f, 1f));
        mat.SetFloat("_BottomAlpha", 0.9f);
        var rend = quad.GetComponent<Renderer>();
        rend.material = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        Debug.Log("CrystalViz: horizon haze band placed at z=-55.");
    }

    /// <summary>
    /// Grassy mountainous hills ringing the horizon (player request
    /// 2026-09-21): a ring of smooth randomized mounds far out on the
    /// meadow, so the horizon reads as rolling green hills with the
    /// panorama's blue mountains layered behind them. Reuses the
    /// CrystalViz/StylizedGrass shader (no new shader, no new variants):
    /// uv.y runs 0 at the base to 1 at the crest for the root-to-tip
    /// gradient, wind and gust push are zeroed so the hills never move, but
    /// the gust light-wave still sweeps them, tying them into the meadow's
    /// weather. ONE combined mesh, one draw call. Distance fog hazes them
    /// into the horizon like the ground plane.
    /// Missing shader => quietly skipped, never a crash.
    ///
    /// The mound definitions are kept in hillDefs so BuildGrassField can
    /// scatter real grass tufts across the slopes (same blades, same wind,
    /// scaled up to read at 60 units).
    /// </summary>
    struct HillDef
    {
        public Vector3 center;
        public float height;
        public float radius;
        public float phase;
    }
    readonly System.Collections.Generic.List<HillDef> hillDefs =
        new System.Collections.Generic.List<HillDef>();

    void BuildHorizonHills()
    {
        var grassShader = Shader.Find("CrystalViz/StylizedGrass");
        if (grassShader == null)
        {
            Debug.LogWarning("CrystalViz: 'CrystalViz/StylizedGrass' shader not found; skipping horizon hills.");
            return;
        }
        var mat = new Material(grassShader);
        mat.SetColor("_RootColor", new Color(0.11f, 0.29f, 0.10f, 1f));
        mat.SetColor("_TipColor", new Color(0.46f, 0.68f, 0.22f, 1f));
        mat.SetFloat("_WindStrength", 0f);   // hills don't sway
        mat.SetFloat("_WindSpeed", 1.7f);
        mat.SetFloat("_GustStrength", 0f);  // ...or get combed flat
        mat.SetFloat("_GustSpeed", 1.8f);
        mat.SetFloat("_GustFreq", 0.18f);  // shared bands: hills catch the light wave
        mat.SetFloat("_GustLighten", 0.28f);

        var rng = new System.Random(20260921);
        var verts = new System.Collections.Generic.List<Vector3>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var tris = new System.Collections.Generic.List<int>();

        // Rolling ridge on the horizon. Camera truth: pos (0,2.5,7.4), pitched
        // 6.2 deg down, vertical FOV 40 -> horizontal half-FOV 9.53 deg,
        // frame top at +13.8 deg elevation. Six broad GENTLE swells, x in
        // [-19.5, 19.5], z staggered [-50, -62]: nearer swells rise higher,
        // farther ones sink lower and paler into the fog (43-74%) for natural
        // aerial perspective. Crests just under the horizon line (~0 to -1 deg
        // elevation), h/r ~0.27: low rolling farmland swells, deliberately
        // flattened (2026-09-21) so they read as landscape, not domes, and
        // halved again (2026-09-21) to sit quieter on the horizon
        hillDefs.Clear();
        const int hills = 6;
        for (int i = 0; i < hills; i++)
        {
            float x = -17.5f + i * 7f + ((float)rng.NextDouble() - 0.5f) * 4f;
            float z = -50f - (float)rng.NextDouble() * 12f;
            float h = 1.6f + (float)rng.NextDouble() * 0.6f;  // 1.6-2.2 tall (half of v1.0.24)
            float rad = 6.5f + (float)rng.NextDouble() * 1.5f;   // 6.5-8 wide (half of v1.0.24)
            var def = new HillDef
            {
                center = new Vector3(x, 0f, z),
                height = h,
                radius = rad,
                phase = (float)rng.NextDouble() * Mathf.PI * 2f,
            };
            hillDefs.Add(def);
            AppendHill(verts, normals, uvs, tris, def);
        }

        var mesh = new Mesh { name = "HorizonHills" };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        var go = new GameObject("HorizonHills");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        Debug.Log($"CrystalViz: horizon hills raised ({hills} mounds, {tris.Count / 3} tris, 1 draw call).");
    }

    /// <summary>
    /// Appends one smooth grassy mound: a polar grid with a single top
    /// vertex, cosine-falloff profile, and a sinusoidal wobble so no two
    /// hills share a silhouette. uv.y is 1 at the crest, 0 at the base.
    /// The grass shader is double-sided (Cull Off), so winding is cosmetic.
    /// </summary>
    static void AppendHill(
        System.Collections.Generic.List<Vector3> verts,
        System.Collections.Generic.List<Vector3> normals,
        System.Collections.Generic.List<Vector2> uvs,
        System.Collections.Generic.List<int> tris,
        HillDef def)
    {
        Vector3 center = def.center;
        float height = def.height;
        float radius = def.radius;
        float phase = def.phase;
        const int rings = 6;
        const int segs = 16;

        int topIdx = verts.Count;
        verts.Add(center + new Vector3(0f, height, 0f));
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(0.5f, 1f));

        for (int r = 1; r <= rings; r++)
        {
            float fr = r / (float)rings; // 0 near-top -> 1 base edge
            float ringR = radius * Mathf.Sin(fr * Mathf.PI * 0.5f);
            // Clamp: cos(pi/2) is ~-4.4e-8 in float, and Pow(negative, 1.25)
            // is NaN -- one NaN vertex poisons RecalculateBounds and the
            // whole mesh gets frustum-culled (invisible hills, no errors).
            float y = height * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(fr * Mathf.PI * 0.5f)), 1.25f);
            for (int s = 0; s < segs; s++)
            {
                float a = (s / (float)segs) * Mathf.PI * 2f;
                float wobble = 1f
                    + 0.14f * Mathf.Sin(a * 3f + phase) * fr
                    + 0.08f * Mathf.Sin(a * 5f + phase * 1.7f) * fr;
                Vector3 p = center + new Vector3(Mathf.Cos(a) * ringR * wobble, y, Mathf.Sin(a) * ringR * wobble);
                verts.Add(p);
                Vector3 n = (Vector3.up * (1f - fr * 0.7f)
                    + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (fr * 0.75f)).normalized;
                normals.Add(n);
                uvs.Add(new Vector2(s / (float)segs, 1f - fr));
            }
        }

        for (int s = 0; s < segs; s++) // fan: top -> first ring
        {
            int a0 = topIdx + 1 + s;
            int a1 = topIdx + 1 + (s + 1) % segs;
            tris.Add(topIdx); tris.Add(a1); tris.Add(a0);
        }
        for (int r = 1; r < rings; r++) // quads between rings
        {
            int r0 = topIdx + 1 + (r - 1) * segs;
            int r1 = topIdx + 1 + r * segs;
            for (int s = 0; s < segs; s++)
            {
                int s1 = (s + 1) % segs;
                tris.Add(r0 + s); tris.Add(r1 + s); tris.Add(r0 + s1);
                tris.Add(r0 + s1); tris.Add(r1 + s); tris.Add(r1 + s1);
            }
        }
    }

    /// <summary>
    /// Covers the horizon hills with the same grass as the meadow: tufts
    /// scattered across each mound's surface using the exact profile formula
    /// AppendHill uses, so blades sit on the slope instead of floating.
    /// Tufts are aligned to the surface normal with a random yaw and scaled
    /// 3-5x — at 50-60 units away, full-size blades would be sub-pixel, so
    /// the scale-up is what makes the hills read as grassy rather than
    /// smooth. Kept proportional to the mounds: oversized tufts (tried 8-12x)
    /// swallowed the silhouettes and turned the ridge into a fuzzy bright
    /// wall. They share the meadow's material and wind uniforms, so the
    /// gust fronts sweep the hills in sync. Appends into the grass field's
    /// mesh lists: still one combined mesh, one draw call.
    /// </summary>
    void AppendHillGrass(
        System.Collections.Generic.List<Vector3> verts,
        System.Collections.Generic.List<Vector3> normals,
        System.Collections.Generic.List<Vector2> uvs,
        System.Collections.Generic.List<int> tris,
        System.Random rng)
    {
        int hillTufts = 0;
        foreach (var def in hillDefs)
        {
            // ~4.5 tufts per unit^2 of mound footprint: a textured grassy
            // cover, not a solid carpet (the smooth hill mesh shows between).
            int count = Mathf.RoundToInt(Mathf.PI * def.radius * def.radius * 4.5f);
            for (int i = 0; i < count; i++)
            {
                float fr = 0.08f + (float)rng.NextDouble() * 0.84f; // avoid exact crest/base
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                // Same profile as AppendHill: ring radius, cosine-falloff
                // height, sinusoidal wobble.
                float ringR = def.radius * Mathf.Sin(fr * Mathf.PI * 0.5f);
                float y = def.height * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(fr * Mathf.PI * 0.5f)), 1.25f);
                float wobble = 1f
                    + 0.14f * Mathf.Sin(a * 3f + def.phase) * fr
                    + 0.08f * Mathf.Sin(a * 5f + def.phase * 1.7f) * fr;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 pos = def.center + new Vector3(radial.x * ringR * wobble, y, radial.z * ringR * wobble);
                // Same normal model as AppendHill.
                Vector3 n = (Vector3.up * (1f - fr * 0.7f) + radial * (fr * 0.75f)).normalized;
                pos -= n * 0.15f; // sink the roots so tufts never float
                Quaternion q = Quaternion.FromToRotation(Vector3.up, n)
                    * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                float s = 2f + (float)rng.NextDouble() * 1.5f; // 2-3.5x: grassy at 60 units, keeps silhouettes
                AppendGrassTuft(verts, normals, uvs, tris,
                    Matrix4x4.TRS(pos, q, Vector3.one * s), rng);
                hillTufts++;
            }
        }
        Debug.Log($"CrystalViz: hill slopes grassed ({hillTufts} tufts across {hillDefs.Count} mounds).");
    }

    // --------------------------------------------------------------------- sun

    void BuildSun()
    {
        var go = new GameObject("Sun");
        sun = go.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.95f, 0.88f, 1f);
        sun.intensity = 2.0f;
        sun.shadows = LightShadows.Soft;
        sun.shadowBias = 0.02f;
        PlaceSun(54f); // initial azimuth: pleasant 3/4 modeling light
    }

    /// <summary>
    /// Positions the sun on its orbit. azimuthDeg increases clockwise as seen
    /// by the camera (slider up = clockwise, slider down = counter-clockwise).
    /// </summary>
    public void PlaceSun(float azimuthDeg)
    {
        float rad = Mathf.Deg2Rad * -azimuthDeg; // negate => clockwise on screen
        float el = Mathf.Deg2Rad * sunElevationDeg;
        Vector3 dir = new Vector3(
            Mathf.Sin(rad) * Mathf.Cos(el),
            Mathf.Sin(el),
            Mathf.Cos(rad) * Mathf.Cos(el));
        sun.transform.position = FocusPoint + dir * sunDistance;
        sun.transform.LookAt(FocusPoint);
        SyncSkySun(); // painted sun disc follows the real light
    }

    // ------------------------------------------------------------------ diorama

    void BuildDiorama()
    {
        BuildGrowingTree();
        BuildGrassField();
        BuildWildflowers();
        // v1.0.29 butterfly prototype (player request): one butterfly flitting
        // between random flowers. Added after BuildWildflowers so flowerHeads
        // is populated. Its Start() runs in play mode; in the CI edit-mode
        // screenshot path Start never fires, so the butterfly simply idles at
        // origin there — harmless.
        var flyGO = new GameObject("Butterfly");
        var fly = flyGO.AddComponent<ButterflyController>();
        fly.bootstrap = this;
    }

    /// <summary>
    /// Interactive tap-to-grow tree (sprout -> mature over 50 taps). The
    /// growing tree is the centerpiece at origin; it replaces the old static
    /// OldTree. ParametricTree builds the geometry, TreeGrowthController owns
    /// tap input + persistence, StageIndicatorUI shows the stage avatar, and
    /// TreeResetButton adds the glowing infinity reset mark at the
    /// bottom-left (true sprout at zero taps, re-tappable to mature).
    /// NOTE: the CI screenshot path builds the scene in edit mode, where
    /// AddComponent does NOT fire Awake() and Start()/Update() never run.
    /// Each component exposes an idempotent Initialize() that is called
    /// explicitly here in dependency order (and again from Awake() in play
    /// mode — the guard makes that a harmless no-op).
    /// </summary>
    void BuildGrowingTree()
    {
        var treeGO = new GameObject("GrowingTree");
        treeGO.transform.position = Vector3.zero;
        // Scale so the mature tree (50 taps) stands ~2.5 units tall, matching
        // the visual volume of the old dead tree prototype.
        treeGO.transform.localScale = Vector3.one * 0.26f;
        treeGO.AddComponent<ParametricTree>().Initialize();
        var ctrl = treeGO.AddComponent<TreeGrowthController>();
        ctrl.Initialize(); // assigns tree, restores taps, calls ApplyGrowth()
        treeGO.AddComponent<StageIndicatorUI>().Initialize();
        // Test-loop reset mark (glowing infinity, bottom-left): pressing it
        // snaps the tree back to the true zero-tap sprout for repeated
        // growth playtesting. Explicit Initialize() for the edit-mode
        // screenshot path, same as the components above.
        treeGO.AddComponent<TreeResetButton>().Initialize();
        Debug.Log("CrystalViz: growing tree planted at origin.");
    }

    // ---------------------------------------------------------- stylized grass

    /// <summary>
    /// Stylized grass field under the tree, following the classic Unity
    /// stylized-grass tutorial beats: tapered blade tufts with a dark-root to
    /// light-tip gradient, wind sway in the vertex shader (stronger at the
    /// tip), and soft wrapped lighting that receives the tree's shadow.
    /// The whole field is ONE combined mesh (a single draw call): thousands
    /// of small tufts scattered across the entire ground, denser near the
    /// trunk. Wind phase comes from world position in the shader, so the
    /// combined mesh still ripples in traveling waves. The
    /// CrystalViz/StylizedGrass shader's variants are pinned in the build by
    /// CrystalVizBuild.EnsureVariantCollection().
    /// </summary>
    void BuildGrassField()
    {
        var grassShader = Shader.Find("CrystalViz/StylizedGrass");
        if (grassShader == null)
        {
            Debug.LogWarning("CrystalViz: 'CrystalViz/StylizedGrass' shader not found; skipping grass field.");
            return;
        }
        var mat = new Material(grassShader);
        mat.SetColor("_RootColor", new Color(0.15f, 0.34f, 0.11f, 1f));
        mat.SetColor("_TipColor", new Color(0.58f, 0.82f, 0.26f, 1f));
        // Perpetual breeze + distinct gust fronts with calm breaks in between
        // (player request 2026-09-21): the meadow always breathes, and every
        // few seconds a visible wave combs through it, then settles.
        mat.SetFloat("_WindStrength", 0.045f);
        mat.SetFloat("_WindSpeed", 1.7f);
        // Gust fronts: explicit here (not just shader defaults) so grass and
        // wildflowers provably share the same wave bands. Wider, slower
        // bands (player request 2026-09-21): long calm stretches between
        // fronts so the field reads as breezy with breaks.
        mat.SetFloat("_GustStrength", 0.12f); // lean, don't flatten: 0.45 combed blades flat and exposed the dark ground in pulsing waves
        mat.SetFloat("_GustSpeed", 1.8f);
        mat.SetFloat("_GustFreq", 0.18f);
        mat.SetFloat("_GustLighten", 0.28f);

        // One combined mesh => one draw call for the entire field (v1.0.5
        // used 800 GameObjects / 800 draw calls). Foliage-paint tool pinned
        // to MAX density with a tightened spawn radius: 150k tufts over the
        // whole 200x200 ground (r=100), single-segment blades (identical
        // silhouette at this size, 2/3 the verts) with tight radial-falloff
        // sampling — spawn radius 12 (down from 20) packs tufts ~2x closer
        // near the camera for a denser carpet, tapering with distance so
        // screen-space density stays constant all the way out to the fog.
        // Blades stay at 25% of v1.0.7 size. Deterministic seed so the
        // field looks identical on every launch.
        // v1.0.29: restored to 150k tufts per player request (the dense
        // carpet look). Frame-rate work moved into the shaders instead:
        // patchiness now computed per-vertex not per-fragment, pow() ->
        // multiply, so the full density runs cheaper than 60k did before.
        var rng = new System.Random(20260919);
        var verts = new System.Collections.Generic.List<Vector3>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var tris = new System.Collections.Generic.List<int>();

        const int count = 150000;
        for (int i = 0; i < count; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            // Tight-packing paint falloff: density ~ 1/(1+(r/12)^2).
            // Inverse-CDF sampling: r = 12*sqrt(70.44^u - 1), u in [0,1).
            // ~78 tufts per unit^2 at the center (2x tighter packing);
            // full 200x200 ground covered.
            float r = 12f * Mathf.Sqrt((float)(System.Math.Pow(70.44, rng.NextDouble()) - 1.0));
            float px = Mathf.Cos(a) * r;
            float pz = Mathf.Sin(a) * r;
            // Camera clearance: the camera sits at (0, 2.5, 7.4). A tuft
            // within ~3 units of it fills the screen as a glitchy dark bar
            // (seen pixel-identical at the right edge across builds). Skip
            // it; the rng sequence stays deterministic per launch.
            float cdx = px - 0f;
            float cdz = pz - 7.4f;
            if (cdx * cdx + cdz * cdz < 9.0f) continue; // 3^2
            var mtx = Matrix4x4.TRS(
                new Vector3(px, 0f, pz),
                Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                // 1.6x larger clusters: tufts overlap and cover bare spots
                // with zero extra geometry instances.
                Vector3.one * (0.7f + (float)rng.NextDouble() * 0.8f) * 1.6f);
            AppendGrassTuft(verts, normals, uvs, tris, mtx, rng);
        }

        // Grass the horizon hill slopes into the same mesh (BuildHorizonHills
        // runs before this in BuildScene, so hillDefs is populated; if the
        // hills were skipped the list is empty and this is a no-op).
        AppendHillGrass(verts, normals, uvs, tris, rng);

        var mesh = new Mesh { name = "GrassField" };
        // 150000 tufts x 20 verts = 3.0M verts: needs 32-bit indices.
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        var field = new GameObject("GrassField");
        field.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = field.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        // Grass doesn't cast (thin blades would speckle the shadows) but
        // it does receive, so the tree throws a soft shadow across it.
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        Debug.Log($"CrystalViz: grass field planted ({count} tufts, {tris.Count / 3} tris, 1 draw call).");
    }

    /// <summary>
    /// Appends one grass tuft (tapered, slightly curled blades) into shared
    /// mesh lists, transformed by the tuft's matrix. Normals all point up
    /// for the soft stylized look; uv.y is 0 at the root and 1 at the tip
    /// so the shader can gradient-color and wind-sway by height.
    /// Blades are tiny (75% smaller than v1.0.7) and densely packed.
    /// Height varies widely per blade (player request 2026-09-21) so the
    /// field has a natural uneven silhouette: 4-6 blades per tuft.
    /// </summary>
    static void AppendGrassTuft(
        System.Collections.Generic.List<Vector3> verts,
        System.Collections.Generic.List<Vector3> normals,
        System.Collections.Generic.List<Vector2> uvs,
        System.Collections.Generic.List<int> tris,
        Matrix4x4 mtx,
        System.Random rng)
    {
        int blades = 4 + rng.Next(3); // 4-6 blades per tuft
        const int segs = 1; // single-segment blades: at 0.05-0.15 tall the
                            // silhouette is identical to 2-seg, at 2/3 the verts
        for (int b = 0; b < blades; b++)
        {
            float ang = (b / (float)blades) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.9f;
            float tilt = 0.25f + (float)rng.NextDouble() * 0.35f;   // outward lean
            float height = 0.05f + (float)rng.NextDouble() * 0.10f; // 0.05-0.15: wide variation
            float width = 0.0075f + (float)rng.NextDouble() * 0.0045f;
            float curl = 0.012f + (float)rng.NextDouble() * 0.015f;  // tip curl
            Vector3 outward = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            Vector3 side = new Vector3(-outward.z, 0f, outward.x); // blade width axis
            Vector3 basePos = outward * (0.002f + (float)rng.NextDouble() * 0.003f);

            int baseIdx = verts.Count;
            for (int s = 0; s <= segs; s++)
            {
                float t = s / (float)segs;
                Vector3 c = basePos
                    + Vector3.up * (height * t)
                    + outward * (Mathf.Sin(tilt) * height * t + curl * t * t);
                float hw = width * 0.5f * (1f - t * 0.92f); // taper to a point
                verts.Add(mtx.MultiplyPoint3x4(c - side * hw));
                verts.Add(mtx.MultiplyPoint3x4(c + side * hw));
                normals.Add(Vector3.up);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0f, t));
                uvs.Add(new Vector2(1f, t));
            }
            for (int s = 0; s < segs; s++)
            {
                int r0 = baseIdx + s * 2;
                int r1 = baseIdx + (s + 1) * 2;
                tris.Add(r0); tris.Add(r1); tris.Add(r0 + 1);
                tris.Add(r0 + 1); tris.Add(r1); tris.Add(r1 + 1);
            }
        }
    }

    // ------------------------------------------------------- wildflowers

    /// <summary>
    /// Scattered wildflowers poking up through the grass: a thin stem plus a
    /// crossed-quad blossom in white, yellow, pink, or lavender. They ride
    /// the same wind field as the grass (CrystalViz/Wildflower shader), so
    /// the whole meadow ripples together. ONE combined mesh, one draw call.
    /// Missing shader => quietly skipped, never a crash.
    /// </summary>
    void BuildWildflowers()
    {
        var flowerShader = Shader.Find("CrystalViz/Wildflower");
        if (flowerShader == null)
        {
            Debug.LogWarning("CrystalViz: 'CrystalViz/Wildflower' shader not found; skipping wildflowers.");
            return;
        }
        var mat = new Material(flowerShader);
        mat.SetColor("_RootColor", new Color(0.12f, 0.30f, 0.10f, 1f));
        mat.SetColor("_TipColor", new Color(0.38f, 0.64f, 0.20f, 1f));
        // Blossom atlas: 4 head shapes (daisy, round wildflower, aster,
        // coneflower) in a 2x2 grid; each flower picks a cell, so the field
        // has real shape variety, not just color variety (player request
        // 2026-09-21). Near-white so the per-flower petal vertex color
        // defines the hue.
        mat.SetTexture("_BlossomMap", MakeBlossomAtlas());
        // Same wind field as the grass so they ripple together; a touch
        // stronger since blossoms sit higher and catch more air. Gust bands
        // are shared with the grass (same direction/speed/frequency) so the
        // whole meadow moves as one wave front. Perpetual breeze with calm
        // breaks between fronts (player request 2026-09-21).
        mat.SetFloat("_WindStrength", 0.06f);
        mat.SetFloat("_WindSpeed", 1.7f);
        mat.SetFloat("_GustStrength", 0.12f); // same: sway, don't plaster
        mat.SetFloat("_GustSpeed", 1.8f);
        mat.SetFloat("_GustFreq", 0.18f);
        mat.SetFloat("_GustLighten", 0.28f);

        var rng = new System.Random(20260920);
        var verts = new System.Collections.Generic.List<Vector3>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var uvs2 = new System.Collections.Generic.List<Vector2>(); // blossom-head texture coords
        var colors = new System.Collections.Generic.List<Color>();
        var tris = new System.Collections.Generic.List<int>();

        var petals = new[]
        {
            new Color(0.96f, 0.96f, 0.93f, 1f), // white daisy
            new Color(0.99f, 0.84f, 0.28f, 1f), // yellow
            new Color(0.96f, 0.55f, 0.66f, 1f), // pink
            new Color(0.66f, 0.56f, 0.90f, 1f), // lavender
        };

        // Same tight-packing falloff as the grass: scattered across the whole
        // 200x200 ground, densest near the tree, kept clear of the trunk.
        const int count = 6000;
        int planted = 0;
        for (int i = 0; i < count; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = 12f * Mathf.Sqrt((float)(System.Math.Pow(70.44, rng.NextDouble()) - 1.0));
            if (r < 0.9f) continue; // not inside the trunk
            // Camera clearance (same as the grass field): a blossom within
            // ~3 units of the camera fills the screen as a glitchy bar.
            float fdx = Mathf.Cos(a) * r - 0f;
            float fdz = Mathf.Sin(a) * r - 7.4f;
            if (fdx * fdx + fdz * fdz < 9.0f) continue; // 3^2
            var mtx = Matrix4x4.TRS(
                new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                Vector3.one * (0.8f + (float)rng.NextDouble() * 0.5f));
            var petal = petals[rng.Next(petals.Length)];
            // Slight per-flower brightness jitter so the field doesn't look stamped.
            float j = 0.88f + (float)rng.NextDouble() * 0.18f;
            petal = new Color(petal.r * j, petal.g * j, petal.b * j, 1f);
            int cell = rng.Next(4); // blossom-head shape from the atlas
            AppendWildflower(verts, normals, uvs, uvs2, colors, tris, mtx, rng, petal, cell, out Vector3 headWorld);
            flowerHeads.Add(headWorld); // v1.0.29: butterfly visit targets
            planted++;
        }

        var mesh = new Mesh { name = "Wildflowers" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uvs2);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        var field = new GameObject("Wildflowers");
        field.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = field.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        Debug.Log($"CrystalViz: wildflowers planted ({planted}, {tris.Count / 3} tris, 1 draw call).");
    }

    /// <summary>
    /// Appends one wildflower: a thin tapered stem quad plus two crossed
    /// vertical blossom quads (slightly trapezoid, wider at the top) riding
    /// at the stem tip. uv.x is 0 for stem / 1 for blossom; uv.y is 0 at the
    /// root and 1 at the blossom for the gradient + wind weighting.
    /// Flowers stand 0.15-0.35 tall: above the grass blades so they pop.
    /// <paramref name="cell"/> picks the blossom-head shape from the 2x2
    /// atlas (uv1 is mapped into that cell), and the blossom's proportions
    /// vary per flower for natural shape variety.
    /// </summary>
    static void AppendWildflower(
        System.Collections.Generic.List<Vector3> verts,
        System.Collections.Generic.List<Vector3> normals,
        System.Collections.Generic.List<Vector2> uvs,
        System.Collections.Generic.List<Vector2> uvs2,
        System.Collections.Generic.List<Color> colors,
        System.Collections.Generic.List<int> tris,
        Matrix4x4 mtx,
        System.Random rng,
        Color petal,
        int cell,
        out Vector3 headWorld) // v1.0.29: world-space blossom center for the butterfly
    {
        float h = 0.15f + (float)rng.NextDouble() * 0.20f;
        float tilt = ((float)rng.NextDouble() - 0.5f) * 0.25f;

        // Stem: single tapered quad, slight lean.
        float sw = 0.006f;
        Vector3 stemTop = new Vector3(tilt * h, h, 0f);
        int b = verts.Count;
        verts.Add(mtx.MultiplyPoint3x4(new Vector3(-sw, 0f, 0f)));
        verts.Add(mtx.MultiplyPoint3x4(new Vector3(sw, 0f, 0f)));
        verts.Add(mtx.MultiplyPoint3x4(stemTop + new Vector3(-sw * 0.5f, 0f, 0f)));
        verts.Add(mtx.MultiplyPoint3x4(stemTop + new Vector3(sw * 0.5f, 0f, 0f)));
        for (int i = 0; i < 4; i++) normals.Add(Vector3.up);
        uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(0f, 1f));
        for (int i = 0; i < 4; i++) uvs2.Add(Vector2.zero); // stem: no blossom texture
        for (int i = 0; i < 4; i++) colors.Add(Color.white);
        tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
        tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);

        // Blossom: two crossed vertical quads centered just above the stem tip.
        // Width and proportions vary per flower (player request 2026-09-21).
        float bw = 0.030f + (float)rng.NextDouble() * 0.035f;
        float bh = bw * (0.9f + (float)rng.NextDouble() * 0.6f);
        Vector3 c = stemTop + new Vector3(0f, bh * 0.28f, 0f);
        // Atlas cell origin: uv1 maps the full blossom quad into one of the
        // 2x2 cells so each flower wears a different head shape.
        float cu = (cell % 2) * 0.5f;
        float cv = (cell / 2) * 0.5f;
        for (int q = 0; q < 2; q++)
        {
            float yaw = q * Mathf.PI * 0.5f;
            Vector3 ax = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)); // width axis
            Vector3 nrm = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)); // face normal
            int qb = verts.Count;
            // Trapezoid: narrower at the bottom, wider at the top.
            verts.Add(mtx.MultiplyPoint3x4(c - ax * bw * 0.30f + new Vector3(0f, -bh * 0.5f, 0f)));
            verts.Add(mtx.MultiplyPoint3x4(c + ax * bw * 0.30f + new Vector3(0f, -bh * 0.5f, 0f)));
            verts.Add(mtx.MultiplyPoint3x4(c - ax * bw * 0.55f + new Vector3(0f, bh * 0.5f, 0f)));
            verts.Add(mtx.MultiplyPoint3x4(c + ax * bw * 0.55f + new Vector3(0f, bh * 0.5f, 0f)));
            for (int i = 0; i < 4; i++) normals.Add(nrm);
            uvs.Add(new Vector2(1f, 0.85f)); uvs.Add(new Vector2(1f, 0.85f));
            uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 1f));
            // Blossom-head texture coords (uv1): full 0..1 quad mapped into
            // this flower's atlas cell.
            uvs2.Add(new Vector2(cu, cv));
            uvs2.Add(new Vector2(cu + 0.5f, cv));
            uvs2.Add(new Vector2(cu, cv + 0.5f));
            uvs2.Add(new Vector2(cu + 0.5f, cv + 0.5f));
            for (int i = 0; i < 4; i++) colors.Add(petal);
            tris.Add(qb); tris.Add(qb + 2); tris.Add(qb + 1);
            tris.Add(qb + 1); tris.Add(qb + 2); tris.Add(qb + 3);
        }
        headWorld = mtx.MultiplyPoint3x4(c);
    }

    /// <summary>
    /// Builds the blossom-head atlas: a 2x2 grid of petal-head shapes on
    /// transparency, alpha-cut in the shader — 8-petal daisy, 5-petal round
    /// wildflower, 12-petal aster, 6-petal coneflower. Near-white so the
    /// per-flower petal vertex color defines the hue.
    /// </summary>
    static Texture2D MakeBlossomAtlas()
    {
        const int S = 128; // 2x2 cells of 64px
        const int C = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        // Per-cell recipe: petal count, groove depth (shape fullness), center size.
        var recipes = new (int petals, float groove, float center)[]
        {
            (8, 0.30f, 0.30f),  // daisy
            (5, 0.45f, 0.34f),  // round wildflower
            (12, 0.35f, 0.22f), // aster
            (6, 0.40f, 0.45f),  // coneflower
        };
        for (int cell = 0; cell < 4; cell++)
        {
            int cx = cell % 2, cy = cell / 2;
            var (petals, groove, centerR) = recipes[cell];
            for (int y = 0; y < C; y++)
            {
                for (int x = 0; x < C; x++)
                {
                    float u = ((float)x / (C - 1)) * 2f - 1f; // -1..1
                    float v = ((float)y / (C - 1)) * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float ang = Mathf.Atan2(v, u);
                    // Rounded petals: radius modulated by angle.
                    float petal = (1f - groove) + groove * Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * petals * 0.5f)), 0.7f);
                    Color c;
                    if (r >= petal)
                    {
                        c = new Color(0f, 0f, 0f, 0f);
                    }
                    else
                    {
                        float shade = 1f - 0.16f * Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * petals * 0.5f)), 0.5f);
                        float center = 1f - Mathf.SmoothStep(0f, centerR, r);
                        c = new Color(shade, shade * (1f - 0.08f * center), shade * (1f - 0.22f * center), 1f);
                    }
                    tex.SetPixel(cx * C + x, cy * C + y, c);
                }
            }
        }
        tex.Apply();
        return tex;
    }
}
