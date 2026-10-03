namespace Switch2Pro.Protocol;

/// <summary>
/// NFC-Leser im rechten Joy-Con und im Pro Controller (Switch 1): amiibo (NTAG215) lesen. Der Leser hängt am
/// Zusatzprozessor (MCU). Ablauf (öffentlich dokumentiert, u. a. in den Emulatoren yuzu/Citron und bei dekuNukem):
/// Eingabemodus 0x31 → MCU einschalten (Unterbefehl 0x22) → MCU in den NFC-Modus (Unterbefehl 0x21, CRC8) →
/// Abfrage starten → Karte erkannt → Seiten 0x00–0x86 in drei Blöcken lesen. MCU-Anfragen gehen als
/// Ausgabebericht 0x11, Antworten kommen in Bericht 0x31 ab Byte 49 (Art) bzw. 50 (Daten).
/// </summary>
public static class Nfc
{
    public const byte InputMcu = 0x31;
    public const byte OutputMcu = 0x11;
    public const byte SubMcuConfig = 0x21, SubMcuState = 0x22;
    public const int McuReportOffset = 49;

    public const byte McuSetDeviceMode = 0x01, McuReadDeviceMode = 0x02;
    public const byte ReportState = 0x01, ReportNfcState = 0x2A, ReportNfcRead = 0x3A, ReportEmpty = 0xFF;
    public const byte ModeStandby = 1, ModeNfc = 4;
    public const byte CmdStartPolling = 0x01, CmdStopPolling = 0x02, CmdNextPacket = 0x04, CmdReadNtag = 0x06;
    public const byte StatusReady = 0x00, StatusPolling = 0x01, StatusLastPacket = 0x04, StatusTagLost = 0x07, StatusTagFound = 0x09;

    /// <summary>NTAG215: 135 Seiten à 4 Byte = 540 Byte (übliches amiibo-Abbild „.bin“).</summary>
    public const int AmiiboSize = 540;

    private static readonly byte[] Crc8Table = BuildTable();

    /// <summary>CRC-8 (Polynom 0x07, Startwert 0).</summary>
    public static byte Crc8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (byte b in data)
            crc = Crc8Table[crc ^ b];
        return crc;
    }

    private static byte[] BuildTable()
    {
        var table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            byte c = (byte)i;
            for (int k = 0; k < 8; k++)
                c = (byte)((c & 0x80) != 0 ? (c << 1) ^ 0x07 : c << 1);
            table[i] = c;
        }
        return table;
    }

    /// <summary>Argumente für Unterbefehl 0x21: MCU in den gewünschten Modus (38 Byte, CRC über Byte 1–36).</summary>
    public static byte[] McuConfig(byte mode)
    {
        var c = new byte[38];
        c[0] = 0x21;   // MCU konfigurieren
        c[1] = 0x00;   // Modus setzen
        c[2] = mode;
        c[37] = Crc8(c.AsSpan(1, 36));
        return c;
    }

    /// <summary>NFC-Anfrage (38 Byte): Befehl, Block, Paket, Kennung „letztes Paket“, Länge, Daten, CRC an Stelle 36.</summary>
    public static byte[] NfcRequest(byte command, byte packetId = 0, ReadOnlySpan<byte> data = default)
    {
        var r = new byte[38];
        r[0] = command;
        r[1] = 0;
        r[2] = packetId;
        r[3] = 0x08;            // letztes Befehlspaket
        r[4] = (byte)data.Length;
        data.CopyTo(r.AsSpan(5));
        r[36] = Crc8(r.AsSpan(0, 36));
        return r;
    }

    public static byte[] StartPolling() => NfcRequest(CmdStartPolling, data: [0x00, 0x00, 0x00, 0x2C, 0x01]);
    public static byte[] StopPolling() => NfcRequest(CmdStopPolling);
    public static byte[] NextPacket(byte packetId = 0) => NfcRequest(CmdNextPacket, packetId);

    /// <summary>NTAG215 vollständig lesen: Seiten 0x00–0x3B, 0x3C–0x77, 0x78–0x86.</summary>
    public static byte[] ReadNtag215() => NfcRequest(CmdReadNtag, data:
    [
        0xD0, 0x07, 0, 0, 0, 0, 0, 0, 0,   // unbekannt, UID-Länge 7, UID (leer = beliebige Karte)
        0x01,                              // Kartentyp NTAG215
        0x03, 0x00, 0x3B, 0x3C, 0x77, 0x78, 0x86, 0x00, 0x00,
    ]);

    /// <summary>Ausgabebericht 0x11 mit MCU-Unterbefehl (z. B. 0x02 + NFC-Anfrage).</summary>
    public static byte[] McuReport(int counter, byte mcuSubcommand, ReadOnlySpan<byte> data, ReadOnlySpan<byte> rumble8)
    {
        var r = Switch1.Subcommand(counter, mcuSubcommand, data, rumble8);
        r[0] = OutputMcu;
        return r;
    }

    /// <summary>MCU-Teil eines Berichts 0x31: Art und Daten; false, wenn noch nichts da ist.</summary>
    public static bool TryGetMcu(ReadOnlySpan<byte> r, out byte kind, out ReadOnlySpan<byte> data)
    {
        kind = ReportEmpty;
        data = default;
        if (r.Length <= McuReportOffset + 8 || r[0] != InputMcu)
            return false;
        kind = r[McuReportOffset];
        data = r[(McuReportOffset + 1)..];
        return kind != ReportEmpty && kind != 0x00;
    }

    /// <summary>Zustandsbericht: MCU ist im Modus <paramref name="mode"/>?</summary>
    public static bool IsMcuMode(byte kind, ReadOnlySpan<byte> d, byte mode) => kind == ReportState && d.Length > 6 && d[6] == mode;

    /// <summary>NFC-Zustand (0x2A, Kennung 00 05, Byte 5 = 0x31): Status in Byte 6.</summary>
    public static bool TryGetNfcStatus(byte kind, ReadOnlySpan<byte> d, out byte status)
    {
        status = 0xFF;
        if (kind != ReportNfcState || d.Length < 16 || d[0] != 0x00 || d[1] != 0x05)
            return false;
        status = d[6];
        return true;
    }

    /// <summary>Karte erkannt? Dann UID (bis 7 Byte, ab Byte 15; Länge in Byte 14).</summary>
    public static bool TryGetTag(byte kind, ReadOnlySpan<byte> d, out byte[] uid)
    {
        uid = [];
        if (!TryGetNfcStatus(kind, d, out byte status) || status is not (StatusTagFound or StatusLastPacket))
            return false;
        int length = Math.Min(d[14], (byte)7);
        if (length == 0 || d.Length < 15 + length)
            return false;
        uid = d.Slice(15, length).ToArray();
        return true;
    }
}

/// <summary>
/// Ring-Con-Rohwert → Biegung −1…+1. Ruhelage = Mittel der ersten Werte nach dem Einschalten (Ring nicht
/// berühren); der Ausschlag passt sich dem größten gemessenen Ausschlag an (mindestens 600 Rohschritte).
/// </summary>
public sealed class RingFlexCalibration
{
    private const int CalibrationSamples = 30;
    private const float MinimumRange = 600f;
    private long _sum;
    private int _count;
    private float _range = MinimumRange;

    public float? Neutral { get; private set; }

    public float? Update(short raw)
    {
        if (Neutral is null)
        {
            _sum += raw;
            if (++_count >= CalibrationSamples)
                Neutral = _sum / (float)_count;
            return null;
        }
        float delta = raw - Neutral.Value;
        _range = Math.Max(_range, Math.Abs(delta));
        float v = delta / _range;
        return Math.Abs(v) < 0.04f ? 0f : Math.Clamp(v, -1f, 1f);
    }
}

/// <summary>Setzt die gelesenen Pakete (Art 0x3A) zum 540-Byte-Abbild zusammen.</summary>
public sealed class AmiiboAssembler
{
    /// <summary>Pakete nach Nummer (Byte 2, ab 1) – doppelt oder verspätet eintreffende Pakete stören so nicht.</summary>
    private readonly SortedDictionary<byte, byte[]> _packets = [];

    public int PacketCount => _packets.Count;

    /// <summary>Fertig, wenn die Pakete 1…n lückenlos vorliegen und zusammen mindestens 540 Byte ergeben.</summary>
    public bool Complete
    {
        get
        {
            int total = 0;
            byte expected = 1;
            foreach (var (number, data) in _packets)
            {
                if (number != expected++)
                    return false;
                total += data.Length;
            }
            return total >= Nfc.AmiiboSize;
        }
    }

    /// <summary>
    /// Ein Lesepaket übernehmen (Byte 1 = 0x07; Byte 2 = Paketnummer; Byte 4–5 = Nutzlänge). Das erste Paket enthält
    /// vorn 60 Byte Kopf, die Daten beginnen bei Byte 66; folgende Pakete ab Byte 6. true, wenn das Paket neu war.
    /// </summary>
    public bool Add(byte kind, ReadOnlySpan<byte> d)
    {
        if (kind != Nfc.ReportNfcRead || d.Length < 8 || d[1] != 0x07 || d[2] == 0 || _packets.ContainsKey(d[2]))
            return false;
        int payload = (d[4] << 8 | d[5]) & 0x7FF;
        int offset = d[2] == 0x01 ? 66 : 6;
        int length = Math.Min(d[2] == 0x01 ? payload - 60 : payload, d.Length - offset);
        if (length <= 0)
            return false;
        _packets[d[2]] = d.Slice(offset, length).ToArray();
        return true;
    }

    public byte[] ToArray()
    {
        var all = _packets.Values.SelectMany(p => p).Take(Nfc.AmiiboSize).ToArray();
        return all;
    }
}
