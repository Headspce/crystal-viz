using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v1.0.47: the shared "meadow glass" material language for CrystalViz UI,
/// distilled from the top-10-games HUD research (principles 3, 4, 11):
/// chrome that feels grown from the meadow — deep forest-green translucent
/// surfaces, warm-white numerals, sage supporting labels, and a single gold
/// hairline motif tying every corner together. All sprites are procedural
/// (no image assets); colors are baked so Images stay plain white.
/// </summary>
public static class MeadowGlassUI
{
    // Palette — sampled to sit on the meadow, not on top of it.
    public static readonly Color GlassDeep = new Color(0.075f, 0.170f, 0.100f, 0.88f);
    public static readonly Color WarmWhite = new Color(0.96f, 0.94f, 0.86f, 1f);
    public static readonly Color Sage = new Color(0.62f, 0.72f, 0.55f, 1f);
    public static readonly Color Gold = new Color(1.00f, 0.78f, 0.22f, 1f);
    public static readonly Color GoldSoft = new Color(1.00f, 0.78f, 0.22f, 0.85f);

    /// <summary>
    /// Circular meadow-glass disc with a baked gold hairline ring near the
    /// rim: the "dewdrop". Used by the corner menu (arrow + sub-buttons).
    /// </summary>
    public static Sprite MakeGlassDisc(int size)
    {
        const float ringPx = 4f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[size * size];
        float r = size / 2f;
        float midR = r - 2f - ringPx / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - r + 0.5f;
                float dy = y - r + 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // Soft outer edge.
                float edge = 1f - Mathf.SmoothStep(r - 2.5f, r - 0.5f, d);
                // Gentle top-light: the glass feels lit from the sky.
                float light = 1f + 0.10f * Mathf.Clamp01(-dy / r);
                Color c = new Color(
                    Mathf.Clamp01(GlassDeep.r * light),
                    Mathf.Clamp01(GlassDeep.g * light),
                    Mathf.Clamp01(GlassDeep.b * light),
                    GlassDeep.a);
                // Gold hairline ring.
                float ring = 1f - Mathf.Clamp01((Mathf.Abs(d - midR) - ringPx / 2f) / 1.5f);
                c = Color.Lerp(c, new Color(Gold.r, Gold.g, Gold.b, 0.95f), ring);
                c.a *= edge;
                pixels[y * size + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>
    /// Rounded-rect (pill) meadow-glass backing with a baked gold hairline
    /// following the capsule edge. Returned as a 9-sliced sprite (borders =
    /// half height) so it stretches to any size without distorting the ring.
    /// Used by the time pill, HUD counter pills, and toasts.
    /// </summary>
    public static Sprite MakeGlassPill(int w, int h)
    {
        const float ringPx = 3f;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[w * h];
        float r = h / 2f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Distance to the capsule edge (horizontal stadium SDF).
                float cx = Mathf.Clamp(x + 0.5f, r, w - r);
                float dx = (x + 0.5f) - cx;
                float dy = (y + 0.5f) - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float edge = 1f - Mathf.SmoothStep(r - 2f, r - 0.5f, d);
                float light = 1f + 0.08f * Mathf.Clamp01(-dy / r);
                Color c = new Color(
                    Mathf.Clamp01(GlassDeep.r * light),
                    Mathf.Clamp01(GlassDeep.g * light),
                    Mathf.Clamp01(GlassDeep.b * light),
                    0.92f);
                float ring = 1f - Mathf.Clamp01(
                    (Mathf.Abs(d - (r - 2f - ringPx / 2f)) - ringPx / 2f) / 1.5f);
                c = Color.Lerp(c, new Color(Gold.r, Gold.g, Gold.b, 0.90f), ring);
                c.a *= edge;
                pixels[y * w + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        float b = r;
        return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f),
            100f, 0u, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
    }

    /// <summary>
    /// v1.0.50: sun-and-moon paired glyph — the lighting toggle symbol. Left
    /// half: a golden sun (disc + 8 rays); right half: a pale crescent moon.
    /// Used by the corner menu's star button and the day/night button atop
    /// the lighting slider. Procedural (no image assets).
    /// </summary>
    public static Texture2D MakeSunMoonIcon(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var sunGold = new Color(1.00f, 0.80f, 0.16f);
        var moonSilver = new Color(0.86f, 0.91f, 0.98f);
        float S = size;
        Vector2 sunC = new Vector2(S * 0.34f, S * 0.5f);
        float sunR = S * 0.15f;
        Vector2 moonC = new Vector2(S * 0.70f, S * 0.5f);
        float moonR = S * 0.18f;
        Vector2 cutC = new Vector2(S * 0.79f, S * 0.5f);
        float cutR = S * 0.155f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                // Keep the two halves cleanly paired: sun clipped to the
                // left half, moon to the right half.
                // NOTE: using local SStep instead of Mathf.SmoothStep —
                // the latter misbehaved in this Unity build (returned `to`).
                float leftClip = 1f - SStep(S * 0.50f, S * 0.54f, p.x);
                float rightClip = SStep(S * 0.46f, S * 0.50f, p.x);
                // Sun disc.
                float dSun = Vector2.Distance(p, sunC);
                float sunA = (1f - SStep(sunR - 1.5f, sunR + 1.5f, dSun)) * leftClip;
                // Sun rays: 8 blades fanning from just outside the disc.
                float rayA = 0f;
                Vector2 rel = p - sunC;
                float rd = rel.magnitude;
                if (rd > sunR + 2f && rd < sunR + S * 0.12f)
                {
                    float ang = Mathf.Atan2(rel.y, rel.x); // -pi..pi
                    float sector = Mathf.PI / 4f; // fold into one 45° sector
                    float aIn = Mathf.Abs(Mathf.Repeat(ang + sector / 2f, sector) - sector / 2f);
                    float halfW = 0.10f + 0.06f * ((rd - sunR) / (S * 0.12f));
                    float inRay = 1f - SStep(halfW - 0.03f, halfW + 0.03f, aIn);
                    float band = SStep(sunR + 1f, sunR + 5f, rd) *
                                 (1f - SStep(sunR + S * 0.10f, sunR + S * 0.12f, rd));
                    rayA = inRay * band * leftClip;
                }
                // Crescent moon: disc minus an offset cutout disc.
                float dMoon = Vector2.Distance(p, moonC);
                float moonDisc = 1f - SStep(moonR - 1.5f, moonR + 1.5f, dMoon);
                float dCut = Vector2.Distance(p, cutC);
                float cut = 1f - SStep(cutR - 1.5f, cutR + 1.5f, dCut);
                float moonA = moonDisc * (1f - cut) * rightClip;
                float a = Mathf.Max(Mathf.Max(sunA, rayA), moonA);
                Color col = (moonA >= sunA && moonA >= rayA) ? moonSilver : sunGold;
                tex.SetPixel(x, y, new Color(col.r, col.g, col.b, a));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// Local smoothstep (0..1). Used by MakeSunMoonIcon instead of
    /// Mathf.SmoothStep, which returned its `to` argument in this build.
    /// </summary>
    static float SStep(float from, float to, float t)
    {
        float x = (t - from) / (to - from);
        x = x < 0f ? 0f : (x > 1f ? 1f : x);
        return x * x * (3f - 2f * x);
    }

    /// <summary>
    /// v1.0.55: warm-white light-bulb glyph — the new lighting-panel toggle
    /// symbol for the corner menu (replaces the old infinity reset mark).
    /// Round glass bulb + short rays + a small screw base, all in the
    /// menu's warm-white ink. Procedural (no image assets).
    /// </summary>
    public static Texture2D MakeLightBulbIcon(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var ink = new Color(0.96f, 0.94f, 0.86f);
        float S = size;
        Vector2 bulbC = new Vector2(S * 0.50f, S * 0.44f);
        float bulbR = S * 0.26f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                // Bulb glass: soft-edged disc.
                float dBulb = Vector2.Distance(p, bulbC);
                float bulbA = 1f - SStep(bulbR - 1.5f, bulbR + 1.5f, dBulb);
                // Rays: 6 blades fanning around the upper bulb (the base
                // occupies the bottom, so rays skip that sector).
                float rayA = 0f;
                Vector2 rel = p - bulbC;
                float rd = rel.magnitude;
                if (rd > bulbR + 2f && rd < bulbR + S * 0.13f)
                {
                    float ang = Mathf.Atan2(rel.y, rel.x); // -pi..pi
                    // Skip the bottom ~100° sector where the base sits.
                    if (ang > -Mathf.PI * 0.72f && ang < -Mathf.PI * 0.28f)
                    {
                        float sector = Mathf.PI / 3f; // fold into one 60° sector
                        float aIn = Mathf.Abs(Mathf.Repeat(ang + sector / 2f, sector) - sector / 2f);
                        float halfW = 0.09f + 0.05f * ((rd - bulbR) / (S * 0.13f));
                        float inRay = 1f - SStep(halfW - 0.03f, halfW + 0.03f, aIn);
                        float band = SStep(bulbR + 1f, bulbR + 5f, rd) *
                                     (1f - SStep(bulbR + S * 0.11f, bulbR + S * 0.13f, rd));
                        rayA = inRay * band;
                    }
                }
                // Screw base: rounded trapezoid tucked under the bulb.
                // (Texture space is y-up: the base sits BELOW the bulb.)
                float baseA = 0f;
                float by0 = S * 0.04f, by1 = S * 0.19f;
                if (p.y > by0 - 2f && p.y < by1 + 2f)
                {
                    float t = (p.y - by0) / (by1 - by0); // 0 bottom -> 1 top
                    float halfW = S * (0.10f + 0.03f * t);
                    float dx = Mathf.Abs(p.x - S * 0.50f);
                    float edge = 1f - SStep(halfW - 1.5f, halfW + 1.5f, dx);
                    float cap = SStep(by0 - 2f, by0 + 3f, p.y) *
                                (1f - SStep(by1 - 3f, by1 + 2f, p.y));
                    baseA = edge * cap;
                }
                float a = Mathf.Max(Mathf.Max(bulbA, rayA), baseA);
                tex.SetPixel(x, y, new Color(ink.r, ink.g, ink.b, a));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// v1.0.55: state-aware day/night glyph — the same sun + crescent-moon
    /// pairing as MakeSunMoonIcon, but the ACTIVE half renders full-strength
    /// while the inactive half drops to a dim ghost, so the corner menu's
    /// day/night button always reads the current lighting state.
    /// </summary>
    public static Texture2D MakeDayNightIcon(int size, bool nightMode)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var sunGold = new Color(1.00f, 0.80f, 0.16f);
        var moonSilver = new Color(0.86f, 0.91f, 0.98f);
        float sunK = nightMode ? 0.28f : 1f;
        float moonK = nightMode ? 1f : 0.28f;
        float S = size;
        Vector2 sunC = new Vector2(S * 0.34f, S * 0.5f);
        float sunR = S * 0.15f;
        Vector2 moonC = new Vector2(S * 0.70f, S * 0.5f);
        float moonR = S * 0.18f;
        Vector2 cutC = new Vector2(S * 0.79f, S * 0.5f);
        float cutR = S * 0.155f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float leftClip = 1f - SStep(S * 0.50f, S * 0.54f, p.x);
                float rightClip = SStep(S * 0.46f, S * 0.50f, p.x);
                float dSun = Vector2.Distance(p, sunC);
                float sunA = (1f - SStep(sunR - 1.5f, sunR + 1.5f, dSun)) * leftClip * sunK;
                float rayA = 0f;
                Vector2 rel = p - sunC;
                float rd = rel.magnitude;
                if (rd > sunR + 2f && rd < sunR + S * 0.12f)
                {
                    float ang = Mathf.Atan2(rel.y, rel.x);
                    float sector = Mathf.PI / 4f;
                    float aIn = Mathf.Abs(Mathf.Repeat(ang + sector / 2f, sector) - sector / 2f);
                    float halfW = 0.10f + 0.06f * ((rd - sunR) / (S * 0.12f));
                    float inRay = 1f - SStep(halfW - 0.03f, halfW + 0.03f, aIn);
                    float band = SStep(sunR + 1f, sunR + 5f, rd) *
                                 (1f - SStep(sunR + S * 0.10f, sunR + S * 0.12f, rd));
                    rayA = inRay * band * leftClip * sunK;
                }
                float dMoon = Vector2.Distance(p, moonC);
                float moonDisc = 1f - SStep(moonR - 1.5f, moonR + 1.5f, dMoon);
                float dCut = Vector2.Distance(p, cutC);
                float cut = 1f - SStep(cutR - 1.5f, cutR + 1.5f, dCut);
                float moonA = moonDisc * (1f - cut) * rightClip * moonK;
                float a = Mathf.Max(Mathf.Max(sunA, rayA), moonA);
                Color col = (moonA >= sunA && moonA >= rayA) ? moonSilver : sunGold;
                tex.SetPixel(x, y, new Color(col.r, col.g, col.b, a));
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// Transparent sprite carrying only a gold ring band — for halos and
    /// feedback pulses (sun-thumb sundial ring, menu tap pulse).
    /// </summary>
    public static Sprite MakeGoldRing(int size, float ringPx)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[size * size];
        float r = size / 2f;
        float midR = r - 2f - ringPx / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - r + 0.5f;
                float dy = y - r + 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = 1f - Mathf.Clamp01((Mathf.Abs(d - midR) - ringPx / 2f) / 1.5f);
                // Faint outer glow so the ring feels lit, not pasted on.
                float glow = d > midR
                    ? Mathf.Pow(Mathf.Clamp01(1f - (d - midR) / (ringPx * 3f)), 2f) * 0.25f
                    : 0f;
                float a = Mathf.Clamp01(ring * 0.95f + glow);
                pixels[y * size + x] = new Color(Gold.r, Gold.g, Gold.b, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>
    /// Star-shaped meadow-glass button: a 5-pointed star with deep-green
    /// fill and a thin gold outline, for the corner menu's lighting toggle
    /// (the "star button" containing the sun-and-moon glyph).
    /// </summary>
    public static Sprite MakeStarDisc(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float r = size / 2f;
        Vector2 c = new Vector2(r, r);
        float outerR = r - 4f;
        float innerR = outerR * 0.45f;
        // Star vertices (5 points, starting at top, going clockwise).
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float rad = (i % 2 == 0) ? outerR : innerR;
            float ang = -Mathf.PI / 2f + i * Mathf.PI / 5f;
            pts[i] = c + new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
        }
        // Slightly larger star for the gold outline.
        var outlinePts = new Vector2[10];
        float outlineOuter = outerR + 3f;
        float outlineInner = innerR + 3f;
        for (int i = 0; i < 10; i++)
        {
            float rad = (i % 2 == 0) ? outlineOuter : outlineInner;
            float ang = -Mathf.PI / 2f + i * Mathf.PI / 5f;
            outlinePts[i] = c + new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
        }
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                bool inOutline = PointInPolygon(p, outlinePts);
                bool inStar = PointInPolygon(p, pts);
                Color col = new Color(0f, 0f, 0f, 0f);
                if (inOutline)
                {
                    if (inStar)
                    {
                        // Deep green fill with gentle top-light.
                        float dy = y - r + 0.5f;
                        float light = 1f + 0.10f * Mathf.Clamp01(-dy / r);
                        col = new Color(
                            Mathf.Clamp01(GlassDeep.r * light),
                            Mathf.Clamp01(GlassDeep.g * light),
                            Mathf.Clamp01(GlassDeep.b * light),
                            GlassDeep.a);
                    }
                    else
                    {
                        // Gold outline.
                        col = new Color(Gold.r, Gold.g, Gold.b, 0.95f);
                    }
                }
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>
    /// Even-odd point-in-polygon test.
    /// </summary>
    static bool PointInPolygon(Vector2 p, Vector2[] pts)
    {
        bool inside = false;
        int n = pts.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector2 pi = pts[i], pj = pts[j];
            if (((pi.y > p.y) != (pj.y > p.y)) &&
                (p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y) + pi.x))
                inside = !inside;
        }
        return inside;
    }
}
