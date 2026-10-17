using System.Text;

namespace Switch2Pro.Protocol;

/// <summary>Ein gekoppeltes Gerät aus dem Bluetooth-Speicher der Switch.</summary>
/// <param name="Offset">Lage des Eintrags in der Datei (zur Fehlersuche).</param>
/// <param name="Address">Bluetooth-Adresse des Controllers, höchstes Byte zuerst.</param>
/// <param name="Name">Gerätename, z. B. „Joy-Con (L)“ oder „Pro Controller“.</param>
/// <param name="LinkKey">Kopplungsschlüssel, so wie die Switch ihn speichert (niedrigstes Byte zuerst, wie bei HCI).
/// hekate/Bluepick schreiben ihn in <c>joycon_mac.ini</c> in umgekehrter Reihenfolge.</param>
public sealed record SwitchBtDevice(int Offset, string Address, string Name, byte[] LinkKey)
{
    /// <summary>Schlüssel in der Reihenfolge von <c>joycon_mac.ini</c> (zum Vergleich mit <see cref="SwitchPairing.LinkKey"/>).</summary>
    public byte[] LinkKeyIniOrder => LinkKey.Reverse().ToArray();
}

/// <summary>
/// Liest das System-Save <c>8000000000000050</c> (BluetoothDevicesSettings), wie Bluepick_RCM es nach
/// <c>sd:/switch/Bluepick_RCM/8000000000000050.bin</c> exportiert. Das Save ist innerhalb der Datei nicht
/// verschlüsselt (verschlüsselt ist nur die NAND-Ebene, die Bluepick beim Export bereits entschlüsselt).
///
/// Layout (ermittelt am 04.10.2026 an einem Save von FW 22.x, gegengeprüft mit joycon_mac.ini; siehe
/// docs/SWITCH-IDENTITAET.md): Die Einträge haben das Format <c>SetSysBluetoothDevicesSettings</c> (libnx, 0x200 Byte),
/// der erste beginnt bei 0x58070, eine zweite Kopie der Tabelle (Save-Journal) bei 0x5C070. Relativ zum Eintrag:
/// Adresse +0x00 (6 Byte, höchstes Byte zuerst), Klasse +0x26, Schlüssel +0x29 (16 Byte, umgekehrt zu joycon_mac.ini),
/// VID/PID +0x40/+0x42, Name +0xD6 (ASCII, nullterminiert).
///
/// Der Parser rastert die ganze Datei in 0x200-Schritten ab Dateianfang. Weil 0x58000 auf diesem Raster liegt, sind
/// <see cref="AddressOffset"/>, <see cref="KeyOffset"/> und <see cref="NameOffset"/> dieselben Felder, gemessen ab dem
/// Rasterpunkt 0x70 Byte vor dem Eintrag (0x70 + 0x00, 0x70 + 0x29, 0x70 + 0xD6). So werden beide Kopien gefunden;
/// doppelte Einträge (gleiche Adresse und gleicher Schlüssel) werden zusammengefasst. Ältere Kopplungen derselben
/// Adresse mit anderem Schlüssel bleiben in <see cref="Parse"/> stehen; den aktuellen Eintrag je Adresse wählt
/// <see cref="Current"/> (nach Testbefund, nicht gesichert). Arbeitsstand: nur das Diagnosewerkzeug BtIdentityProbe
/// nutzt den Parser.
/// </summary>
public static class SwitchBtSave
{
    public const string FileName = "8000000000000050.bin";
    public const int EntrySize = 0x200;
    /// <summary>Adresse: 0x70 Byte ab Rasterpunkt = Eintrag +0x00.</summary>
    public const int AddressOffset = 0x70;
    /// <summary>Kopplungsschlüssel: Eintrag +0x29.</summary>
    public const int KeyOffset = 0x99;
    /// <summary>Gerätename: Eintrag +0xD6.</summary>
    public const int NameOffset = 0x146;
    private const int NameLength = 0x20;

    /// <summary>Alle plausiblen Einträge, ohne Dubletten (gleiche Adresse und gleicher Schlüssel).</summary>
    public static List<SwitchBtDevice> Parse(ReadOnlySpan<byte> save)
    {
        var result = new List<SwitchBtDevice>();
        for (int e = 0; e + EntrySize <= save.Length; e += EntrySize)
        {
            if (TryParseEntry(save.Slice(e, EntrySize), e, out var device)
                && !result.Any(d => d.Address == device.Address && d.LinkKey.AsSpan().SequenceEqual(device.LinkKey)))
                result.Add(device);
        }
        return result;
    }

    /// <summary>
    /// Je Adresse nur der aktuelle Eintrag: der erste in Dateireihenfolge (<paramref name="devices"/> so wie von
    /// <see cref="Parse"/> geliefert), die Reihenfolge der Adressen bleibt.
    /// Nach Testbefund, nicht gesichert: Im Save vom 04.10.2026 traf der erste Eintrag je Adresse für beide Joy-Con aus
    /// joycon_mac.ini deren Schlüssel; die ältere Kopplung des einen stand weiter hinten (nachgeprüft am 17.10.2026). Für
    /// die übrigen Adressen dort gibt es keinen Vergleich. Beide Kopien der Tabelle waren gleich – welche die
    /// Save-Verwaltung gelten lässt, wenn sie sich unterscheiden, liest der Parser nicht aus. Für Joy-Con bleibt
    /// joycon_mac.ini die sichere Quelle (Schlüssel aus dem Joy-Con selbst).
    /// </summary>
    public static List<SwitchBtDevice> Current(IEnumerable<SwitchBtDevice> devices)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return devices.Where(d => seen.Add(d.Address)).ToList();
    }

    private static bool TryParseEntry(ReadOnlySpan<byte> entry, int offset, out SwitchBtDevice device)
    {
        device = null!;
        var address = entry.Slice(AddressOffset, 6);
        var key = entry.Slice(KeyOffset, 16);
        var nameBytes = entry.Slice(NameOffset, NameLength);
        int end = nameBytes.IndexOf((byte)0);
        if (end <= 0)
            return false;
        nameBytes = nameBytes[..end];
        // Name muss lesbarer Text sein, Adresse und Schlüssel dürfen nicht leer sein.
        foreach (byte b in nameBytes)
        {
            if (b is < 0x20 or > 0x7E)
                return false;
        }
        if (IsEmpty(address) || IsEmpty(key) || address.IndexOfAnyExcept((byte)0xFF) < 0)
            return false;
        device = new SwitchBtDevice(offset, BtAddress.Format(address), Encoding.ASCII.GetString(nameBytes), key.ToArray());
        return true;
    }

    private static bool IsEmpty(ReadOnlySpan<byte> bytes) => bytes.IndexOfAnyExcept((byte)0) < 0;
}
