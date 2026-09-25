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
                float leftClip = 1f - Mathf.SmoothStep(S * 0.50f, S * 0.54f, p.x);
                float rightClip = Mathf.SmoothStep(S * 0.46f, S * 0.50f, p.x);
                // Sun disc.
                float dSun = Vector2.Distance(p, sunC);
                float sunA = (1f - Mathf.SmoothStep(sunR - 1.5f, sunR + 1.5f, dSun)) * leftClip;
                // Sun rays: 8 blades fanning from just outside the disc.
                float rayA = 0f;
                Vector2 rel = p - sunC;
                float rd = rel.magnitude;
                if (rd > sunR + 2f && rd < sunR + S * 0.12f)
                {
                    float ang = Mathf.Atan2(rel.y, rel.x); // -pi..pi
                    float sector = Mathf.PI / 4f; // fold into one 45° sector
                    float a = Mathf.Abs(Mathf.Repeat(ang + sector / 2f, sector) - sector / 2f);
                    float halfW = 0.10f + 0.06f * ((rd - sunR) / (S * 0.12f));
                    float inRay = 1f - Mathf.SmoothStep(halfW - 0.03f, halfW + 0.03f, a);
                    float band = Mathf.SmoothStep(sunR + 1f, sunR + 5f, rd) *
                                 (1f - Mathf.SmoothStep(sunR + S * 0.10f, sunR + S * 0.12f, rd));
                    rayA = inRay * band * leftClip;
                }
                // Crescent moon: disc minus an offset cutout disc.
                float dMoon = Vector2.Distance(p, moonC);
                float moonDisc = 1f - Mathf.SmoothStep(moonR - 1.5f, moonR + 1.5f, dMoon);
                float dCut = Vector2.Distance(p, cutC);
                float cut = 1f - Mathf.SmoothStep(cutR - 1.5f, cutR + 1.5f, dCut);
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
}
