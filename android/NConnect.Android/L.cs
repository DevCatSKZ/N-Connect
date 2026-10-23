namespace NConnect.Android;

/// <summary>
/// Übersetzung der Oberfläche – wie in SKZ Connect für Windows: Die App ist auf Deutsch geschrieben, sichtbare Texte
/// werden über Tabellen in die Sprache des Geräts übersetzt (12 Sprachen). Fehlt ein Text, greift Englisch, zuletzt
/// Deutsch. Feste Texte über <see cref="T"/>, Texte mit Werten über Vorlagen und <see cref="F"/>.
/// Die Tabellen (L.Texts.*.cs) erzeugt android/i18n/generate.py – nicht von Hand bearbeiten.
/// </summary>
public static partial class L
{
    public static readonly string[] Supported = ["de", "en", "es", "fr", "it", "pt", "nl", "pl", "ru", "ja", "zh", "ko"];

    private static string? _lang;

    /// <summary>Aktive Sprache (ISO 639-1) – nach der Systemsprache des Geräts.</summary>
    public static string Lang
    {
        get
        {
            if (_lang is not null)
                return _lang;
            string code = (Java.Util.Locale.Default.Language ?? "en").ToLowerInvariant();
            return _lang = Supported.Contains(code) ? code : "en";
        }
    }

    /// <summary>Nur zum Testen: Sprache erzwingen.</summary>
    public static void Force(string? code) => _lang = code is not null && Supported.Contains(code) ? code : null;

    private static Dictionary<string, string>? Table(string code) => code switch
    {
        "en" => En,
        "es" => Es,
        "fr" => Fr,
        "it" => It,
        "pt" => Pt,
        "nl" => Nl,
        "pl" => Pl,
        "ru" => Ru,
        "ja" => Ja,
        "zh" => Zh,
        "ko" => Ko,
        _ => null,
    };

    /// <summary>Deutschen Text übersetzen (unbekannte Texte bleiben unverändert).</summary>
    public static string T(string? german)
    {
        if (string.IsNullOrEmpty(german) || Lang == "de")
            return german ?? "";
        if (Table(Lang) is { } table && table.TryGetValue(german, out var text))
            return text;
        return En.TryGetValue(german, out var en) ? en : german;
    }

    /// <summary>Vorlage übersetzen und füllen, z. B. F("Spieler {0} · {1}", 1, name).</summary>
    public static string F(string germanTemplate, params object?[] args) => string.Format(T(germanTemplate), args);
}
