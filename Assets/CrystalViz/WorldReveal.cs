using System.Collections;
using UnityEngine;

/// <summary>
/// World-reveal sequencer (v1.0.40): after the loading screen fades, the
/// world loads in as two waves sweeping from the back of the world toward
/// the viewer — first a glowing grid draws itself across the dark ground,
/// then the real textured world (grass, wildflowers, tree) grows in as a
/// second wave while the grid dissolves. Purely aesthetic, no gameplay use.
/// Play-mode only; the CI edit-mode screenshot path never runs it, so the
/// shaders' defaults (fully grown, no grid) keep captures identical.
/// </summary>
public class WorldReveal : MonoBehaviour
{
    const float FarZ = -110f;
    const float NearZ = 20f;
    const float Wave1Start = 0.2f;
    const float Wave1Dur = 1.3f;
    const float Wave2Start = 1.5f;
    const float Wave2Dur = 2.2f;

    Material grassMat;
    Material flowerMat;
    Material gridMat;
    GameObject gridPlane;
    GameObject treeWrapper;
    BeeController beeController;
    GameObject stageCanvas;
    GameObject sunSliderCanvas;
    GameObject resetButtonCanvas;
    float t;
    bool treePopped;

    public static void Begin()
    {
        var go = new GameObject("WorldReveal");
        go.AddComponent<WorldReveal>();
    }

    void Start()
    {
        var grassField = GameObject.Find("GrassField");
        if (grassField != null)
        {
            var mr = grassField.GetComponent<MeshRenderer>();
            if (mr != null) grassMat = mr.material;
        }
        var flowers = GameObject.Find("Wildflowers");
        if (flowers != null)
        {
            var mr = flowers.GetComponent<MeshRenderer>();
            if (mr != null) flowerMat = mr.material;
        }
        var treeGO = GameObject.Find("GrowingTree");
        beeController = FindObjectOfType<BeeController>();
        stageCanvas = GameObject.Find("StageCanvas");
        sunSliderCanvas = GameObject.Find("SunSliderCanvas");
        resetButtonCanvas = GameObject.Find("ResetButtonCanvas");

        // Collapse the meadow before the first revealed frame.
        if (grassMat != null) grassMat.SetFloat("_GrowFront", FarZ);
        if (flowerMat != null) flowerMat.SetFloat("_GrowFront", FarZ);

        BuildGridPlane();

        // The tree pops in on a wrapper so the growth controller's own
        // scale animation is never fought.
        if (treeGO != null)
        {
            treeWrapper = new GameObject("TreeRevealWrapper");
            treeWrapper.transform.position = treeGO.transform.position;
            treeWrapper.transform.rotation = treeGO.transform.rotation;
            treeGO.transform.SetParent(treeWrapper.transform, true);
            treeWrapper.transform.localScale = Vector3.zero;
        }

        // Bees and HUD wait until the world has loaded. The bee controller is
        // parked and its entrance timers re-staggered (v1.0.41) so no bee
        // or FX spawns or floats in view while the reveal owns the screen.
        if (beeController != null)
        {
            beeController.HideForReveal();
            beeController.enabled = false;
        }
        SetUI(false);
    }

    void BuildGridPlane()
    {
        var shader = Shader.Find("CrystalViz/GridReveal");
        if (shader == null)
        {
            Debug.LogWarning("CrystalViz: 'CrystalViz/GridReveal' not found; skipping grid phase.");
            return;
        }
        gridMat = new Material(shader);
        gridMat.SetFloat("_Wave1", FarZ);
        gridMat.SetFloat("_Wave2", FarZ);
        gridMat.SetFloat("_Colorize", 0f); // v1.0.41: starts black & white
        gridPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        DestroyImmediate(gridPlane.GetComponent<Collider>());
        gridPlane.name = "RevealGrid";
        gridPlane.transform.position = new Vector3(0f, 0.02f, -15f);
        gridPlane.transform.localScale = new Vector3(13f, 1f, 13f); // 130x130
        var mr = gridPlane.GetComponent<MeshRenderer>();
        mr.material = gridMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    void Update()
    {
        t += Time.deltaTime;
        float w1 = Wavefront(Wave1Start, Wave1Dur);
        float w2 = Wavefront(Wave2Start, Wave2Dur);
        if (gridMat != null)
        {
            gridMat.SetFloat("_Wave1", w1);
            gridMat.SetFloat("_Wave2", w2);
            // v1.0.41: the grid starts black & white and fades into its blue
            // color as the reveal plays.
            gridMat.SetFloat("_Colorize", Mathf.Clamp01((t - 0.4f) / 2.4f));
        }
        if (grassMat != null) grassMat.SetFloat("_GrowFront", w2);
        if (flowerMat != null) flowerMat.SetFloat("_GrowFront", w2);

        // The tree pops when the texture wave reaches it.
        if (!treePopped && treeWrapper != null && w2 >= 0f)
        {
            treePopped = true;
            StartCoroutine(PopTree());
        }

        if (t >= Wave2Start + Wave2Dur + 0.4f) Finish();
    }

    float Wavefront(float start, float dur)
    {
        float k = Mathf.Clamp01((t - start) / dur);
        k = k * k * (3f - 2f * k); // smootherstep: ease in and out
        return Mathf.Lerp(FarZ, NearZ, k);
    }

    IEnumerator PopTree()
    {
        float pt = 0f;
        while (pt < 0.55f)
        {
            pt += Time.deltaTime;
            float s = EaseOutBack(Mathf.Clamp01(pt / 0.55f));
            treeWrapper.transform.localScale = Vector3.one * Mathf.Max(0.001f, s);
            yield return null;
        }
        treeWrapper.transform.localScale = Vector3.one;
    }

    static float EaseOutBack(float x)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    void Finish()
    {
        // Park the uniforms back at "fully revealed" so nothing lingers.
        if (grassMat != null) grassMat.SetFloat("_GrowFront", 10000f);
        if (flowerMat != null) flowerMat.SetFloat("_GrowFront", 10000f);
        if (beeController != null) beeController.enabled = true;
        SetUI(true);
        if (gridPlane != null) Destroy(gridPlane);
        Destroy(gameObject);
    }

    void SetUI(bool on)
    {
        if (stageCanvas != null) stageCanvas.SetActive(on);
        if (sunSliderCanvas != null) sunSliderCanvas.SetActive(on);
        if (resetButtonCanvas != null) resetButtonCanvas.SetActive(on);
    }
}
