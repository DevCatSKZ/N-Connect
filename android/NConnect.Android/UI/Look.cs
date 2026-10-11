using Android.Content;
using Android.Content.Res;
using AndroidX.AppCompat.App;
using Switch2Pro.Protocol;
using Color = Android.Graphics.Color;

namespace NConnect.Android.UI;

/// <summary>
/// Aussehen wie in N-Connect für Windows: dieselben Farbschemata (Windows, Neon, Aurora, Sunset, Joy-Con, aus
/// <see cref="ColorSchemes"/>) und dieselbe Wahl Dunkel/Hell/Wie System (<see cref="Settings.Theme"/>,
/// <see cref="Settings.ColorScheme"/> – gleiche Einstellungsdatei).
/// </summary>
public static class Look
{
    public static readonly (string Id, string Label)[] Modes = [("system", "Wie das System"), ("dark", "Dunkel"), ("light", "Hell")];

    /// <summary>Gespeicherter Modus; ohne Einstellung folgt Android dem System (unter Windows: dunkel).</summary>
    public static string Mode => AppPrefs.Settings.Theme is "dark" or "light" or "system" ? AppPrefs.Settings.Theme! : "system";

    public static ColorScheme Scheme => ColorSchemes.ById(AppPrefs.Settings.ColorScheme);

    public static bool IsDark(Context ctx) => Mode switch
    {
        "dark" => true,
        "light" => false,
        _ => (ctx.Resources!.Configuration!.UiMode & UiMode.NightMask) == UiMode.NightYes,
    };

    public static PaletteColors P(Context ctx) => IsDark(ctx) ? Scheme.DarkPalette : Scheme.LightPalette;

    /// <summary>Material-Komponenten (Dialoge, Menüs) dem gewählten Modus folgen lassen. Beim App-Start aufrufen.</summary>
    public static void ApplyNightMode() => AppCompatDelegate.DefaultNightMode = Mode switch
    {
        "dark" => AppCompatDelegate.ModeNightYes,
        "light" => AppCompatDelegate.ModeNightNo,
        _ => AppCompatDelegate.ModeNightFollowSystem,
    };

    public static Color C(int rgb) => new(unchecked((int)0xFF000000) | rgb);
    public static Color C(int rgb, int alpha) => new((alpha << 24) | rgb);

    /// <summary>Akzent für Linien, Symbole und Text: bei Neon im dunklen Modus aufgehellt (wie Theme.AccentLine unter Windows).</summary>
    public static int AccentLine(Context ctx)
    {
        var p = P(ctx);
        return p.Dark && Scheme.Neon ? ColorSchemes.Blend(p.Accent, 0xFFFFFF, 0.28f) : p.Accent;
    }

    public static int Accent2Line(Context ctx)
    {
        var p = P(ctx);
        return p.Dark && Scheme.Neon ? ColorSchemes.Blend(p.Accent2, 0xFFFFFF, 0.28f) : p.Accent2;
    }

    public static int OnAccent(Context ctx) => ColorSchemes.OnAccent(P(ctx).Accent);

    /// <summary>Zustandsabhängige Farbe (z. B. Schalter an/aus).</summary>
    public static ColorStateList States(int checkedRgb, int uncheckedRgb) => new(
        [[global::Android.Resource.Attribute.StateChecked], []],
        [C(checkedRgb).ToArgb(), C(uncheckedRgb).ToArgb()]);

    public static ColorStateList Solid(int rgb) => ColorStateList.ValueOf(C(rgb));
}
