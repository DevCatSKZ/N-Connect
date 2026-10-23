using System.Globalization;
using System.Text.RegularExpressions;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge.Tests;

/// <summary>
/// Sprache und Darstellung: „Automatisch“ folgt der Windows-Sprache, „Automatisch“ und „Dunkel“ werden ausdrücklich
/// gespeichert, das Setup übergibt eine Sprache nur bei bewusster Wahl, und die
/// Wahl im Installer füllt nur Einstellungen, die noch leer sind (stille Updates und spätere Setups ändern nichts).
/// Ohne Registry: <see cref="InstallerChoices.Apply"/> selbst wird hier nicht aufgerufen.
/// </summary>
[Collection(LanguageCollection.Name)]
public sealed class LanguageAndThemeTests : IDisposable
{
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _uiCulture;
        Tr.SetLanguage("de");
        Theme.Init(null, transparency: false);
    }

    [Theory]
    [InlineData("ja-JP", "ja")]
    [InlineData("pt-BR", "pt")]
    [InlineData("pt-PT", "pt")]
    [InlineData("de-AT", "de")]
    [InlineData("zh-CN", "zh")]
    [InlineData("en-GB", "en")]
    [InlineData("sv-SE", "en")] // keine Tabelle → Englisch
    public void AutomatischFolgtDerWindowsSprache(string culture, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        Tr.Init(null);
        Assert.Equal(expected, Tr.Lang);
        Tr.Init("auto");
        Assert.Equal(expected, Tr.Lang);
    }

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("de", "de")]
    [InlineData("xx", "en")]
    public void FesteSpracheGiltUnabhaengigVonWindows(string setting, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo("ja-JP");
        Tr.Init(setting);
        Assert.Equal(expected, Tr.Lang);
    }

    [Theory]
    [InlineData(0, "dark")]
    [InlineData(1, "light")]
    [InlineData(2, "system")]
    [InlineData(-1, "dark")]
    public void DarstellungWirdAusdruecklichGespeichert(int index, string expected) =>
        Assert.Equal(expected, SettingsForm.ThemeMode(index));

    [Fact]
    public void OhneWahlUndDunkelSindDunkel_HellIstHell()
    {
        Theme.Init(null, transparency: false);
        Assert.True(Theme.Dark);
        Theme.Init("dark", transparency: false);
        Assert.True(Theme.Dark);
        Theme.Init("light", transparency: false);
        Assert.False(Theme.Dark);
    }

    [Fact]
    public void ArgumenteDesInstallersWerdenGelesen()
    {
        string[] args = [@"C:\Program Files\SKZ-Connect\SKZ-Connect.exe", "--setup-lang=fr", "--setup-theme=light"];
        Assert.Equal("fr", InstallerChoices.Arg(args, "--setup-lang="));
        Assert.Equal("light", InstallerChoices.Arg(args, "--setup-theme="));
        Assert.Null(InstallerChoices.Arg([@"C:\Program Files\SKZ-Connect\SKZ-Connect.exe", "--autostart"], "--setup-lang="));
    }

    [Fact]
    public void InstallerWahlFuelltNurLeereEinstellungen()
    {
        var settings = new Settings();
        Assert.True(InstallerChoices.Adopt(settings, "fr", "light", "de"));
        Assert.Equal("fr", settings.Language);
        Assert.Equal("light", settings.Theme);
        // Späteres Setup mit anderer Wahl (oder alte Registry-Werte): die Wahl bleibt.
        Assert.False(InstallerChoices.Adopt(settings, "en", "dark", "de"));
        Assert.Equal("fr", settings.Language);
        Assert.Equal("light", settings.Theme);
    }

    [Fact]
    public void InDerAppGewaehltesDunkelBleibtTrotzInstallerHell()
    {
        var settings = new Settings { Theme = SettingsForm.ThemeMode(0), Language = "en" };
        Assert.False(InstallerChoices.Adopt(settings, "fr", "light", "de"));
        Assert.Equal("dark", settings.Theme);
        Assert.Equal("en", settings.Language);
    }

    [Fact]
    public void NurFehlendesWirdErgaenzt()
    {
        var settings = new Settings { Language = "it" };
        Assert.True(InstallerChoices.Adopt(settings, "fr", "system", "de"));
        Assert.Equal("it", settings.Language);
        Assert.Equal("system", settings.Theme);
    }

    [Theory]
    [InlineData("xx", "blue")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void UngueltigeWerteWerdenIgnoriert(string? language, string? theme)
    {
        var settings = new Settings();
        Assert.False(InstallerChoices.Adopt(settings, language, theme, "de"));
        Assert.Null(settings.Language);
        Assert.Null(settings.Theme);
    }

    /// <summary>
    /// Installersprache = Windows-Sprache: nichts speichern – null (nie gewählt) ergibt dieselbe Sprache und folgt
    /// Windows weiter, auch wenn später ein Setup mit anderer Windows-Sprache läuft.
    /// </summary>
    [Fact]
    public void InstallerSpracheWieWindowsBleibtAutomatisch()
    {
        var settings = new Settings();
        Assert.True(InstallerChoices.Adopt(settings, "de", "light", "de"));
        Assert.Null(settings.Language);
        Assert.Equal("light", settings.Theme);
        Assert.False(InstallerChoices.Adopt(settings, "ja", "dark", "ja"));
        Assert.Null(settings.Language);
        CultureInfo.CurrentUICulture = new CultureInfo("ja-JP");
        Tr.Init(settings.Language);
        Assert.Equal("ja", Tr.Lang);
    }

    [Fact]
    public void AbweichendeInstallerSpracheWirdUebernommen()
    {
        var settings = new Settings();
        Assert.True(InstallerChoices.Adopt(settings, "en", null, "de"));
        Assert.Equal("en", settings.Language);
        Assert.Null(settings.Theme);
    }

    [Fact]
    public void SprachlisteSpeichertAutomatischAusdruecklich()
    {
        Assert.Equal("auto", SettingsForm.LanguageCode(0));
        Assert.Equal("auto", SettingsForm.LanguageCode(-1));
        Assert.Equal("auto", SettingsForm.LanguageCode(99));
        Assert.Equal(Tr.Supported, Enumerable.Range(1, Tr.Supported.Length).Select(SettingsForm.LanguageCode));
    }

    /// <summary>
    /// Englisches Setup auf deutschem Windows, danach in der App „Automatisch“ gewählt: Ein späteres Setup, das wieder
    /// Englisch übergibt, ändert daran nichts – die App folgt weiter Windows.
    /// </summary>
    [Fact]
    public void InDerAppGewaehltesAutomatischBleibtTrotzAbweichenderInstallerSprache()
    {
        var settings = new Settings();
        Assert.True(InstallerChoices.Adopt(settings, "en", "dark", "de"));
        Assert.Equal("en", settings.Language);
        settings.Language = SettingsForm.LanguageCode(0);
        Assert.False(InstallerChoices.Adopt(settings, "en", "light", "de"));
        Assert.Equal("auto", settings.Language);
        Assert.Equal("dark", settings.Theme);
        CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
        Tr.Init(settings.Language);
        Assert.Equal("de", Tr.Lang);
    }

    [Fact]
    public void AutomatischBleibtNachSpeichernUndLadenErhalten()
    {
        string dir = Path.Combine(Path.GetTempPath(), "skzconnect-bridge-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new Settings { Language = "auto", Theme = "dark" }.Save(path);
            var loaded = Settings.Load(path);
            Assert.Null(loaded.LoadError);
            Assert.Equal("auto", loaded.Language);
            Assert.Equal("dark", loaded.Theme);
            File.WriteAllText(path, """{ "Language": "xx" }""");
            Assert.Null(Settings.Load(path).Language);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* bleibt im Temp-Ordner */ }
        }
    }

    /// <summary>
    /// Setup und App passen zusammen: dieselben Argumente, nur Sprachcodes mit Tabelle, genau die drei Darstellungen
    /// und der Deinstallationsschlüssel zur AppId (dort liest die App beim ersten Start die gemerkte Wahl).
    /// </summary>
    [Fact]
    public void InstallerSkriptPasstZurApp()
    {
        string iss = File.ReadAllText(Path.Combine(Repo.Root(), "installer", "SKZ-Connect.iss"));
        Assert.Contains("Result := '--setup-theme=' + ThemeValue;", iss);
        Assert.Contains("Result := '--setup-lang=' + ChosenLangCode + ' ' + Result;", iss);
        Assert.DoesNotContain("'--setup-lang=' + InstallerLangCode", iss);
        Assert.Contains("SetPreviousData(PreviousDataKey, 'Theme', ThemeValue);", iss);
        Assert.Contains("SetPreviousData(PreviousDataKey, 'Language', ChosenLangCode);", iss);

        var appId = Regex.Match(iss, @"^AppId=\{(\{[0-9A-F-]+\})", RegexOptions.Multiline);
        Assert.True(appId.Success, "AppId nicht gefunden");
        Assert.EndsWith($@"\{appId.Groups[1].Value}_is1", InstallerChoices.SetupKey);

        string languages = Regex.Match(iss, @"function InstallerLangCode: String;.*?\nend;", RegexOptions.Singleline).Value;
        var codes = Regex.Matches(languages, @"Result := '([a-z]+)'").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(Tr.Supported.Order(), codes.Distinct().Order());

        string themes = Regex.Match(iss, @"function ThemeValue: String;.*?\nend;", RegexOptions.Singleline).Value;
        Assert.Equal(["dark", "light", "system"], Regex.Matches(themes, @"Result := '([a-z]+)'").Select(m => m.Groups[1].Value).Order());
    }

    /// <summary>
    /// Das Setup erkennt eine bewusst gewählte Sprache (ChosenLangCode) nur richtig, wenn es die Windows-Sprache so
    /// bestimmt wie die App (Tr.WindowsLanguage), Englisch sein Vorschlag ohne passende Sprache ist (erster Eintrag) und
    /// SetupLangs genau die Sprachen sammelt, die es tatsächlich anbietet.
    /// </summary>
    [Fact]
    public void InstallerKenntSeineSprachenWieDieApp()
    {
        // Zeilenenden vereinheitlichen (Checkout mit core.autocrlf, z. B. auf dem Windows-Runner der CI).
        string iss = File.ReadAllText(Path.Combine(Repo.Root(), "installer", "SKZ-Connect.iss")).Replace("\r\n", "\n");

        string windows = Regex.Match(iss, @"function WindowsLangCode: String;.*?\nend;", RegexOptions.Singleline).Value;
        var lcids = Regex.Matches(windows, @"\$([0-9A-F]{2}): Result := '([a-z]+)'").ToList();
        Assert.Equal(Tr.Supported.Where(l => l != "en").Order(), lcids.Select(m => m.Groups[2].Value).Order());
        foreach (var m in lcids)
            Assert.Equal(m.Groups[2].Value, CultureInfo.GetCultureInfo(Convert.ToInt32(m.Groups[1].Value, 16)).TwoLetterISOLanguageName);
        Assert.Matches(@"else\s+Result := 'en';", windows);

        string languages = Regex.Match(iss, @"^\[Languages\]$.*?^\[", RegexOptions.Singleline | RegexOptions.Multiline).Value;
        Assert.Matches(@"^Name: ""english""; MessagesFile: ""compiler:Default.isl""", languages.Split('\n').First(l => l.StartsWith("Name:")));

        // Name der Sprache im Setup → Sprachcode der App (InstallerLangCode; „english“ ist der else-Zweig).
        string mapping = Regex.Match(iss, @"function InstallerLangCode: String;.*?\nend;", RegexOptions.Singleline).Value;
        var codeOf = new Dictionary<string, string> { ["english"] = "en" };
        foreach (Match line in Regex.Matches(mapping, @"^.*L = '.*Result := '([a-z]+)'.*$", RegexOptions.Multiline))
            foreach (Match name in Regex.Matches(line.Value, @"L = '(\w+)'"))
                codeOf[name.Groups[1].Value] = line.Groups[1].Value;

        var always = Regex.Matches(languages, @"^Name: ""(\w+)"";", RegexOptions.Multiline).Select(m => m.Groups[1].Value)
            .Except(Regex.Matches(languages, @"#if [^\n]*\nName: ""(\w+)"";").Select(m => m.Groups[1].Value)).ToList();
        Assert.Equal(["english", "german"], always);
        Assert.Contains(@"#define SetupLangs ""en de""", languages);
        var optional = Regex.Matches(languages,
            @"#if FileExists\(InnoLangDir \+ ""\\\w+\.isl""\)\nName: ""(\w+)"";[^\n]*\n\s*#define SetupLangs SetupLangs \+ "" ([a-z]+)""\n#endif").ToList();
        Assert.Equal(Regex.Matches(languages, "#if ").Count, optional.Count);
        foreach (var m in optional)
            Assert.Equal(codeOf[m.Groups[1].Value], m.Groups[2].Value);
        Assert.Equal(Tr.Supported.Order(), always.Concat(optional.Select(m => m.Groups[1].Value)).Select(n => codeOf[n]).Distinct().Order());
    }

    /// <summary>
    /// Bewusst gewählt ist jede Setup-Sprache, die vom Vorschlag des Setups abweicht – auch wenn es die Windows-Sprache gar
    /// nicht anbot (traditionelles Chinesisch, Chinesisch ohne ISL-Datei): Wer dort Deutsch wählt, bekommt Deutsch statt
    /// der vereinfacht-chinesischen Tabelle. Verworfen wird nur der Vorschlag selbst (Windows-Sprache bzw. Englisch).
    /// </summary>
    [Fact]
    public void InstallerVerwirftNurDenEigenenVorschlag()
    {
        string iss = File.ReadAllText(Path.Combine(Repo.Root(), "installer", "SKZ-Connect.iss")).Replace("\r\n", "\n");
        string chosen = Regex.Match(iss, @"function ChosenLangCode: String;.*?\nend;", RegexOptions.Singleline).Value;
        Assert.NotEqual("", chosen);
        var proposal = Regex.Match(chosen,
            @"if \(Pos\(' ' \+ WinLang \+ ' ', ' \{#SetupLangs\} '\) > 0\)\s*and not \(\(WinLang = 'zh'\) and \((?<hant>[^\n]*)\)\) then\s*" +
            @"Proposal := WinLang\s*else\s*Proposal := 'en';\s*Result := InstallerLangCode;\s*if Result = Proposal then\s*Result := '';\s*end;$");
        Assert.True(proposal.Success, chosen);
        // Alle Kennungen für traditionelles Chinesisch (Taiwan, Hongkong, Macau, neutral) – Vorschlag dort Englisch.
        var hant = Regex.Matches(proposal.Groups["hant"].Value, @"Lang = \$([0-9A-F]{4})").Select(m => Convert.ToInt32(m.Groups[1].Value, 16)).ToList();
        Assert.Equal([0x0404, 0x0C04, 0x1404, 0x7C04], hant);
        foreach (int lcid in hant)
        {
            var culture = CultureInfo.GetCultureInfo(lcid);
            Assert.True(culture.Name == "zh-Hant" || culture.Parent.Name == "zh-Hant", $"{lcid:X4}: {culture.Name}");
        }
        // Nur eine Stelle setzt die Sprache auf „nichts übergeben“.
        Assert.Single(Regex.Matches(chosen, @"Result := '';"));
    }

    [Fact]
    public void GrossschreibungIstEgal()
    {
        var settings = new Settings();
        Assert.True(InstallerChoices.Adopt(settings, "PT", "System", "de"));
        Assert.Equal("pt", settings.Language);
        Assert.Equal("system", settings.Theme);
    }
}
