using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Bee prototype (v1.0.30, player request): replaces the v1.0.29 butterfly.
/// Tyler clarified he wanted a bee, not a butterfly ("make it a bee please").
/// One chunky bumblebee works only the wildflowers closest to the viewer —
/// within a small radius around the tree at origin — so it is always easy
/// to see. It hops between blossoms on low direct arcs, hovers over each
/// one buzzing its translucent wings as if gathering pollen, then moves on.
/// Eye-candy only for now; the interactive layer comes after Tyler reviews
/// the look.
/// Built fully in code: striped fuzzy-look body (procedural texture), dark
/// head, tiny stinger, fast-blurring translucent wings. No assets to go
/// missing. Start() only fires in play mode; in the CI edit-mode screenshot
/// path the Bee GameObject stays empty so captures stay clean.
/// </summary>
public class BeeController : MonoBehaviour
{
    [HideInInspector] public CrystalVizBootstrap bootstrap;

    Transform bodyT;
    Transform wingL;
    Transform wingR;

    enum State { ToFlower, Hover, Feeding, Takeoff }
    State state = State.ToFlower;

    Vector3 fromPos;
    Vector3 targetFlower;
    Vector3 ctrlPos; // bezier control point for the flight arc
    float flightT;
    float flightDur;
    float stateTimer;
    float flapPhase;
    Camera mainCam; // phone-screen bounds come from the main camera's viewport

    System.Random rng = new System.Random(20260922);
    List<Vector3> nearHeads = new List<Vector3>();

    // Tuning (player-tweakable):
    const float VisitRadius = 6.0f;  // only flowers within this of the tree (origin)
    const float CruiseHeight = 0.35f; // bees fly low and direct, not lofty arcs
    const float FlightSpeed = 2.2f;   // world units per second
    const float FeedTime = 2.8f;      // seconds hovering over a blossom
    const float FlapFlight = 44f;     // wing blur rad/s in flight
    const float FlapFeed = 30f;       // hovering buzz while feeding
    const float BodyLen = 0.095f;     // chunky and visible, per Tyler
    // Viewport margins: the bee never leaves this rect on the phone screen.
    const float MarginX = 0.06f;
    const float MarginYBottom = 0.07f;
    const float MarginYTop = 0.06f;

    void Start()
    {
        if (bootstrap == null) bootstrap = FindObjectOfType<CrystalVizBootstrap>();
        mainCam = Camera.main;
        BuildBee();
        CollectNearFlowers();
        PickNextFlower(initial: true);
    }

    /// <summary>
    /// Only blossoms that are BOTH within VisitRadius of the tree AND on the
    /// phone screen (inside the camera viewport with a margin) — the bee
    /// picks its flowers from this list, so targets are always visible.
    /// </summary>
    void CollectNearFlowers()
    {
        nearHeads.Clear();
        List<Vector3> heads = bootstrap != null ? bootstrap.flowerHeads : null;
        if (heads == null) return;
        foreach (var h in heads)
        {
            Vector2 flat = new Vector2(h.x, h.z);
            if (flat.magnitude > VisitRadius) continue;
            if (mainCam != null)
            {
                Vector3 vp = mainCam.WorldToViewportPoint(h);
                if (vp.z <= 0f) continue; // behind the camera
                if (vp.x < MarginX || vp.x > 1f - MarginX) continue;
                if (vp.y < MarginYBottom || vp.y > 1f - MarginYTop) continue;
            }
            nearHeads.Add(h);
        }
    }

    /// <summary>
    /// Safety net: pin the bee inside the camera viewport every frame so even
    /// mid-flight bezier arcs and the takeoff zip can never leave the phone
    /// screen. Tyler's request (v1.0.31): the bee was only visible
    /// occasionally because flight paths wandered out of frame.
    /// </summary>
    void ClampToScreen()
    {
        if (mainCam == null || bodyT == null) return;
        Vector3 vp = mainCam.WorldToViewportPoint(bodyT.position);
        if (vp.z <= 0f) return; // behind the camera; leave it alone
        float cx = Mathf.Clamp(vp.x, MarginX, 1f - MarginX);
        float cy = Mathf.Clamp(vp.y, MarginYBottom, 1f - MarginYTop);
        if (!Mathf.Approximately(cx, vp.x) || !Mathf.Approximately(cy, vp.y))
            bodyT.position = mainCam.ViewportToWorldPoint(new Vector3(cx, cy, vp.z));
    }

    void BuildBee()
    {
        var root = new GameObject("Bee");
        root.transform.SetParent(transform, false);
        bodyT = root.transform;

        // Body: striped sphere, poles rotated onto the long (Z) axis so the
        // texture bands ring the body like a real bumblebee's stripes.
        var bodyGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(bodyGo.GetComponent<Collider>());
        bodyGo.transform.SetParent(bodyT, false);
        bodyGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        bodyGo.transform.localScale = new Vector3(BodyLen * 0.55f, BodyLen, BodyLen * 0.55f);
        var bodyMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        bodyMat.SetTexture("_BaseMap", MakeStripeTexture());
        bodyMat.SetFloat("_Smoothness", 0.15f); // fuzzy, not shiny
        bodyGo.GetComponent<MeshRenderer>().material = bodyMat;

        // Head: small dark sphere at the front.
        var headGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(headGo.GetComponent<Collider>());
        headGo.transform.SetParent(bodyT, false);
        headGo.transform.localPosition = new Vector3(0f, 0.004f, BodyLen * 0.52f);
        headGo.transform.localScale = new Vector3(0.028f, 0.026f, 0.026f);
        var darkMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        darkMat.color = new Color(0.09f, 0.07f, 0.05f);
        darkMat.SetFloat("_Smoothness", 0.3f);
        headGo.GetComponent<MeshRenderer>().material = darkMat;

        // Stinger: tiny dark cone at the back.
        var stingGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(stingGo.GetComponent<Collider>());
        stingGo.transform.SetParent(bodyT, false);
        stingGo.transform.localPosition = new Vector3(0f, -0.002f, -BodyLen * 0.55f);
        stingGo.transform.localScale = new Vector3(0.010f, 0.010f, 0.022f);
        stingGo.GetComponent<MeshRenderer>().material = darkMat;

        // Wings: translucent quads hinged at the top of the thorax; the fast
        // flap reads as a blur, like a real bee.
        var wingMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        wingMat.color = new Color(0.92f, 0.96f, 1f, 0.38f);
        wingMat.SetFloat("_Surface", 1f); // transparent
        wingMat.SetFloat("_Cull", 0f);    // visible from below too
        wingMat.SetFloat("_Smoothness", 0.6f);
        wingL = MakeWing("WingL", -1f, wingMat);
        wingR = MakeWing("WingR", 1f, wingMat);
    }

    Transform MakeWing(string name, float side, Material wingMat)
    {
        var pivot = new GameObject(name).transform;
        pivot.SetParent(bodyT, false);
        pivot.localPosition = new Vector3(side * 0.012f, 0.020f, 0.008f);

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(pivot, false);
        quad.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // flat, facing up
        float span = 0.055f;
        quad.transform.localPosition = new Vector3(side * span * 0.45f, 0f, -0.008f);
        quad.transform.localScale = new Vector3(side * span, span * 0.8f, 1f);
        quad.GetComponent<MeshRenderer>().material = wingMat;
        return pivot;
    }

    /// <summary>
    /// Bumblebee stripes: golden-yellow with black bands ringing the body.
    /// Bands vary along the texture's vertical axis, which maps pole to pole
    /// on the sphere — and the poles were rotated onto the body axis.
    /// </summary>
    static Texture2D MakeStripeTexture()
    {
        int w = 64, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var gold = new Color(0.98f, 0.72f, 0.12f);
        var black = new Color(0.08f, 0.06f, 0.04f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = y / (float)h;
                // Three soft black bands across the golden body.
                float band = Mathf.Abs(Mathf.Sin(v * Mathf.PI * 3.5f));
                Color c = band < 0.42f ? black : gold;
                // Slight noise so it reads fuzzy, not plastic.
                float n = Mathf.Sin(x * 12.9f + y * 7.7f) * 0.5f + 0.5f;
                c = Color.Lerp(c, c * 0.85f, n * 0.25f);
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }

    void PickNextFlower(bool initial = false)
    {
        if (nearHeads.Count == 0)
        {
            // No near flowers (unexpected): hover in place by the tree.
            targetFlower = new Vector3(1.5f, 0.35f, 1.5f);
        }
        else
        {
            targetFlower = nearHeads[rng.Next(nearHeads.Count)];
        }
        fromPos = bodyT.position;
        if (initial) fromPos = targetFlower + new Vector3(1.0f, CruiseHeight + 0.3f, 0.7f);
        // Low direct arc: lift off, cruise, drop onto the blossom.
        ctrlPos = (fromPos + targetFlower) * 0.5f + Vector3.up * CruiseHeight;
        float dist = Vector3.Distance(fromPos, targetFlower);
        flightDur = Mathf.Max(0.6f, dist / FlightSpeed);
        flightT = 0f;
        state = State.ToFlower;
    }

    void Update()
    {
        if (bodyT == null) return;
        float dt = Time.deltaTime;

        switch (state)
        {
            case State.ToFlower:
            {
                flightT += dt / flightDur;
                float t = Mathf.Min(1f, flightT);
                // Quadratic bezier: fromPos -> ctrlPos -> targetFlower.
                Vector3 a = Vector3.Lerp(fromPos, ctrlPos, t);
                Vector3 b = Vector3.Lerp(ctrlPos, targetFlower, t);
                Vector3 pos = Vector3.Lerp(a, b, t);
                // Face travel direction with a slight eager wobble.
                Vector3 vel = (b - a).normalized;
                if (vel.sqrMagnitude > 0.001f)
                {
                    Quaternion look = Quaternion.LookRotation(vel, Vector3.up);
                    bodyT.rotation = Quaternion.Slerp(bodyT.rotation, look, 1f - Mathf.Exp(-7f * dt));
                }
                bodyT.position = pos + Vector3.up * Mathf.Sin(Time.time * 11f) * 0.010f;
                Flap(dt, FlapFlight);
                if (t >= 1f) { state = State.Hover; stateTimer = 0.5f; }
                break;
            }
            case State.Hover:
                // Lock onto the blossom: ease into hover position.
                stateTimer -= dt;
                bodyT.position = Vector3.Lerp(bodyT.position, targetFlower + Vector3.up * 0.045f,
                    1f - Mathf.Exp(-9f * dt));
                Flap(dt, FlapFeed);
                if (stateTimer <= 0f) { state = State.Feeding; stateTimer = FeedTime * (0.8f + (float)rng.NextDouble() * 0.5f); }
                break;
            case State.Feeding:
                // Hover-feeding: bobbing over the blossom, dipping toward it,
                // wings a constant buzz, tiny sideways shuffles.
                stateTimer -= dt;
                Flap(dt, FlapFeed);
                float dip = 0.045f + Mathf.Sin(Time.time * 3.3f) * 0.012f;
                bodyT.position = targetFlower + Vector3.up * dip
                    + new Vector3(Mathf.Sin(Time.time * 2.4f) * 0.008f, 0f, Mathf.Cos(Time.time * 1.9f) * 0.008f);
                bodyT.rotation = Quaternion.Slerp(bodyT.rotation,
                    Quaternion.Euler(24f, bodyT.rotation.eulerAngles.y, 0f),
                    1f - Mathf.Exp(-5f * dt));
                if (stateTimer <= 0f) { state = State.Takeoff; stateTimer = 0.35f; }
                break;
            case State.Takeoff:
                // Zip upward before choosing the next near flower.
                stateTimer -= dt;
                bodyT.position += Vector3.up * dt * 1.1f;
                Flap(dt, FlapFlight);
                if (stateTimer <= 0f) PickNextFlower();
                break;
        }

        // Tyler (v1.0.31): the bee must never leave the phone screen.
        ClampToScreen();
    }

    /// <summary>
    /// Beats both wings by rotating the hinge pivots about Z. Fast enough to
    /// read as a blur rather than individual flaps.
    /// </summary>
    void Flap(float dt, float speed)
    {
        flapPhase += dt * speed;
        float wingAngle = 12f + Mathf.Sin(flapPhase) * 55f;
        wingL.localRotation = Quaternion.Euler(0f, 0f, wingAngle);
        wingR.localRotation = Quaternion.Euler(0f, 0f, -wingAngle);
    }
}
