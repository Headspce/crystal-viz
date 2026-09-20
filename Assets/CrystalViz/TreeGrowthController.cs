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
///
/// Test loop: the reset button (TreeResetButton) calls ResetToSprout(), which
/// drops the tree back to the true zero-tap sprout with the tap counter at
/// zero, so the sprout -> mature growth can be re-tapped perpetually. The
/// persisted "sapling start" PlayerPrefs flag ("CrystalViz_TreeTaps_SaplingStart")
/// is cleared by the reset, so post-reset growth follows the normal 0-50
/// thresholds exactly like a fresh start.
/// </summary>
public class TreeGrowthController : MonoBehaviour
{
    public int totalTaps = 50;
    public int currentTaps;
    public ParametricTree tree;
    public UnityEvent<int> onStageChanged = new UnityEvent<int>();
    /// <summary>
    /// Screen rect of the reset button, set by TreeResetButton. Taps landing
    /// inside it are swallowed so pressing reset never also grows the tree.
    /// </summary>
    public RectTransform resetButtonRect;

    const string PrefsKey = "CrystalViz_TreeTaps";
    const string SaplingStartKey = "CrystalViz_TreeTaps_SaplingStart";
    const float AnimDuration = 0.5f;
    /// <summary>Growth value where the sapling stage begins (11 taps / 50).</summary>
    const float SaplingGrowth = 11f / 50f;

    /// <summary>Legacy "sapling start" mode flag (0 taps = sapling). The reset
    /// button clears it, so post-reset growth is the normal 0-50 progression.</summary>
    public bool saplingStart;

    float displayedG; // animated 0..1 value actually fed to the tree
    float animFrom;
    float animTo;
    float animT = 1f; // >= 1 means settled
    int lastStage = -1;

    /// <summary>
    /// Target growth from tap count (0..1). In sapling-start mode 0 taps maps
    /// to the sapling geometry instead of the sprout.
    /// </summary>
    public float GrowthTarget
    {
        get
        {
            if (totalTaps <= 0) return 0f;
            float t = (float)currentTaps / totalTaps;
            return saplingStart ? Mathf.Lerp(SaplingGrowth, 1f, t) : t;
        }
    }

    /// <summary>Currently displayed (animated) growth (0..1).</summary>
    public float Growth01 => displayedG;

    /// <summary>
    /// 0 Sprout, 1 Sapling, 2 YoungTree, 3 Mature. In sapling-start mode the
    /// sprout stage is skipped: 0-24 Sapling, 25-39 YoungTree, 40-50 Mature.
    /// </summary>
    public int CurrentStage =>
        saplingStart
            ? (currentTaps <= 24 ? 1 : currentTaps <= 39 ? 2 : 3)
            : (currentTaps <= 10 ? 0 :
               currentTaps <= 25 ? 1 :
               currentTaps <= 40 ? 2 : 3);

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
        currentTaps = Mathf.Clamp(PlayerPrefs.GetInt(PrefsKey, 0), 0, totalTaps);
        saplingStart = PlayerPrefs.GetInt(SaplingStartKey, 0) == 1;
        displayedG = GrowthTarget;
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
        Vector2 tapPos = Input.mousePosition;
        if (!tapped && Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            tapped = true;
            tapPos = Input.GetTouch(0).position;
        }
        // A press on the reset button must not also grow the tree.
        if (tapped && !IsOnResetButton(tapPos)) RegisterTap();

        if (animT < 1f)
        {
            animT = Mathf.Min(1f, animT + Time.deltaTime / AnimDuration);
            displayedG = Mathf.Lerp(animFrom, animTo, Smooth(animT));
            if (tree != null) tree.SetGrowth(displayedG);
        }
    }

    /// <summary>True when the given screen position is inside the reset button.</summary>
    bool IsOnResetButton(Vector2 screenPos)
    {
        if (resetButtonRect == null) return false;
        // Null camera is correct here: the button lives on a
        // ScreenSpaceOverlay canvas.
        return RectTransformUtility.RectangleContainsScreenPoint(resetButtonRect, screenPos, null);
    }

    /// <summary>
    /// Test-loop reset (reset button): the tree snaps back to the true
    /// zero-tap sprout and the tap counter returns to zero, so the full
    /// sprout -> mature growth can be re-tapped perpetually. Snaps instantly
    /// (no grow animation) so the test loop stays tight. Persists like
    /// normal taps. Safe to call from UI buttons too.
    /// </summary>
    public void ResetToSprout()
    {
        currentTaps = 0;
        saplingStart = false;
        PlayerPrefs.SetInt(PrefsKey, 0);
        PlayerPrefs.SetInt(SaplingStartKey, 0);
        PlayerPrefs.Save();

        displayedG = GrowthTarget; // sprout geometry (growth 0)
        animT = 1f; // snap, don't animate
        if (tree != null) tree.SetGrowth(displayedG);

        int stage = CurrentStage;
        if (stage != lastStage)
        {
            lastStage = stage;
            onStageChanged.Invoke(stage);
        }
        else
        {
            lastStage = stage;
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
