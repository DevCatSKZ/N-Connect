using System.Text;
using System.Text.RegularExpressions;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge.Tests;

/// <summary>Tests, die die Oberflächensprache (globaler Zustand von Tr) umstellen, laufen nacheinander.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LanguageCollection
{
    public const string Name = "Oberflächensprache";
}

/// <summary>
/// Übersetzungstabellen (Tr.Texts.cs, Tr.Lang*.cs) und Muster (Tr.cs): vollständig, ohne Doppel, gleiche Muster in
/// gleicher Reihenfolge, Platzhalter bleiben erhalten – und die bekannten Lücken (Kurzanleitung, Update-Rückfragen,
/// Xbox nativ, Tooltip bei Bluetooth-Problemen) sind in allen Sprachen geschlossen.
/// </summary>
[Collection(LanguageCollection.Name)]
public sealed partial class TranslationTests : IDisposable
{
    /// <summary>Fremdsprachen mit eigener Tabelle (ohne Deutsch = Schlüssel und Englisch = Rückfall).</summary>
    private static readonly string[] Foreign = Tr.Supported.Where(l => l is not ("de" or "en")).ToArray();

    private static readonly (string File, string Lang)[] TableFiles =
    [
        ("Tr.Texts.cs", "en"), ("Tr.LangEs.cs", "es"), ("Tr.LangFr.cs", "fr"), ("Tr.LangIt.cs", "it"), ("Tr.LangPt.cs", "pt"),
        ("Tr.LangNl.cs", "nl"), ("Tr.LangPl.cs", "pl"), ("Tr.LangRu.cs", "ru"), ("Tr.LangJa.cs", "ja"), ("Tr.LangZh.cs", "zh"),
        ("Tr.LangKo.cs", "ko"),
    ];

    public void Dispose() => Tr.SetLanguage("de");

    [Fact]
    public void JedeUnterstuetzteSpracheHatTabelleUndMuster()
    {
        Assert.Equal(12, Tr.Supported.Length);
        Assert.Equal(Tr.Supported.Length - 1, TableFiles.Length);
        foreach (var (_, lang) in TableFiles)
        {
            Assert.NotNull(Tr.Table(lang));
            Assert.NotNull(Tr.PatternsFor(lang));
        }
    }

    [Fact]
    public void JedeSpracheHatGenauDieSchluesselDerEnglischenTabelle()
    {
        var english = Tr.Table("en")!;
        foreach (var lang in Foreign)
        {
            var table = Tr.Table(lang)!;
            var missing = english.Keys.Except(table.Keys).ToList();
            var extra = table.Keys.Except(english.Keys).ToList();
            Assert.True(missing.Count == 0 && extra.Count == 0,
                $"{lang}: {missing.Count} fehlen ({string.Join(" | ", missing.Take(5))}), " +
                $"{extra.Count} zu viel ({string.Join(" | ", extra.Take(5))})");
        }
    }

    /// <summary>
    /// Doppelte Schlüssel fallen im Indexer-Initialisierer nicht auf (der spätere gewinnt still) – deshalb im Quelltext
    /// suchen. Gegenprobe: Der Parser findet genau die Einträge der Tabelle.
    /// </summary>
    [Fact]
    public void KeineTabelleHatDoppelteSchluessel()
    {
        foreach (var (file, lang) in TableFiles)
        {
            var keys = SourceKeys(Path.Combine(BridgeSourceDir(), file));
            var table = Tr.Table(lang)!;
            var duplicates = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(duplicates.Count == 0, $"{file}: doppelt {string.Join(" | ", duplicates)}");
            Assert.All(keys, k => Assert.True(table.ContainsKey(k), $"{file}: Schlüssel falsch gelesen: {k}"));
            Assert.Equal(table.Count, keys.Count);
        }
    }

    [Fact]
    public void JedeSpracheHatDieselbenMusterInDerselbenReihenfolge()
    {
        var english = Tr.PatternsFor("en")!;
        foreach (var lang in Foreign)
        {
            var patterns = Tr.PatternsFor(lang)!;
            Assert.True(english.Length == patterns.Length, $"{lang}: {patterns.Length} statt {english.Length} Muster");
            for (int i = 0; i < english.Length; i++)
            {
                Assert.True(english[i].Pattern.ToString() == patterns[i].Pattern.ToString()
                            && english[i].Pattern.Options == patterns[i].Pattern.Options,
                    $"{lang}, Muster {i}: {patterns[i].Pattern} statt {english[i].Pattern}");
            }
        }
    }

    /// <summary>
    /// Zeilenumbrüche bleiben in jeder Übersetzung erhalten; ein einzelnes „&amp;“ (in Menüs und Beschriftungen ein
    /// Tastenkürzel, das Zeichen verschwindet) nur, wo auch der Schlüssel eines hat – sichtbar ist „&amp;&amp;“ oder ein Wort.
    /// </summary>
    [Fact]
    public void FesteTexteBehaltenUmbruecheUndKeineNeuenTastenkuerzel()
    {
        static int Single(string text) => Count(text, "&") - 2 * Count(text, "&&");
        foreach (var (_, lang) in TableFiles)
        {
            foreach (var (key, value) in Tr.Table(lang)!)
            {
                Assert.True(Count(key, "\n") == Count(value, "\n"), $"{lang}: Zeilenumbrüche weichen ab: {key}");
                Assert.True(Single(value) <= Single(key), $"{lang}: einzelnes „&“ in der Übersetzung: {key}");
            }
        }
    }

    /// <summary>
    /// Je Muster (gleiche Reihenfolge wie <c>Tr.Patterns</c>) ein Beispieltext. Freie Platzhalter tragen ein „Q“, damit
    /// sie keiner Übersetzung entsprechen; Zahlen und Versionen werden wörtlich erwartet.
    /// </summary>
    private static readonly string[] PatternSamples =
    [
        "Allgemeiner Wert (Qv0) – Regler verschieben für einen eigenen",
        "Eigener Wert · allgemein: Qv1",
        "Spieler 2  ·  Qk2  ·  Gyro-Maus",
        "  ·  Gyro-Maus",
        "  ·  Gyro-Stick",
        "Spieler 5 · Qk5 57 %",
        "Qk6 verbunden (Spieler 6)",
        "Spieler 7 – tauschen mit Qk7",
        "Spieler 8 – frei",
        "Spieler 9",
        "Xbox 360 · Platz 10",
        "Xbox · Platz 11 · nativ",
        "noch 12 s",
        "Fertig – 13 Controller verbunden",
        "✓ Qk14 verbunden – Spieler 14",
        "Spieler 15  ·  Qv15",
        "Gekoppelt: Qk16 – verbindet gleich …",
        "Ins Spiel wechseln … 17",
        "Qk18 ist jetzt Spieler 18",
        "Spieler 19 und 20 getauscht",
        "Name für Qk20 (leer = Standardname):",
        "Qk21 (Joy-Con-Paar)",
        "Mitte 2048 / 2050 · Abweichung vom Kreis: vorher 12 %, neu 3 %",
        "Joy-Con zusammengefasst (Spieler 23)",
        "Joy-Con getrennt – Qk24 ist jetzt Spieler 24",
        "Spieler 25: Akku Qk25 fast leer (14 %) – bitte aufladen",
        "Spieler 26: nach 15 min ohne Eingabe getrennt",
        "Profil „Qp27“ aktiv",
        "Profil: Qp28",
        "Profil „Qp29“ löschen?",
        "Profil „Qp30“ importiert",
        "⬇ Neue Version 9.8.31 herunterladen …",
        "Version 9.8.32 ist erschienen (installiert: 9.8.0). Klicken zum Herunterladen.",
        "Version 9.8.33 ist erschienen (installiert: 9.8.0). Klicken zum Installieren.",
        "⬇ Auf Version 9.8.34 aktualisieren …",
        "Lade Version 9.8.35 herunter …",
        "HidHide 9.8.36 ist erschienen (installiert: 9.8.0). Klicken zum Installieren.",
        "Lade HidHide 9.8.37 herunter …",
        HidHideQuestion("9.8.38"),
        UpdateQuestion("9.8.39", portable: false),
        "Qt40 · 40 Berichte/s",
        "41 %  (3,70 V)  ⚡ lädt",
        "Speichern fehlgeschlagen: Qe42",
        "Lesen fehlgeschlagen: Qe43",
        "amiibo konnte nicht gelesen werden: Qe44",
        "Gekoppelt: Qk45\nDer Controller erscheint gleich in der Übersicht.",
        "Gekoppelt: Qk46",
        "Gefunden: Qk47 – kopple …",
        "amiibo erkannt (Qu48) – lese …",
        "320 × 240 · 12,5 Bilder/s",
        "50 Minuten",
        "✓ 51 Schritte, Dauer 520 ms",
        "Controller-Taste „Qb52“:\nJetzt die gewünschte Taste oder Tastenkombination drücken.",
        "Solange „Qb53“ gehalten wird, wird diese Taste schnell wiederholt gedrückt\n(Geschwindigkeit unter 5. „Turbo“):",
        "Beim Drücken von „Qb54“ wird diese Folge einmal abgespielt. Schritte durch Komma trennen …",
        "Ring-Con: Qe55",
        "⌨  Taste: Qk56",
        "🔁  Turbo: Taste Qk57",
        "🔁  Turbo: Qk58",
        "⏯  Makro: Qk59",
        "Tastatur: Qk60",
        "Gamepad: Qk61",
        "Qk62  (verbunden)",
        "P63 Qk63 57 %",
        "Bluetooth nicht verfügbar: Qe64",
        "SKZ Connect: Qp65",
        "Qk66 per USB ist von einem anderen Programm belegt (z. B. Steam).",
        "Schon 8 Controller verbunden – Qk67 wird nicht verwendet.",
        "Qk68: schwache Bluetooth-Verbindung (14 statt 33–60 Berichte/s).",
        "Qk69 getrennt",
    ];

    [Fact]
    public void BeispielePassenZuDenMustern()
    {
        var patterns = Tr.PatternsFor("en")!;
        Assert.Equal(patterns.Length, PatternSamples.Length);
        for (int i = 0; i < patterns.Length; i++)
            Assert.True(patterns[i].Pattern.IsMatch(PatternSamples[i]), $"Beispiel {i} passt nicht zu {patterns[i].Pattern}");
    }

    /// <summary>Zahlen, Versionen und Namen (die benannten Gruppen) kommen in jeder Übersetzung eines Musters vor.</summary>
    [Fact]
    public void MusterBehaltenIhrePlatzhalter()
    {
        foreach (var lang in Tr.Supported.Where(l => l != "de"))
        {
            Tr.SetLanguage(lang);
            var patterns = Tr.PatternsFor(lang)!;
            for (int i = 0; i < patterns.Length; i++)
            {
                var match = patterns[i].Pattern.Match(PatternSamples[i]);
                Assert.True(match.Success, $"{lang}, Muster {i}");
                string output = patterns[i].Replace(match);
                Assert.False(string.IsNullOrWhiteSpace(output), $"{lang}, Muster {i}: leer");
                foreach (Group group in match.Groups.Cast<Group>().Skip(1))
                {
                    if (group.Success && IsPlaceholder(group.Value))
                        Assert.True(output.Contains(group.Value, StringComparison.Ordinal),
                            $"{lang}, Muster {i}: „{group.Name}“ = {group.Value} fehlt in: {output}");
                }
            }
        }
    }

    // ---------- Stichproben: bisher nur deutsch oder englisch, jetzt in allen Sprachen ----------

    public static TheoryData<string> Samples() =>
    [
        "SKZ Connect – Willkommen",
        .. WelcomeForm.Paragraphs,
        UpdateQuestion("1.0.22", portable: true),
        UpdateQuestion("1.0.22", portable: false),
        HidHideQuestion("1.5.230"),
        "Xbox · Platz 2 · nativ",
        "Xbox · nativ",
        "SKZ Connect: Bluetooth ist ausgeschaltet.",
        "SKZ Connect: ViGEmBus fehlt",
        "Gekoppelt: Pro Controller\nDer Controller erscheint gleich in der Übersicht.",
        "Mitte 2048 / 2048 · Abweichung vom Kreis: vorher 12 %, neu 3 %",
        "320 × 240 · 12,5 Bilder/s",
        "Xbox über Bluetooth: One S, Series X|S, Elite",
        "Beim Drücken von „A“ wird diese Folge einmal abgespielt. Schritte durch Komma trennen …",
        // Meldungen, die der Windows-Kern an neuen Stellen zeigt (HidHide-Fehler, Spielerlimit, Bluetooth-Zustand).
        "Original-Controller nicht versteckt (Abfrage abgebrochen). Nachholen: Controller → Einstellungen → Extras → „Doppelt angezeigt?“.",
        "Original-Controller nicht versteckt (Fehler, Details im Protokoll). Nachholen: Controller → Einstellungen → Extras → „Doppelt angezeigt?“.",
        "HidHide konnte nicht eingerichtet werden (abgebrochen oder fehlgeschlagen). Details im Protokoll.",
        "Schon 8 Controller verbunden – Pro Controller wird nicht verwendet. Erst einen anderen trennen.",
        "Bluetooth ist ausgeschaltet.",
        "Dieser Bluetooth-Adapter unterstützt kein Bluetooth LE.",
        "Kein Bluetooth-Adapter gefunden.",
        "Bluetooth-Suche konnte nicht starten.",
        "Bluetooth nicht verfügbar: Zugriff verweigert",
        "SKZ Connect: warte auf Controller (SYNC drücken)",
        "SKZ Connect: Bluetooth-Suche konnte nicht starten.",
        // Zusammengesetzt aus Muster und festem Tipp (ControllerManager) bzw. Kurznamen gedrückter Tasten (Karte).
        "Pro Controller 2: schwache Bluetooth-Verbindung (14 statt 33–60 Berichte/s). Tipp: Bluetooth-Stick per Verlängerung " +
        "näher an den Controller, weg von USB-3-Anschlüssen und Funkkopfhörern.",
        "Pro Controller 2 per USB ist von einem anderen Programm belegt (z. B. Steam). Das Programm schließen oder in Steam " +
        "die Nintendo-Unterstützung abschalten.",
        "L-Stick",
        "R-Stick",
        // Lizenzen Dritter (Allgemein → Erweitert) und Hinweise beim Start auf unlesbare bzw. wiederhergestellte Einstellungen.
        "Lizenzen Dritter",
        "Open-Source-Software und übernommener Code in SKZ Connect – mit Lizenzen und Copyright-Vermerken.",
        "Einstellungen wiederhergestellt",
        "settings.json konnte nicht gelesen werden – es gelten die Standardwerte. Die beschädigte Datei wird als " +
        "„settings.json.unlesbar-…“ aufbewahrt (Klicken öffnet den Ordner).",
        "Die Einstellungen waren beschädigt und wurden aus der letzten Sicherung wiederhergestellt. Die zuletzt " +
        "geänderten Werte fehlen womöglich (Klicken öffnet den Ordner).",
    ];

    [Theory]
    [MemberData(nameof(Samples))]
    public void StichprobeIstInJederSpracheUebersetzt(string german)
    {
        Tr.SetLanguage("en");
        string english = Tr.T(german);
        Assert.NotEqual(german, english);
        Assert.True(Tr.HasOwnTranslation(german, "en"), $"en: {german}");
        foreach (var lang in Foreign)
        {
            Tr.SetLanguage(lang);
            string translated = Tr.T(german);
            Assert.True(translated != german, $"{lang} bleibt deutsch: {german}");
            Assert.True(Tr.HasOwnTranslation(german, lang), $"{lang} hat keine eigene Übersetzung: {german}");
        }
    }

    /// <summary>Gegenprobe für die Prüfung oben: Unbekanntes zählt nicht als übersetzt, auch nicht als zweiter Satz.</summary>
    [Fact]
    public void EigeneUebersetzungErkenntLuecken()
    {
        foreach (var lang in Tr.Supported.Where(l => l != "de"))
        {
            Assert.False(Tr.HasOwnTranslation("Diesen Text gibt es nicht", lang));
            Assert.False(Tr.HasOwnTranslation("Bluetooth ist ausgeschaltet. Diesen Satz gibt es nicht.", lang));
            Assert.True(Tr.HasOwnTranslation("Bluetooth ist ausgeschaltet. Controller per USB funktionieren weiterhin.", lang));
            Assert.True(Tr.HasOwnTranslation("Standard: Immer", lang));
        }
    }

    [Fact]
    public void UpdateRueckfrageMitUndOhneAdminSatz()
    {
        foreach (var lang in Tr.Supported.Where(l => l != "de"))
        {
            Tr.SetLanguage(lang);
            string setup = Tr.T(UpdateQuestion("1.0.22", portable: false)), portable = Tr.T(UpdateQuestion("1.0.22", portable: true));
            Assert.Contains("1.0.22", setup);
            Assert.Contains("1.0.22", portable);
            // Setup-Variante = portable Variante + Satz zu den Administratorrechten.
            Assert.StartsWith(portable, setup, StringComparison.Ordinal);
            Assert.True(setup.Length > portable.Length, $"{lang}: Admin-Satz fehlt");
        }
    }

    [Fact]
    public void TooltipUebersetztAuchDasBluetoothProblem()
    {
        foreach (var lang in Foreign)
        {
            Tr.SetLanguage(lang);
            foreach (var problem in new[] { "Bluetooth ist ausgeschaltet.", "Kein Bluetooth-Adapter gefunden.", "Dieser Bluetooth-Adapter unterstützt kein Bluetooth LE." })
            {
                string tooltip = Tr.T($"SKZ Connect: {problem}");
                Assert.Contains(Tr.Table(lang)![problem], tooltip);
                Assert.StartsWith("SKZ Connect", tooltip, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void KurzanleitungHatKeinenDeutschenAbsatzMehr()
    {
        foreach (var lang in Tr.Supported.Where(l => l != "de"))
        {
            Tr.SetLanguage(lang);
            string guide = WelcomeForm.Guide;
            Assert.All(WelcomeForm.Paragraphs, p => Assert.DoesNotContain(p, guide));
            Assert.Equal(WelcomeForm.Paragraphs.Length, guide.Split("\n\n").Length);
        }
        Tr.SetLanguage("de");
        Assert.Equal(string.Join("\n\n", WelcomeForm.Paragraphs), WelcomeForm.Guide);
    }

    /// <summary>Einblendungen im Infobereich fassen höchstens 255 Zeichen (NOTIFYICONDATA.szInfo) – Windows schneidet den Rest ab.</summary>
    [Fact]
    public void HinweiseZuDenEinstellungenPassenInDieEinblendung()
    {
        string[] texts =
        [
            "settings.json konnte nicht gelesen werden – es gelten die Standardwerte. Die beschädigte Datei wird als " +
            "„settings.json.unlesbar-…“ aufbewahrt (Klicken öffnet den Ordner).",
            "Die Einstellungen waren beschädigt und wurden aus der letzten Sicherung wiederhergestellt. Die zuletzt " +
            "geänderten Werte fehlen womöglich (Klicken öffnet den Ordner).",
        ];
        foreach (var lang in Tr.Supported)
        {
            Tr.SetLanguage(lang);
            foreach (var text in texts)
                Assert.True(Tr.T(text).Length <= 255, $"{lang}: {Tr.T(text).Length} Zeichen: {Tr.T(text)}");
        }
    }

    [Fact]
    public void ToteFunkadapterTexteSindEntfernt()
    {
        foreach (var (_, lang) in TableFiles)
            Assert.DoesNotContain(Tr.Table(lang)!.Keys, k => k.Contains("ESP32", StringComparison.Ordinal) || k.Contains("nRF52840", StringComparison.Ordinal));
    }

    [Fact]
    public void WidgetZeigtStandardStattLeeremProfil()
    {
        Tr.SetLanguage("de");
        var settings = new Settings();
        Assert.Null(ControllerWidget.ProfileStatus(settings));
        settings.ForcedProfile = ""; // im Infobereich fest „Standard“
        Assert.Equal("Profil: Standard", ControllerWidget.ProfileStatus(settings));
        settings.ForcedProfile = "Zelda";
        Assert.Equal("Profil: Zelda", ControllerWidget.ProfileStatus(settings));
        settings.ForcedProfile = null;
        settings.DetectedProfile = "Mario Kart";
        Assert.Equal("Profil: Mario Kart", ControllerWidget.ProfileStatus(settings));

        Tr.SetLanguage("fr");
        settings.ForcedProfile = "";
        Assert.Equal($"{Tr.Table("fr")!["Profil"]}: {Tr.Table("fr")!["Standard"]}", ControllerWidget.ProfileStatus(settings));
    }

    // ---------- Hilfen ----------

    /// <summary>Rückfrage vor dem Update, wie TrayApp.InstallUpdateAsync sie baut (portable: ohne Admin-Satz).</summary>
    private static string UpdateQuestion(string version, bool portable) =>
        $"Version {version} herunterladen und installieren? SKZ Connect wird dafür kurz beendet und " +
        "danach wieder gestartet." + (portable ? "" : " Windows fragt dabei nach Administratorrechten.");

    /// <summary>Rückfrage vor dem HidHide-Update, wie TrayApp.InstallHidHideUpdateAsync sie baut.</summary>
    private static string HidHideQuestion(string version) =>
        $"HidHide {version} herunterladen und installieren? Das HidHide-Setup entfernt zuerst die alte " +
        "Version und verlangt einen Neustart von Windows – bitte seinen Anweisungen folgen. Windows fragt dabei " +
        "nach Administratorrechten. Danach versteckt SKZ Connect die Controller automatisch neu.";

    /// <summary>Platzhalter, der wörtlich übernommen werden muss: Zahl/Version/Prozent oder ein „Q“-Name.</summary>
    private static bool IsPlaceholder(string value) => value.Contains('Q') || NumberLike().IsMatch(value);

    [GeneratedRegex(@"^[\d.,% ]+$")]
    private static partial Regex NumberLike();

    private static int Count(string text, string part)
    {
        int count = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    /// <summary>Quellordner der Windows-App.</summary>
    private static string BridgeSourceDir() => Path.Combine(Repo.Root(), "src", "Switch2Pro.Bridge");

    /// <summary>Schlüssel der Texts-Tabelle einer Quelldatei in Quelltext-Reihenfolge (auch über mehrere Zeilen mit „+“).</summary>
    private static List<string> SourceKeys(string path)
    {
        var keys = new List<string>();
        string[] lines = File.ReadAllLines(path);
        bool inTable = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimStart();
            if (lines[i].Contains("Dictionary<string, string> Texts", StringComparison.Ordinal))
            {
                inTable = true;
                continue;
            }
            if (!inTable)
                continue;
            if (line.StartsWith("};", StringComparison.Ordinal))
                break;
            if (!line.StartsWith("[\"", StringComparison.Ordinal))
                continue;
            string text = lines[i];
            while (!KeyEnd().IsMatch(text))
                text += "\n" + lines[++i];
            text = text[..KeyEnd().Match(text).Index] + "\"";
            var key = new StringBuilder();
            foreach (Match literal in Literal().Matches(text))
                key.Append(Regex.Unescape(literal.Groups[1].Value));
            keys.Add(key.ToString());
        }
        return keys;
    }

    /// <summary>Ende des Schlüssels: <c>"] =</c>.</summary>
    [GeneratedRegex("\"\\]\\s*=")]
    private static partial Regex KeyEnd();

    /// <summary>Ein C#-Zeichenkettenliteral (mit Escapes).</summary>
    [GeneratedRegex("\"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex Literal();
}
