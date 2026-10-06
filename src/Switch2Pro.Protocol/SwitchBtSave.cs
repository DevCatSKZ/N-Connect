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
/// Layout (ermittelt am 04.10.2026 an einem Save von FW 22.x, gegengeprüft mit joycon_mac.ini):
/// Einträge zu je 0x200 Byte, Adresse bei +0x70 (höchstes Byte zuerst), Schlüssel bei +0x99 (16 Byte,
/// umgekehrt zu joycon_mac.ini), Name bei +0x146 (ASCII, nullterminiert). Der Speicher hält zwei Kopien der
/// Tabelle (Save-Journal); deshalb wird die ganze Datei durchsucht und doppelte Einträge zusammengefasst.
/// </summary>
public static class SwitchBtSave
{
    public const string FileName = "8000000000000050.bin";
    public const int EntrySize = 0x200;
    public const int AddressOffset = 0x70;
    public const int KeyOffset = 0x99;
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
