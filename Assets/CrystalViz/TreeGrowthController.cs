using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tap-to-grow logic for the parametric tree. Each screen tap advances the
/// tree one step toward maturity (default 50 taps, sprout -> mature).
///
/// Progress persists across sessions via PlayerPrefs ("CrystalViz_TreeTaps").
/// The displayed growth animates smoothly (~0.5s) toward the tapped target so
/// the tree grows continuously instead of popping between steps.
///
/// Growth stages: 0-10 taps Sprout, 11-25 Sapling, 26-40 YoungTree, 41-50 Mature.
/// Fires onStageChanged(newStage) whenever the stage transitions.
/// </summary>
public class TreeGrowthController : MonoBehaviour
{
    public int totalTaps = 50;
    public int currentTaps;
    public ParametricTree tree;
    public UnityEvent<int> onStageChanged = new UnityEvent<int>();

    const string PrefsKey = "CrystalViz_TreeTaps";
    const float AnimDuration = 0.5f;

    float displayedG; // animated 0..1 value actually fed to the tree
    float animFrom;
    float animTo;
    float animT = 1f; // >= 1 means settled
    int lastStage = -1;

    /// <summary>Target growth from tap count (0..1).</summary>
    public float GrowthTarget => totalTaps <= 0 ? 0f : (float)currentTaps / totalTaps;

    /// <summary>Currently displayed (animated) growth (0..1).</summary>
    public float Growth01 => displayedG;

    /// <summary>0 Sprout, 1 Sapling, 2 YoungTree, 3 Mature.</summary>
    public int CurrentStage =>
        currentTaps <= 10 ? 0 :
        currentTaps <= 25 ? 1 :
        currentTaps <= 40 ? 2 : 3;

    bool initialized;

    void Awake()
    {
        Initialize();
    }

    /// <summary>
    /// Assigns the tree, restores tap progress from PlayerPrefs, and builds
    /// the mesh at the current growth. Called from Awake() in play mode; the
    /// CI screenshot path builds the scene in edit mode, where AddComponent
    /// does NOT fire Awake(), so CrystalVizBootstrap calls this explicitly
    /// after AddComponent. Idempotent: safe to call twice.
    /// </summary>
    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        tree = GetComponent<ParametricTree>();
        // DEBUG: start at 30 taps (young tree) for prototype screenshots; revert to 0 for release
        currentTaps = Mathf.Clamp(PlayerPrefs.GetInt(PrefsKey, 0), 0, totalTaps);
        displayedG = GrowthTarget;
        // DIAGNOSTIC (temporary): prove the debug default applied and the tree ref resolved.
        Debug.LogWarning($"DIAG TreeGrowthController.Initialize: currentTaps={currentTaps}, " +
            $"displayedG={displayedG:F3}, tree={(tree != null ? "ok" : "NULL")}");
        // Build the mesh here, not just in Start(): CI screenshot captures run
        // in edit mode, where Start()/Update() never execute, leaving the
        // trunk/leaf meshes empty (invisible tree).
        ApplyGrowth();
    }

    void Start()
    {
        lastStage = CurrentStage;
        ApplyGrowth();
        if (tree == null)
            Debug.LogError("TreeGrowthController: no ParametricTree on this GameObject.");
    }

    void Update()
    {
        // Touch takes priority; mouse click covers editor testing. (On mobile
        // the first touch also raises mouse-button events; the bool collapses
        // both into a single tap per frame.)
        bool tapped = Input.GetMouseButtonDown(0);
        if (!tapped && Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            tapped = true;
        if (tapped) RegisterTap();

        if (animT < 1f)
        {
            animT = Mathf.Min(1f, animT + Time.deltaTime / AnimDuration);
            displayedG = Mathf.Lerp(animFrom, animTo, Smooth(animT));
            if (tree != null) tree.SetGrowth(displayedG);
        }
    }

    /// <summary>
    /// Rebuilds the tree mesh at the currently displayed growth value.
    /// Idempotent (ParametricTree skips rebuilds for tiny changes), so it is
    /// safe to call from Awake, Start, and explicitly after AddComponent.
    /// </summary>
    public void ApplyGrowth()
    {
        if (tree != null) tree.SetGrowth(displayedG);
        lastStage = CurrentStage;
    }

    /// <summary>Advance one growth step. Safe to call from UI buttons too.</summary>
    public void RegisterTap()
    {
        if (currentTaps >= totalTaps) return;
        currentTaps++;
        PlayerPrefs.SetInt(PrefsKey, currentTaps);
        PlayerPrefs.Save();

        animFrom = displayedG;
        animTo = GrowthTarget;
        animT = 0f;

        int stage = CurrentStage;
        if (stage != lastStage)
        {
            lastStage = stage;
            onStageChanged.Invoke(stage);
        }
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);
}
