using System.Globalization;
using System.Text.RegularExpressions;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge.Tests;

/// <summary>
/// Sprache und Darstellung: „Automatisch“ folgt der Windows-Sprache, „Dunkel“ wird ausdrücklich gespeichert, und die
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
        string[] args = [@"C:\Program Files\N-Connect\N-Connect.exe", "--setup-lang=fr", "--setup-theme=light"];
        Assert.Equal("fr", InstallerChoices.Arg(args, "--setup-lang="));
        Assert.Equal("light", InstallerChoices.Arg(args, "--setup-theme="));
        Assert.Null(InstallerChoices.Arg([@"C:\Program Files\N-Connect\N-Connect.exe", "--autostart"], "--setup-lang="));
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
    /// Installersprache = Windows-Sprache: nichts speichern – „Automatisch“ (null) ergibt dieselbe Sprache und folgt
    /// Windows weiter, auch wenn es in der App ausdrücklich gewählt war und später ein Setup läuft.
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

    /// <summary>
    /// Setup und App passen zusammen: dieselben Argumente, nur Sprachcodes mit Tabelle, genau die drei Darstellungen
    /// und der Deinstallationsschlüssel zur AppId (dort liest die App beim ersten Start die gemerkte Wahl).
    /// </summary>
    [Fact]
    public void InstallerSkriptPasstZurApp()
    {
        string iss = File.ReadAllText(Path.Combine(Repo.Root(), "installer", "N-Connect.iss"));
        Assert.Contains("'--setup-lang=' + InstallerLangCode + ' --setup-theme=' + ThemeValue", iss);
        Assert.Contains("SetPreviousData(PreviousDataKey, 'Theme', ThemeValue);", iss);
        Assert.Contains("SetPreviousData(PreviousDataKey, 'Language', InstallerLangCode);", iss);

        var appId = Regex.Match(iss, @"^AppId=\{(\{[0-9A-F-]+\})", RegexOptions.Multiline);
        Assert.True(appId.Success, "AppId nicht gefunden");
        Assert.EndsWith($@"\{appId.Groups[1].Value}_is1", InstallerChoices.SetupKey);

        string languages = Regex.Match(iss, @"function InstallerLangCode: String;.*?\nend;", RegexOptions.Singleline).Value;
        var codes = Regex.Matches(languages, @"Result := '([a-z]+)'").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(Tr.Supported.Order(), codes.Distinct().Order());

        string themes = Regex.Match(iss, @"function ThemeValue: String;.*?\nend;", RegexOptions.Singleline).Value;
        Assert.Equal(["dark", "light", "system"], Regex.Matches(themes, @"Result := '([a-z]+)'").Select(m => m.Groups[1].Value).Order());
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
