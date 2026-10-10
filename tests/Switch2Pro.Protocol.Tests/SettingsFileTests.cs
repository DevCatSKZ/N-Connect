using Switch2Pro.Protocol;
using Xunit;

namespace Switch2Pro.Protocol.Tests;

/// <summary>Sicheres Speichern der Einstellungen: keine halbe Datei, Sicherung, Wiederherstellung.</summary>
public class SettingsFileTests
{
    private static string NewFolder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nconnect-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Speichern_HinterlaesstKeineTemporaereDatei_UndLegtSicherungAn()
    {
        string dir = NewFolder();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new Settings { InactivityMinutes = 10 }.Save(path);
            new Settings { InactivityMinutes = 20 }.Save(path);

            Assert.False(File.Exists(path + ".tmp"));
            Assert.Equal(20, Settings.Load(path).InactivityMinutes);
            // Sicherung = vorherige Fassung
            Assert.Equal(10, Settings.Load(Settings.BackupPath(path)).InactivityMinutes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Laden_NimmtSicherung_WennDateiBeschaedigt()
    {
        string dir = NewFolder();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new Settings { InactivityMinutes = 15 }.Save(path);
            new Settings { InactivityMinutes = 30 }.Save(path);
            File.WriteAllText(path, "{ \"InactivityMinutes\": 3"); // halb geschrieben (z. B. Stromausfall)

            var loaded = Settings.Load(path);

            Assert.Null(loaded.LoadError);
            Assert.Equal(15, loaded.InactivityMinutes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Laden_MeldetFehler_WennDateiUndSicherungFehlen()
    {
        string dir = NewFolder();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, ""); // leere Datei, keine Sicherung

            var loaded = Settings.Load(path);

            Assert.NotNull(loaded.LoadError);
            Assert.Equal("", File.ReadAllText(path)); // kaputte Datei bleibt zur Untersuchung erhalten
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
