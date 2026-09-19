using UnityEngine;

/// <summary>
/// Crystal Viz bootstrap: builds the entire diorama at runtime so the shipped
/// scene file stays tiny. The scene is a dead tree on a stylized grass field
/// under a drifting cloud sky, with a single sun that orbits the tree under
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
        BuildCloudBackdrop();
        BuildHorizonHaze();
        BuildSun();
        BuildDiorama();
        var ctrl = gameObject.AddComponent<SunOrbitControl>();
        ctrl.bootstrap = this;
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
        cam.clearFlags = CameraClearFlags.SolidColor;
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
        RenderSettings.fogStartDistance = 45f;
        RenderSettings.fogEndDistance = 100f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.43f, 0.46f, 1f);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "GrassGround";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(20f, 1f, 20f); // 200x200 world units, past the sky plane
        var gmat = NewLitMaterial();
        if (gmat != null)
        {
            gmat.color = new Color(0.10f, 0.23f, 0.08f, 1f); // dark shadowed moss: gaps read as depth under the grass, not neon
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

    // ------------------------------------------------------- cloud backdrop

    /// <summary>
    /// Sky backdrop: ONE single massive upright plane standing at the horizon
    /// behind the diorama (world-space, not camera-parented), textured with
    /// Assets/CrystalViz/Resources/clouds.jpg drifting left to right at an
    /// extremely slow crawl via the CrystalViz/ScrollingClouds shader. The
    /// plane faces the camera upright, as if looking toward the horizon. The
    /// shader has no fog code, so scene fog never washes it out. Missing
    /// texture or shader => quietly skipped, never a crash.
    /// </summary>
    void BuildCloudBackdrop()
    {
        var cloudTex = Resources.Load<Texture2D>("clouds");
        if (cloudTex == null)
        {
            Debug.Log("CrystalViz: no 'clouds' texture in Resources; skipping cloud backdrop.");
            return;
        }
        // One single tile stretched across the whole plane: one cloud type
        // everywhere, no tiling seams. Mirrored wrap keeps the ultra-slow
        // scroll from ever revealing a texture edge. The texture's mountain
        // strip lands at the bottom of the plane, i.e. at the horizon.
        cloudTex.wrapMode = TextureWrapMode.Mirror;
        var shader = Shader.Find("CrystalViz/ScrollingClouds");
        if (shader == null)
        {
            Debug.LogWarning("CrystalViz: 'CrystalViz/ScrollingClouds' shader not found; skipping cloud backdrop.");
            return;
        }

        // Massive upright plane at the horizon, facing the camera (+Z).
        // Camera sits near z=7.4 looking toward -Z, so the sky stands deep
        // at -Z. Far plane is 250; this sits comfortably inside it.
        const float skyDist = 60f;
        const float skyW = 220f;
        const float skyH = 60f;
        const float skyCenterY = 12f;

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "SkyPlane";
        DestroyNow(quad.GetComponent<Collider>());
        // Unity quads face +Z by default: upright, facing the camera. No
        // rotation needed.
        quad.transform.position = new Vector3(0f, skyCenterY, -skyDist);
        quad.transform.localScale = new Vector3(skyW, skyH, 1f);
        var mat = new Material(shader);
        mat.mainTexture = cloudTex;
        // One tile fills the entire sky: a single cloud image, enlarged,
        // instead of a tiled grid.
        if (mat.HasProperty("_Tiling"))
            mat.SetVector("_Tiling", new Vector4(1f, 1f, 0f, 0f));
        // Extremely slow drift: a fraction of the old camera-quad speed.
        if (mat.HasProperty("_ScrollSpeed"))
            mat.SetFloat("_ScrollSpeed", 0.0012f);
        var rend = quad.GetComponent<Renderer>();
        rend.material = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        Debug.Log($"CrystalViz: sky plane {skyW}x{skyH} at z={-skyDist}, upright, facing camera.");
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
    }

    // ------------------------------------------------------------------ diorama

    void BuildDiorama()
    {
        BuildOldTree();
        BuildGrassField();
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
            var mtx = Matrix4x4.TRS(
                new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r),
                Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                Vector3.one * (0.7f + (float)rng.NextDouble() * 0.8f));
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

    // -------------------------------------------------------- imported old tree

    /// <summary>
    /// The dead tree: "Old tree" by evolveduk (CC-BY) from Sketchfab, imported
    /// as Assets/CrystalViz/Resources/Models/OldTree/old.fbx with its bark,
    /// stump and branch textures. Replaces the old procedural BuildBranches().
    /// The model is ~858 units tall (cm); it is scaled so the crown rises above
    /// the grass field, with the base buried slightly like the old trunk was.
    /// See THIRD-PARTY-NOTICES.md for the required attribution.
    /// </summary>
    void BuildOldTree()
    {
        var treePrefab = Resources.Load<GameObject>("Models/OldTree/old");
        if (treePrefab == null)
        {
            Debug.LogWarning("CrystalViz: 'Models/OldTree/old' not found in Resources; skipping tree.");
            return;
        }
        var tree = Instantiate(treePrefab);
        tree.name = "OldTree";

        // Measure the model in its own units, then scale so the full tree
        // (base to crown) stands targetHeight world units tall.
        var bounds = new Bounds();
        bool any = false;
        foreach (var mf in tree.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            if (!any) { bounds = mf.sharedMesh.bounds; any = true; }
            else bounds.Encapsulate(mf.sharedMesh.bounds);
        }
        const float targetHeight = 2.5f;
        const float buryDepth = 0.35f;
        float s = any && bounds.size.y > 0f ? targetHeight / bounds.size.y : 0.003f;
        tree.transform.localScale = Vector3.one * s;
        // Drop the tree so its lowest point sits buryDepth below the ground.
        tree.transform.position = new Vector3(0f, -buryDepth - bounds.min.y * s, 0f);

        // The branch cards (branch06.png) are alpha-mapped twigs: enable alpha
        // cutout so they don't render as opaque quads. _ALPHATEST_ON is
        // already pinned in the build's ShaderVariantCollection.
        foreach (var rend in tree.GetComponentsInChildren<Renderer>())
        {
            foreach (var mat in rend.materials)
            {
                var tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : mat.mainTexture;
                if (tex != null && tex.name.ToLowerInvariant().Contains("branch"))
                {
                    mat.SetFloat("_AlphaClip", 1f);
                    mat.SetFloat("_Cutoff", 0.5f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                }
            }
        }
        Debug.Log($"CrystalViz: old tree placed at scale {s:F5}.");
    }
}
