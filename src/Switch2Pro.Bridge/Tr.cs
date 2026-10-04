using System.Globalization;
using System.Text.RegularExpressions;

namespace Switch2Pro.Bridge;

/// <summary>
/// Oberfläche auf Englisch: Die App ist auf Deutsch geschrieben; ist Englisch aktiv (Einstellung oder Windows-Sprache
/// nicht Deutsch), werden sichtbare Texte über diese Tabelle übersetzt – feste Texte 1:1, Meldungen mit Zahlen
/// und Namen über Muster. Unbekannte Texte bleiben unverändert (nie leer). Das Protokoll bleibt deutsch.
/// </summary>
internal static partial class Tr
{
    /// <summary>Englische Oberfläche aktiv?</summary>
    public static bool English { get; private set; }

    /// <summary>Nur für die Prüfhilfe: Texte in der Originalsprache sammeln.</summary>
    internal static void SetEnglish(bool english) => English = english;

    /// <summary>Kennzeichen (Tag) für Steuerelemente/Menüeinträge mit Benutzerdaten (z. B. Profilnamen): nicht übersetzen.</summary>
    public const string UserData = "user-data";

    /// <summary>Sprache festlegen: "de", "en" oder null = wie Windows (Deutsch nur bei deutscher Windows-Sprache).</summary>
    public static void Init(string? language) =>
        English = language switch
        {
            "de" => false,
            "en" => true,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "de",
        };

    public static string T(string? german)
    {
        if (german is null)
            return "";
        if (!English || german.Length == 0)
            return german;
        if (Texts.TryGetValue(german, out var english))
            return english;
        if (german.StartsWith("Standard: ", StringComparison.Ordinal))
            return "Default: " + T(german["Standard: ".Length..]);
        foreach (var (pattern, replace) in Patterns)
        {
            var m = pattern.Match(german);
            if (m.Success)
                return replace(m);
        }
        // Aus mehreren Sätzen zusammengesetzt (z. B. Problem + Tipp): Satz für Satz übersetzen.
        // Abkürzungen wie „z. B.“ sind kein Satzende.
        string guarded = german;
        foreach (var abbreviation in new[] { "z. B. ", "bzw. ", "d. h. ", "ca. ", "u. a. ", "usw. " })
            guarded = guarded.Replace(abbreviation, abbreviation.Replace(' ', '\u0001'));
        var sentences = SentenceBreak().Split(guarded).Select(s => s.Replace('\u0001', ' ')).ToArray();
        if (sentences.Length > 1)
        {
            var translated = sentences.Select(T).ToArray();
            if (!translated.SequenceEqual(sentences))
                return string.Join(" ", translated);
        }
        return german;
    }

    [GeneratedRegex(@"(?<=[.!?…])\s+(?=[A-ZÄÖÜ„])")]
    private static partial Regex SentenceBreak();

    /// <summary>Alle Texte eines Fensters bzw. Steuerelements (rekursiv) übersetzen, auch Listeneinträge.</summary>
    public static void Apply(Control root)
    {
        if (!English)
            return;
        if (root.Text is { Length: > 0 } text && root is not (TextBox or ISelfTranslating))
            root.Text = T(text);
        if (root is ComboBox combo && !Equals(combo.Tag, UserData))
        {
            for (int i = 0; i < combo.Items.Count; i++)
                if (combo.Items[i] is string s)
                    combo.Items[i] = T(s);
        }
        foreach (Control child in root.Controls)
            Apply(child);
    }

    public static void Apply(ToolStripItemCollection items)
    {
        if (!English)
            return;
        foreach (ToolStripItem item in items)
        {
            if (!Equals(item.Tag, UserData))
                item.Text = T(item.Text);
            if (item is ToolStripMenuItem { HasDropDownItems: true } menu)
                Apply(menu.DropDownItems);
        }
    }

    public static DialogResult Show(IWin32Window? owner, string text, string caption = "",
        MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None) =>
        Theme.Message(owner, T(text), T(caption), buttons, icon);

    private static string N(Match m, string group) => T(m.Groups[group].Value);

    /// <summary>Meldungen mit Platzhaltern (Spielernummer, Controllername, Prozent …).</summary>
    private static readonly (Regex Pattern, Func<Match, string> Replace)[] Patterns =
    [
        (Re(@"^Allgemeiner Wert \((?<v>.+)\) – Regler verschieben für einen eigenen$"), m => $"General value ({m.Groups["v"]}) – move the slider to set your own"),
        (Re(@"^Eigener Wert · allgemein: (?<v>.+)$"), m => $"Own value · general: {m.Groups["v"]}"),
        (Re(@"^Spieler (?<n>\d+)  ·  (?<k>.+?)(?<rest>(  ·  .*)?)$"),m => $"Player {m.Groups["n"]}  ·  {N(m, "k")}{T(m.Groups["rest"].Value)}"),
        (Re(@"^  ·  Gyro-Maus(?<r>.*)$"), m => "  ·  Gyro mouse" + T(m.Groups["r"].Value)),
        (Re(@"^  ·  Gyro-Stick$"), _ => "  ·  Gyro stick"),
        (Re(@"^Spieler (?<n>\d+) · (?<k>.+?) (?<b>\d+ %.*)?$"), m => $"Player {m.Groups["n"]} · {N(m, "k")} {m.Groups["b"]}"),
        (Re(@"^(?<k>.+) verbunden \(Spieler (?<n>\d+)\)$"), m => $"{N(m, "k")} connected (player {m.Groups["n"]})"),
        (Re(@"^Spieler (?<n>\d+) – tauschen mit (?<k>.+)$"), m => $"Player {m.Groups["n"]} – swap with {N(m, "k")}"),
        (Re(@"^Spieler (?<n>\d+) – frei$"), m => $"Player {m.Groups["n"]} – free"),
        (Re(@"^Spieler (?<n>\d+)$"), m => $"Player {m.Groups["n"]}"),
        (Re(@"^Xbox 360 · Platz (?<n>\d+)$"), m => $"Xbox 360 · slot {m.Groups["n"]}"),
        (Re(@"^noch (?<n>\d+) s$"), m => $"{m.Groups["n"]} s left"),
        (Re(@"^Fertig – (?<n>\d+) Controller verbunden$"), m => $"Done – {m.Groups["n"]} controller(s) connected"),
        (Re(@"^✓ (?<k>.+) verbunden – Spieler (?<n>\d+)$"), m => $"✓ {N(m, "k")} connected – player {m.Groups["n"]}"),
        (Re(@"^Spieler (?<n>\d+)  ·  (?<v>.+)$"), m => $"Player {m.Groups["n"]}  ·  {m.Groups["v"]}"),
        (Re(@"^Gekoppelt: (?<k>.+) – verbindet gleich …$"), m => $"Paired: {m.Groups["k"]} – connecting …"),
        (Re(@"^Ins Spiel wechseln … (?<n>\d+)$"), m => $"Switch to the game … {m.Groups["n"]}"),
        (Re(@"^(?<k>.+) ist jetzt Spieler (?<n>\d+)$"), m => $"{N(m, "k")} is now player {m.Groups["n"]}"),
        (Re(@"^Spieler (?<a>\d+) und (?<b>\d+) getauscht$"), m => $"Players {m.Groups["a"]} and {m.Groups["b"]} swapped"),
        (Re(@"^Name für (?<k>.+) \(leer = Standardname\):$"), m => $"Name for {N(m, "k")} (empty = default name):"),
        (Re(@"^(?<k>.+) \(Joy-Con-Paar\)$"), m => $"{m.Groups["k"]} (Joy-Con pair)"),
        (Re(@"^Mitte (?<x>\d+) / (?<y>\d+) · Abweichung vom Kreis: vorher (?<a>\d+) %, neu (?<b>\d+) %$"),
            m => $"Centre {m.Groups["x"]} / {m.Groups["y"]} · deviation from circle: before {m.Groups["a"]} %, new {m.Groups["b"]} %"),
        (Re(@"^Joy-Con zusammengefasst \(Spieler (?<n>\d+)\)$"), m => $"Joy-Cons combined (player {m.Groups["n"]})"),
        (Re(@"^Joy-Con getrennt – (?<k>.+) ist jetzt Spieler (?<n>\d+)$"), m => $"Joy-Con split – {N(m, "k")} is now player {m.Groups["n"]}"),
        (Re(@"^Spieler (?<n>\d+): Akku (?<k>.+) fast leer \((?<p>\d+) %\) – bitte aufladen$"),
            m => $"Player {m.Groups["n"]}: {N(m, "k")} battery low ({m.Groups["p"]} %) – please charge"),
        (Re(@"^Spieler (?<n>\d+): nach (?<m>\d+) min ohne Eingabe getrennt$"),
            m => $"Player {m.Groups["n"]}: disconnected after {m.Groups["m"]} min without input"),
        (Re(@"^(?<k>.+) per USB verbunden\. Sieht ein Spiel ihn doppelt\? Im Fenster „Doppelt angezeigt\? Verstecken“ klicken\.$"),
            m => $"{N(m, "k")} connected via USB. Does a game see it twice? Click “Shown twice? Hide” in the window."),
        (Re(@"^Profil „(?<p>.+)“ aktiv$"), m => $"Profile “{ProfileName(m.Groups["p"].Value)}” active"),
        (Re(@"^Profil: (?<p>.+)$"), m => $"Profile: {ProfileName(m.Groups["p"].Value)}"),
        (Re(@"^Profil „(?<p>.+)“ löschen\?$"), m => $"Delete profile “{m.Groups["p"]}”?"),
        (Re(@"^Profil „(?<p>.+)“ importiert(?<r>.*)$"), m => $"Profile “{m.Groups["p"]}” imported" + m.Groups["r"].Value.Replace("aktiv bei:", "active for:")),
        (Re(@"^⬇ Neue Version (?<v>.+) herunterladen …$"), m => $"⬇ Download new version {m.Groups["v"]} …"),
        (Re(@"^Version (?<v>.+) ist erschienen \(installiert: (?<i>.+)\)\. Klicken zum Herunterladen\.$"),
            m => $"Version {m.Groups["v"]} is available (installed: {m.Groups["i"]}). Click to download."),
        (Re(@"^Version (?<v>.+) ist erschienen \(installiert: (?<i>.+)\)\. Klicken zum Installieren\.$"),
            m => $"Version {m.Groups["v"]} is available (installed: {m.Groups["i"]}). Click to install."),
        (Re(@"^⬇ Auf Version (?<v>.+) aktualisieren …$"), m => $"⬇ Update to version {m.Groups["v"]} …"),
        (Re(@"^Lade Version (?<v>.+) herunter …$"), m => $"Downloading version {m.Groups["v"]} …"),
        (Re(@"^Version (?<v>.+) herunterladen und installieren\? N-Connect wird dafür kurz beendet und danach wieder gestartet\. Windows fragt dabei nach Administratorrechten\.$"),
            m => $"Download and install version {m.Groups["v"]}? N-Connect closes briefly and starts again afterwards. Windows will ask for administrator rights."),
        (Re(@"^(?<t>.+) · (?<r>\d+) Berichte/s$"), m => $"{T(m.Groups["t"].Value)} · {m.Groups["r"]} reports/s"),
        (Re(@"^(?<p>\d+ %)(?<v>  \(.*\))?  ⚡ lädt$"), m => $"{m.Groups["p"]}{m.Groups["v"]}  ⚡ charging"),
        (Re(@"^Speichern fehlgeschlagen: (?<e>.*)$"), m => $"Saving failed: {m.Groups["e"]}"),
        (Re(@"^Lesen fehlgeschlagen: (?<e>.*)$"), m => $"Reading failed: {m.Groups["e"]}"),
        (Re(@"^amiibo konnte nicht gelesen werden: (?<e>.*)$"), m => $"Could not read amiibo: {T(m.Groups["e"].Value)}"),
        (Re(@"^Gekoppelt: (?<k>.+)\nDer Controller erscheint gleich in der Übersicht\.$"),
            m => $"Paired: {m.Groups["k"]}\nThe controller will appear in the overview shortly."),
        (Re(@"^Gekoppelt: (?<k>.+)$"), m => $"Paired: {m.Groups["k"]}"),
        (Re(@"^Gefunden: (?<k>.+) – kopple …$"), m => $"Found: {m.Groups["k"]} – pairing …"),
        (Re(@"^amiibo erkannt \((?<u>.+)\) – lese …$"), m => $"amiibo detected ({m.Groups["u"]}) – reading …"),
        (Re(@"^(?<w>\d+) × (?<h>\d+) · (?<f>[\d.,]+) Bilder/s$"), m => $"{m.Groups["w"]} × {m.Groups["h"]} · {m.Groups["f"]} frames/s"),
        (Re(@"^(?<n>\d+) Minuten$"), m => $"{m.Groups["n"]} minutes"),
        (Re(@"^✓ (?<n>\d+) Schritte, Dauer (?<ms>\d+) ms$"), m => $"✓ {m.Groups["n"]} steps, {m.Groups["ms"]} ms total"),
        (Re(@"^Controller-Taste „(?<b>.+)“:\nJetzt die gewünschte Taste oder Tastenkombination drücken\.$"),
            m => $"Controller button “{T(m.Groups["b"].Value)}”:\nNow press the key or key combination."),
        (Re(@"^Solange „(?<b>.+)“ gehalten wird, wird diese Taste schnell wiederholt gedrückt\n\(Geschwindigkeit unter 5\. „Turbo“\):$"),
            m => $"While “{T(m.Groups["b"].Value)}” is held, this button is pressed repeatedly\n(speed under 5. “Turbo”):"),
        (Re(@"^Beim Drücken von „(?<b>.+?)“ wird diese Folge einmal abgespielt\.(?<r>.*)$", RegexOptions.Singleline),
            m => $"Pressing “{T(m.Groups["b"].Value)}” plays this sequence once. Separate steps with commas; for each step what is held and " +
                 "for how long (ms). Gamepad: A B X Y LB RB LT RT Up Down Left Right LS RS Start Back Guide, several at once with +. " +
                 "Keyboard: Key:Ctrl+C. Wait: Pause.\nExamples:  “A 80, Pause 60, A 80” (double tap)  ·  “Down+B 150”  ·  “Key:Ctrl+S 50”"),
        (Re(@"^Ring-Con: (?<e>.*)$"), m => $"Ring-Con: {T(m.Groups["e"].Value)}"),
        (Re(@"^Gyro-Stick: voller Ausschlag bei (?<s>\d+) °/s, mindestens (?<m>\d+) %   ·   Kennlinie (?<c>[\d.,]+)   ·   Trigger: ab (?<d>\d+) %, voll ab (?<f>\d+) %   ·   Turbo (?<t>\d+)× pro Sekunde$"),
            m => $"Gyro stick: full deflection at {m.Groups["s"]} °/s, at least {m.Groups["m"]} %   ·   curve {m.Groups["c"]}   ·   " +
                 $"triggers: from {m.Groups["d"]} %, full at {m.Groups["f"]} %   ·   turbo {m.Groups["t"]}× per second"),
        (Re(@"^⌨  Taste: (?<k>.+)$"), m => $"⌨  Key: {m.Groups["k"]}"),
        (Re(@"^🔁  Turbo: Taste (?<k>.+)$"), m => $"🔁  Turbo: key {m.Groups["k"]}"),
        (Re(@"^🔁  Turbo: (?<k>.+)$"), m => $"🔁  Turbo: {T(m.Groups["k"].Value)}"),
        (Re(@"^⏯  Makro: (?<k>.+)$"), m => $"⏯  Macro: {m.Groups["k"]}"),
        (Re(@"^Tastatur: (?<k>.+)$"), m => $"Keyboard: {m.Groups["k"]}"),
        (Re(@"^Gamepad: (?<k>.+)$"), m => $"Gamepad: {m.Groups["k"]}"),
        (Re(@"^(?<k>.+)  \(verbunden\)$"), m => $"{N(m, "k")}  (connected)"),
        (Re(@"^P(?<n>\d+) (?<k>.+)$"), m => m.Value),
        (Re(@"^Bluetooth nicht verfügbar: (?<e>.*)$"), m => $"Bluetooth not available: {m.Groups["e"]}"),
        (Re(@"^(?<k>.+) per USB ist von einem anderen Programm belegt \(z\. B\. Steam\)\.$"),
            m => $"{N(m, "k")} via USB is in use by another program (e.g. Steam)."),
        (Re(@"^Schon (?<n>\d+) Controller verbunden – (?<k>.+) wird nicht verwendet\.$"),
            m => $"Already {m.Groups["n"]} controllers connected – {N(m, "k")} is not used."),
        (Re(@"^(?<k>.+): schwache Bluetooth-Verbindung \((?<r>\d+) statt 33–60 Berichte/s\)\.$"),
            m => $"{N(m, "k")}: weak Bluetooth connection ({m.Groups["r"]} instead of 33–60 reports/s)."),
        // Allgemein zuletzt, sonst würde es speziellere Meldungen mit „getrennt“ am Ende verschlucken.
        (Re(@"^(?<k>.+) getrennt$"), m => $"{N(m, "k")} disconnected"),
    ];

    /// <summary>Profilname: nur „Standard“ übersetzen, eigene Namen bleiben, wie der Benutzer sie geschrieben hat.</summary>
    private static string ProfileName(string name) => name == "Standard" ? T(name) : name;

    private static Regex Re(string pattern, RegexOptions options = RegexOptions.None) => new(pattern, options | RegexOptions.CultureInvariant);
}
