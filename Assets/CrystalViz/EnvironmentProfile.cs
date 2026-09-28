using UnityEngine;

/// <summary>
/// v1.0.62: per-species environment profile. The diorama's shared look —
/// fog, ambient, sun, fill, night sky tint — is data now, not hardcoded
/// constants. The day/night system lerps between each profile's day and
/// night values, so lighting stays fully day/night compatible.
/// </summary>
[System.Serializable]
public struct EnvironmentProfile
{
    public string name;
    public Color fogColorDay;
    public Color fogColorNight;
    public float fogStart;
    public float fogEnd;
    public Color ambientDay;
    public Color ambientNight;
    public Color sunColorDay;
    public Color sunColorNight;
    public float sunIntensityDay;
    public float sunIntensityNight;
    public float fillIntensityDay;
    public float fillIntensityNight;
    public Color skyTintNight; // _Tint on the textured sky dome at night
    public bool waterEnabled;  // willow lakeside props on/off
    public int reedTufts;
    public float mistAlpha;
}

/// <summary>
/// v1.0.62: resolves the EnvironmentProfile for a tree species. Every
/// species except Willow returns the exact v1.0.61 grassland values, so
/// oak/pine/birch/cherry/palm render unchanged; only Willow gets the
/// lakeside.
/// </summary>
public static class EnvironmentProfiles
{
    public static EnvironmentProfile ForSpecies(ParametricTree.TreeSpecies species)
    {
        // Grassland default: the exact hardcoded values CrystalVizBootstrap
        // used before v1.0.62. Do not touch without re-verifying oak.
        var p = new EnvironmentProfile
        {
            name = "Grassland",
            fogColorDay = new Color(0.611f, 0.672f, 0.824f, 1f),
            fogColorNight = new Color(0.09f, 0.12f, 0.22f, 1f),
            fogStart = 40f,
            fogEnd = 80f,
            ambientDay = new Color(0.42f, 0.43f, 0.46f, 1f),
            ambientNight = new Color(0.15f, 0.18f, 0.27f, 1f),
            sunColorDay = new Color(1f, 0.95f, 0.88f, 1f),
            sunColorNight = new Color(0.55f, 0.68f, 1f, 1f),
            sunIntensityDay = 2.0f,
            sunIntensityNight = 0.75f,
            fillIntensityDay = 0.35f,
            fillIntensityNight = 0.2f,
            skyTintNight = new Color(0.28f, 0.34f, 0.58f, 1f),
            waterEnabled = false,
            reedTufts = 0,
            mistAlpha = 0f,
        };

        if (species == ParametricTree.TreeSpecies.Willow)
        {
            // Lakeside: cooler, denser fog that rolls off the water; the
            // lighting rig (sun/fill/ambient/sky) stays identical to the
            // grassland so day/night transitions look like the same world.
            p.name = "Lakeside";
            p.fogColorDay = new Color(0.560f, 0.628f, 0.786f, 1f);
            p.fogColorNight = new Color(0.075f, 0.105f, 0.205f, 1f);
            p.fogStart = 22f;
            p.fogEnd = 55f;
            p.waterEnabled = true;
            p.reedTufts = 140;
            p.mistAlpha = 0.22f;
        }

        return p;
    }
}
