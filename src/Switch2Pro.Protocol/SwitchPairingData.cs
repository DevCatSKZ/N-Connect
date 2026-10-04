using System.Globalization;

namespace Switch2Pro.Protocol;

/// <summary>Bluetooth-Adressen im Format "AA:BB:CC:DD:EE:FF" (höchstes Byte zuerst, wie Windows sie anzeigt).</summary>
public static class BtAddress
{
    /// <summary>Liest "AA:BB:CC:DD:EE:FF", "AA-BB-…" oder "AABBCCDDEEFF"; gibt die einheitliche Schreibweise zurück.</summary>
    public static bool TryNormalize(string? text, out string address)
    {
        address = "";
        if (text is null)
            return false;
        string hex = text.Trim().Replace(":", "").Replace("-", "");
        if (hex.Length != 12 || !ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            return false;
        address = Format(Convert.FromHexString(hex));
        return true;
    }

    /// <summary>6 Byte, höchstes Byte zuerst.</summary>
    public static string Format(ReadOnlySpan<byte> bytes) =>
        string.Join(':', bytes[..6].ToArray().Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    public static string Format(ulong address) =>
        string.Join(':', Enumerable.Range(0, 6).Reverse().Select(i => ((address >> (i * 8)) & 0xFF).ToString("X2", CultureInfo.InvariantCulture)));

    public static bool IsEmpty(string address) => address.All(c => c is '0' or ':');

    public static bool Same(string? a, string? b) =>
        TryNormalize(a, out var x) && TryNormalize(b, out var y) && x == y;
}

/// <summary>Art des Controllers laut Kopplungsdaten der Switch (hekate/Bluepick: Joy-Con-Kennung).</summary>
public enum SwitchPairedType
{
    /// <summary>Kein gültiger Eintrag oder mit einem anderen Gerät als der Switch gekoppelt (Typ 0).</summary>
    Unknown,
    JoyConLeft,
    JoyConRight,
}

/// <summary>
/// Ein Kopplungseintrag aus dem Joy-Con-Speicher (SPI 0x2000), wie hekate bzw. Bluepick_RCM ihn auf die SD-Karte
/// schreiben. <see cref="LinkKey"/> steht in der Reihenfolge der Datei (hekate dreht die Bytes aus dem Joy-Con um).
/// </summary>
public sealed record SwitchPairing(int Slot, byte RawType, string ControllerAddress, string HostAddress, byte[] LinkKey)
{
    /// <summary>Bit 0x01 = Joy-Con (L), 0x02 = Joy-Con (R), 0x20 = HORI-Controller; 0 = nicht mit einer Switch gekoppelt.</summary>
    public SwitchPairedType Type => (RawType & 0x03) switch
    {
        0x01 => SwitchPairedType.JoyConLeft,
        0x02 => SwitchPairedType.JoyConRight,
        _ => SwitchPairedType.Unknown,
    };

    /// <summary>Hekate setzt den Typ auf 0, wenn der Joy-Con zuletzt mit einem PC/Android statt mit der Switch gekoppelt war.</summary>
    public bool PairedWithSwitch => RawType != 0;

    public bool IsHori => (RawType & 0x20) != 0;

    /// <summary>Schlüssel in umgekehrter Reihenfolge – so, wie er im Joy-Con steht (niedrigstes Byte zuerst, wie bei HCI).</summary>
    public byte[] LinkKeyReversed => LinkKey.Reverse().ToArray();

    public string LinkKeyHex => Convert.ToHexString(LinkKey);

    public string DisplayName => Type switch
    {
        SwitchPairedType.JoyConLeft => IsHori ? "HORI-Controller (links)" : "Joy-Con (L)",
        SwitchPairedType.JoyConRight => IsHori ? "HORI-Controller (rechts)" : "Joy-Con (R)",
        _ => "Controller",
    };

    public ControllerKind Kind => Type switch
    {
        SwitchPairedType.JoyConLeft => ControllerKind.JoyCon1Left,
        SwitchPairedType.JoyConRight => ControllerKind.JoyCon1Right,
        _ => ControllerKind.Unknown,
    };
}

/// <summary>
/// Kopplungsdaten von einer Switch-SD-Karte: <c>switchroot/joycon_mac.ini</c> (lesbar), ersatzweise
/// <c>switchroot/joycon_mac.bin</c> (2 × 29 Byte: Typ, Controller-Adresse, Host-Adresse, Schlüssel) und die
/// Bluetooth-Adresse der Konsole aus <c>switchroot/switch.cal</c> („device_bt_mac=“).
/// Geschrieben von Bluepick_RCM („Dump Joy-Con BT pairing → SD“, auch in FULL AUTO) bzw. hekate (Nyx → Konsole
/// → „Dump Joy-Con BT“) – gleiches Format, für switchroot/Android gedacht.
/// </summary>
public sealed class SwitchPairingData
{
    /// <summary>Größe eines Eintrags in joycon_mac.bin (jc_bt_conn_t: u8 type, u8 mac[6], u8 host_mac[6], u8 ltk[16]).</summary>
    public const int BinEntrySize = 29;

    public List<SwitchPairing> Controllers { get; } = [];

    /// <summary>Bluetooth-Adresse der Switch (aus switch.cal), sonst die Host-Adresse der Einträge.</summary>
    public string? ConsoleAddress { get; set; }

    /// <summary>Mit einer Switch gekoppelte, gültige Einträge.</summary>
    public IEnumerable<SwitchPairing> Valid => Controllers.Where(c => !BtAddress.IsEmpty(c.ControllerAddress) && !BtAddress.IsEmpty(c.HostAddress));

    /// <summary>Lesbare Datei: Abschnitte [joycon_00], [joycon_01] mit type, mac, host, ltk.</summary>
    public static List<SwitchPairing> ParseIni(string text)
    {
        var result = new List<SwitchPairing>();
        Dictionary<string, string>? current = null;
        int slot = -1;
        void Flush()
        {
            if (current is null)
                return;
            if (current.TryGetValue("mac", out var mac) && BtAddress.TryNormalize(mac, out var controller)
                && current.TryGetValue("host", out var hostText) && BtAddress.TryNormalize(hostText, out var host)
                && current.TryGetValue("ltk", out var ltkText) && TryHex(ltkText, 16, out var ltk))
            {
                byte type = current.TryGetValue("type", out var t) && byte.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? b : (byte)0;
                result.Add(new SwitchPairing(slot, type, controller, host, ltk));
            }
            current = null;
        }
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                string section = line[1..^1].Trim();
                if (section.StartsWith("joycon_", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(section["joycon_".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out slot))
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            int eq = line.IndexOf('=');
            if (current is not null && eq > 0)
                current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        Flush();
        return result;
    }

    /// <summary>Binärdatei: Einträge zu je 29 Byte hintereinander (Adressen höchstes Byte zuerst).</summary>
    public static List<SwitchPairing> ParseBin(ReadOnlySpan<byte> data)
    {
        var result = new List<SwitchPairing>();
        for (int i = 0; (i + 1) * BinEntrySize <= data.Length && i < 8; i++)
        {
            var e = data.Slice(i * BinEntrySize, BinEntrySize);
            result.Add(new SwitchPairing(i, e[0], BtAddress.Format(e.Slice(1, 6)), BtAddress.Format(e.Slice(7, 6)), e.Slice(13, 16).ToArray()));
        }
        return result;
    }

    /// <summary>„device_bt_mac=AA:BB:…“ aus switch.cal.</summary>
    public static string? ParseConsoleAddress(string calText)
    {
        foreach (var raw in calText.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("device_bt_mac", StringComparison.OrdinalIgnoreCase) && line.IndexOf('=') is int eq and > 0
                && BtAddress.TryNormalize(line[(eq + 1)..], out var address) && !BtAddress.IsEmpty(address))
                return address;
        }
        return null;
    }

    /// <summary>Aus den Dateiinhalten zusammensetzen (jede Datei darf fehlen). Die lesbare Datei hat Vorrang.</summary>
    public static SwitchPairingData Parse(string? ini, byte[]? bin, string? cal)
    {
        var data = new SwitchPairingData();
        var entries = ini is not null ? ParseIni(ini) : [];
        if (entries.Count == 0 && bin is not null)
            entries = ParseBin(bin);
        data.Controllers.AddRange(entries);
        data.ConsoleAddress = cal is not null ? ParseConsoleAddress(cal) : null;
        data.ConsoleAddress ??= data.Valid.Where(c => c.PairedWithSwitch).Select(c => c.HostAddress).FirstOrDefault();
        return data;
    }

    private static bool TryHex(string text, int length, out byte[] bytes)
    {
        bytes = [];
        text = text.Trim();
        if (text.Length != length * 2)
            return false;
        try
        {
            bytes = Convert.FromHexString(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Was auf einem Laufwerk gefunden wurde, das wie eine Switch-SD-Karte aussieht.</summary>
public sealed record SwitchCardInfo(string Root, bool IsSwitchCard, IReadOnlyList<string> Markers, bool HasPairingExport, bool HasSaveBackup)
{
    /// <summary>Kurzbeschreibung für die Anzeige, z. B. „E:\ – Nintendo, atmosphere, bootloader“.</summary>
    public string Summary => $"{Root} – {string.Join(", ", Markers)}";
}

/// <summary>
/// Erkennt Switch-SD-Karten (im Kartenleser oder per USB-Massenspeicher, z. B. hekate UMS) an typischen Ordnern
/// und liest die Kopplungsdaten. Greift nur lesend auf die Karte zu.
/// </summary>
public static class SwitchCard
{
    public const string IniFile = "switchroot/joycon_mac.ini";
    public const string BinFile = "switchroot/joycon_mac.bin";
    public const string CalFile = "switchroot/switch.cal";
    /// <summary>Gesicherte Bluetooth-Speicherdaten von Bluepick_RCM („Export … → SD“): verschlüsseltes Switch-Save.</summary>
    public const string BluepickSave = "switch/Bluepick_RCM/8000000000000050.bin";

    /// <summary>Ordner, an denen eine Switch-SD-Karte zu erkennen ist.</summary>
    private static readonly string[] MarkerDirs = ["Nintendo", "atmosphere", "bootloader", "switch", "switchroot", "emuMMC"];

    /// <summary>Größte Datei, die gelesen wird (Schutz vor falschen Dateien gleichen Namens).</summary>
    private const long MaxFileSize = 64 * 1024;

    public static SwitchCardInfo Inspect(string root)
    {
        var markers = MarkerDirs.Where(d => SafeDirExists(Path.Combine(root, d))).ToList();
        bool export = SafeFileExists(Path.Combine(root, IniFile)) || SafeFileExists(Path.Combine(root, BinFile));
        bool save = SafeFileExists(Path.Combine(root, BluepickSave));
        // „Nintendo“ legt die Konsole selbst an; atmosphere/bootloader stammen von CFW/hekate.
        bool isSwitch = markers.Contains("Nintendo") || markers.Contains("atmosphere") && markers.Contains("bootloader")
                        || export || save;
        return new SwitchCardInfo(root, isSwitch, markers, export, save);
    }

    /// <summary>Liest die Kopplungsdaten (nur lesend); null, wenn keine Datei vorhanden ist.</summary>
    public static SwitchPairingData? ReadPairings(string root)
    {
        string? ini = ReadText(Path.Combine(root, IniFile));
        byte[]? bin = ReadBytes(Path.Combine(root, BinFile));
        string? cal = ReadText(Path.Combine(root, CalFile));
        return ini is null && bin is null ? null : SwitchPairingData.Parse(ini, bin, cal);
    }

    private static string? ReadText(string path) => ReadBytes(path) is { } b ? System.Text.Encoding.UTF8.GetString(b) : null;

    private static byte[]? ReadBytes(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileSize)
                return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[info.Length];
            stream.ReadExactly(buffer);
            return buffer;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool SafeDirExists(string path)
    {
        try { return Directory.Exists(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool SafeFileExists(string path)
    {
        try { return File.Exists(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
