using System.Collections.Generic;
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
    public static readonly Vector3 SphereCenter = new Vector3(0f, 2.6f, 0f);
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
    /// Slowly scrolling cloud backdrop: a large quad parented to the camera
    /// (so it always fills the frame) textured with Assets/CrystalViz/Resources/
    /// clouds.png drifting left to right via the CrystalViz/ScrollingClouds
    /// shader. The shader has no fog code, so scene fog never washes it out.
    /// Missing texture or shader => quietly skipped, never a crash.
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

        var cam = Camera.main;
        if (cam == null) return;
        const float dist = 30f; // well inside the far plane, shader ignores fog
        float h = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * cam.aspect;

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "CloudBackdrop";
        DestroyNow(quad.GetComponent<Collider>());
        quad.transform.SetParent(cam.transform, false);
        quad.transform.localPosition = new Vector3(0f, 0f, -dist);
        quad.transform.localRotation = Quaternion.identity; // Quad faces +Z => toward camera
        quad.transform.localScale = new Vector3(w * 1.1f, h * 1.1f, 1f);
        var mat = new Material(shader);
        mat.mainTexture = cloudTex;
        var rend = quad.GetComponent<Renderer>();
        rend.material = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
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
        BuildBranches();
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
            gmat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.20f));
            gmat.SetFloat("_Smoothness", 1f);
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

    // -------------------------------------------------------- procedural branches

    static Vector3 QuadBezier(Vector3 p0, Vector3 c, Vector3 p1, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * c + t * t * p1;
    }

    static Vector3 QuadBezierTangent(Vector3 p0, Vector3 c, Vector3 p1, float t)
    {
        return 2f * (1f - t) * (c - p0) + 2f * t * (p1 - c);
    }

    static void AddTube(List<Vector3> verts, List<Vector3> norms, List<int> tris,
        Vector3 p0, Vector3 c, Vector3 p1,
        float r0, float r1, int lengthSegs, int radialSegs)
    {
        int baseIndex = verts.Count;
        int rings = lengthSegs + 1;
        Vector3 upRef = Vector3.up;
        for (int i = 0; i < rings; i++)
        {
            float t = (float)i / lengthSegs;
            Vector3 pos = QuadBezier(p0, c, p1, t);
            Vector3 tangent = QuadBezierTangent(p0, c, p1, t).normalized;
            if (Mathf.Abs(Vector3.Dot(tangent, upRef)) > 0.9f)
                upRef = Mathf.Abs(tangent.y) > 0.9f ? Vector3.right : Vector3.up;
            Vector3 b = Vector3.Cross(tangent, upRef).normalized;
            Vector3 n = Vector3.Cross(b, tangent).normalized;
            float r = Mathf.Lerp(r0, r1, t);
            for (int j = 0; j < radialSegs; j++)
            {
                float a = (j / (float)radialSegs) * Mathf.PI * 2f;
                Vector3 ringDir = n * Mathf.Cos(a) + b * Mathf.Sin(a);
                verts.Add(pos + ringDir * r);
                norms.Add(ringDir);
            }
        }
        for (int i = 0; i < lengthSegs; i++)
        {
            for (int j = 0; j < radialSegs; j++)
            {
                int a0 = baseIndex + i * radialSegs + j;
                int a1 = baseIndex + i * radialSegs + (j + 1) % radialSegs;
                int b0 = baseIndex + (i + 1) * radialSegs + j;
                int b1 = baseIndex + (i + 1) * radialSegs + (j + 1) % radialSegs;
                tris.Add(a0); tris.Add(b0); tris.Add(a1);
                tris.Add(a1); tris.Add(b0); tris.Add(b1);
            }
        }
    }

    void BuildBranches()
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris = new List<int>();

        // Trunk: rises from below the ground, slight organic lean.
        AddTube(verts, norms, tris,
            new Vector3(0f, -0.4f, 0f), new Vector3(0.18f, 0.7f, 0.06f), new Vector3(0f, 1.55f, 0f),
            0.30f, 0.15f, 10, 8);

        // Six "fingers" arc up and inward, tips landing on the sphere's lower
        // hemisphere like hands cradling the ball in the reference image.
        int fingers = 6;
        for (int i = 0; i < fingers; i++)
        {
            float ang = (i / (float)fingers) * Mathf.PI * 2f + Random.Range(-0.15f, 0.15f);
            Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            Vector3 fp0 = dir * 0.10f + new Vector3(0f, 1.50f, 0f);
            Vector3 fp1 = SphereCenter + dir * 0.60f + new Vector3(0f, -0.66f, 0f);
            Vector3 fc = (fp0 + fp1) * 0.5f + dir * 0.38f + new Vector3(0f, -0.12f, 0f);
            AddTube(verts, norms, tris, fp0, fc, fp1, 0.11f, 0.035f, 12, 7);

            // A couple of small twigs per finger for silhouette interest.
            for (int k = 0; k < 2; k++)
            {
                float t = 0.35f + 0.30f * k + Random.Range(-0.05f, 0.05f);
                Vector3 tb = QuadBezier(fp0, fc, fp1, t);
                Vector3 td = (dir * Random.Range(0.6f, 1f) + Vector3.up * Random.Range(0.8f, 1.4f)).normalized;
                Vector3 tp1 = tb + td * Random.Range(0.30f, 0.50f);
                Vector3 tc = tb + td * 0.20f + dir * 0.10f;
                AddTube(verts, norms, tris, tb, tc, tp1, 0.035f, 0.012f, 6, 5);
            }
        }

        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        var go = new GameObject("Branches");
        go.AddComponent<MeshFilter>().mesh = mesh;
        var bark = NewLitMaterial();
        if (bark != null)
        {
            bark.color = new Color(0.16f, 0.19f, 0.11f, 1f); // dark mossy green-brown
            bark.SetFloat("_Smoothness", 0.15f);
            go.AddComponent<MeshRenderer>().material = bark;
        }
        else
        {
            go.AddComponent<MeshRenderer>(); // default material; error already logged
        }
    }
}
