using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>Eigene Controller-Namen und gemerkte Spielerplätze: setzen, entfernen, speichern/laden, Aufräumen.</summary>
public class ControllerIdentityTests
{
    private const string Address = "98:E2:55:B7:35:7C";

    [Fact]
    public void Name_SetzenUndEntfernen_GrossKleinEgal()
    {
        var s = new Settings();
        s.SetName(Address, "  Lenas Joy-Con  ");
        Assert.Equal("Lenas Joy-Con", s.NameFor(Address.ToLowerInvariant()));
        s.SetName(Address, "");
        Assert.Null(s.NameFor(Address));
    }

    [Fact]
    public void Name_WirdGekuerzt()
    {
        var s = new Settings();
        s.SetName(Address, new string('x', 100));
        Assert.Equal(40, s.NameFor(Address)!.Length);
    }

    [Fact]
    public void Platz_NurGueltigeWerte()
    {
        var s = new Settings();
        Assert.Null(s.SlotFor(Address));
        s.SetSlot(Address, 3);
        Assert.Equal(3, s.SlotFor(Address));
        s.SetSlot(Address, 99);
        Assert.Equal(7, s.SlotFor(Address));
    }

    [Fact]
    public void SpeichernUndLaden_BehaeltNamenUndPlaetze()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nconnect-test-{Guid.NewGuid():N}.json");
        try
        {
            var s = new Settings();
            s.SetName(Address, "Wohnzimmer");
            s.SetSlot(Address, 2);
            s.Save(path);
            var loaded = Settings.Load(path);
            Assert.Equal("Wohnzimmer", loaded.NameFor(Address));
            Assert.Equal(2, loaded.SlotFor(Address));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Laden_RaeumtUngueltigeEintraegeAuf()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nconnect-test-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "ControllerNames": { "A": "  ", "B": "Gut" }, "PlayerSlots": { "A": 12, "B": 1 } }""");
            var loaded = Settings.Load(path);
            Assert.Null(loaded.NameFor("A"));
            Assert.Equal("Gut", loaded.NameFor("b"));
            Assert.Null(loaded.SlotFor("A"));
            Assert.Equal(1, loaded.SlotFor("B"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Ausgabeart_JeController_SonstAllgemein()
    {
        var s = new Settings();
        Assert.Null(s.OutputFor(Address));
        Assert.True(s.HideFromGames); // Standard: Originale vor Steam/Spielen verstecken
        var before = s.ControllerOutputs;
        s.SetOutput(Address, OutputMode.DualShock4);
        Assert.NotSame(before, s.ControllerOutputs); // neue Kopie – andere Threads lesen gleichzeitig
        Assert.Equal(OutputMode.DualShock4, s.OutputFor(Address.ToLowerInvariant()));
        s.SetOutput(Address, null);
        Assert.Null(s.OutputFor(Address));
    }

    [Fact]
    public void Ausgabeart_SpeichernLadenUndAufraeumen()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nconnect-test-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "ControllerOutputs": { "A": "DualShock4", "B": 7 }, "HideFromGames": false }""");
            var loaded = Settings.Load(path);
            Assert.Equal(OutputMode.DualShock4, loaded.OutputFor("a"));
            Assert.Null(loaded.OutputFor("B")); // ungültiger Wert verworfen
            Assert.False(loaded.HideFromGames);
            var copy = new Settings();
            copy.CopyFrom(loaded);
            Assert.Equal(OutputMode.DualShock4, copy.OutputFor("A"));
            Assert.False(copy.HideFromGames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CopyFrom_UebernimmtNamenUndPlaetze()
    {
        var a = new Settings();
        a.SetName(Address, "X");
        a.SetSlot(Address, 5);
        var b = new Settings();
        b.CopyFrom(a);
        Assert.Equal("X", b.NameFor(Address));
        Assert.Equal(5, b.SlotFor(Address));
    }
}
