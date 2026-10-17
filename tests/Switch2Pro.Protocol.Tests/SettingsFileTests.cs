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
            Assert.True(loaded.RestoredFromBackup); // die App soll darauf hinweisen können
            Assert.Equal(15, loaded.InactivityMinutes);
            Assert.False(Settings.Load(Settings.BackupPath(path)).RestoredFromBackup);
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
            Assert.False(loaded.RestoredFromBackup);
            Assert.Equal("", File.ReadAllText(path)); // kaputte Datei bleibt zur Untersuchung erhalten
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string[] Unreadable(string dir, string prefix) =>
        Directory.GetFiles(dir, prefix + ".unlesbar-*").OrderBy(f => f, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Speichern_nach_Ladefehler_sichert_die_unlesbare_Datei_einmalig()
    {
        string dir = NewFolder();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            const string broken = "{ \"InactivityMinutes\": 3, kaputt";
            File.WriteAllText(path, broken);

            var loaded = Settings.Load(path);
            Assert.NotNull(loaded.LoadError);
            Assert.Empty(Unreadable(dir, "settings.json")); // Laden allein legt nichts an
            loaded.InactivityMinutes = 20;
            loaded.Save(path);   // z. B. HidHide-Version beim Start
            loaded.Save(path);   // z. B. beim ersten Controller – früher war die Originaldatei spätestens jetzt weg

            string copy = Assert.Single(Unreadable(dir, "settings.json"));
            Assert.Equal(broken, File.ReadAllText(copy));
            Assert.Equal(copy, loaded.UnreadableCopy);
            Assert.Matches(@"settings\.json\.unlesbar-\d{8}-\d{6}$", copy);
            Assert.False(File.Exists(path + ".tmp"));
            // Danach wird ganz normal gespeichert.
            var again = Settings.Load(path);
            Assert.Null(again.LoadError);
            Assert.Equal(20, again.InactivityMinutes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Speichern_nach_Ladefehler_sichert_auch_die_unlesbare_Sicherung()
    {
        string dir = NewFolder();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{ kaputt");
            File.WriteAllText(Settings.BackupPath(path), "{ auch kaputt");

            var loaded = Settings.Load(path);
            Assert.NotNull(loaded.LoadError);
            loaded.Save(path);
            loaded.Save(path);

            Assert.Equal("{ kaputt", File.ReadAllText(Assert.Single(Unreadable(dir, "settings.json"))));
            Assert.Equal("{ auch kaputt", File.ReadAllText(Assert.Single(Unreadable(dir, "settings.json.bak"))));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Speichern_nach_Laden_der_Sicherung_bewahrt_die_unlesbare_Hauptdatei()
    {
        string dir = NewFolder();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new Settings { InactivityMinutes = 15 }.Save(path);
            new Settings { InactivityMinutes = 30 }.Save(path);
            File.WriteAllText(path, "{ \"InactivityMinutes\": 45, "); // z. B. Tippfehler beim Bearbeiten von Hand

            var loaded = Settings.Load(path);
            Assert.True(loaded.RestoredFromBackup);
            loaded.Save(path);
            loaded.Save(path);

            Assert.Equal("{ \"InactivityMinutes\": 45, ", File.ReadAllText(Assert.Single(Unreadable(dir, "settings.json"))));
            Assert.Empty(Unreadable(dir, "settings.json.bak")); // die Sicherung war lesbar, sie steckt im Ergebnis
            Assert.Equal(15, Settings.Load(path).InactivityMinutes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Uebernommene_Werte_sichern_die_unlesbare_Datei_beim_Speichern()
    {
        // Wie TrayApp.ReloadSettings: neu laden (nur die Sicherung lesbar) und in das laufende Objekt übernehmen.
        string dir = NewFolder();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            var running = new Settings { InactivityMinutes = 15 };
            running.Save(path);
            running.Save(path);
            File.WriteAllText(path, "{ von Hand kaputt");

            running.CopyFrom(Settings.Load(path));
            running.Save(path);

            Assert.Equal("{ von Hand kaputt", File.ReadAllText(Assert.Single(Unreadable(dir, "settings.json"))));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]  // nur die Sicherung lesbar (RestoredFromBackup)
    [InlineData(false)] // beide unlesbar (LoadError)
    public void Verworfenes_Neuladen_behaelt_die_Werte_und_sichert_die_unlesbare_Datei(bool backupReadable)
    {
        // Wie ein Neuladen im Betrieb, das die App verwirft: Der laufende Stand (30) ist neuer als die Sicherung (15).
        string dir = NewFolder();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            var running = new Settings { InactivityMinutes = 15 };
            running.Save(path);
            running.InactivityMinutes = 30;
            running.Save(path);
            File.WriteAllText(path, "{ von Hand kaputt");
            if (!backupReadable)
                File.WriteAllText(Settings.BackupPath(path), "{ auch kaputt");

            var fresh = Settings.Load(path);
            Assert.Equal(backupReadable, fresh.RestoredFromBackup);
            Assert.Equal(!backupReadable, fresh.LoadError is not null);
            running.KeepUnreadableOf(fresh);
            Assert.Empty(Unreadable(dir, "settings.json")); // erst beim Speichern
            running.Save(path);
            running.Save(path);

            Assert.Equal("{ von Hand kaputt", File.ReadAllText(Assert.Single(Unreadable(dir, "settings.json"))));
            Assert.Equal(backupReadable ? 0 : 1, Unreadable(dir, "settings.json.bak").Length);
            Assert.Equal(running.UnreadableCopy, Assert.Single(Unreadable(dir, "settings.json")));
            var again = Settings.Load(path);
            Assert.False(again.RestoredFromBackup);
            Assert.Equal(30, again.InactivityMinutes); // nicht auf die Sicherung zurückgefallen
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Speichern_an_anderer_Stelle_sichert_die_unlesbare_Datei_ebenfalls_vorher()
    {
        string dir = NewFolder();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{ kaputt");
            var loaded = Settings.Load(path);

            loaded.Save(Path.Combine(dir, "export.json")); // z. B. „Sicherung anlegen“ im Fenster Kopplungsdaten
            Assert.Equal("{ kaputt", File.ReadAllText(path));
            Assert.Single(Unreadable(dir, "settings.json"));
            loaded.Save(path);
            Assert.Single(Unreadable(dir, "settings.json")); // einmalig
            Assert.Null(Settings.Load(path).LoadError);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Speichern_ueberschreibt_nichts_wenn_das_Sichern_scheitert()
    {
        if (!OperatingSystem.IsWindows())
            return; // gesperrte Dateien lassen sich nur unter Windows nicht kopieren
        string dir = NewFolder();
        try
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{ kaputt");
            var loaded = Settings.Load(path);
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.ThrowsAny<IOException>(() => loaded.Save(path));
            Assert.Equal("{ kaputt", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Empty(Unreadable(dir, "settings.json"));

            loaded.Save(path); // Sperre weg: jetzt wird gesichert und gespeichert
            Assert.Single(Unreadable(dir, "settings.json"));
            Assert.Null(Settings.Load(path).LoadError);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

/// <summary>Von Hand bearbeitete oder von einer anderen Version stammende settings.json.</summary>
public class SettingsHandEditTests
{
    private static Settings LoadJson(string json)
    {
        string dir = Path.Combine(Path.GetTempPath(), "nconnect-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, json);
            var s = Settings.Load(path);
            Assert.Null(s.LoadError);
            Assert.False(s.RestoredFromBackup);
            return s;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Unbekannte_Enum_Namen_werden_einzeln_verworfen()
    {
        // Wie eine Datei aus einer neueren Version mit neuer Controller-Art „Pro3“, neuer Taste „GX“ usw.
        var s = LoadJson("""
            {
              "OutputMode": "Xbox720",
              "Layout": "Diagonal",
              "PairGyroSource": "Mitte",
              "ConnectNotify": 42,
              "GyroStick": "Manchmal",
              "InactivityMinutes": 15,
              "Remap": { "GL": "LS", "GX": "A", "GR": "Laser", "C": "A, B" },
              "Profiles": { "Pro2": { "C": "Guide", "GX": "A" }, "Pro3": { "A": "B" } },
              "ShiftProfiles": { "Pro3": { "A": "B" } },
              "Deadzones": { "Pro2": 0.2, "Pro3": 0.3 },
              "Tuning": { "Pro3": { "StickCurve": 2 }, "Pro2": { "StickCurve": 1.5 } },
              "ControllerKinds": { "AA:BB:CC:DD:EE:01": "Pro3", "AA:BB:CC:DD:EE:02": "Pro2" },
              "ControllerOutputs": { "AA:BB:CC:DD:EE:01": "Xbox720", "AA:BB:CC:DD:EE:02": "DualShock4" },
              "NamedProfiles": [ { "Name": "Spiel", "Buttons": { "Pro3": { "A": "B" }, "Pro2": { "GL": "Shift", "GX": "A" } } } ]
            }
            """);

        Assert.Equal(OutputMode.Xbox360, s.OutputMode);
        Assert.Equal(FaceButtonLayout.Xbox, s.Layout);
        Assert.Equal(GyroSource.Right, s.PairGyroSource);
        Assert.Equal(ConnectNotifications.NConnect, s.ConnectNotify);
        Assert.Equal(GyroStickMode.Off, s.GyroStick);
        Assert.Equal(15, s.InactivityMinutes); // der Rest der Datei bleibt erhalten
        Assert.Equal(ExtraButtonTarget.LS, Assert.Single(s.Remap).Value);
        Assert.Equal("Guide", Assert.Single(Assert.Single(s.Profiles).Value).Value);
        Assert.Empty(s.ShiftProfiles);
        Assert.Equal(0.2f, Assert.Single(s.Deadzones).Value);
        Assert.Equal(1.5f, Assert.Single(s.Tuning).Value.StickCurve);
        Assert.Equal(ControllerKind.Pro2, Assert.Single(s.ControllerKinds).Value);
        Assert.Equal(OutputMode.DualShock4, Assert.Single(s.ControllerOutputs).Value);
        var profile = Assert.Single(s.NamedProfiles);
        Assert.Equal("Shift", Assert.Single(Assert.Single(profile.Buttons).Value).Value);
    }

    [Fact]
    public void Unbekannte_Werte_landen_nicht_als_Zahl_in_der_Datei()
    {
        var s = LoadJson("""{ "OutputMode": "Xbox720", "Remap": { "GX": "A", "GL": "Laser" }, "ControllerKinds": { "AA:BB:CC:DD:EE:01": "Pro3" } }""");
        string dir = Path.Combine(Path.GetTempPath(), "nconnect-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(dir, "settings.json");
            s.Save(path);
            string text = File.ReadAllText(path);
            Assert.DoesNotContain("-1", text);
            Assert.DoesNotContain("4294967295", text);
            Assert.Contains("\"OutputMode\": \"Xbox360\"", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("\"DualShock4\"", OutputMode.DualShock4)]
    [InlineData("\"dualshock4\"", OutputMode.DualShock4)]   // Groß/Klein egal
    [InlineData("1", OutputMode.DualShock4)]                // Zahl wie bisher
    [InlineData("4294967297", OutputMode.Xbox360)]          // 2^32 + 1: nicht still zu 1 abschneiden
    [InlineData("1.5", OutputMode.Xbox360)]
    [InlineData("null", OutputMode.Xbox360)]
    [InlineData("true", OutputMode.Xbox360)]
    [InlineData("{ \"x\": [1, 2] }", OutputMode.Xbox360)]
    [InlineData("\"\"", OutputMode.Xbox360)]
    [InlineData("\"Xbox360, DualShock4\"", OutputMode.Xbox360)]  // nicht zu 0 | 1 = DualShock4 verknüpfen
    public void Enum_Wert_jeder_Art_macht_die_Datei_nicht_unlesbar(string value, OutputMode expected)
    {
        var s = LoadJson($$"""{ "OutputMode": {{value}}, "InactivityMinutes": 7 }""");
        Assert.Equal(expected, s.OutputMode);
        Assert.Equal(7, s.InactivityMinutes); // auch der Wert dahinter wird noch gelesen
    }

    [Fact]
    public void Gross_Klein_Dubletten_werden_zusammengefuehrt_letzter_Wert_gewinnt()
    {
        var s = LoadJson("""
            {
              "ControllerNames": { "aa:bb:cc:dd:ee:ff": "Alt", "AA:BB:CC:DD:EE:FF": "Neu" },
              "PlayerSlots": { "aa:bb:cc:dd:ee:ff": 1, "AA:BB:CC:DD:EE:FF": 2, "Aa:Bb:Cc:Dd:Ee:Ff": 9 },
              "ControllerKinds": { "aa:bb:cc:dd:ee:ff": "Pro1", "AA:BB:CC:DD:EE:FF": "Pro2" },
              "ControllerOutputs": { "aa:bb:cc:dd:ee:ff": "Xbox360", "AA:BB:CC:DD:EE:FF": "DualShock4" },
              "GyroCalibration": { "aa:bb:cc:dd:ee:ff": { "X": 1, "Y": 1, "Z": 1 }, "AA:BB:CC:DD:EE:FF": { "X": 2, "Y": 2, "Z": 2 } },
              "StickCalibrations": {
                "aa:bb:cc:dd:ee:ff|L": { "X": { "Neutral": 2000, "Max": 1500, "Min": 1500 }, "Y": { "Neutral": 2000, "Max": 1500, "Min": 1500 } },
                "AA:BB:CC:DD:EE:FF|L": { "X": { "Neutral": 2100, "Max": 1500, "Min": 1500 }, "Y": { "Neutral": 2100, "Max": 1500, "Min": 1500 } }
              },
              "Remap": { "GL": "LS", "gl": "RS" }
            }
            """);

        const string other = "Aa:bB:cC:Dd:eE:fF";
        Assert.Equal("Neu", s.NameFor(other));
        Assert.Equal(2, s.SlotFor(other)); // 9 ist ungültig: der spätere gültige Eintrag gewinnt
        Assert.Equal(ControllerKind.Pro2, Assert.Single(s.ControllerKinds).Value);
        Assert.Equal(OutputMode.DualShock4, s.OutputFor(other));
        Assert.Equal(new GyroBias(2, 2, 2), s.GyroBiasFor(other, default));
        Assert.Equal(2100, s.StickCalibrationFor(other, left: true)!.Value.X.Neutral);
        Assert.Equal(ExtraButtonTarget.RS, Assert.Single(s.Remap).Value);
        // Die Wörterbücher bleiben unabhängig von Groß/Klein (für die Set…-Methoden der Apps).
        s.SetName("aa:bb:cc:dd:ee:ff", "Lena");
        Assert.Equal("Lena", Assert.Single(s.ControllerNames).Value);
    }

    [Fact]
    public void Null_Eintraege_in_Listen_werden_entfernt()
    {
        var s = LoadJson("""
            {
              "HiddenDevices": [ "HID\\VID_057E&PID_2009\\1", null ],
              "AllowedControllers": [ null ],
              "KnownControllers": [ null, "AA:BB:CC:DD:EE:FF" ],
              "SingleJoyCons": [ null ],
              "UprightJoyCons": [ null ],
              "NamedProfiles": [ null, { "Name": "Spiel", "Programs": [ null, "Cemu.exe" ] } ],
              "ImportedPairings": [ null ],
              "ControllerNames": { "AA:BB:CC:DD:EE:FF": null }
            }
            """);

        Assert.True(s.IsHidden("HID\\VID_057E&PID_2009\\1"));
        Assert.False(s.IsHidden("HID\\VID_057E&PID_2006\\2")); // früher NullReferenceException beim Verbinden
        Assert.DoesNotContain(s.HiddenDevices, d => d is null);
        Assert.True(s.IsAllowed("11:22:33:44:55:66"));         // leere Liste = alle erlaubt
        Assert.True(s.IsKnown("aa:bb:cc:dd:ee:ff"));
        Assert.False(s.IsSingleJoyCon("AA:BB:CC:DD:EE:FF"));
        Assert.False(s.IsUprightJoyCon("AA:BB:CC:DD:EE:FF"));
        Assert.Equal(["Cemu.exe"], Assert.Single(s.NamedProfiles).Programs);
        Assert.Empty(s.ImportedPairings);
        Assert.Null(s.NameFor("AA:BB:CC:DD:EE:FF"));
    }

    [Fact]
    public void Profil_Import_mit_unbekannten_Namen_uebernimmt_den_Rest()
    {
        var p = NamedProfile.FromJson("""
            { "Name": "Neu", "Buttons": { "Pro3": { "A": "B" }, "Pro2": { "GX": "A", "C": "Key:F5" } } }
            """);
        Assert.NotNull(p);
        Assert.Equal("Key:F5", Assert.Single(Assert.Single(p!.Buttons).Value).Value);
        Assert.Contains("\"Pro2\"", p.ToJson()); // weiterhin als Namen gespeichert
    }
}
