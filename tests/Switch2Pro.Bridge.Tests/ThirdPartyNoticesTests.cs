using System.Text.RegularExpressions;

namespace Switch2Pro.Bridge.Tests;

/// <summary>
/// THIRD-PARTY-NOTICES.md: liegt jeder Weitergabe bei (Setup, APK), nennt jedes mitgelieferte Paket mit der Version aus
/// den Projektdateien, und jede dort als „nach C# übertragen“ geführte Datei trägt den SDL-Herkunftsvermerk.
/// </summary>
public sealed partial class ThirdPartyNoticesTests
{
    private static string RepoFile(params string[] parts) => Path.Combine([Repo.Root(), .. parts]);

    private static string Notices() => File.ReadAllText(RepoFile(SettingsForm.NoticesFile)).Replace("\r\n", "\n");

    [Fact]
    public void SetupUndApkLiefernDieHinweiseMit()
    {
        Assert.Equal("THIRD-PARTY-NOTICES.md", SettingsForm.NoticesFile);
        Assert.True(File.Exists(RepoFile(SettingsForm.NoticesFile)));
        // Das Setup legt sie neben die EXE – dort sucht „Lizenzen Dritter“ (Allgemein → Erweitert).
        string iss = File.ReadAllText(RepoFile("installer", "SKZ-Connect.iss"));
        Assert.Contains($"Source: \"..\\{SettingsForm.NoticesFile}\"; DestDir: \"{{app}}\"", iss);
        // Android: dieselbe Datei als Asset (Hilfe → Lizenzen), nicht als zweite Kopie im Projekt.
        string android = File.ReadAllText(RepoFile("android", "NConnect.Android", "NConnect.Android.csproj"));
        Assert.Contains($"<AndroidAsset Include=\"..\\..\\{SettingsForm.NoticesFile}\"", android);
        Assert.False(File.Exists(RepoFile("android", "NConnect.Android", "Assets", SettingsForm.NoticesFile)));
    }

    /// <summary>Projekte, deren Pakete mit SKZ Connect weitergegeben werden (ohne Tests und Prüfwerkzeuge).</summary>
    public static TheoryData<string> ShippedProjects() =>
    [
        "src/Switch2Pro.Bridge/Switch2Pro.Bridge.csproj",
        "src/Switch2Pro.Protocol/Switch2Pro.Protocol.csproj",
        "android/NConnect.Android/NConnect.Android.csproj",
        "linux/NConnect.Linux/NConnect.Linux.csproj",
    ];

    [Theory]
    [MemberData(nameof(ShippedProjects))]
    public void JedesMitgeliefertePaketStehtMitVersionInDenHinweisen(string project)
    {
        string csproj = File.ReadAllText(RepoFile(project.Split('/')));
        string notices = Notices();
        foreach (Match package in PackageReference().Matches(csproj))
        {
            string name = package.Groups["name"].Value, version = package.Groups["version"].Value;
            // Name und Version in einer Zeile (Übersicht oder „NuGet Name Version“).
            Assert.True(Regex.IsMatch(notices, $@"{Regex.Escape(name)}[^\n]*?(?<![\w.]){Regex.Escape(version)}(?![\w.])"),
                $"{project}: {name} {version} fehlt in THIRD-PARTY-NOTICES.md");
        }
        foreach (Match maven in MavenLibrary().Matches(csproj))
        {
            string group = maven.Groups["group"].Value, artifact = maven.Groups["artifact"].Value, version = maven.Groups["version"].Value;
            string section = Regex.Match(notices, $@"`{Regex.Escape(group)}:[^\n]*(\n  [^\n]*)*").Value;
            Assert.True(section.Contains($":{artifact}`", StringComparison.Ordinal) && section.Contains(version, StringComparison.Ordinal),
                $"{project}: {group}:{artifact} {version} fehlt in THIRD-PARTY-NOTICES.md");
        }
        Assert.DoesNotContain("System.IO.Ports", csproj); // Funkadapter entfernt
    }

    [Fact]
    public void FunkadapterIstAusDenHinweisenEntfernt() => Assert.DoesNotContain("System.IO.Ports", Notices());

    /// <summary>zlib-Lizenz, Klausel 2: geänderte Fassungen deutlich kennzeichnen.</summary>
    [Fact]
    public void SdlAbgeleiteteDateienTragenDenHerkunftsvermerk()
    {
        string sdl = Regex.Match(Notices(), @"### B\.1 SDL.*?\n### ", RegexOptions.Singleline).Value;
        var files = SourcePath().Matches(sdl).Select(m => m.Groups["path"].Value).Distinct().ToList();
        Assert.True(files.Count >= 7, $"nur {files.Count} Dateien in B.1 gefunden");
        foreach (string file in files)
        {
            string path = file.StartsWith("Links/", StringComparison.Ordinal)
                ? RepoFile("src", "Switch2Pro.Bridge", "Links", file["Links/".Length..])
                : RepoFile("src", file);
            Assert.True(File.Exists(path), $"{file} aus B.1 gibt es nicht");
            Assert.StartsWith("// Teile nach SDL (zlib-Lizenz), angepasst – siehe THIRD-PARTY-NOTICES.md", File.ReadLines(path).First());
        }
    }

    [GeneratedRegex("<PackageReference Include=\"(?<name>[^\"]+)\" Version=\"(?<version>[^\"]+)\"")]
    private static partial Regex PackageReference();

    [GeneratedRegex("<AndroidMavenLibrary Include=\"(?<group>[^\":]+):(?<artifact>[^\"]+)\" Version=\"(?<version>[^\"]+)\"")]
    private static partial Regex MavenLibrary();

    [GeneratedRegex(@"`(?<path>(?:Switch2Pro\.Protocol|Links)/\w+\.cs)`")]
    private static partial Regex SourcePath();
}
