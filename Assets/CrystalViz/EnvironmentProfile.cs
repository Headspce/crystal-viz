using UnityEngine;

/// <summary>
/// v1.0.62: per-species environment profile. The diorama's shared look —
/// fog, ambient, sun, fill, night sky tint — is data now, not hardcoded
/// constants. The day/night system lerps between each profile's day and
/// night values, so lighting stays fully day/night compatible.
///
/// v1.0.63: five bespoke environments join the willow lakeside. The profile
/// now also carries the ground tint, grass gradient, and a generalized pond
/// (center + radius; willow's lakeside was the first, cherry's garden pond
/// and palm's turquoise shallows follow the same path).
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
    // v1.0.63: generalized pond. Replaces the willow-only waterEnabled flag;
    // the grass/vegetation clip follows pondCenter/pondRadius for whichever
    // species has pondEnabled (willow lakeside, cherry garden pond, palm
    // shallows). Grass is cleared to pondRadius + PondRim.
    public bool pondEnabled;
    public Vector3 pondCenter;
    public float pondRadius;
    public int reedTufts;      // willow only
    public float mistAlpha;    // willow only
    // v1.0.63: ground + grass tints per species. The grassland defaults are
    // the exact legacy constants — every meadow species renders as before
    // unless its profile overrides them.
    public Color groundColor;     // MeadowGround _BaseColor
    public Color grassRootColor;  // StylizedGrass _RootColor
    public Color grassTipColor;   // StylizedGrass _TipColor
    public Color hillRootColor;   // horizon-hill StylizedGrass _RootColor
    public Color hillTipColor;    // horizon-hill StylizedGrass _TipColor
}

/// <summary>
/// v1.0.62: resolves the EnvironmentProfile for a tree species.
/// v1.0.63: all six species now have bespoke environments; the grassland
/// defaults survive as the shared base every profile starts from.
/// </summary>
public static class EnvironmentProfiles
{
    // Grass blades are suppressed inside pondRadius + this rim band.
    public const float PondRim = 0.25f;

    public static EnvironmentProfile ForSpecies(ParametricTree.TreeSpecies species)
    {
        // Grassland default: the exact hardcoded values CrystalVizBootstrap
        // used before v1.0.62. Oak inherits this almost unchanged (it is the
        // meadow's native tree); do not touch without re-verifying oak.
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
            pondEnabled = false,
            pondCenter = Vector3.zero,
            pondRadius = 0f,
            reedTufts = 0,
            mistAlpha = 0f,
            groundColor = new Color(0.24f, 0.45f, 0.17f, 1f),
            grassRootColor = new Color(0.15f, 0.34f, 0.11f, 1f),
            grassTipColor = new Color(0.58f, 0.82f, 0.26f, 1f),
            hillRootColor = new Color(0.11f, 0.29f, 0.10f, 1f),
            hillTipColor = new Color(0.46f, 0.68f, 0.22f, 1f),
        };

        switch (species)
        {
            case ParametricTree.TreeSpecies.Broadleaf: // Oak
                // Ancient meadow: the grassland the oak has always stood in,
                // warmed into late-afternoon gold. The existing glowing
                // fireflies feel native here at night.
                p.name = "Ancient Meadow";
                p.sunColorDay = new Color(1f, 0.92f, 0.78f, 1f);
                p.sunIntensityDay = 2.15f;
                p.fogColorDay = new Color(0.635f, 0.675f, 0.805f, 1f);
                p.ambientDay = new Color(0.44f, 0.43f, 0.44f, 1f);
                break;

            case ParametricTree.TreeSpecies.Pine:
                // Alpine clearing: cool crisp light, snow-dusted ground and
                // frosted grass tips, distant peaks dissolving into the haze.
                p.name = "Alpine Clearing";
                p.fogColorDay = new Color(0.68f, 0.74f, 0.85f, 1f);
                p.fogColorNight = new Color(0.08f, 0.11f, 0.20f, 1f);
                p.fogStart = 45f;
                p.fogEnd = 95f;
                p.ambientDay = new Color(0.40f, 0.44f, 0.50f, 1f);
                p.ambientNight = new Color(0.14f, 0.17f, 0.26f, 1f);
                p.sunColorDay = new Color(0.92f, 0.96f, 1f, 1f);
                p.sunIntensityDay = 1.9f;
                p.groundColor = new Color(0.42f, 0.47f, 0.50f, 1f);
                p.grassRootColor = new Color(0.20f, 0.30f, 0.26f, 1f);
                p.grassTipColor = new Color(0.62f, 0.70f, 0.64f, 1f);
                p.hillRootColor = new Color(0.20f, 0.30f, 0.32f, 1f);
                p.hillTipColor = new Color(0.68f, 0.74f, 0.78f, 1f);
                break;

            case ParametricTree.TreeSpecies.Birch:
                // Birch grove: soft green-gold dappled light, mossy floor,
                // airy and elegant.
                p.name = "Birch Grove";
                p.fogColorDay = new Color(0.64f, 0.69f, 0.79f, 1f);
                p.sunColorDay = new Color(1f, 0.95f, 0.84f, 1f);
                p.sunIntensityDay = 2.0f;
                p.ambientDay = new Color(0.44f, 0.45f, 0.44f, 1f);
                p.groundColor = new Color(0.26f, 0.44f, 0.20f, 1f);
                p.grassRootColor = new Color(0.16f, 0.36f, 0.14f, 1f);
                p.grassTipColor = new Color(0.60f, 0.80f, 0.30f, 1f);
                p.hillRootColor = new Color(0.14f, 0.32f, 0.12f, 1f);
                p.hillTipColor = new Color(0.50f, 0.68f, 0.26f, 1f);
                break;

            case ParametricTree.TreeSpecies.Willow:
                // Lakeside: cooler, denser fog that rolls off the water; the
                // lighting rig (sun/fill/ambient/sky) stays close to the
                // grassland so day/night transitions look like the same world.
                p.name = "Lakeside";
                p.fogColorDay = new Color(0.560f, 0.628f, 0.786f, 1f);
                p.fogColorNight = new Color(0.075f, 0.105f, 0.205f, 1f);
                p.fogStart = 22f;
                p.fogEnd = 55f;
                p.pondEnabled = true;
                p.pondCenter = new Vector3(0f, 0f, -4.5f);
                p.pondRadius = 3.5f;
                p.reedTufts = 140;
                p.mistAlpha = 0.22f;
                break;

            case ParametricTree.TreeSpecies.Cherry:
                // Sakura garden: soft pink-gold light, a still garden pond,
                // petals always on the breeze.
                p.name = "Sakura Garden";
                p.fogColorDay = new Color(0.67f, 0.66f, 0.79f, 1f);
                p.fogColorNight = new Color(0.10f, 0.11f, 0.21f, 1f);
                p.sunColorDay = new Color(1f, 0.93f, 0.88f, 1f);
                p.sunIntensityDay = 2.0f;
                p.ambientDay = new Color(0.44f, 0.42f, 0.46f, 1f);
                p.pondEnabled = true;
                p.pondCenter = new Vector3(-1.4f, 0f, -3.0f);
                p.pondRadius = 1.6f;
                p.groundColor = new Color(0.22f, 0.40f, 0.18f, 1f);
                p.grassRootColor = new Color(0.14f, 0.32f, 0.12f, 1f);
                p.grassTipColor = new Color(0.55f, 0.76f, 0.28f, 1f);
                p.hillRootColor = new Color(0.13f, 0.30f, 0.11f, 1f);
                p.hillTipColor = new Color(0.48f, 0.66f, 0.24f, 1f);
                break;

            case ParametricTree.TreeSpecies.Palm:
                // Tropical shore: warm bright light, sand underfoot,
                // turquoise shallows lapping at the frame's edge.
                p.name = "Tropical Shore";
                p.fogColorDay = new Color(0.68f, 0.72f, 0.82f, 1f);
                p.sunColorDay = new Color(1f, 0.96f, 0.88f, 1f);
                p.sunIntensityDay = 2.3f;
                p.ambientDay = new Color(0.46f, 0.45f, 0.44f, 1f);
                p.pondEnabled = true;
                p.pondCenter = new Vector3(4.2f, 0f, -3.8f);
                p.pondRadius = 4.2f;
                p.groundColor = new Color(0.52f, 0.46f, 0.30f, 1f);
                p.grassRootColor = new Color(0.30f, 0.34f, 0.16f, 1f);
                p.grassTipColor = new Color(0.62f, 0.64f, 0.34f, 1f);
                p.hillRootColor = new Color(0.32f, 0.34f, 0.20f, 1f);
                p.hillTipColor = new Color(0.58f, 0.58f, 0.34f, 1f);
                break;
        }

        return p;
    }

    /// <summary>
    /// v1.0.63: grass/vegetation clear radius for a profile's pond
    /// (water disc + shore rim band).
    /// </summary>
    public static float PondClearRadius(EnvironmentProfile p)
    {
        return p.pondEnabled ? p.pondRadius + PondRim : 0f;
    }
}
