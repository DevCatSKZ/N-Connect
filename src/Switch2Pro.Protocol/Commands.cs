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
    public const byte TransportUsb = 0x00;

    public const byte CmdMemory = 0x02;
    public const byte SubMemoryRead = 0x04;
    /// <summary>Speicher lesen über USB (feste Blockgröße 0x40, wie in SDL).</summary>
    public const byte SubMemoryReadUsb = 0x01;
    public const byte CmdPlayerLeds = 0x09;
    public const byte SubSetLedPattern = 0x07;
    public const byte CmdVibration = 0x0A;
    public const byte SubPlaySample = 0x02;

    /// <summary>Werkskalibrierung linker Stick (9 Byte).</summary>
    public const uint AddrLeftStickCalibration = 0x130A8;
    /// <summary>Werkskalibrierung rechter Stick (9 Byte).</summary>
    public const uint AddrRightStickCalibration = 0x130E8;
    /// <summary>Gerätedaten: Seriennummer, VID/PID, Farben (0x40 Byte).</summary>
    public const uint AddrDeviceInfo = 0x13000;
    /// <summary>Benutzerkalibrierung linker/rechter Stick: Kennung B2 A1, dann 9 Byte wie Werkskalibrierung.</summary>
    public const uint AddrUserLeftStickCalibration = 0x1FC040;
    public const uint AddrUserRightStickCalibration = 0x1FC080;
    /// <summary>Gespeicherte Kopplung (Host-Adressen und Schlüssel), siehe <see cref="PairingRecord"/>.</summary>
    public const uint AddrPairing = 0x1FA000;
    /// <summary>GameCube-Controller: Ruhewerte der analogen Trigger (2 Byte).</summary>
    public const uint AddrTriggerCalibration = 0x13140;
    /// <summary>Werkskalibrierung Gyro-Nullpunkt (3 × float32 ab Offset 4, in rad/s).</summary>
    public const uint AddrGyroCalibration = 0x13040;

    public static byte[] Build(byte command, byte subcommand, ReadOnlySpan<byte> data = default,
        byte transport = TransportBluetooth)
    {
        var buf = new byte[8 + data.Length];
        buf[0] = command;
        buf[1] = DirectionRequest;
        buf[2] = transport;
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

    /// <summary>
    /// Start-Sequenz für USB (Bulk-Endpunkte von Interface 1). Aus SDL (SDL_hidapi_switch2.c,
    /// HIDAPI_DriverSwitch2_InitUSB), dort mit echter Hardware erprobt. Schreibt nichts in den Speicher.
    /// Danach sendet der Controller HID-Bericht 0x05 (64 Byte) auf Interface 0.
    /// </summary>
    public static IReadOnlyList<byte[]> UsbInitSequence { get; } =
    [
        Hex("07 91 00 01 00 00 00 00"),
        // Feature-Maske setzen: Tasten, Sticks, Bewegungssensor (0x27 wie SDL).
        Hex("0C 91 00 02 00 04 00 00 27 00 00 00"),
        Hex("11 91 00 01 00 00 00 00"),
        Hex("0A 91 00 08 00 14 00 00 01 FF FF FF FF FF FF FF FF 35 00 46 00 00 00 00 00 00 00 00"),
        // Features einschalten.
        Hex("0C 91 00 04 00 04 00 00 27 00 00 00"),
        Hex("01 91 00 0C 00 00 00 00"),
        // Vibration einschalten.
        Hex("01 91 00 01 00 00 00 00"),
        Hex("08 91 00 02 00 04 00 00 01 00 00 00"),
        // Berichtsformat 0x05 (mit Bewegungsdaten).
        Hex("03 91 00 0A 00 04 00 00 05 00 00 00"),
        // Ausgabe starten.
        Hex("03 91 00 0D 00 08 00 00 01 00 FF FF FF FF FF FF"),
    ];

    /// <summary>
    /// Start-Sequenz für USB mit der Feature-Maske des Controllers (wie <see cref="UsbInitSequence"/>, aber mit
    /// derselben Maske wie per Bluetooth, z. B. 0x2F mit Vibration).
    /// </summary>
    public static IEnumerable<byte[]> UsbStartSequence(byte featureMask) =>
        UsbInitSequence.Select(c => c[0] == 0x0C && c.Length >= 9 ? WithMask(c, featureMask) : c);

    private static byte[] WithMask(byte[] command, byte mask)
    {
        var copy = (byte[])command.Clone();
        copy[8] = mask;
        return copy;
    }

    /// <summary>Speicherblock (0x40 Byte) über USB lesen. Antwort: 16 Byte Kopf, dann Daten.</summary>
    public static byte[] ReadMemoryUsb(uint address) =>
        Build(CmdMemory, SubMemoryReadUsb,
        [
            0x40, 0x00, 0x00, 0x00,
            (byte)address, (byte)(address >> 8), (byte)(address >> 16), (byte)(address >> 24),
        ], TransportUsb);

    /// <summary>Feature-Maske setzen (Bit 0 Tasten, 1 Sticks, 2 Bewegungssensor, … ; 0xFF = alles).</summary>
    public static byte[] SetFeatures(byte mask) => Build(0x0C, 0x02, [mask, 0, 0, 0]);

    /// <summary>
    /// Feature-Bits je Controller (commands.md, „Feature Flags“): 0x01 Tasten, 0x02 Sticks, 0x04 Bewegungssensor,
    /// 0x10 Maussensor (nur Joy-Con), 0x20 Vibration. Pro Controller 2 und GameCube wie die Konsole (0x2F);
    /// Joy-Con 2 zusätzlich mit Maussensor. Undokumentierte Bits bleiben aus (sonst Phantom-ZL/ZR bei Joy-Con).
    /// </summary>
    public static byte FeatureMask(ControllerKind kind) =>
        kind is ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right ? (byte)0x37 : (byte)0x2F;

    /// <summary>Funktionen der Maske einschalten.</summary>
    public static byte[] EnableFeatures(byte mask) => Build(0x0C, 0x04, [mask, 0, 0, 0]);

    /// <summary>Joy-Con 2: Daten des Charging Grip lesen (0x20 Byte); nur gültig, wenn der Joy-Con im Griff steckt.</summary>
    public static byte[] GripInfo() => Build(0x08, 0x01, [0x20, 0, 0, 0]);

    /// <summary>Joy-Con 2: GL/GR-Tasten des Charging Grip ein- oder ausschalten.</summary>
    public static byte[] EnableGripButtons(bool enable) => Build(0x08, 0x02, [enable ? (byte)1 : (byte)0, 0, 0, 0]);

    /// <summary>
    /// Steckt der Joy-Con im Charging Grip? Antwortdaten: 4 × 0, dann Gerätedaten des Griffs wie bei 0x13000
    /// (Seriennummer, ab 0x12 VID 057E und PID 2068).
    /// </summary>
    public static bool IsInGrip(ReadOnlySpan<byte> response) =>
        IsResponseTo(response, 0x08, 0x01) && response.Length >= 8 + 4 + 0x16
        && response[8 + 4 + 0x12] == 0x7E && response[8 + 4 + 0x13] == 0x05 && response[8 + 4 + 0x14] == 0x68 && response[8 + 4 + 0x15] == 0x20;

    /// <summary>Firmware-Version abfragen (Antwortdaten ab Byte 8).</summary>
    public static byte[] FirmwareVersion() => Build(0x10, 0x01);

    /// <summary>Spieler-LEDs setzen (Bit 0–3 = LED 1–4).</summary>
    public static byte[] SetPlayerLeds(byte mask, byte transport = TransportBluetooth) =>
        Build(CmdPlayerLeds, SubSetLedPattern, [mask, 0, 0, 0, 0, 0, 0, 0], transport);

    /// <summary>LED-Muster wie auf der Konsole (Spieler 1–8).</summary>
    public static byte PlayerLedMask(int playerIndex)
    {
        ReadOnlySpan<byte> pattern = [0x1, 0x3, 0x7, 0xF, 0x9, 0x5, 0xD, 0x6];
        return pattern[((playerIndex % 8) + 8) % 8];
    }

    /// <summary>Kurzes „Verbunden“-Klicken abspielen (eingebautes Muster 3).</summary>
    public static byte[] PlayConnectSample(byte transport = TransportBluetooth) =>
        Build(CmdVibration, SubPlaySample, [0x03, 0, 0, 0], transport);

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
    public static bool TryParseMemoryRead(ReadOnlySpan<byte> response, uint expectedAddress, out byte[] data,
        byte subcommand = SubMemoryRead)
    {
        data = [];
        if (!IsResponseTo(response, CmdMemory, subcommand) || response.Length < 16)
            return false;
        var payload = response[8..];
        int length = payload[0];
        uint address = (uint)(payload[4] | (payload[5] << 8) | (payload[6] << 16) | (payload[7] << 24));
        if (address != expectedAddress || payload.Length < 8 + length)
            return false;
        data = payload.Slice(8, length).ToArray();
        return true;
    }

    public static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));
}

/// <summary>
/// Gespeicherte Kopplung (Speicher 0x1FA000). Aufbau aus einer echten Aufzeichnung (Pro Controller 2):
/// Byte 0 = Anzahl Einträge, ab 0x08 Einträge zu je 0x28 Byte: Host-Adresse (6, höchstes Byte zuerst),
/// 12 × 0, LTK (16), 6 × 0. Die Konsole trägt zwei Adressen mit demselben Schlüssel ein.
/// </summary>
public sealed record PairingRecord(IReadOnlyList<ulong> HostAddresses, byte[] LongTermKey)
{
    public const int EntryOffset = 0x08;
    public const int EntrySize = 0x28;

    public static bool TryParse(ReadOnlySpan<byte> block, out PairingRecord record)
    {
        record = new PairingRecord([], []);
        if (block.Length < EntryOffset + EntrySize || block[0] is 0 or 0xFF)
            return false;
        var hosts = new List<ulong>();
        byte[]? ltk = null;
        for (int i = 0; i < block[0]; i++)
        {
            int o = EntryOffset + i * EntrySize;
            if (o + EntrySize > block.Length)
                break;
            ulong address = 0;
            foreach (byte b in block.Slice(o, 6))
                address = address << 8 | b;
            hosts.Add(address);
            ltk ??= block.Slice(o + 0x12, 16).ToArray();
        }
        if (ltk is null || ltk.All(b => b == 0) || ltk.All(b => b == 0xFF))
            return false;
        record = new PairingRecord(hosts, ltk);
        return true;
    }
}
