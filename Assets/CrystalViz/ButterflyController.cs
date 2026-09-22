using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Butterfly prototype (v1.0.29, player request): a single butterfly that
/// flits between random wildflowers, lands on each blossom, flutters its
/// wings as if gathering pollen, then moves on. Pure eye-candy for now —
/// the interactive layer comes after Tyler reviews the look.
/// The butterfly is built in code: a small dark body (stretched box) plus
/// two wing quads hinged at the body, flapped by rotating the wing pivots.
/// Wings use a procedural orange-and-black monarch-style texture so no
/// asset can go missing.
/// </summary>
public class ButterflyController : MonoBehaviour
{
    [HideInInspector] public CrystalVizBootstrap bootstrap;

    Transform bodyT;
    Transform wingL;
    Transform wingR;
    Material wingMat;

    enum State { ToFlower, Settling, Feeding, Takeoff }
    State state = State.ToFlower;

    Vector3 fromPos;
    Vector3 targetFlower;
    Vector3 ctrlPos; // bezier control point for the flight arc
    float flightT;
    float flightDur;
    float stateTimer;
    float flapPhase;

    System.Random rng = new System.Random(20260922);

    // Tuning (player-tweakable):
    const float CruiseHeight = 0.55f;   // how high it arcs between flowers
    const float FlightSpeed = 1.6f;      // world units per second
    const float FeedTime = 3.2f;        // seconds fluttering on a blossom
    const float FlapFast = 26f;         // wing flap rad/s in flight
    const float FlapFeed = 9f;          // slower contented flutter while feeding
    const float WingSpan = 0.055f;      // wing quad size (world units)

    void Start()
    {
        if (bootstrap == null) bootstrap = FindObjectOfType<CrystalVizBootstrap>();
        BuildButterfly();
        PickNextFlower(initial: true);
    }

    void BuildButterfly()
    {
        var root = new GameObject("Butterfly");
        root.transform.SetParent(transform, false);
        bodyT = root.transform;

        // Body: tiny dark stretched box.
        var bodyGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(bodyGo.GetComponent<Collider>());
        bodyGo.transform.SetParent(bodyT, false);
        bodyGo.transform.localScale = new Vector3(0.012f, 0.012f, 0.035f);
        var bodyMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        bodyMat.color = new Color(0.08f, 0.06f, 0.05f);
        bodyGo.GetComponent<MeshRenderer>().material = bodyMat;

        // Wings: procedural monarch texture on two quads hinged at the body.
        wingMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        wingMat.SetTexture("_BaseMap", MakeWingTexture());
        wingMat.SetFloat("_Smoothness", 0.4f);
        wingMat.SetFloat("_Cull", 0f); // Off — wings visible from below too

        wingL = MakeWing("WingL", new Vector3(-1f, 0f, 0f));
        wingR = MakeWing("WingR", new Vector3(1f, 0f, 0f));
    }

    Transform MakeWing(string name, Vector3 hingeDir)
    {
        // Pivot at the body edge; the quad extends outward so rotating the
        // pivot about Z flaps the wing up/down.
        var pivot = new GameObject(name).transform;
        pivot.SetParent(bodyT, false);
        pivot.localPosition = new Vector3(hingeDir.x * 0.006f, 0.004f, 0f);

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(pivot, false);
        // Quad faces +Z; lay it flat (facing up) and offset outward.
        quad.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        quad.transform.localPosition = new Vector3(hingeDir.x * WingSpan * 0.5f, 0f, 0f);
        quad.transform.localScale = new Vector3(WingSpan, WingSpan * 1.25f, 1f);
        // Mirror the right wing's UVs so the pattern isn't mirrored wrong.
        if (hingeDir.x > 0f) quad.transform.localScale = new Vector3(-WingSpan, WingSpan * 1.25f, 1f);
        quad.GetComponent<MeshRenderer>().material = wingMat;
        return pivot;
    }

    /// <summary>
    /// Procedural monarch-style wing: orange field, black borders and veins,
    /// white dots along the edge. 128x160, drawn in code.
    /// </summary>
    static Texture2D MakeWingTexture()
    {
        int w = 128, h = 160;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var orange = new Color(0.98f, 0.45f, 0.08f);
        var black = new Color(0.05f, 0.04f, 0.04f);
        var white = new Color(0.95f, 0.93f, 0.88f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                Color c = orange;
                // Black border growing toward the outer (top/right) edges.
                float edge = Mathf.Max(u, v);
                if (edge > 0.72f) c = black;
                // Veins radiating from the hinge (bottom-left).
                float ang = Mathf.Atan2(v, u);
                float vein = Mathf.Abs(Mathf.Sin(ang * 7f));
                if (vein < 0.08f && edge < 0.85f) c = Color.Lerp(c, black, 0.75f);
                // White dots along the black border.
                if (edge > 0.78f && edge < 0.95f)
                {
                    float d = Mathf.Sin(u * 40f) * Mathf.Sin(v * 40f);
                    if (d > 0.55f) c = white;
                }
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }

    void PickNextFlower(bool initial = false)
    {
        List<Vector3> heads = bootstrap != null ? bootstrap.flowerHeads : null;
        if (heads == null || heads.Count == 0)
        {
            // No flowers (shader missing?): hover in place near the tree.
            targetFlower = new Vector3(1.5f, 0.35f, 1.5f);
        }
        else
        {
            // Random flower, biased toward the visible near-field so the
            // butterfly stays in frame: prefer heads within ~9 units.
            Vector3 pick = heads[rng.Next(heads.Count)];
            for (int i = 0; i < 4; i++)
            {
                var c = heads[rng.Next(heads.Count)];
                if (c.magnitude < 9f) { pick = c; break; }
                pick = c;
            }
            targetFlower = pick;
        }
        fromPos = bodyT.position;
        if (initial) fromPos = targetFlower + new Vector3(1.2f, CruiseHeight, 0.8f);
        // Bezier arc: lift off, cruise, descend onto the blossom.
        ctrlPos = (fromPos + targetFlower) * 0.5f + Vector3.up * CruiseHeight;
        float dist = Vector3.Distance(fromPos, targetFlower);
        flightDur = Mathf.Max(0.8f, dist / FlightSpeed);
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
                // Face travel direction; bank slightly into turns.
                Vector3 vel = (b - a).normalized;
                if (vel.sqrMagnitude > 0.001f)
                {
                    Quaternion look = Quaternion.LookRotation(vel, Vector3.up);
                    bodyT.rotation = Quaternion.Slerp(bodyT.rotation, look, 1f - Mathf.Exp(-6f * dt));
                }
                bodyT.position = pos;
                Flap(dt, FlapFast);
                // Gentle bob while flying.
                bodyT.position += Vector3.up * Mathf.Sin(Time.time * 9f) * 0.012f;
                if (t >= 1f) { state = State.Settling; stateTimer = 0.45f; }
                break;
            }
            case State.Settling:
                // Brief hover-touchdown: ease the last centimeters.
                stateTimer -= dt;
                bodyT.position = Vector3.Lerp(bodyT.position, targetFlower + Vector3.up * 0.015f,
                    1f - Mathf.Exp(-8f * dt));
                Flap(dt, FlapFast * 0.6f);
                if (stateTimer <= 0f) { state = State.Feeding; stateTimer = FeedTime * (0.8f + (float)rng.NextDouble() * 0.5f); }
                break;
            case State.Feeding:
                // Perched: slow contented flutter, tiny shuffles as if probing
                // the blossom. Body pitches down toward the flower.
                stateTimer -= dt;
                Flap(dt, FlapFeed);
                bodyT.position = targetFlower + Vector3.up * 0.015f
                    + new Vector3(Mathf.Sin(Time.time * 2.1f) * 0.006f, 0f, Mathf.Cos(Time.time * 1.7f) * 0.006f);
                bodyT.rotation = Quaternion.Slerp(bodyT.rotation,
                    Quaternion.Euler(18f, bodyT.rotation.eulerAngles.y, 0f),
                    1f - Mathf.Exp(-4f * dt));
                if (stateTimer <= 0f) { state = State.Takeoff; stateTimer = 0.4f; }
                break;
            case State.Takeoff:
                // Hop up before choosing the next flower.
                stateTimer -= dt;
                bodyT.position += Vector3.up * dt * 0.9f;
                Flap(dt, FlapFast);
                if (stateTimer <= 0f) PickNextFlower();
                break;
        }
    }

    /// <summary>
    /// Flaps both wings by rotating the hinge pivots about Z. Rest angle
    /// holds the wings slightly open; amplitude swings them up and down.
    /// </summary>
    void Flap(float dt, float speed)
    {
        flapPhase += dt * speed;
        float wingAngle = 18f + Mathf.Sin(flapPhase) * 62f;
        wingL.localRotation = Quaternion.Euler(0f, 0f, wingAngle);
        wingR.localRotation = Quaternion.Euler(0f, 0f, -wingAngle);
    }
}
