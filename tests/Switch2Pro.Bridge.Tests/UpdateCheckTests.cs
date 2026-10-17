using System.Reflection;
using System.Xml.Linq;

namespace Switch2Pro.Bridge.Tests;

/// <summary>Versionen der Update-Prüfung: CI-Builds ohne Tag („1.0.22-ci.7“) sind Vorabversionen des nächsten Releases
/// und dürfen nie neuer aussehen als dieses Release.</summary>
public sealed class UpdateCheckTests
{
    private static readonly Version Fallback = new(9, 8, 7, 6);

    [Theory]
    [InlineData("1.0.22", "1.0.22", false, "1.0.22")]
    [InlineData("1.0.22+3f2a9c1", "1.0.22", false, "1.0.22")]
    [InlineData("1.0.22-ci.57", "1.0.22", true, "1.0.22-ci.57")]
    [InlineData("1.0.22-ci.57+3f2a9c1d", "1.0.22", true, "1.0.22-ci.57")]
    [InlineData(" 1.0.22-beta.1 ", "1.0.22", true, "1.0.22-beta.1")]
    public void ParseBuild_LiestZahlVorabversionUndAnzeige(string informational, string version, bool prerelease, string label)
    {
        var build = UpdateCheck.ParseBuild(informational, Fallback);
        Assert.Equal(Version.Parse(version), build.Version);
        Assert.Equal(prerelease, build.Prerelease);
        Assert.Equal(label, build.Label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ci-build")]
    public void ParseBuild_UnlesbarNimmtAssemblyVersion(string? informational)
    {
        var build = UpdateCheck.ParseBuild(informational, Fallback);
        Assert.Equal(Fallback, build.Version);
        Assert.False(build.Prerelease);
        Assert.Equal("9.8.7", build.Label);
    }

    [Theory]
    // Vorabversion 1.0.22-ci.N: das Release 1.0.22 ist neuer, 1.0.21 nicht.
    [InlineData("1.0.22", "1.0.22", true, true)]
    [InlineData("1.0.22.0", "1.0.22", true, true)]
    [InlineData("1.0.23", "1.0.22", true, true)]
    [InlineData("1.0.21", "1.0.22", true, false)]
    // Release-Build: nur höhere Versionen; 1.2 und 1.2.0 gelten als gleich.
    [InlineData("1.0.22", "1.0.22", false, false)]
    [InlineData("1.0.22.0", "1.0.22", false, false)]
    [InlineData("1.0", "1.0.0", false, false)]
    [InlineData("1.0.23", "1.0.22", false, true)]
    [InlineData("1.1", "1.0.99", false, true)]
    [InlineData("1.0.21", "1.0.22", false, false)]
    public void IsNewer_VorabversionAelterAlsGleichnamigesRelease(string release, string current, bool prerelease, bool expected)
        => Assert.Equal(expected, UpdateCheck.IsNewer(Version.Parse(release), Version.Parse(current), prerelease));

    [Fact]
    public void EigeneVersion_IstDieAusSwitch2ProBridgeCsproj()
    {
        // Die Zahl kommt aus der Projektdatei (der Build-Workflow hängt höchstens „-ci.N“ an), die Anzeige ohne „+Commit“.
        var csproj = XDocument.Load(Path.Combine(Repo.Root(), "src", "Switch2Pro.Bridge", "Switch2Pro.Bridge.csproj"));
        string version = csproj.Descendants("Version").Single().Value;
        string? informational = typeof(UpdateCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.NotNull(informational);
        Assert.Equal(Version.Parse(version), UpdateCheck.Current);
        Assert.Equal(informational!.Split('+')[0], UpdateCheck.Label);
        Assert.StartsWith(version, UpdateCheck.Label, StringComparison.Ordinal);
    }
}
