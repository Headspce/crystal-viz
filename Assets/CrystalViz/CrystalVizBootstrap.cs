using UnityEngine;

/// <summary>
/// Crystal Viz bootstrap: builds the entire diorama at runtime so the shipped
/// scene file stays tiny. Emulates the reference image: a glass/crystal sphere
/// cradled from below by dark tree branches, warm studio-grey backdrop, and a
/// single sun that orbits the sphere under slider control (see SunOrbitControl).
/// Attach to an empty GameObject in CrystalViz.unity; it finds MainCamera itself.
/// </summary>
public class CrystalVizBootstrap : MonoBehaviour
{
    public static readonly Vector3 SphereCenter = new Vector3(0f, 2.35f, 0f);
    public const float SphereRadius = 0.9f;

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
        cam.transform.LookAt(new Vector3(0f, 2.3f, 0f));
        cam.fieldOfView = 40f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.71f, 0.68f, 0.65f, 1f);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 100f;
    }

    // -------------------------------------------------------------- environment

    void BuildEnvironment()
    {
        // Warm studio-grey sweep: ground plane + matching linear fog.
        var bg = new Color(0.71f, 0.68f, 0.65f, 1f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = bg;
        RenderSettings.fogStartDistance = 12f;
        RenderSettings.fogEndDistance = 34f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.43f, 0.46f, 1f);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "StudioGround";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(8f, 1f, 8f); // 80x80 world units
        var gmat = NewLitMaterial();
        if (gmat != null)
        {
            gmat.color = new Color(0.74f, 0.71f, 0.68f, 1f);
            gmat.SetFloat("_Smoothness", 0f);
            ground.GetComponent<Renderer>().material = gmat;
        }

        // Faint cool fill so shadow sides of the glass don't go pitch black.
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
        cloudTex.wrapMode = TextureWrapMode.Repeat;
        var shader = Shader.Find("CrystalViz/ScrollingClouds");
        if (shader == null)
        {
            Debug.LogWarning("CrystalViz: 'CrystalViz/ScrollingClouds' shader not found; skipping cloud backdrop.");
            return;
        }

        // Massive upright plane at the horizon, facing the camera (+Z).
        // Camera sits near z=7.4 looking toward -Z, so the sky stands deep
        // at -Z. Far plane is 100; this sits comfortably inside it.
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
        // Tile the texture across the massive plane so clouds keep a natural
        // scale instead of stretching.
        if (mat.HasProperty("_Tiling"))
            mat.SetVector("_Tiling", new Vector4(8f, 2f, 0f, 0f));
        // Extremely slow drift: a fraction of the old camera-quad speed.
        if (mat.HasProperty("_ScrollSpeed"))
            mat.SetFloat("_ScrollSpeed", 0.0012f);
        var rend = quad.GetComponent<Renderer>();
        rend.material = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        Debug.Log($"CrystalViz: sky plane {skyW}x{skyH} at z={-skyDist}, upright, facing camera.");
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
        sun.transform.position = SphereCenter + dir * sunDistance;
        sun.transform.LookAt(SphereCenter);
    }

    // ------------------------------------------------------------------ diorama

    void BuildDiorama()
    {
        BuildOldTree();
        BuildGlassSphere();
    }

    void BuildGlassSphere()
    {
        // Outer glass shell.
        var glass = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glass.name = "CrystalBall";
        DestroyNow(glass.GetComponent<Collider>());
        glass.transform.position = SphereCenter;
        glass.transform.localScale = Vector3.one * SphereRadius * 2f;
        var gmat = NewLitMaterial();
        if (gmat != null)
        {
            gmat.SetFloat("_Surface", 1f); // transparent
            gmat.SetFloat("_Blend", 0f);   // alpha blend
            gmat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            gmat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            gmat.SetInt("_ZWrite", 0);
            gmat.DisableKeyword("_ALPHATEST_ON");
            gmat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            gmat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            gmat.SetColor("_BaseColor", new Color(0.75f, 0.90f, 1f, 0.15f)); // faint cyan whisper: background must dominate for a glass read
            gmat.SetFloat("_Smoothness", 0.85f);
            gmat.SetFloat("_Metallic", 0f);
            glass.GetComponent<Renderer>().material = gmat;
        }
        glass.GetComponent<Renderer>().shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off; // glass shouldn't blob-shadow

        // Bright inner core: fakes the caustic glow in the reference.
        var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.name = "CrystalCore";
        DestroyNow(core.GetComponent<Collider>());
        core.transform.position = SphereCenter + new Vector3(0f, -0.12f, 0f);
        core.transform.localScale = Vector3.one * SphereRadius * 1.05f;
        var cmat = NewLitMaterial();
        if (cmat != null)
        {
            cmat.SetFloat("_Surface", 1f);
            cmat.SetFloat("_Blend", 0f);
            cmat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            cmat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            cmat.SetInt("_ZWrite", 0);
            cmat.DisableKeyword("_ALPHATEST_ON");
            cmat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            cmat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 1;
            cmat.SetColor("_BaseColor", new Color(1f, 0.98f, 0.94f, 0.30f));
            cmat.SetFloat("_Smoothness", 1f);
            core.GetComponent<Renderer>().material = cmat;
        }
        core.GetComponent<Renderer>().shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // -------------------------------------------------------- imported old tree

    /// <summary>
    /// The dead tree: "Old tree" by evolveduk (CC-BY) from Sketchfab, imported
    /// as Assets/CrystalViz/Resources/Models/OldTree/old.fbx with its bark,
    /// stump and branch textures. Replaces the old procedural BuildBranches().
    /// The model is ~858 units tall (cm); it is scaled so the crown cradles the
    /// crystal sphere, with the base buried slightly like the old trunk was.
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
