using UnityEngine;

/// <summary>
/// Time-of-day engine for CrystalViz: owns the 12 PM -> 12 AM time value,
/// drives the sun azimuth, the hard 7 PM / 6 AM day/night lighting switch,
/// and the bee<->firefly transformation through one funnel (SetTimeOfDay).
///
/// v1.0.55: the lighting SLIDER is gone — player request, removed from the
/// right edge entirely. The corner menu's day/night button is now the only
/// time control (it snaps between 12 PM and 12 AM through SetTimeOfDay).
/// Everything visual (slider strip, knob, sundial ring, touch zone, panel)
/// was deleted; the time state, the hourly detents, and the lighting/night
/// mechanism are untouched.
/// </summary>
public class SunOrbitControl : MonoBehaviour
{
    [HideInInspector] public CrystalVizBootstrap bootstrap;

    // The single time-of-day value: 0 = 12:00 PM (noon), 1 = 12:00 AM.
    float timeValue;
    float currentAzimuth = 54f;
    float targetAzimuth = 54f;

    // v1.0.51: the time runs on 13 hourly detents (12 PM .. 12 AM).
    public const int HourSteps = 12; // 12 one-hour steps -> 13 detents

    // v1.0.51: the bee<->firefly swap rides the hard day/night switch — when
    // the night factor flips, the BeeController transforms the squad.
    BeeController beeController;
    float lastNightFactor = -1f;

    void Start()
    {
        BuildForScreenshot();
        // v1.0.46: the time initializes to the device's real local time
        // (player request) — opening the app on a real evening lands in
        // moonlight with zero interaction.
        SetTimeOfDay(DeviceTimeSliderValue());
    }

    /// <summary>
    /// v1.0.48: the single funnel for time-of-day changes. Drives the sun
    /// azimuth target and the day/night lighting together from one value.
    /// (v1.0.55: the slider is gone — the day/night button calls this
    /// directly. v1.0.51: every value snaps to the nearest hourly detent.)
    /// </summary>
    public void SetTimeOfDay(float v)
    {
        v = SnapHour(v);
        timeValue = v;
        targetAzimuth = v * 360f;
        ApplyTimeOfDay(v);
    }

    /// <summary>
    /// v1.0.51: snaps a time value to the nearest hourly detent (13 stops:
    /// 12 PM at v=0 through 12 AM at v=1).
    /// </summary>
    public static float SnapHour(float v)
    {
        v = Mathf.Clamp01(v);
        return Mathf.Round(v * HourSteps) / HourSteps;
    }

    /// <summary>
    /// The current time-of-day snapped to its hourly detent.
    /// </summary>
    public float CurrentSnappedValue => SnapHour(timeValue);

    /// <summary>
    /// Resolves the time engine and sets its initial value. Called from
    /// Start at runtime; the CI screenshot tool calls it directly in edit
    /// mode (Start never runs in edit mode).
    /// </summary>
    public void BuildForScreenshot()
    {
        if (bootstrap == null) bootstrap = FindObjectOfType<CrystalVizBootstrap>();
        // The tap/swipe UI guards (TreeGrowthController, BeeController) raycast
        // through the EventSystem — the old slider UI used to create it, so
        // the time engine keeps that guarantee now that the slider is gone.
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule)).name = "EventSystem";
        }
        // v1.0.46: captures must be deterministic — CI runs at any hour, so
        // the screenshot pins to noon (full daylight) instead of the device
        // clock. The sun keeps its pleasant 54° 3/4 modeling azimuth for
        // continuity with earlier captures.
        // v1.0.48: funnel through SetTimeOfDay like every other path.
        SetTimeOfDay(0f);
    }

    /// <summary>
    /// Maps the device clock onto the 12 PM -> 12 AM range: morning maps to
    /// noon — daylight, which is the honest answer for 9 AM on a 12-hour
    /// afternoon/evening scale.
    /// </summary>
    static float DeviceTimeSliderValue()
    {
        var now = System.DateTime.Now;
        float hour = now.Hour + now.Minute / 60f + now.Second / 3600f;
        if (hour < 12f) return 0f;
        return Mathf.Clamp01((hour - 12f) / 12f);
    }

    /// <summary>
    /// v1.0.49: HARD day/night switch (player request — no gradual fade).
    /// v=0 is 12:00 PM (noon), v=1 is 12:00 AM (midnight).
    /// Daylight runs noon until 7:00 PM; dark runs 7:00 PM until midnight.
    /// Stated generally: dark when hour >= 19 OR hour < 6, daylight
    /// otherwise — so a 6:00 AM sunrise holds if the range ever extends.
    /// </summary>
    public static float NightFactor(float v)
    {
        float hour24 = 12f + v * 12f; // 12 (noon) .. 24 (midnight)
        return (hour24 >= 19f || hour24 < 6f) ? 1f : 0f;
    }

    /// <summary>
    /// v1.0.46: pushes the time-of-day into the scene lighting.
    /// v1.0.51: the bee<->firefly swap rides the hard 7 PM / 6 AM switch —
    /// when the night factor flips, the BeeController transforms the squad
    /// (every active bee becomes a firefly at night, back to a bee at day).
    /// Safe in the CI edit-mode screenshot path: the bees list is empty
    /// there until Start runs, so the calls are no-ops.
    /// </summary>
    void ApplyTimeOfDay(float v)
    {
        if (bootstrap != null) bootstrap.ApplyTimeOfDayLighting(NightFactor(v));
        float nf = NightFactor(v);
        if (lastNightFactor < 0f)
        {
            // First frame: seed the mode without transforming anyone — no
            // bees are flying yet; the spawn path applies the night form.
            if (beeController == null) beeController = FindObjectOfType<BeeController>();
            if (beeController != null) beeController.SeedNightMode(nf > 0.5f);
        }
        else if (!Mathf.Approximately(nf, lastNightFactor))
        {
            if (beeController == null) beeController = FindObjectOfType<BeeController>();
            if (beeController != null) beeController.SetNightMode(nf > 0.5f);
        }
        lastNightFactor = nf;
    }

    void Update()
    {
        if (bootstrap == null || bootstrap.sun == null) return;
        // Smooth-damped follow so the sun glides instead of snapping.
        currentAzimuth = Mathf.LerpAngle(currentAzimuth, targetAzimuth,
            1f - Mathf.Exp(-8f * Time.deltaTime));
        bootstrap.PlaceSun(currentAzimuth);
    }
}
