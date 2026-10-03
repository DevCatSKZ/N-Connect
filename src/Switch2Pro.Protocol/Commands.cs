namespace Switch2Pro.Protocol;

/// <summary>
/// Befehle für den Befehlskanal (<see cref="Gatt.CommandOutput"/>).
/// Kopf (8 Byte): Befehl, Richtung (0x91 = Host→Controller), Transport (0x01 = Bluetooth),
/// Unterbefehl, 0x00, Datenlänge, 0x00, 0x00 — danach die Daten.
/// Antworten tragen Richtung 0x01 und kommen auf <see cref="Gatt.CommandResponse"/>.
/// </summary>
public static class Commands
{
    public const byte DirectionRequest = 0x91;
    public const byte DirectionResponse = 0x01;
    public const byte TransportBluetooth = 0x01;

    public const byte CmdMemory = 0x02;
    public const byte SubMemoryRead = 0x04;
    public const byte CmdPlayerLeds = 0x09;
    public const byte SubSetLedPattern = 0x07;
    public const byte CmdVibration = 0x0A;
    public const byte SubPlaySample = 0x02;

    /// <summary>Werkskalibrierung linker Stick (9 Byte).</summary>
    public const uint AddrLeftStickCalibration = 0x130A8;
    /// <summary>Werkskalibrierung rechter Stick (9 Byte).</summary>
    public const uint AddrRightStickCalibration = 0x130E8;
    /// <summary>Werkskalibrierung Gyro-Nullpunkt (3 × float32 ab Offset 4, in rad/s).</summary>
    public const uint AddrGyroCalibration = 0x13040;

    public static byte[] Build(byte command, byte subcommand, ReadOnlySpan<byte> data = default)
    {
        var buf = new byte[8 + data.Length];
        buf[0] = command;
        buf[1] = DirectionRequest;
        buf[2] = TransportBluetooth;
        buf[3] = subcommand;
        buf[5] = (byte)data.Length;
        data.CopyTo(buf.AsSpan(8));
        return buf;
    }

    /// <summary>
    /// Start-Sequenz für Bluetooth. Übernommen aus der unter Windows erprobten
    /// NS2Pro-Bridge-Windows (MIT), die sie wiederum von Switch2BTLink/joycon2cpp hat.
    /// Wichtig: Es werden KEINE Pairing-Schlüssel geschrieben — die Kopplung mit der
    /// Switch 2 bleibt erhalten.
    /// </summary>
    public static IReadOnlyList<byte[]> InitSequence { get; } =
    [
        Hex("03 91 01 0D 00 08 00 00 01 00 FF FF FF FF FF FF"),
        Hex("07 91 01 01 00 00 00 00"),
        Hex("16 91 01 01 00 00 00 00"),
        Hex("15 91 01 03 00 01 00 00 00"),
        // Feature-Maske setzen: Tasten, Sticks, Bewegungssensor, Rumble, Magnetometer.
        Hex("0C 91 01 02 00 04 00 00 FF 00 00 00"),
        Hex("11 91 01 03 00 00 00 00"),
        Hex("0A 91 01 08 00 14 00 00 01 FF FF FF FF FF FF FF FF 35 00 46 00 00 00 00 00 00 00 00"),
        // Features einschalten.
        Hex("0C 91 01 04 00 04 00 00 FF 00 00 00"),
        Hex("03 91 01 0A 00 04 00 00 09 00 00 00"),
        Hex("10 91 01 01 00 00 00 00"),
        Hex("01 91 01 0C 00 00 00 00"),
        Hex("01 91 01 01 00 04 00 00 00 00 00 00"),
    ];

    /// <summary>Spieler-LEDs setzen (Bit 0–3 = LED 1–4).</summary>
    public static byte[] SetPlayerLeds(byte mask) =>
        Build(CmdPlayerLeds, SubSetLedPattern, [mask, 0, 0, 0, 0, 0, 0, 0]);

    /// <summary>LED-Muster wie auf der Konsole (Spieler 1–8).</summary>
    public static byte PlayerLedMask(int playerIndex)
    {
        ReadOnlySpan<byte> pattern = [0x1, 0x3, 0x7, 0xF, 0x9, 0x5, 0xD, 0x6];
        return pattern[((playerIndex % 8) + 8) % 8];
    }

    /// <summary>Kurzes „Verbunden“-Klicken abspielen (eingebautes Muster 3).</summary>
    public static byte[] PlayConnectSample() => Build(CmdVibration, SubPlaySample, [0x03, 0, 0, 0]);

    /// <summary>Speicher lesen (max. 0x4F Byte über Bluetooth).</summary>
    public static byte[] ReadMemory(uint address, byte length)
    {
        if (length is 0 or > 0x4F)
            throw new ArgumentOutOfRangeException(nameof(length));
        return Build(CmdMemory, SubMemoryRead,
        [
            length, 0x7E, 0x00, 0x00,
            (byte)address, (byte)(address >> 8), (byte)(address >> 16), (byte)(address >> 24),
        ]);
    }

    /// <summary>Ist <paramref name="response"/> die Antwort auf Befehl/Unterbefehl?</summary>
    public static bool IsResponseTo(ReadOnlySpan<byte> response, byte command, byte subcommand) =>
        response.Length >= 8 && response[0] == command && response[1] == DirectionResponse && response[3] == subcommand;

    /// <summary>
    /// Wertet die Antwort eines Speicher-Lesebefehls aus.
    /// Antwortdaten: Länge, 3 × 0, Adresse (LE, 4 Byte), Daten.
    /// </summary>
    public static bool TryParseMemoryRead(ReadOnlySpan<byte> response, uint expectedAddress, out byte[] data)
    {
        data = [];
        if (!IsResponseTo(response, CmdMemory, SubMemoryRead) || response.Length < 16)
            return false;
        var payload = response[8..];
        int length = payload[0];
        uint address = (uint)(payload[4] | (payload[5] << 8) | (payload[6] << 16) | (payload[7] << 24));
        if (address != expectedAddress || payload.Length < 8 + length)
            return false;
        data = payload.Slice(8, length).ToArray();
        return true;
    }

    internal static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));
}
