using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge.Tests;

/// <summary>Einstellungsdateien älterer Versionen laden weiter, auch mit Feldern des entfernten Funkadapters.</summary>
public sealed class LegacySettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "skzconnect-bridge-tests-" + Guid.NewGuid().ToString("N"));

    public LegacySettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* Testordner bleibt im Temp-Ordner */ }
    }

    [Fact]
    public void AlteFunkadapterFelder_WerdenUeberlesen()
    {
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """
            {
              "UseAdapter": true,
              "AdapterPort": "COM15",
              "Language": "fr",
              "HideFromGames": false,
              "KnownControllers": [ "AA:BB:CC:00:11:22" ]
            }
            """);
        var settings = Settings.Load(path);
        Assert.Null(settings.LoadError);
        Assert.Equal("fr", settings.Language);
        Assert.False(settings.HideFromGames);
        Assert.Contains("AA:BB:CC:00:11:22", settings.KnownControllers);
    }

    [Fact]
    public void Speichern_SchreibtKeineFunkadapterFelderMehr()
    {
        string path = Path.Combine(_dir, "settings.json");
        new Settings().Save(path);
        string json = File.ReadAllText(path);
        Assert.DoesNotContain("UseAdapter", json, StringComparison.Ordinal);
        Assert.DoesNotContain("AdapterPort", json, StringComparison.Ordinal);
    }
}
