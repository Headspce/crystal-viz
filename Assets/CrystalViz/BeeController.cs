using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Bee squad (v1.0.32, player request): THREE bumblebees, each flying its own
/// independent path between the on-screen wildflowers near the tree.
/// Interaction: swipe across a bee — or just tap it — and it POPS: a
/// cartoonish, non-violent rainbow confetti burst with a soft poof ring
/// (Tyler: "have fun with the design of the pop, I'll fix it later").
/// The bee respawns a couple of seconds later and goes back to work.
/// v1.0.33 fix: the pop FX used URP/Unlit via Shader.Find, which Unity
/// strips from the player build (nothing serialised references it), so the
/// pop threw on the phone and bees never popped. The FX now uses URP/Lit
/// (guaranteed in the build, same transparent recipe as the wings), the
/// shader is resolved once in Start, and a missing shader skips the FX
/// instead of throwing — the bee always pops.
/// Every bee stays pinned inside the phone screen (v1.0.31 viewport clamp),
/// and only visits flowers that are actually visible.
/// Built fully in code: striped fuzzy-look bodies, dark heads, tiny
/// stingers, fast-blurring translucent wings, procedural pop particles.
/// No assets to go missing. Start() only fires in play mode; in the CI
/// edit-mode screenshot path the Bee GameObject stays empty so captures
/// stay clean.
/// </summary>
public class BeeController : MonoBehaviour
{
    [HideInInspector] public CrystalVizBootstrap bootstrap;

    Camera mainCam;
    List<Vector3> nearHeads = new List<Vector3>();
    List<BeeAgent> bees = new List<BeeAgent>();
    List<PopFX> pops = new List<PopFX>();

    // Swipe tracking (screen pixels) for the pop interaction.
    List<Vector2> swipePts = new List<Vector2>();
    bool swiping;
    bool swipeBlockedByUI;

    enum State { Waiting, ToFlower, Hover, Feeding, Takeoff, Popped }

    class BeeAgent
    {
        public GameObject root;
        public Transform bodyT;
        public Transform wingL;
        public Transform wingR;
        public State state;
        public Vector3 fromPos;
        public Vector3 targetFlower;
        public Vector3 ctrlPos;
        public float flightT;
        public float flightDur;
        public float stateTimer;
        public float flapPhase;
        public float spawnT = 1f; // <1 while springing back in after respawn
        public System.Random rng;
        public float speed;
        public float cruiseH;
        public bool alive;
    }

    // One pop particle: a confetti petal with its own motion.
    class Petal
    {
        public Transform t;
        public Vector3 vel;
        public Vector3 spin;
        public float life;
        public float age;
        public Material mat;
        public float size;
    }

    // An active pop burst: confetti petals + expanding ring + soft poof.
    class PopFX
    {
        public GameObject root;
        public List<Petal> petals = new List<Petal>();
        public Transform ringT;
        public Material ringMat;
        public float ringAge;
        public Transform poofT;
        public Material poofMat;
        public float poofAge;
        public float age;
    }

    // Tuning (player-tweakable):
    const int BeeCount = 3;       // the squad: three bees, independent paths
    const float VisitRadius = 6.0f;  // only flowers within this of the tree (origin)
    const float BaseCruiseHeight = 0.35f; // bees fly low and direct, not lofty arcs
    const float BaseFlightSpeed = 2.2f;   // world units per second
    const float FeedTime = 2.8f;      // seconds hovering over a blossom
    const float FlapFlight = 44f;     // wing blur rad/s in flight
    const float FlapFeed = 30f;       // hovering buzz while feeding
    const float BodyLen = 0.095f;     // chunky and visible, per Tyler
    const float RespawnDelay = 2.4f;  // seconds before a popped bee returns
    // Viewport margins: no bee ever leaves this rect on the phone screen.
    const float MarginX = 0.06f;
    const float MarginYBottom = 0.07f;
    const float MarginYTop = 0.06f;

    static Texture2D ringTex; // shared soft-ring sprite for pop FX

    // Resolved once in Start: the ONLY shader the pop FX may use. URP/Lit is
    // guaranteed in the player build (pinned in the variant collection and
    // referenced by scene materials). Never Shader.Find a shader at pop time:
    // v1.0.32 used URP/Unlit, which Unity strips from the build because
    // nothing serialised references it — Shader.Find returned null on the
    // phone, new Material(null) threw, and the bee never popped. Silent on
    // desktop, broken on device.
    Shader popShader;

    void Start()
    {
        if (bootstrap == null) bootstrap = FindObjectOfType<CrystalVizBootstrap>();
        mainCam = Camera.main;
        popShader = Shader.Find("Universal Render Pipeline/Lit");
        if (popShader == null)
            Debug.LogError("BeeController: URP/Lit not found; pop FX will be skipped.");
        CollectNearFlowers();
        for (int i = 0; i < BeeCount; i++)
        {
            var bee = new BeeAgent
            {
                rng = new System.Random(20260922 + i * 101),
                speed = BaseFlightSpeed + (i - 1) * 0.25f,
                cruiseH = BaseCruiseHeight + (i - 1) * 0.04f,
                state = State.Waiting,
                stateTimer = i * 0.9f, // staggered entrances
                alive = false,
            };
            BuildBeeAgent(bee, i);
            bee.root.SetActive(false);
            bees.Add(bee);
        }
    }

    /// <summary>
    /// Only blossoms that are BOTH within VisitRadius of the tree AND on the
    /// phone screen (inside the camera viewport with a margin) — bees pick
    /// their flowers from this list, so targets are always visible.
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

    void BuildBeeAgent(BeeAgent bee, int index)
    {
        var root = new GameObject("Bee" + (index + 1));
        root.transform.SetParent(transform, false);
        bee.root = root;
        bee.bodyT = root.transform;

        // Body: striped sphere, poles rotated onto the long (Z) axis so the
        // texture bands ring the body like a real bumblebee's stripes.
        var bodyGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(bodyGo.GetComponent<Collider>());
        bodyGo.transform.SetParent(bee.bodyT, false);
        bodyGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        bodyGo.transform.localScale = new Vector3(BodyLen * 0.55f, BodyLen, BodyLen * 0.55f);
        var bodyMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        bodyMat.SetTexture("_BaseMap", MakeStripeTexture());
        bodyMat.SetFloat("_Smoothness", 0.15f); // fuzzy, not shiny
        bodyGo.GetComponent<MeshRenderer>().material = bodyMat;

        // Head: small dark sphere at the front.
        var headGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(headGo.GetComponent<Collider>());
        headGo.transform.SetParent(bee.bodyT, false);
        headGo.transform.localPosition = new Vector3(0f, 0.004f, BodyLen * 0.52f);
        headGo.transform.localScale = new Vector3(0.028f, 0.026f, 0.026f);
        var darkMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        darkMat.color = new Color(0.09f, 0.07f, 0.05f);
        darkMat.SetFloat("_Smoothness", 0.3f);
        headGo.GetComponent<MeshRenderer>().material = darkMat;

        // Stinger: tiny dark cone at the back.
        var stingGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(stingGo.GetComponent<Collider>());
        stingGo.transform.SetParent(bee.bodyT, false);
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
        bee.wingL = MakeWing("WingL", -1f, wingMat, bee.bodyT);
        bee.wingR = MakeWing("WingR", 1f, wingMat, bee.bodyT);
    }

    Transform MakeWing(string name, float side, Material wingMat, Transform parent)
    {
        var pivot = new GameObject(name).transform;
        pivot.SetParent(parent, false);
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

    void PickNextFlower(BeeAgent bee, bool initial = false)
    {
        Vector3 choice;
        if (nearHeads.Count == 0)
        {
            // No near flowers (unexpected): hover in place by the tree.
            choice = new Vector3(1.5f, 0.35f, 1.5f);
        }
        else
        {
            // Prefer a blossom no other bee is currently working, so the
            // three flight paths stay independent.
            var taken = new HashSet<Vector3>();
            foreach (var o in bees)
                if (o != bee && o.alive && o.state != State.Waiting && o.state != State.Popped)
                    taken.Add(o.targetFlower);
            Vector3 fallback = nearHeads[bee.rng.Next(nearHeads.Count)];
            choice = fallback;
            for (int tries = 0; tries < 8; tries++)
            {
                Vector3 cand = nearHeads[bee.rng.Next(nearHeads.Count)];
                if (!taken.Contains(cand)) { choice = cand; break; }
            }
        }
        bee.targetFlower = choice;
        bee.fromPos = bee.bodyT.position;
        if (initial) bee.fromPos = choice + new Vector3(1.0f, bee.cruiseH + 0.3f, 0.7f);
        // Low direct arc: lift off, cruise, drop onto the blossom.
        bee.ctrlPos = (bee.fromPos + bee.targetFlower) * 0.5f + Vector3.up * bee.cruiseH;
        float dist = Vector3.Distance(bee.fromPos, bee.targetFlower);
        bee.flightDur = Mathf.Max(0.6f, dist / bee.speed);
        bee.flightT = 0f;
        bee.state = State.ToFlower;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        foreach (var bee in bees)
            UpdateBee(bee, dt);
        UpdateSwipe();
        UpdatePops(dt);
    }

    void UpdateBee(BeeAgent bee, float dt)
    {
        switch (bee.state)
        {
            case State.Waiting:
                // Staggered entrance at scene start.
                bee.stateTimer -= dt;
                if (bee.stateTimer <= 0f)
                {
                    bee.root.SetActive(true);
                    bee.alive = true;
                    bee.spawnT = 0f;
                    PickNextFlower(bee, initial: true);
                }
                break;
            case State.ToFlower:
            {
                bee.flightT += dt / bee.flightDur;
                float t = Mathf.Min(1f, bee.flightT);
                // Quadratic bezier: fromPos -> ctrlPos -> targetFlower.
                Vector3 a = Vector3.Lerp(bee.fromPos, bee.ctrlPos, t);
                Vector3 b = Vector3.Lerp(bee.ctrlPos, bee.targetFlower, t);
                Vector3 pos = Vector3.Lerp(a, b, t);
                // Face travel direction with a slight eager wobble.
                Vector3 vel = (b - a).normalized;
                if (vel.sqrMagnitude > 0.001f)
                {
                    Quaternion look = Quaternion.LookRotation(vel, Vector3.up);
                    bee.bodyT.rotation = Quaternion.Slerp(bee.bodyT.rotation, look, 1f - Mathf.Exp(-7f * dt));
                }
                bee.bodyT.position = pos + Vector3.up * Mathf.Sin(Time.time * 11f + bee.flapPhase) * 0.010f;
                Flap(bee, dt, FlapFlight);
                if (t >= 1f) { bee.state = State.Hover; bee.stateTimer = 0.5f; }
                break;
            }
            case State.Hover:
                // Lock onto the blossom: ease into hover position.
                bee.stateTimer -= dt;
                bee.bodyT.position = Vector3.Lerp(bee.bodyT.position, bee.targetFlower + Vector3.up * 0.045f,
                    1f - Mathf.Exp(-9f * dt));
                Flap(bee, dt, FlapFeed);
                if (bee.stateTimer <= 0f) { bee.state = State.Feeding; bee.stateTimer = FeedTime * (0.8f + (float)bee.rng.NextDouble() * 0.5f); }
                break;
            case State.Feeding:
                // Hover-feeding: bobbing over the blossom, dipping toward it,
                // wings a constant buzz, tiny sideways shuffles.
                bee.stateTimer -= dt;
                Flap(bee, dt, FlapFeed);
                float dip = 0.045f + Mathf.Sin(Time.time * 3.3f + bee.flapPhase) * 0.012f;
                bee.bodyT.position = bee.targetFlower + Vector3.up * dip
                    + new Vector3(Mathf.Sin(Time.time * 2.4f) * 0.008f, 0f, Mathf.Cos(Time.time * 1.9f) * 0.008f);
                bee.bodyT.rotation = Quaternion.Slerp(bee.bodyT.rotation,
                    Quaternion.Euler(24f, bee.bodyT.rotation.eulerAngles.y, 0f),
                    1f - Mathf.Exp(-5f * dt));
                if (bee.stateTimer <= 0f) { bee.state = State.Takeoff; bee.stateTimer = 0.35f; }
                break;
            case State.Takeoff:
                // Zip upward before choosing the next near flower.
                bee.stateTimer -= dt;
                bee.bodyT.position += Vector3.up * dt * 1.1f;
                Flap(bee, dt, FlapFlight);
                if (bee.stateTimer <= 0f) PickNextFlower(bee);
                break;
            case State.Popped:
                // Hidden; counting down to respawn.
                bee.stateTimer -= dt;
                if (bee.stateTimer <= 0f)
                {
                    bee.root.SetActive(true);
                    bee.alive = true;
                    bee.spawnT = 0f; // spring back in cartoon-style
                    PickNextFlower(bee, initial: true);
                }
                break;
        }

        if (bee.alive)
        {
            // Spawn spring-in: cartoonish overshoot scale on (re)entrance.
            if (bee.spawnT < 1f)
            {
                bee.spawnT = Mathf.Min(1f, bee.spawnT + dt / 0.35f);
                float s = EaseOutBack(bee.spawnT);
                bee.bodyT.localScale = new Vector3(s, s, s);
            }
            // Tyler (v1.0.31): no bee ever leaves the phone screen.
            ClampToScreen(bee.bodyT);
        }
    }

    /// <summary>
    /// Beats both wings by rotating the hinge pivots about Z. Fast enough to
    /// read as a blur rather than individual flaps.
    /// </summary>
    void Flap(BeeAgent bee, float dt, float speed)
    {
        bee.flapPhase += dt * speed;
        float wingAngle = 12f + Mathf.Sin(bee.flapPhase) * 55f;
        bee.wingL.localRotation = Quaternion.Euler(0f, 0f, wingAngle);
        bee.wingR.localRotation = Quaternion.Euler(0f, 0f, -wingAngle);
    }

    /// <summary>
    /// Safety net: pin the bee inside the camera viewport every frame so even
    /// mid-flight bezier arcs and the takeoff zip can never leave the phone
    /// screen.
    /// </summary>
    void ClampToScreen(Transform t)
    {
        if (mainCam == null || t == null) return;
        Vector3 vp = mainCam.WorldToViewportPoint(t.position);
        if (vp.z <= 0f) return; // behind the camera; leave it alone
        float cx = Mathf.Clamp(vp.x, MarginX, 1f - MarginX);
        float cy = Mathf.Clamp(vp.y, MarginYBottom, 1f - MarginYTop);
        if (!Mathf.Approximately(cx, vp.x) || !Mathf.Approximately(cy, vp.y))
            t.position = mainCam.ViewportToWorldPoint(new Vector3(cx, cy, vp.z));
    }

    static float EaseOutBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    // ------------------------------------------------------------------ swipe-to-pop

    void UpdateSwipe()
    {
        // Touch takes priority; mouse covers editor testing. (On mobile the
        // first touch also raises mouse-button events; the touch branch wins.)
        bool pressed = Input.GetMouseButtonDown(0);
        bool released = Input.GetMouseButtonUp(0);
        bool down = Input.GetMouseButton(0);
        Vector2 pos = Input.mousePosition;
        if (Input.touchCount > 0)
        {
            var touch = Input.GetTouch(0);
            pos = touch.position;
            pressed = touch.phase == TouchPhase.Began;
            released = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
            down = touch.phase == TouchPhase.Began || touch.phase == TouchPhase.Moved
                || touch.phase == TouchPhase.Stationary;
        }

        if (pressed)
        {
            swiping = true;
            swipePts.Clear();
            swipePts.Add(pos);
            // A swipe that starts on UI (the sun slider, reset button) is a
            // UI gesture, not a bee pop.
            swipeBlockedByUI = IsPointerOverUI(pos);
            // Tap-to-pop: a press that lands right on a bee pops it at once.
            // This also covers super-fast flicks that never emit Moved events.
            if (!swipeBlockedByUI)
                TryPopBeeAt(pos);
        }
        else if (down && swiping)
        {
            if (swipePts.Count == 0 || Vector2.Distance(swipePts[swipePts.Count - 1], pos) > 4f)
                swipePts.Add(pos);
        }
        if (released)
        {
            swiping = false;
            swipePts.Clear();
        }

        if (swiping && !swipeBlockedByUI && swipePts.Count >= 2)
            CheckBeeHits();
    }

    bool IsPointerOverUI(Vector2 screenPos)
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null) return false;
        var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = screenPos };
        var results = new List<UnityEngine.EventSystems.RaycastResult>();
        es.RaycastAll(ped, results);
        return results.Count > 0;
    }

    /// <summary>
    /// If any segment of the current swipe passes close to a bee's screen
    /// position, that bee pops.
    /// </summary>
    void CheckBeeHits()
    {
        if (mainCam == null || swipePts.Count < 2) return;
        for (int i = 1; i < swipePts.Count; i++)
            TryPopBeeNearSegment(swipePts[i - 1], swipePts[i]);
    }

    /// <summary>Pop any live bee within fingertip radius of a screen point.</summary>
    void TryPopBeeAt(Vector2 screenPos)
    {
        if (mainCam == null) return;
        float popRadius = PopRadiusPx();
        foreach (var bee in bees)
        {
            if (!BeePoppable(bee)) continue;
            if (Vector2.Distance(mainCam.WorldToScreenPoint(bee.bodyT.position), screenPos) <= popRadius)
                PopBee(bee);
        }
    }

    /// <summary>Pop any live bee within fingertip radius of a swipe segment.</summary>
    void TryPopBeeNearSegment(Vector2 a, Vector2 b)
    {
        float popRadius = PopRadiusPx();
        foreach (var bee in bees)
        {
            if (!BeePoppable(bee)) continue;
            Vector2 beeScreen = mainCam.WorldToScreenPoint(bee.bodyT.position);
            if (SegmentPointDistance(a, b, beeScreen) <= popRadius)
                PopBee(bee);
        }
    }

    static bool BeePoppable(BeeAgent bee)
    {
        return bee.alive && bee.state != State.Popped && bee.state != State.Waiting;
    }

    static float PopRadiusPx()
    {
        // Generous fingertip radius, scaled to the phone's screen height.
        return Screen.height * 0.06f;
    }

    static float SegmentPointDistance(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 0.0001f) return Vector2.Distance(a, p);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
        return Vector2.Distance(a + ab * t, p);
    }

    void PopBee(BeeAgent bee)
    {
        SpawnPopFX(bee.bodyT.position);
        bee.root.SetActive(false);
        bee.alive = false;
        bee.state = State.Popped;
        bee.stateTimer = RespawnDelay * (0.85f + (float)bee.rng.NextDouble() * 0.4f);
    }

    // ------------------------------------------------------------------ pop FX

    static Material MakePopMaterial(Color c, Shader shader)
    {
        // Lit with transparency: same recipe as the bee wings, which are
        // proven to render on device. Returns null if the shader is missing
        // so callers can skip the FX instead of throwing.
        if (shader == null) return null;
        var mat = new Material(shader);
        mat.SetFloat("_Surface", 1f); // transparent
        mat.SetFloat("_Cull", 0f);
        mat.color = c;
        return mat;
    }

    static Texture2D GetRingTex()
    {
        if (ringTex != null) return ringTex;
        int s = 128;
        ringTex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        float c = s / 2f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                // Soft ring: peak at r=0.36*s, fading both sides.
                float ring = 1f - Mathf.Clamp01(Mathf.Abs(d - s * 0.36f) / (s * 0.10f));
                ring *= ring;
                ringTex.SetPixel(x, y, new Color(1f, 1f, 1f, ring * 0.9f));
            }
        ringTex.Apply();
        return ringTex;
    }

    /// <summary>
    /// Cartoonish, non-violent pop: a rainbow confetti burst, an expanding
    /// soft ring, and a quick cotton-candy poof. Tyler's placeholder design
    /// ("have fun with it, I'll fix it later").
    /// </summary>
    void SpawnPopFX(Vector3 pos)
    {
        // If the pop shader didn't survive the build, skip the FX entirely —
        // the bee still pops (hides + respawns); only the confetti is lost.
        if (popShader == null) return;
        var fx = new PopFX();
        var root = new GameObject("BeePop");
        root.transform.position = pos;
        fx.root = root;
        var localRng = new System.Random((int)(Time.time * 1000f) + pos.GetHashCode());

        // Rainbow confetti petals.
        int petalCount = 18;
        for (int i = 0; i < petalCount; i++)
        {
            var petal = new Petal();
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            go.transform.position = pos;
            float size = 0.030f + (float)localRng.NextDouble() * 0.025f;
            petal.size = size;
            go.transform.localScale = new Vector3(size, size * 0.7f, 1f);
            Color col = i % 6 == 5
                ? Color.white
                : Color.HSVToRGB(i / (float)petalCount, 0.85f, 1f);
            petal.mat = MakePopMaterial(col, popShader);
            go.GetComponent<MeshRenderer>().material = petal.mat;
            petal.t = go.transform;
            // Billboarding happens per-frame in UpdatePops; face camera now.
            if (mainCam != null) petal.t.rotation = mainCam.transform.rotation;
            Vector3 dir = new Vector3(
                (float)localRng.NextDouble() * 2f - 1f,
                (float)localRng.NextDouble() * 1.4f + 0.2f,
                (float)localRng.NextDouble() * 2f - 1f).normalized;
            petal.vel = dir * (1.4f + (float)localRng.NextDouble() * 1.3f);
            petal.spin = new Vector3(
                ((float)localRng.NextDouble() * 2f - 1f) * 540f,
                ((float)localRng.NextDouble() * 2f - 1f) * 540f,
                ((float)localRng.NextDouble() * 2f - 1f) * 540f);
            petal.life = 0.7f + (float)localRng.NextDouble() * 0.4f;
            petal.age = 0f;
            fx.petals.Add(petal);
        }

        // Expanding soft ring flash.
        var ringGo = new GameObject("PopRing").transform;
        ringGo.SetParent(root.transform, false);
        ringGo.position = pos;
        fx.ringMat = MakePopMaterial(new Color(1f, 0.95f, 0.75f, 0.9f), popShader);
        var ringMesh = ringGo.gameObject.AddComponent<MeshRenderer>();
        var ringFilter = ringGo.gameObject.AddComponent<MeshFilter>();
        ringFilter.mesh = MakeQuadMesh();
        ringMesh.material = fx.ringMat;
        // Bake the ring texture into the material via a sprite-like quad UV.
        fx.ringMat.mainTexture = GetRingTex();
        ringGo.localScale = new Vector3(0.12f, 0.12f, 1f);
        if (mainCam != null) ringGo.rotation = mainCam.transform.rotation;
        fx.ringT = ringGo;
        fx.ringAge = 0f;

        // Soft cotton-candy poof at the center.
        var poofGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(poofGo.GetComponent<Collider>());
        poofGo.transform.SetParent(root.transform, false);
        poofGo.transform.position = pos;
        poofGo.transform.localScale = new Vector3(0.06f, 0.06f, 0.06f);
        fx.poofMat = MakePopMaterial(new Color(1f, 0.98f, 0.9f, 0.75f), popShader);
        poofGo.GetComponent<MeshRenderer>().material = fx.poofMat;
        fx.poofT = poofGo.transform;
        fx.poofAge = 0f;

        fx.age = 0f;
        pops.Add(fx);
    }

    static Mesh quadMesh;
    static Mesh MakeQuadMesh()
    {
        if (quadMesh != null) return quadMesh;
        quadMesh = new Mesh();
        quadMesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
        };
        quadMesh.uv = new Vector2[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f),
        };
        quadMesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
        quadMesh.RecalculateNormals();
        return quadMesh;
    }

    void UpdatePops(float dt)
    {
        for (int i = pops.Count - 1; i >= 0; i--)
        {
            var fx = pops[i];
            fx.age += dt;

            // Petals: fly out, drift, spin, shrink away.
            foreach (var p in fx.petals)
            {
                p.age += dt;
                float k = Mathf.Clamp01(p.age / p.life);
                p.vel *= 1f / (1f + 2.2f * dt); // drag
                p.vel.y -= 0.7f * dt;           // gentle gravity
                p.t.position += p.vel * dt;
                p.t.Rotate(p.spin * dt, Space.Self);
                float grow = Mathf.Clamp01(p.age / 0.12f);
                float shrink = 1f - k * 0.65f;
                float s = p.size * grow * shrink;
                p.t.localScale = new Vector3(s, s * 0.7f, 1f);
                Color c = p.mat.color;
                c.a = 1f - k;
                p.mat.color = c;
            }

            // Ring: expand and fade over ~0.5s, always facing the camera.
            fx.ringAge += dt;
            float rk = Mathf.Clamp01(fx.ringAge / 0.5f);
            float rs = 0.12f + fx.ringAge * 1.15f;
            fx.ringT.localScale = new Vector3(rs, rs, 1f);
            if (mainCam != null) fx.ringT.rotation = mainCam.transform.rotation;
            Color rc = fx.ringMat.color;
            rc.a = 0.9f * (1f - rk);
            fx.ringMat.color = rc;

            // Poof: quick cotton-candy bloom, gone in 0.3s.
            fx.poofAge += dt;
            float pk = Mathf.Clamp01(fx.poofAge / 0.3f);
            float ps = 0.06f + fx.poofAge * 0.6f;
            fx.poofT.localScale = new Vector3(ps, ps, ps);
            Color pc = fx.poofMat.color;
            pc.a = 0.75f * (1f - pk);
            fx.poofMat.color = pc;

            if (fx.age > 1.15f)
            {
                foreach (var p in fx.petals) Destroy(p.mat);
                Destroy(fx.ringMat);
                Destroy(fx.poofMat);
                Destroy(fx.root);
                pops.RemoveAt(i);
            }
        }
    }
}
