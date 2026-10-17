using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>
/// Bluetooth-Save der Switch (8000000000000050.bin) mit synthetischen Daten – keine echten Adressen oder Schlüssel.
/// Hält das jetzige Verhalten fest (Arbeitsstand „Switch-Identität“, siehe docs/SWITCH-IDENTITAET.md).
/// </summary>
public class SwitchBtSaveTests
{
    /// <summary>Lage der Tabelle wie im echten Save: erster Eintrag 0x58070, Journal-Kopie 0x5C070.</summary>
    private const int Table = 0x58070, Copy = 0x5C070, SaveSize = 0x60000;

    private static readonly byte[] AddressL = [0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x01];
    private static readonly byte[] AddressR = [0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x02];
    private static readonly byte[] Key1 = [.. Enumerable.Range(1, 16).Select(i => (byte)i)];
    private static readonly byte[] Key2 = [.. Enumerable.Range(0x41, 16).Select(i => (byte)i)];

    /// <summary>Eintrag im Format SetSysBluetoothDevicesSettings: Adresse +0x00, Schlüssel +0x29, Name +0xD6.</summary>
    private static void Entry(byte[] save, int start, byte[] address, byte[] key, string name)
    {
        address.CopyTo(save, start);
        key.CopyTo(save, start + 0x29);
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(save, start + 0xD6);
    }

    [Fact]
    public void Offsets_im_Raster_sind_die_Felder_ab_0x58070()
    {
        // Die Konstanten zählen ab dem 0x200-Rasterpunkt 0x70 Byte vor dem Eintrag (siehe Kommentar in SwitchBtSave).
        Assert.Equal(0x58000, Table - 0x70);
        Assert.Equal(0, (Table - 0x70) % SwitchBtSave.EntrySize);
        Assert.Equal(0x70 + 0x00, SwitchBtSave.AddressOffset);
        Assert.Equal(0x70 + 0x29, SwitchBtSave.KeyOffset);
        Assert.Equal(0x70 + 0xD6, SwitchBtSave.NameOffset);
    }

    [Fact]
    public void Eintraege_werden_gefunden_und_Journal_Kopien_zusammengefasst()
    {
        var save = new byte[SaveSize];
        foreach (int table in new[] { Table, Copy })
        {
            Entry(save, table, AddressL, Key1, "Joy-Con (L)");
            Entry(save, table + SwitchBtSave.EntrySize, AddressR, Key2, "Joy-Con (R)");
        }

        var devices = SwitchBtSave.Parse(save);

        Assert.Equal(2, devices.Count);
        Assert.Equal(0x58000, devices[0].Offset); // Rasterpunkt, an dem der Eintrag gefunden wurde
        Assert.Equal("0A:0B:0C:0D:0E:01", devices[0].Address);
        Assert.Equal("Joy-Con (L)", devices[0].Name);
        Assert.Equal(Key1, devices[0].LinkKey);
        Assert.Equal(Key1.Reverse().ToArray(), devices[0].LinkKeyIniOrder); // joycon_mac.ini: umgekehrt
        Assert.Equal("0A:0B:0C:0D:0E:02", devices[1].Address);
        Assert.Equal(0x58200, devices[1].Offset);
        Assert.Equal(Key2, devices[1].LinkKey);
    }

    [Fact]
    public void Aeltere_Kopplung_mit_anderem_Schluessel_bleibt_stehen()
    {
        // Welcher Eintrag der aktuelle ist, ist noch offen – bisher werden beide geliefert, in Dateireihenfolge.
        var save = new byte[SaveSize];
        Entry(save, Table, AddressL, Key1, "Joy-Con (L)");
        Entry(save, Table + SwitchBtSave.EntrySize, AddressL, Key2, "Joy-Con (L)");

        var devices = SwitchBtSave.Parse(save);

        Assert.Equal(2, devices.Count);
        Assert.All(devices, d => Assert.Equal("0A:0B:0C:0D:0E:01", d.Address));
        Assert.Equal(Key1, devices[0].LinkKey);
        Assert.Equal(Key2, devices[1].LinkKey);
    }

    [Fact]
    public void Unplausible_Eintraege_werden_uebersprungen()
    {
        var save = new byte[SaveSize];
        int e = Table;
        int Next() => e += SwitchBtSave.EntrySize;
        Entry(save, e, AddressL, Key1, "");                                    // kein Name
        Entry(save, Next(), AddressL, Key1, "Joy\aCon");                      // Steuerzeichen im Namen
        Entry(save, Next(), AddressL, Key1, new string('X', 0x20));           // Name ohne Abschluss-Null
        Entry(save, Next(), new byte[6], Key1, "Joy-Con (L)");                 // Adresse leer
        Entry(save, Next(), [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF], Key1, "Joy-Con (L)"); // Adresse unbeschrieben
        Entry(save, Next(), AddressL, new byte[16], "Joy-Con (L)");            // Schlüssel leer
        Entry(save, Next(), AddressR, Key2, new string('Y', 0x1F));           // längster gültiger Name

        var device = Assert.Single(SwitchBtSave.Parse(save));
        Assert.Equal(new string('Y', 0x1F), device.Name);
        Assert.Equal("0A:0B:0C:0D:0E:02", device.Address);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(SwitchBtSave.EntrySize - 1)]
    [InlineData(SwitchBtSave.EntrySize)]
    [InlineData(0x58000 + SwitchBtSave.EntrySize - 1)] // letzter Eintrag abgeschnitten
    public void Kurze_oder_abgeschnittene_Datei_liefert_nichts(int length)
    {
        var save = new byte[SaveSize];
        Entry(save, Table, AddressL, Key1, "Joy-Con (L)");
        Assert.Empty(SwitchBtSave.Parse(save.AsSpan(0, length)));
    }

    [Fact]
    public void Eintrag_am_Dateiende_wird_gelesen()
    {
        var save = new byte[0x58000 + SwitchBtSave.EntrySize];
        Entry(save, Table, AddressL, Key1, "Pro Controller");
        Assert.Equal("Pro Controller", Assert.Single(SwitchBtSave.Parse(save)).Name);
    }
}
