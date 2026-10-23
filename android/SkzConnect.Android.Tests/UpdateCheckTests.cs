using System.Text.Json;
using SkzConnect.Android.Core;

namespace SkzConnect.Android.Tests;

/// <summary>
/// Update-Prüfung: Die APKs hängen an den Windows-Releases (Tag v1.0.x) – verglichen wird die Version im Dateinamen,
/// Releases ohne APK zählen nicht (Befund AN-1/RD-4: vorher meldete jede Prüfung „1.0.21 verfügbar“).
/// </summary>
public class UpdateCheckTests
{
    private static string Asset(string name, string? url = null, long size = 1000, string? digest = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["name"] = name,
            ["browser_download_url"] = url ?? $"https://github.com/DevCatSKZ/SKZ-Connect/releases/download/x/{name}",
            ["size"] = size,
            ["digest"] = digest,
        });

    private static string Release(string tag, bool draft = false, bool prerelease = false, params string[] assets) =>
        $"{{\"tag_name\":\"{tag}\",\"draft\":{(draft ? "true" : "false")},\"prerelease\":{(prerelease ? "true" : "false")}," +
        $"\"html_url\":\"https://github.com/DevCatSKZ/SKZ-Connect/releases/tag/{tag}\",\"assets\":[{string.Join(",", assets)}]}}";

    private static UpdateCheck.Update? Newest(params string[] releases)
    {
        using var doc = JsonDocument.Parse($"[{string.Join(",", releases)}]");
        return UpdateCheck.Newest(doc.RootElement);
    }

    [Theory]
    [InlineData("SKZ-Connect-Android-0.9.1.apk", "0.9.1")]
    [InlineData("n-connect-android-1.2.apk", "1.2")]
    [InlineData("SKZ-Connect-Android-0.10.0.1.apk", "0.10.0.1")]
    public void VersionOf_LiestVersionAusDemDateinamen(string name, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateCheck.VersionOf(name));

    [Theory]
    [InlineData("SKZ-Connect-Setup-1.0.21.exe")]
    [InlineData("SKZ-Connect-Android.apk")]
    [InlineData("SKZ-Connect-Android-0.9.1.apk.sha256")]
    [InlineData("SKZ-Connect-Android-latest.apk")]
    [InlineData(null)]
    public void VersionOf_IgnoriertAndereDateien(string? name) => Assert.Null(UpdateCheck.VersionOf(name));

    [Fact]
    public void HeutigerStand_KeinUpdateFuer090()
    {
        // v1.0.21 ohne APK, v1.0.20 mit 0.9.0 – installiert 0.9.0: kein Update (vorher: „1.0.21 verfügbar“).
        var best = Newest(
            Release("v1.0.21", assets: Asset("SKZ-Connect-Setup-1.0.21.exe")),
            Release("v1.0.20", assets: [Asset("SKZ-Connect-Setup-1.0.20.exe"), Asset("SKZ-Connect-Android-0.9.0.apk")]));
        Assert.NotNull(best);
        Assert.Equal(new Version(0, 9, 0), best.Version);
        Assert.False(UpdateCheck.IsNewer(best.Version, new Version(0, 9, 0)));
        Assert.True(UpdateCheck.IsNewer(best.Version, new Version(0, 8, 5)));
    }

    [Fact]
    public void NeuesteApk_ZaehltNachDateiname_NichtNachTag()
    {
        var best = Newest(
            Release("v1.0.23", assets: Asset("SKZ-Connect-Setup-1.0.23.exe")),
            Release("v1.0.22", assets: Asset("SKZ-Connect-Android-0.9.1.apk", digest: "sha256:ABCDEF", size: 123456)),
            Release("v1.0.20", assets: Asset("SKZ-Connect-Android-0.9.0.apk")));
        Assert.NotNull(best);
        Assert.Equal(new Version(0, 9, 1), best.Version);
        Assert.Equal("SKZ-Connect-Android-0.9.1.apk", best.File.Name);
        Assert.Equal(123456, best.File.Size);
        Assert.Equal("ABCDEF", best.File.Sha256);
        Assert.Equal("https://github.com/DevCatSKZ/SKZ-Connect/releases/tag/v1.0.22", best.Url);
        // Nach der Installation von 0.9.1 wird dieselbe APK nicht erneut angeboten.
        Assert.False(UpdateCheck.IsNewer(best.Version, new Version(0, 9, 1)));
    }

    [Fact]
    public void Reihenfolge_DerReleasesSpieltKeineRolle()
    {
        var best = Newest(
            Release("v1.0.20", assets: Asset("SKZ-Connect-Android-0.9.0.apk")),
            Release("v1.0.25", assets: Asset("SKZ-Connect-Android-0.10.0.apk")),
            Release("v1.0.22", assets: Asset("SKZ-Connect-Android-0.9.1.apk")));
        Assert.Equal(new Version(0, 10, 0), best!.Version);
    }

    [Fact]
    public void EntwuerfeVorabversionenUndFremdeAdressen_ZaehlenNicht()
    {
        var best = Newest(
            Release("v1.0.30", draft: true, assets: Asset("SKZ-Connect-Android-0.12.0.apk")),
            Release("v1.0.29", prerelease: true, assets: Asset("SKZ-Connect-Android-0.11.0.apk")),
            Release("v1.0.28", assets: Asset("SKZ-Connect-Android-0.10.5.apk", url: "https://example.com/SKZ-Connect-Android-0.10.5.apk")),
            Release("v1.0.28", assets: Asset("SKZ-Connect-Android-0.10.4.apk", url: "http://github.com/x/SKZ-Connect-Android-0.10.4.apk")),
            Release("v1.0.22", assets: Asset("SKZ-Connect-Android-0.9.1.apk")));
        Assert.Equal(new Version(0, 9, 1), best!.Version);
    }

    [Fact]
    public void OhneApk_Null()
    {
        Assert.Null(Newest(Release("v1.0.21", assets: Asset("SKZ-Connect-Setup-1.0.21.exe")), Release("v1.0.19")));
        Assert.Null(Newest());
    }

    [Fact]
    public void KaputteEintraege_WerdenUebersprungen()
    {
        var best = Newest("{\"tag_name\":\"v1\",\"assets\":\"kein Array\"}", "{\"assets\":[{\"name\":42}]}", "17",
            Release("v1.0.22", assets: Asset("SKZ-Connect-Android-0.9.1.apk")));
        Assert.Equal(new Version(0, 9, 1), best!.Version);
    }

    [Theory]
    [InlineData("0.9.1", "0.9.1.0", false)]
    [InlineData("0.9", "0.9.0", false)]
    [InlineData("0.9.2", "0.9.1", true)]
    [InlineData("0.10.0", "0.9.9", true)]
    [InlineData("0.9.0", "1.0.21", false)]
    public void IsNewer_VergleichtNormalisiert(string available, string installed, bool expected) =>
        Assert.Equal(expected, UpdateCheck.IsNewer(Version.Parse(available), Version.Parse(installed)));

    [Theory]
    [InlineData("https://github.com/DevCatSKZ/SKZ-Connect/releases/download/v1/a.apk", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/a.apk", true)]
    [InlineData("http://github.com/a.apk", false)]
    [InlineData("https://github.com.example.org/a.apk", false)]
    [InlineData("https://evilgithubusercontent.com/a.apk", false)]
    public void IsGitHub_NurHttpsVonGitHub(string url, bool expected) => Assert.Equal(expected, UpdateCheck.IsGitHub(url));
}
