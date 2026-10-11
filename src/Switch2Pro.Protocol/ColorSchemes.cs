namespace Switch2Pro.Protocol;

/// <summary>
/// Farben eines Farbschemas in einem Modus (dunkel oder hell), als 0xRRGGBB. Gemeinsame Quelle für Windows
/// (<c>Theme.cs</c>) und Android, damit beide Apps gleich aussehen.
/// </summary>
public sealed record PaletteColors(
    bool Dark,
    int Window,        // Fensterhintergrund
    int Surface,       // Karten/Abschnitte
    int SurfaceHover,  // Eingabefelder, Knöpfe
    int Border,        // dezenter Rand von Karten und Abschnitten
    int Text,
    int TextMuted,
    int Canvas,        // Hintergrund der Controller-Grafik
    int ControlBorder, // kräftigerer Rand von Knöpfen, Eingabe- und Auswahlfeldern
    int Accent,        // Verlaufsanfang – Flächen mit weißer Schrift (Kontrast ≥ 4,5 : 1)
    int Accent2,       // Verlaufsende
    int Pressed);      // gedrückte Tasten in der Controller-Grafik (leuchtend)

/// <summary>Farbschema: Kennung (in <see cref="Settings.ColorScheme"/>), Name und je eine Palette für dunkel und hell.
/// <paramref name="Neon"/>: Verläufe und Leuchten; ohne (Windows) einfarbiger Akzent und ruhige Flächen.</summary>
public sealed record ColorScheme(string Id, string Name, PaletteColors DarkPalette, PaletteColors LightPalette, bool Neon = true);

public static class ColorSchemes
{
    private static PaletteColors P(bool dark, int window, int surface, int control, int border, int text, int muted, int canvas,
        int controlBorder, int accent, int accent2, int pressed) =>
        new(dark, window, surface, control, border, text, muted, canvas, controlBorder, accent, accent2, pressed);

    /// <summary>Alle Farbschemata (das erste ist der Standard).</summary>
    public static readonly ColorScheme[] All =
    [
        // Windows (Standard): Farben von Windows 11 (Fluent) – Hintergrund #202020, Karten #2B2B2B, Steuerelemente #373737,
        // Akzent im Windows-Standardblau (dunkel #4CC2FF mit schwarzer Schrift wie in Windows, hell #005FB8 mit weißer).
        new("windows", "Windows",
            P(true, 0x202020, 0x2B2B2B, 0x373737, 0x1D1D1D, 0xFFFFFF, 0xC5C5C5, 0x272727, 0x4A4A4A, 0x4CC2FF, 0x4CC2FF, 0x4CC2FF),
            P(false, 0xF3F3F3, 0xFBFBFB, 0xFFFFFF, 0xE5E5E5, 0x1B1B1B, 0x5C5C5C, 0xEEEEEE, 0xC8C8C8, 0x005FB8, 0x005FB8, 0x0078D4),
            Neon: false),
        // Neon: Farben aus dem Logo – Navy, Neon-Blau → Violett.
        new("neon", "Neon",
            P(true, 0x0E1019, 0x171A27, 0x222639, 0x2A2F45, 0xF2F4FA, 0xA0A6BE, 0x131623, 0x4A5378, 0x2B6FF2, 0x7C3AED, 0x00BEFF),
            P(false, 0xF0F2F8, 0xFCFCFF, 0xFFFFFF, 0xDCDFEA, 0x15172A, 0x4A4E64, 0xEAECF5, 0xA3AAC2, 0x1F5FD6, 0x7444E0, 0x0094E0)),
        // Aurora: Polarlicht – tiefes Blaugrün, Türkis → Grün.
        new("aurora", "Aurora",
            P(true, 0x0A1314, 0x111C1D, 0x1A292A, 0x223435, 0xEEF6F5, 0x9CB3B1, 0x0E1919, 0x416363, 0x0E7490, 0x15803D, 0x2EE6A8),
            P(false, 0xEEF5F4, 0xFBFEFD, 0xFFFFFF, 0xD4E3E1, 0x122322, 0x455A58, 0xE6EFED, 0x99B7B3, 0x0E7490, 0x15803D, 0x0BA67A)),
        // Sunset: Abendrot – warmes Pflaume, Orange → Pink.
        new("sunset", "Sunset",
            P(true, 0x140E13, 0x1E151B, 0x2A1E26, 0x352731, 0xFAF1F4, 0xBBA5AF, 0x1A1217, 0x5F4658, 0xC2410C, 0xBE185D, 0xFF8A3D),
            P(false, 0xF8F2F2, 0xFFFCFB, 0xFFFFFF, 0xEADBDC, 0x2A1519, 0x5E494E, 0xF2E8E8, 0xC2A6AA, 0xC2410C, 0xBE185D, 0xF26B1D)),
        // Joy-Con: Neonrot → Neonblau wie die Joy-Con, neutrales Anthrazit.
        new("joycon", "Joy-Con",
            P(true, 0x111113, 0x1A1A1E, 0x25252A, 0x2F2F35, 0xF4F4F6, 0xA8A8B2, 0x161619, 0x585862, 0xD7263D, 0x0A6FC2, 0xFF4B5C),
            P(false, 0xF2F2F4, 0xFDFDFE, 0xFFFFFF, 0xDEDEE3, 0x18181C, 0x4C4C55, 0xECECF0, 0xA8A8B3, 0xD7263D, 0x0A6FC2, 0xE8364A)),
    ];

    /// <summary>Schema zu einer Kennung ("neon", "aurora" …); unbekannt oder null = Standard (Windows).</summary>
    public static ColorScheme ById(string? id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];

    /// <summary>Relative Helligkeit (WCAG) einer 0xRRGGBB-Farbe, 0 = schwarz, 1 = weiß.</summary>
    public static float Luminance(int rgb)
    {
        static float L(int v) { float s = v / 255f; return s <= 0.03928f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f); }
        return 0.2126f * L((rgb >> 16) & 0xFF) + 0.7152f * L((rgb >> 8) & 0xFF) + 0.0722f * L(rgb & 0xFF);
    }

    /// <summary>Text auf Akzentflächen: schwarz auf hellem Akzent (Windows dunkel), sonst weiß (0xRRGGBB).</summary>
    public static int OnAccent(int accent) => Luminance(accent) > 0.36f ? 0x000000 : 0xFFFFFF;

    /// <summary>Mischt zwei 0xRRGGBB-Farben (t = 0 → a, 1 → b).</summary>
    public static int Blend(int a, int b, float t)
    {
        int Ch(int shift) => (int)(((a >> shift) & 0xFF) + ((((b >> shift) & 0xFF) - ((a >> shift) & 0xFF)) * t));
        return (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
    }
}
