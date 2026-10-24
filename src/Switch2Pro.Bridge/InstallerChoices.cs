using Microsoft.Win32;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Sprache und Darstellung aus dem Installer: Das Setup startet SKZ Connect nach der Installation im Kontext des
/// angemeldeten Benutzers mit „--setup-theme=dark|light|system“ und, falls dort bewusst eine andere Sprache als der
/// Vorschlag des Setups gewählt wurde (ChosenLangCode), „--setup-lang=xx“ (beim stillen Update ohne Argumente). Übernommen
/// wird nur, was in settings.json noch nicht gewählt ist – danach entscheidet allein die App, spätere Setups
/// überschreiben nichts. Beim allerersten Start ohne diese Argumente (im Setup „Jetzt starten“ abgewählt, weiteres
/// Benutzerkonto) gilt die Wahl, die das Setup in seinem Deinstallationsschlüssel (HKLM) merkt.
/// </summary>
internal static class InstallerChoices
{
    /// <summary>
    /// Deinstallationsschlüssel des Setups (AppId aus installer/SKZ-Connect.iss). Inno Setup legt dort die Werte aus
    /// SetPreviousData als „Inno Setup CodeFile: Name“ ab (RegisterPreviousData im Setup: Language, Theme).
    /// </summary>
    internal const string SetupKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6F3C2B9E-52A1-4C8B-9E44-2D5F1B7A9C31}_is1";

    /// <summary>
    /// Befehlszeile, beim ersten Start (<paramref name="firstRun"/>: noch keine settings.json) die gemerkte Wahl des
    /// Setups übernehmen; <paramref name="save"/> nur, wenn sich etwas geändert hat.
    /// </summary>
    public static void Apply(Settings settings, IReadOnlyList<string> args, bool firstRun, Action save)
    {
        string windows = Tr.WindowsLanguage();
        string? language = Arg(args, "--setup-lang="), theme = Arg(args, "--setup-theme=");
        bool changed = Adopt(settings, language, theme, windows);
        // Portable Variante: Die Registry gehört einer installierten SKZ-Connect-Version (falls vorhanden) – nicht anfassen.
        if (firstRun && language is null && theme is null && !Paths.IsPortable)
        {
            var setup = ReadSetup();
            changed |= Adopt(settings, setup.Language, setup.Theme, windows);
        }
        if (changed)
        {
            Log.Info($"Sprache/Darstellung aus dem Installer übernommen: {settings.Language ?? "wie Windows"}, {settings.Theme ?? "dark"}");
            save();
        }
    }

    /// <summary>Wert eines Arguments „--name=wert“ (null = nicht angegeben).</summary>
    internal static string? Arg(IReadOnlyList<string> args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) is { } a ? a[prefix.Length..].Trim() : null;

    /// <summary>
    /// Gültige Werte übernehmen, wo die Einstellung noch leer ist (null = nie gewählt; „Automatisch“ und „Dunkel“
    /// speichert die App ausdrücklich als "auto" bzw. "dark"). Unbekannte Codes werden ignoriert. Die Sprache nur, wenn
    /// sie von der Windows-Sprache <paramref name="windowsLanguage"/> abweicht: Sonst ergibt null dieselbe Sprache und
    /// folgt weiter Windows. Das Setup übergibt die Sprache ohnehin nur, wenn sie dort bewusst gewählt wurde
    /// (ChosenLangCode in installer/SKZ-Connect.iss). Liefert true, wenn sich etwas geändert hat.
    /// </summary>
    internal static bool Adopt(Settings settings, string? language, string? theme, string windowsLanguage)
    {
        bool changed = false;
        language = language?.ToLowerInvariant();
        theme = theme?.ToLowerInvariant();
        if (settings.Language is null && language is not null && Tr.Supported.Contains(language) && language != windowsLanguage)
        {
            settings.Language = language;
            changed = true;
        }
        if (settings.Theme is null && theme is "dark" or "light" or "system")
        {
            settings.Theme = theme;
            changed = true;
        }
        return changed;
    }

    /// <summary>Wahl der letzten Installation aus dem Deinstallationsschlüssel (nur lesen).</summary>
    private static (string? Language, string? Theme) ReadSetup()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SetupKey);
            return (key?.GetValue("Inno Setup CodeFile: Language") as string, key?.GetValue("Inno Setup CodeFile: Theme") as string);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return default;
        }
    }
}
