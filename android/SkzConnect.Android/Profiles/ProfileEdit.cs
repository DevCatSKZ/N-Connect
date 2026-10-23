using Switch2Pro.Protocol;

namespace SkzConnect.Android.Profiles;

/// <summary>
/// Änderungen an „Profilen je Spiel“ ohne Android-Abhängigkeiten (testbar in android/SkzConnect.Android.Tests).
/// Profile sind unveränderlich: jede Änderung liefert eine neue Kopie, weil der Bluetooth-Thread gleichzeitig liest.
/// </summary>
public static class ProfileEdit
{
    /// <summary>Controller-Arten, die SKZ Connect für Android verbindet (ein hochkant gehaltener Joy-Con gilt als Paar).</summary>
    public static readonly ControllerKind[] Kinds =
        [ControllerKind.Pro2, ControllerKind.JoyConPair, ControllerKind.JoyCon2Left, ControllerKind.JoyCon2Right, ControllerKind.GameCube2];

    /// <summary>
    /// Ziele, die unter Android wirken (virtueller Xbox-Controller; Touchpad-Klick nur über DSU), mit deutschem Namen für
    /// die Auswahl (übersetzt über L.T). Tastatur, Maus und Shift-Ebene gibt es unter Android nicht.
    /// </summary>
    public static readonly (string Action, string Label)[] Targets =
    [
        ("None", "— keine Funktion —"),
        ("A", "A"), ("B", "B"), ("X", "X"), ("Y", "Y"), ("LB", "LB"), ("RB", "RB"), ("LT", "LT"), ("RT", "RT"),
        ("Back", "Ansicht (Back)"), ("Start", "Menü (Start)"), ("Guide", "Xbox-Taste"),
        ("LS", "Linker Stick-Klick"), ("RS", "Rechter Stick-Klick"),
        ("Up", "Steuerkreuz hoch"), ("Down", "Steuerkreuz runter"), ("Left", "Steuerkreuz links"), ("Right", "Steuerkreuz rechts"),
        ("Touchpad", "Touchpad-Klick (nur DSU)"),
        (nameof(SpecialAction.GyroStick), "🎯  Gyro als rechter Stick (solange gehalten)"),
        (nameof(SpecialAction.GyroPause), "✋  Gyro anhalten (solange gehalten, „Ratchet“)"),
    ];

    /// <summary>
    /// Neues Profil mit der gerade geltenden Standardbelegung aller Controller-Arten (fest übernommen – spätere
    /// Änderungen am Standard ändern das Profil nicht) und einer Kopie der Shift-Ebene.
    /// </summary>
    public static NamedProfile CopyOfStandard(Settings settings, string name, string package)
    {
        var buttons = new Dictionary<ControllerKind, Dictionary<ProButtons, string>>();
        foreach (var kind in Kinds)
        {
            buttons[kind] = ControllerButtons.For(kind)
                .ToDictionary(b => b, b => Mapping.ActionFor(b, kind, settings, profile: null, shift: false).ToString());
        }
        return new NamedProfile
        {
            Name = name,
            Programs = [package],
            Buttons = buttons,
            ShiftButtons = settings.ShiftProfiles.ToDictionary(p => p.Key, p => new Dictionary<ProButtons, string>(p.Value)),
        };
    }

    /// <summary>Aktion einer Taste setzen; null = Standard (Eintrag entfernen, dann gilt die Standardbelegung).</summary>
    public static NamedProfile WithAction(NamedProfile profile, ControllerKind kind, ProButtons button, string? action)
    {
        var buttons = profile.Buttons.ToDictionary(p => p.Key, p => new Dictionary<ProButtons, string>(p.Value));
        if (!buttons.TryGetValue(kind, out var map))
            buttons[kind] = map = [];
        if (action is null)
            map.Remove(button);
        else
            map[button] = action;
        if (map.Count == 0)
            buttons.Remove(kind);
        return profile with { Buttons = buttons };
    }

    /// <summary>Alle Tasten einer Controller-Art auf Standard (eigene Einträge entfernen).</summary>
    public static NamedProfile WithoutKind(NamedProfile profile, ControllerKind kind) => profile with
    {
        Buttons = profile.Buttons.Where(p => p.Key != kind).ToDictionary(p => p.Key, p => new Dictionary<ProButtons, string>(p.Value)),
    };

    /// <summary>
    /// Makro-Text prüfen (Zeilen wie Kommas, siehe <see cref="MacroScript"/>). Tastatur-Schritte („Key:“) gibt es unter
    /// Android nicht – solche Makros gelten als ungültig. Liefert das bereinigte Makro oder null.
    /// </summary>
    public static string? ValidMacro(string? text)
    {
        string script = (text ?? "").Replace("\r", "").Replace('\n', ',').Trim();
        return MacroScript.TryParse(script, out var parsed) && parsed.Steps.All(s => s.Keys is null) ? script : null;
    }

    /// <summary>Profilnamen sind eindeutig (ohne Groß-/Kleinschreibung); <paramref name="except"/> = der bisherige Name.</summary>
    public static bool NameFree(IEnumerable<NamedProfile> profiles, string name, string? except = null) =>
        name.Length > 0 && !profiles.Any(p => p.Name != except && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Profil umbenennen; das fest gewählte bzw. erkannte Profil zieht mit.</summary>
    public static void Rename(Settings settings, string name, string renamed)
    {
        settings.NamedProfiles = [.. settings.NamedProfiles.Select(p => p.Name == name ? p with { Name = renamed } : p)];
        if (settings.ForcedProfile == name)
            settings.ForcedProfile = renamed;
        if (settings.DetectedProfile == name)
            settings.DetectedProfile = renamed;
    }

    /// <summary>Profil löschen; war es fest gewählt, gilt wieder „Automatisch“.</summary>
    public static void Delete(Settings settings, string name)
    {
        settings.NamedProfiles = [.. settings.NamedProfiles.Where(p => p.Name != name)];
        if (settings.ForcedProfile == name)
            settings.ForcedProfile = null;
        if (settings.DetectedProfile == name)
            settings.DetectedProfile = null;
    }

    /// <summary>Ein Profil durch seine geänderte Kopie ersetzen (gleicher Name).</summary>
    public static void Replace(Settings settings, NamedProfile updated) =>
        settings.NamedProfiles = [.. settings.NamedProfiles.Select(p => p.Name == updated.Name ? updated : p)];
}
