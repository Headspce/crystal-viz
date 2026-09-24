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
        public Material spriteMat;
        public GameObject shadow;    // soft blob shadow tracking the bee
        public Transform shadowT;
        public Material shadowMat; // per-bee: holds the current flap frame
        public float flipX = 1f;    // paper-turn: +1 faces right, -1 faces left
        public float flipTarget = 1f;
        public Vector3 lastPos;
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

    // An active pop burst: pure confetti (v1.0.34 — the ring and poof were
    // removed per Tyler: they fought the confetti visually).
    class PopFX
    {
        public GameObject root;
        public List<Petal> petals = new List<Petal>();
        public float age;
    }

    // An active spawn burst: a comic starburst that pops behind a bee the
    // moment it (re)appears — Tyler's pop-art reference, v1.0.39.
    class SpawnBurst
    {
        public GameObject root;
        public Transform t;
        public Material mat;
        public float age;
    }

    // Tuning (player-tweakable):
    const int BeeCount = 3;       // the squad: three bees, independent paths
    const float VisitRadius = 6.0f;  // only flowers within this of the tree (origin)
    const float BaseCruiseHeight = 0.35f; // bees fly low and direct, not lofty arcs
    const float BaseFlightSpeed = 2.2f;   // world units per second
    const float FeedTime = 2.8f;      // seconds hovering over a blossom
    const float FlapFlight = 44f;     // wing frame swap rad/s in flight
    const float FlapFeed = 30f;       // hovering buzz while feeding
    const float SpriteSize = 0.17f;   // paper-bee quad size in world units
    const float RespawnDelay = 6.0f;  // seconds before a popped bee returns
    // Viewport margins: no bee ever leaves this rect on the phone screen.
    const float MarginX = 0.06f;
    const float MarginYBottom = 0.07f;
    const float MarginYTop = 0.06f;

    // v1.0.37 game loop: each pop banks one tree-tap credit on the
    // TreeGrowthController. Resolved once in Start.
    TreeGrowthController treeController;

    // Resolved once in Start: the ONLY shader the pop FX may use. URP/Lit is
    // guaranteed in the player build (pinned in the variant collection and
    // referenced by scene materials). Never Shader.Find a shader at pop time:
    // v1.0.32 used URP/Unlit, which Unity strips from the build because
    // nothing serialised references it — Shader.Find returned null on the
    // phone, new Material(null) threw, and the bee never popped. Silent on
    // desktop, broken on device.
    Shader popShader;

    // v1.0.35: dedicated sprite shader for the Paper Mario bee quads.
    // Resolved once in Start; falls back to URP/Lit if stripped.
    Shader spriteShader;

    void Start()
    {
        if (bootstrap == null) bootstrap = FindObjectOfType<CrystalVizBootstrap>();
        treeController = FindObjectOfType<TreeGrowthController>();
        mainCam = Camera.main;
        popShader = Shader.Find("Universal Render Pipeline/Lit");
        if (popShader == null)
            Debug.LogError("BeeController: URP/Lit not found; pop FX will be skipped.");
        // v1.0.35: the bee sprite quad needs REAL alpha blending. v1.0.34
        // used URP/Lit with SetFloat("_AlphaClip", 1f), but that never enables
        // the _ALPHATEST_ON keyword on a runtime-created material, so the
        // quad rendered opaque and the transparent texels showed as a black
        // box around each bee on the phone. CrystalViz/SpritePaper is a
        // keyword-free unlit alpha-blend shader (pinned in the variant
        // collection, so Shader.Find resolves on device).
        spriteShader = Shader.Find("CrystalViz/SpritePaper");
        if (spriteShader == null)
        {
            Debug.LogError("BeeController: CrystalViz/SpritePaper not found; bee sprites fall back to URP/Lit.");
            spriteShader = popShader;
        }
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

    /// <summary>
    /// Paper Mario bee: a flat cartoon sprite on a camera-facing quad — 2D
    /// and 3D at once. The body never rotates in 3D; turning is a horizontal
    /// paper flip (scale.x sweeping through zero), and the wings are two
    /// sprite frames swapped at flap speed.
    /// </summary>
    void BuildBeeAgent(BeeAgent bee, int index)
    {
        EnsureBeeSprites();
        var root = new GameObject("Bee" + (index + 1));
        root.transform.SetParent(transform, false);
        bee.root = root;
        bee.bodyT = root.transform;

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(bee.bodyT, false);
        bee.spriteMat = new Material(spriteShader);
        if (spriteShader != null && spriteShader.name == "CrystalViz/SpritePaper")
        {
            // Keyword-free alpha blend: no _AlphaClip/_Cutoff needed, and
            // Material.mainTexture maps straight to _MainTex, so the wing
            // flap frame swap in Update works on device too.
            bee.spriteMat.SetTexture("_MainTex", beeTexUp);
        }
        else
        {
            // Fallback path (should never happen — the paper shader is pinned
            // in the variant collection): legacy URP/Lit setup.
            bee.spriteMat.SetTexture("_BaseMap", beeTexUp);
            bee.spriteMat.SetFloat("_AlphaClip", 1f);
            bee.spriteMat.SetFloat("_Cutoff", 0.4f);
            bee.spriteMat.SetFloat("_Cull", 0f);
            bee.spriteMat.SetFloat("_Smoothness", 0f); // flat cartoon, no shine
            bee.spriteMat.SetFloat("_Metallic", 0f);
        }
        quad.GetComponent<MeshRenderer>().material = bee.spriteMat;
        bee.lastPos = bee.bodyT.position;

        BuildBlobShadow(bee);
    }

    static Texture2D blobTex; // soft radial shadow blob, drawn once

    static void EnsureBlobTexture()
    {
        if (blobTex != null) return;
        int s = 128;
        blobTex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x - s / 2f) / (s / 2f);
                float dy = (y - s / 2f) / (s / 2f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a); // smootherstep: soft edge
                blobTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        blobTex.Apply();
        blobTex.filterMode = FilterMode.Bilinear;
    }

    /// <summary>
    /// v1.0.39: a soft semi-transparent blob shadow under each bee, like the
    /// tree's — the paper shader has no shadow-caster pass, so this cartoon
    /// blob is the honest way to ground them.
    /// </summary>
    void BuildBlobShadow(BeeAgent bee)
    {
        EnsureBlobTexture();
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(go.GetComponent<Collider>());
        // Sibling of the bee root (which billboards every frame) — the
        // shadow stays flat on the ground and is positioned in world space.
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        go.transform.position = new Vector3(0f, 0.015f, 0f);
        var mat = new Material(spriteShader);
        mat.mainTexture = blobTex;
        mat.color = new Color(0f, 0f, 0f, 0f);
        go.GetComponent<MeshRenderer>().material = mat;
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        go.SetActive(false);

        bee.shadow = go;
        bee.shadowT = go.transform;
        bee.shadowMat = mat;
    }

    static Texture2D beeTexUp;   // wings raised
    static Texture2D beeTexDown; // wings lowered

    static void EnsureBeeSprites()
    {
        if (beeTexUp != null) return;
        // v1.0.39: Tyler's redesigned bee frames (white keyed out + cropped
        // square + mirrored to face right at author time). Fall back to the
        // procedural sprite if the assets are missing for any reason.
        beeTexUp = Resources.Load<Texture2D>("bee-wings-up");
        beeTexDown = Resources.Load<Texture2D>("bee-wings-down");
        if (beeTexUp == null) beeTexUp = DrawBeeSprite(wingsUp: true);
        if (beeTexDown == null) beeTexDown = DrawBeeSprite(wingsUp: false);
    }

    /// <summary>
    /// Draws a flat-shaded cartoon bee (side view, facing right) onto a
    /// 256px canvas: dark outline, golden body with a cel shade band, black
    /// stripes, head with a cute eye, stinger, and pale wings.
    /// </summary>
    static Texture2D DrawBeeSprite(bool wingsUp)
    {
        int s = 256;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                tex.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
        var outline = new Color(0.25f, 0.16f, 0.05f);
        var gold = new Color(0.99f, 0.73f, 0.13f);
        var shade = new Color(0.80f, 0.54f, 0.07f);
        var black = new Color(0.10f, 0.08f, 0.05f);
        var wing = new Color(0.88f, 0.94f, 1.0f);

        // Stinger tucks behind the body.
        FillTriangle(tex, new Vector2(48, 128), new Vector2(16, 116), new Vector2(16, 140), black);

        // Wings (behind the body).
        int wy = wingsUp ? 196 : 148;
        FillEllipse(tex, 142, wy - 6, 40, 24, outline);
        FillEllipse(tex, 142, wy - 6, 36, 20, wing);
        FillEllipse(tex, 100, wy + 4, 46, 28, outline);
        FillEllipse(tex, 100, wy + 4, 42, 24, wing);

        // Body: outline, cel shade crescent, then the golden body.
        FillEllipse(tex, 126, 128, 90, 65, outline);
        FillEllipse(tex, 126, 118, 86, 61, shade);
        FillEllipse(tex, 126, 134, 84, 59, gold);

        // Three black stripes, clipped to the body ellipse.
        int bcx = 126, bcy = 134, brx = 84, bry = 59;
        int[,] bands = { { 80, 94 }, { 120, 134 }, { 160, 174 } };
        for (int b = 0; b < 3; b++)
            for (int y = bcy - bry; y <= bcy + bry; y++)
                for (int x = bands[b, 0]; x <= bands[b, 1]; x++)
                {
                    float dx = (x - bcx) / (float)brx, dy = (y - bcy) / (float)bry;
                    if (dx * dx + dy * dy <= 1f) tex.SetPixel(x, y, black);
                }

        // Head with a cute eye.
        FillCircle(tex, 200, 124, 30, outline);
        FillCircle(tex, 200, 124, 26, black);
        FillCircle(tex, 208, 132, 10, Color.white);
        FillCircle(tex, 211, 133, 5, black);

        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }

    static void FillEllipse(Texture2D tex, int cx, int cy, int rx, int ry, Color c)
    {
        for (int y = cy - ry; y <= cy + ry; y++)
        {
            if (y < 0 || y >= tex.height) continue;
            for (int x = cx - rx; x <= cx + rx; x++)
            {
                if (x < 0 || x >= tex.width) continue;
                float dx = (x - cx) / (float)rx, dy = (y - cy) / (float)ry;
                if (dx * dx + dy * dy <= 1f) tex.SetPixel(x, y, c);
            }
        }
    }

    static void FillCircle(Texture2D tex, int cx, int cy, int r, Color c)
    {
        FillEllipse(tex, cx, cy, r, r, c);
    }

    static void FillTriangle(Texture2D tex, Vector2 a, Vector2 b, Vector2 c, Color col)
    {
        int minX = Mathf.Max(0, (int)Mathf.Min(a.x, Mathf.Min(b.x, c.x)));
        int maxX = Mathf.Min(tex.width - 1, (int)Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
        int minY = Mathf.Max(0, (int)Mathf.Min(a.y, Mathf.Min(b.y, c.y)));
        int maxY = Mathf.Min(tex.height - 1, (int)Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
        float denom = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
        if (Mathf.Abs(denom) < 1e-6f) return;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float w1 = ((b.y - c.y) * (x - c.x) + (c.x - b.x) * (y - c.y)) / denom;
                float w2 = ((c.y - a.y) * (x - c.x) + (a.x - c.x) * (y - c.y)) / denom;
                float w3 = 1f - w1 - w2;
                if (w1 >= 0f && w2 >= 0f && w3 >= 0f) tex.SetPixel(x, y, col);
            }
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

    /// <summary>
    /// v1.0.41: park every bee and kill all in-flight FX so the loading
    /// screen and world reveal own the screen alone. Bee 1's entrance timer
    /// is 0s, so without this it spawned (with a starburst) on the first
    /// frame after the diorama built — then the reveal froze the burst
    /// mid-animation in the middle of the screen. Timers are re-staggered
    /// here; they only tick once the controller is re-enabled after the
    /// reveal, so entrances land after the world has loaded.
    /// </summary>
    public void HideForReveal()
    {
        for (int i = 0; i < bees.Count; i++)
        {
            var bee = bees[i];
            bee.root.SetActive(false);
            bee.alive = false;
            bee.state = State.Waiting;
            bee.stateTimer = i * 0.9f; // staggered entrances, post-reveal
            bee.spawnT = 1f;
            if (bee.shadow != null) bee.shadow.SetActive(false);
        }
        for (int i = spawnBursts.Count - 1; i >= 0; i--)
        {
            Destroy(spawnBursts[i].mat);
            Destroy(spawnBursts[i].root);
        }
        spawnBursts.Clear();
        for (int i = pops.Count - 1; i >= 0; i--)
        {
            foreach (var p in pops[i].petals) Destroy(p.mat);
            Destroy(pops[i].root);
        }
        pops.Clear();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        foreach (var bee in bees)
            UpdateBee(bee, dt);
        UpdateSwipe();
        UpdatePops(dt);
        UpdateSpawnBursts(dt);
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
                    // v1.0.41: teleport the bee to its actual spawn point
                    // BEFORE the starburst fires — the burst used to spawn at
                    // the stale pre-teleport position, so it appeared offset
                    // from where the bee showed up.
                    bee.bodyT.position = bee.fromPos;
                    bee.lastPos = bee.bodyT.position;
                    SpawnSpawnBurst(bee.bodyT.position);
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
                // Paper bee: no 3D turning here — the billboard flip at the
                // end of UpdateBee handles facing. Just fly the position.
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
                    // v1.0.41: same stale-position fix as the entrance burst —
                    // teleport first so the starburst lands exactly where the
                    // bee reappears.
                    bee.bodyT.position = bee.fromPos;
                    bee.lastPos = bee.bodyT.position;
                    SpawnSpawnBurst(bee.bodyT.position);
                }
                break;
        }

        if (bee.alive)
        {
            // Spawn spring-in: cartoonish overshoot scale on (re)entrance.
            float spawnS = 1f;
            if (bee.spawnT < 1f)
            {
                bee.spawnT = Mathf.Min(1f, bee.spawnT + dt / 0.35f);
                spawnS = EaseOutBack(bee.spawnT);
            }

            // Paper Mario billboard: the paper bee always faces the camera.
            // Turning is an illusion — a horizontal paper flip (scale.x
            // sweeping through edge-on) driven by camera-space motion, plus
            // a slight paper tilt with vertical movement.
            if (mainCam != null)
            {
                bee.bodyT.rotation = mainCam.transform.rotation;
                Vector3 velW = (bee.bodyT.position - bee.lastPos) / Mathf.Max(dt, 0.0001f);
                bee.lastPos = bee.bodyT.position;
                Vector3 velC = mainCam.transform.InverseTransformDirection(velW);
                if (Mathf.Abs(velC.x) > 0.2f) bee.flipTarget = velC.x > 0f ? 1f : -1f;
                bee.flipX = Mathf.MoveTowards(bee.flipX, bee.flipTarget, dt * 9f);
                float bank = Mathf.Clamp(-velC.y * 5f, -12f, 12f);
                bee.bodyT.Rotate(0f, 0f, bank);
            }
            bee.bodyT.localScale = new Vector3(SpriteSize * bee.flipX * spawnS, SpriteSize * spawnS, 1f);

            // Tyler (v1.0.31): no bee ever leaves the phone screen.
            ClampToScreen(bee.bodyT);

            // Blob shadow: tracks the bee on the ground, growing fainter and
            // wider as the bee climbs — semi-transparent like the tree's.
            if (bee.shadowT != null)
            {
                bee.shadow.SetActive(true);
                Vector3 bp = bee.bodyT.position;
                bee.shadowT.position = new Vector3(bp.x, 0.015f, bp.z);
                float h = Mathf.Max(0f, bp.y - 0.015f);
                float fade = Mathf.Clamp01(1f - h / 0.9f);
                float shS = (0.16f + h * 0.18f) * Mathf.Max(0.001f, spawnS);
                bee.shadowT.localScale = new Vector3(shS, shS, 1f);
                var sc = bee.shadowMat.color;
                sc.a = 0.36f * fade * Mathf.Clamp01(spawnS);
                bee.shadowMat.color = sc;
            }
        }
        else if (bee.shadow != null)
        {
            bee.shadow.SetActive(false);
        }
    }

    /// <summary>
    /// Paper-bee wing buzz: swap between the wings-up and wings-down sprite
    /// frames at flap speed — classic 2D sprite animation.
    /// </summary>
    void Flap(BeeAgent bee, float dt, float speed)
    {
        bee.flapPhase += dt * speed;
        var frame = Mathf.Sin(bee.flapPhase) > 0f ? beeTexUp : beeTexDown;
        if (bee.spriteMat != null && bee.spriteMat.mainTexture != frame)
            bee.spriteMat.mainTexture = frame;
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

    /// <summary>
    /// Hit-test for TreeGrowthController (v1.0.35 tap/swipe disambiguation):
    /// true when the screen point lands on a live, poppable bee. Uses the
    /// same fingertip radius as the pop itself, so "tap pops the bee" and
    /// "tap doesn't grow the tree" always agree with each other.
    /// </summary>
    public bool IsBeeAtScreenPoint(Vector2 screenPos)
    {
        if (mainCam == null || bees == null) return false;
        float popRadius = PopRadiusPx();
        foreach (var bee in bees)
        {
            if (!BeePoppable(bee)) continue;
            if (Vector2.Distance(mainCam.WorldToScreenPoint(bee.bodyT.position), screenPos) <= popRadius)
                return true;
        }
        return false;
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

    /// <summary>
    /// How many bees are currently flying around unpopped — drives the HUD
    /// bee counter. Goes 3 -> 0 as bees are popped and refills on respawn.
    /// </summary>
    public int FlyingBeeCount
    {
        get
        {
            int n = 0;
            foreach (var bee in bees)
                if (BeePoppable(bee)) n++;
            return n;
        }
    }

    /// <summary>
    /// The procedural bee sprite (wings-up frame) so the HUD can show a bee
    /// icon next to the live-bee count.
    /// </summary>
    public static Texture2D BeeIconTexture
    {
        get { EnsureBeeSprites(); return beeTexUp; }
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
        // v1.0.45: WIDE per-bee respawn desync (player request). Each bee
        // rolls its own independent delay — 6s x [0.7, 1.6] = 4.2s..9.6s —
        // so popping all three at once no longer brings all three back in
        // lockstep. (The old 0.85..1.25 band was only a 2.4s spread, which
        // read as simultaneous.) UnityEngine.Random: every pop is a fresh
        // independent draw per bee.
        bee.stateTimer = RespawnDelay * UnityEngine.Random.Range(0.7f, 1.6f);
        // v1.0.37 game loop: one pop banks one tree-tap credit.
        if (treeController == null) treeController = FindObjectOfType<TreeGrowthController>();
        if (treeController != null) treeController.AddTapCredit(1);
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

        // Rainbow confetti petals — v1.0.34: 36 petals at 3x size, the whole show.
        int petalCount = 36;
        for (int i = 0; i < petalCount; i++)
        {
            var petal = new Petal();
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            go.transform.position = pos;
            float size = 0.090f + (float)localRng.NextDouble() * 0.075f;
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

        // v1.0.34: the expanding ring flash and center poof are gone —
        // Tyler felt they fought the confetti. Pop is confetti-only now.

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

    // ------------------------------------------------------- spawn burst FX

    const float SpawnBurstLife = 0.6f;  // starburst pop lifetime
    const float SpawnBurstFadeStart = 0.20f; // dissolve begins (smoothstepped to 0)
    const float SpawnBurstSize = 0.34f; // ~2x the bee quad

    readonly List<SpawnBurst> spawnBursts = new List<SpawnBurst>();
    static Texture2D burstTex; // jagged comic star, drawn once

    static void EnsureBurstTexture()
    {
        if (burstTex != null) return;
        burstTex = DrawBurstStar();
    }

    /// <summary>
    /// Draws a jagged comic-book starburst (dark outline, golden fill,
    /// lighter core) onto a 256px canvas — the pop-art spawn effect.
    /// </summary>
    static Texture2D DrawBurstStar()
    {
        int s = 256;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                tex.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));

        Vector2 c = new Vector2(s / 2f, s / 2f);
        FillStar(tex, c, 120f, 58f, 12, new Color(0.23f, 0.10f, 0.02f)); // outline
        FillStar(tex, c, 108f, 52f, 12, new Color(1.00f, 0.80f, 0.16f)); // gold
        FillStar(tex, c, 58f, 28f, 12, new Color(1.00f, 0.93f, 0.48f)); // core

        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }

    static void FillStar(Texture2D tex, Vector2 center, float rOuter, float rInner, int spikes, Color col)
    {
        int n = spikes * 2;
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float r = (i % 2 == 0) ? rOuter : rInner;
            float a = (i / (float)n) * Mathf.PI * 2f - Mathf.PI / 2f;
            pts[i] = center + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }
        FillPolygon(tex, pts, col);
    }

    static void FillPolygon(Texture2D tex, Vector2[] pts, Color col)
    {
        int w = tex.width, h = tex.height;
        float minX = w, maxX = 0, minY = h, maxY = 0;
        foreach (var p in pts)
        {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
        }
        int x0 = Mathf.Max(0, Mathf.FloorToInt(minX)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(maxX));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(maxY));
        int n = pts.Length;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                // Even-odd point-in-polygon over the star's vertices.
                bool inside = false;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    Vector2 pi = pts[i], pj = pts[j];
                    if (((pi.y > y) != (pj.y > y)) &&
                        (x < (pj.x - pi.x) * (y - pi.y) / (pj.y - pi.y) + pi.x))
                        inside = !inside;
                }
                if (inside) tex.SetPixel(x, y, col);
            }
    }

    /// <summary>
    /// Comic starburst pop behind a bee the moment it (re)appears.
    /// </summary>
    void SpawnSpawnBurst(Vector3 pos)
    {
        // v1.0.42: the starburst texture has transparent texels, so it needs
        // REAL alpha blending. It used to build its material on URP/Lit via
        // MakePopMaterial, but setting _Surface=1 never enables the
        // _SURFACE_TYPE_TRANSPARENT keyword on a runtime-created material —
        // the quad rendered opaque and the transparent texels showed as a
        // black box around the golden star on the phone (same bug class as
        // the v1.0.35 bee-sprite black boxes). CrystalViz/SpritePaper is a
        // keyword-free unlit alpha-blend shader, pinned in the variant
        // collection so Shader.Find resolves on device.
        if (spriteShader == null || mainCam == null) return;
        EnsureBurstTexture();

        var fx = new SpawnBurst();
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(go.GetComponent<Collider>());
        // Sit just behind the bee quad so the star frames it, never z-fights.
        go.transform.position = pos - mainCam.transform.forward * 0.03f;
        go.transform.rotation = mainCam.transform.rotation;
        go.transform.localScale = Vector3.zero;
        var mat = MakePopMaterial(Color.white, spriteShader);
        if (mat == null) { Destroy(go); return; }
        mat.mainTexture = burstTex;
        go.GetComponent<MeshRenderer>().material = mat;
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        fx.root = go;
        fx.t = go.transform;
        fx.mat = mat;
        fx.age = 0f;
        spawnBursts.Add(fx);
    }

    void UpdateSpawnBursts(float dt)
    {
        for (int i = spawnBursts.Count - 1; i >= 0; i--)
        {
            var fx = spawnBursts[i];
            fx.age += dt;
            float t = Mathf.Clamp01(fx.age / SpawnBurstLife);

            if (mainCam != null) fx.t.rotation = mainCam.transform.rotation;

            // Pop in with overshoot, hold, then fade.
            float pop = EaseOutBack(Mathf.Clamp01(fx.age / 0.18f));
            float s = SpawnBurstSize * Mathf.Max(0.001f, pop);
            fx.t.localScale = new Vector3(s, s, 1f);

            var c = fx.mat.color;
            // v1.0.43: smooth transparent dissolve over the back half of the
            // life (was a quick linear tail that read as a pop-out, and on
            // <=v1.0.41 the opaque shader ignored alpha entirely).
            float fadeT = Mathf.Clamp01((fx.age - SpawnBurstFadeStart) / (SpawnBurstLife - SpawnBurstFadeStart));
            c.a = 1f - fadeT * fadeT * (3f - 2f * fadeT);
            fx.mat.color = c;

            if (t >= 1f)
            {
                Destroy(fx.mat);
                Destroy(fx.root);
                spawnBursts.RemoveAt(i);
            }
        }
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

            if (fx.age > 1.15f)
            {
                foreach (var p in fx.petals) Destroy(p.mat);
                Destroy(fx.root);
                pops.RemoveAt(i);
            }
        }
    }
}
