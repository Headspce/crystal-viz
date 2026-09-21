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
        var gmat = NewLitMaterial();
        if (gmat != null)
        {
            gmat.color = new Color(0.24f, 0.45f, 0.17f, 1f); // dark shadowed moss: gaps read as depth under the grass, not neon
            gmat.SetFloat("_Smoothness", 0f);
            ground.GetComponent<Renderer>().material = gmat;
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
    /// plane) wearing the fully procedural CrystalViz/AnimeSkybox shader —
    /// painted blue gradient, cel-shaded cumulus, thin cirrus wisps drifting
    /// ultra-slowly, horizon color matched to the fog for a seamless blend.
    /// Pure math => no texture seam anywhere, no matter how long you stare
    /// at it. A mesh dome instead of RenderSettings.skybox: the CI screenshot
    /// renders the camera directly in edit mode, where the skybox pass does
    /// not draw (verified: sky rendered as flat clear color), while plain
    /// meshes render reliably on that path.
    /// </summary>
    void BuildSkyDome()
    {
        // Fully procedural anime sky dome (CrystalViz/AnimeSkybox shader).
        // The textured path was retired in v1.0.21: the Sketchfab panorama's
        // publicly served texture carries diagonal stripe artifacts baked
        // into Sketchfab's viewer pipeline (anti-theft degradation; the clean
        // original is only available via authenticated download), which is
        // what produced the striped sky in v1.0.20. The procedural dome is
        // the v1.0.18-approved look: painted gradient, cel-shaded cumulus,
        // cirrus wisps, fog-matched horizon.
        Debug.Log("CrystalViz: procedural anime sky dome installed (r=200).");
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
        // Wind bend is in world units: scaled to the tiny blades so the sway
        // reads as a shimmer, not a thrash.
        mat.SetFloat("_WindStrength", 0.02f);
        mat.SetFloat("_WindSpeed", 1.7f);
        // Gust fronts: explicit here (not just shader defaults) so grass and
        // wildflowers provably share the same wave bands. Fewer, slower
        // gusts (player request 2026-09-21).
        mat.SetFloat("_GustStrength", 0.38f);
        mat.SetFloat("_GustSpeed", 1.8f);
        mat.SetFloat("_GustFreq", 0.06f);
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
    /// Blades are tiny (75% smaller than v1.0.7) and densely packed:
    /// 5 blades x 2 segments.
    /// </summary>
    static void AppendGrassTuft(
        System.Collections.Generic.List<Vector3> verts,
        System.Collections.Generic.List<Vector3> normals,
        System.Collections.Generic.List<Vector2> uvs,
        System.Collections.Generic.List<int> tris,
        Matrix4x4 mtx,
        System.Random rng)
    {
        const int blades = 5;
        const int segs = 1; // single-segment blades: at 0.07-0.125 tall the
                            // silhouette is identical to 2-seg, at 2/3 the verts
        for (int b = 0; b < blades; b++)
        {
            float ang = (b / (float)blades) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.9f;
            float tilt = 0.25f + (float)rng.NextDouble() * 0.35f;   // outward lean
            float height = 0.07f + (float)rng.NextDouble() * 0.055f;
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
        // Textured blossom: a daisy-like petal head on a transparent
        // background, alpha-cut in the shader; near-white so the per-flower
        // petal vertex color defines the hue.
        mat.SetTexture("_BlossomMap", MakeBlossomTexture());
        // Same wind field as the grass so they ripple together; a touch
        // stronger since blossoms sit higher and catch more air. Gust bands
        // are shared with the grass (same direction/speed/frequency) so the
        // whole meadow moves as one wave front.
        mat.SetFloat("_WindStrength", 0.035f);
        mat.SetFloat("_WindSpeed", 1.7f);
        mat.SetFloat("_GustStrength", 0.30f);
        mat.SetFloat("_GustSpeed", 1.8f);
        mat.SetFloat("_GustFreq", 0.06f);
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
            AppendWildflower(verts, normals, uvs, uvs2, colors, tris, mtx, rng, petal);
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
    /// Flowers stand 0.16-0.32 tall: above the grass blades so they pop.
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
        Color petal)
    {
        float h = 0.16f + (float)rng.NextDouble() * 0.16f;
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
        float bw = 0.035f + (float)rng.NextDouble() * 0.025f;
        float bh = bw * 1.2f;
        Vector3 c = stemTop + new Vector3(0f, bh * 0.28f, 0f);
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
            // Blossom-head texture coords (uv1): full 0..1 quad.
            uvs2.Add(new Vector2(0f, 0f)); uvs2.Add(new Vector2(1f, 0f));
            uvs2.Add(new Vector2(0f, 1f)); uvs2.Add(new Vector2(1f, 1f));
            for (int i = 0; i < 4; i++) colors.Add(petal);
            tris.Add(qb); tris.Add(qb + 2); tris.Add(qb + 1);
            tris.Add(qb + 1); tris.Add(qb + 2); tris.Add(qb + 3);
        }
    }

    /// <summary>
    /// Builds a small blossom-head albedo: a daisy-like flower with 8 rounded
    /// petals and a warm center, near-white so the per-flower petal vertex
    /// color defines the hue. Transparent background for alpha-test cutout.
    /// </summary>
    static Texture2D MakeBlossomTexture()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = ((float)x / (S - 1)) * 2f - 1f; // -1..1
                float v = ((float)y / (S - 1)) * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float ang = Mathf.Atan2(v, u);
                // 8 rounded petals: radius modulated by angle.
                float petal = 0.78f + 0.22f * Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 4f)), 0.7f);
                if (r >= petal)
                {
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                    continue;
                }
                float groove = Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * 4f)), 0.5f);
                float shade = 1f - 0.16f * groove;
                float center = 1f - Mathf.SmoothStep(0f, 0.30f, r);
                tex.SetPixel(x, y, new Color(shade, shade * (1f - 0.08f * center), shade * (1f - 0.22f * center), 1f));
            }
        }
        tex.Apply();
        return tex;
    }
}
