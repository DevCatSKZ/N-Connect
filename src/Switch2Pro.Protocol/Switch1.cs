namespace Switch2Pro.Protocol;

/// <summary>
/// Protokoll der Switch-1-Controller (Pro Controller, Joy-Con L/R) über Bluetooth-Classic-HID.
/// Quellen: dekuNukem/Nintendo_Switch_Reverse_Engineering und SDL (SDL_hidapi_switch.c).
/// Ausgabe 0x01 = Vibration + Unterbefehl, 0x10 = nur Vibration.
/// Eingabe 0x21 = Antwort auf Unterbefehl, 0x30 = Vollbericht (Tasten, Sticks, 3 IMU-Proben), 0x3F = einfacher Modus.
/// </summary>
public static class Switch1
{
    public const ushort JoyConLeftProductId = 0x2006;
    public const ushort JoyConRightProductId = 0x2007;
    public const ushort ProControllerProductId = 0x2009;

    public const byte OutputRumbleAndSubcommand = 0x01;
    public const byte OutputRumble = 0x10;
    public const byte InputSubcommandReply = 0x21;
    public const byte InputFull = 0x30;

    public const byte SubDeviceInfo = 0x02;
    public const byte SubSetInputMode = 0x03;
    public const byte SubSetHciState = 0x06;
    public const byte SubSpiRead = 0x10;
    public const byte SubPlayerLights = 0x30;
    public const byte SubHomeLight = 0x38;
    public const byte SubEnableImu = 0x40;
    public const byte SubEnableVibration = 0x48;

    /// <summary>Länge eines Ausgabeberichts über Bluetooth (mit Report-ID).</summary>
    public const int OutputLength = 49;

    public static ReadOnlySpan<byte> NeutralRumble => [0x00, 0x01, 0x40, 0x40];

    /// <summary>Bericht 0x01: Report-ID, Zähler, 8 Byte Vibration (links, rechts), Unterbefehl, Daten.</summary>
    public static byte[] Subcommand(int counter, byte subcommand, ReadOnlySpan<byte> data, ReadOnlySpan<byte> rumble8 = default)
    {
        var r = new byte[OutputLength];
        r[0] = OutputRumbleAndSubcommand;
        r[1] = (byte)(counter & 0x0F);
        WriteRumble(r, rumble8);
        r[10] = subcommand;
        data.CopyTo(r.AsSpan(11));
        return r;
    }

    /// <summary>Bericht 0x10: nur Vibration.</summary>
    public static byte[] RumbleReport(int counter, ReadOnlySpan<byte> rumble8)
    {
        var r = new byte[OutputLength];
        r[0] = OutputRumble;
        r[1] = (byte)(counter & 0x0F);
        WriteRumble(r, rumble8);
        return r;
    }

    private static void WriteRumble(byte[] r, ReadOnlySpan<byte> rumble8)
    {
        if (rumble8.Length == 8)
        {
            rumble8.CopyTo(r.AsSpan(2));
            return;
        }
        NeutralRumble.CopyTo(r.AsSpan(2));
        NeutralRumble.CopyTo(r.AsSpan(6));
    }

    /// <summary>Unterbefehl 0x10: SPI-Flash lesen (Adresse LE, Länge ≤ 0x1D).</summary>
    public static byte[] SpiReadArgs(uint address, byte length) =>
        [(byte)address, (byte)(address >> 8), (byte)(address >> 16), (byte)(address >> 24), length];

    /// <summary>
    /// Antwort 0x21 auf <paramref name="subcommand"/>? Liefert die Daten ab Byte 15 (mit Report-ID gezählt).
    /// Bei SPI-Lesen wird die Adresse geprüft und nur der Datenteil geliefert.
    /// </summary>
    public static bool TryParseReply(ReadOnlySpan<byte> report, byte subcommand, out byte[] data, uint spiAddress = 0)
    {
        data = [];
        if (report.Length < 16 || report[0] != InputSubcommandReply || report[14] != subcommand || (report[13] & 0x80) == 0)
            return false;
        var payload = report[15..];
        if (subcommand == SubSpiRead)
        {
            if (payload.Length < 5)
                return false;
            uint address = (uint)(payload[0] | (payload[1] << 8) | (payload[2] << 16) | (payload[3] << 24));
            int length = payload[4];
            if (address != spiAddress || payload.Length < 5 + length)
                return false;
            data = payload.Slice(5, length).ToArray();
            return true;
        }
        data = payload.ToArray();
        return true;
    }

    // ---------- Kalibrierung ----------

    public const uint SpiFactoryStickLeft = 0x603D;
    public const uint SpiFactoryStickRight = 0x6046;
    public const uint SpiUserStickLeft = 0x8010;
    public const uint SpiUserStickRight = 0x801B;
    public const uint SpiFactoryImu = 0x6020;
    public const uint SpiUserImu = 0x8026;
    public const uint SpiBodyColor = 0x6050;

    /// <summary>
    /// Stick-Kalibrierung aus 9 Byte. Linker Stick: Max über Mitte, Mitte, Min unter Mitte;
    /// rechter Stick: Mitte, Min, Max (jeweils X/Y gepackt).
    /// </summary>
    public static bool TryParseStick(ReadOnlySpan<byte> d, bool left, out StickCalibration calibration)
    {
        calibration = StickCalibration.Default;
        if (d.Length < 9 || d[..9].IndexOfAnyExcept((byte)0xFF) < 0)
            return false;
        var a = StickCalibration.Unpack12(d[0..3]);
        var b = StickCalibration.Unpack12(d[3..6]);
        var c = StickCalibration.Unpack12(d[6..9]);
        var (max, center, min) = left ? (a, b, c) : (c, a, b);
        if (center.A is 0 or 0xFFF || center.B is 0 or 0xFFF || max.A < 200 || max.B < 200 || min.A < 200 || min.B < 200)
            return false;
        calibration = new StickCalibration(new AxisCalibration(center.A, max.A, min.A), new AxisCalibration(center.B, max.B, min.B));
        return true;
    }

    /// <summary>Benutzerkalibrierung gilt nur mit Kennung B2 A1 davor.</summary>
    public static bool HasUserMagic(ReadOnlySpan<byte> d) => d.Length >= 2 && d[0] == 0xB2 && d[1] == 0xA1;

    /// <summary>
    /// IMU-Kalibrierung (24 Byte): Beschl.-Nullpunkt, Beschl.-Empfindlichkeit, Gyro-Nullpunkt, Gyro-Empfindlichkeit (je X/Y/Z, int16).
    /// </summary>
    public static bool TryParseImu(ReadOnlySpan<byte> d, out ImuCalibration calibration)
    {
        calibration = ImuCalibration.Default;
        if (d.Length < 24 || d[..24].IndexOfAnyExcept((byte)0xFF) < 0)
            return false;
        var c = new ImuCalibration(S16(d, 0), S16(d, 1), S16(d, 2), S16(d, 3), S16(d, 4), S16(d, 5),
            S16(d, 6), S16(d, 7), S16(d, 8), S16(d, 9), S16(d, 10), S16(d, 11));
        if (c.AccelSensX == c.AccelOriginX || c.GyroSensX == c.GyroOriginX)
            return false;
        calibration = c;
        return true;
    }

    /// <summary>Int16 (LE) Nummer <paramref name="index"/> im Puffer.</summary>
    private static short S16(ReadOnlySpan<byte> b, int index) => (short)(b[index * 2] | (b[index * 2 + 1] << 8));

    // ---------- Eingaben ----------

    /// <summary>
    /// Vollbericht 0x30 (mit Report-ID). Die Bewegungsdaten werden in die Einheiten und Achsen
    /// der Switch-2-Controller umgerechnet (Gyro 2000 °/s ≙ 32767, Beschl. 4096 ≙ 1 g), damit der
    /// Rest der Kette (DS4-Ausgabe) für alle Controller gleich ist.
    /// </summary>
    public static bool TryParseFull(ReadOnlySpan<byte> r, ControllerKind kind, in ImuCalibration imu, out ControllerState state)
    {
        state = new ControllerState();
        if (r.Length < 25 || r[0] != InputFull && r[0] != InputSubcommandReply)
            return false;

        byte power = r[2];
        byte right = r[3], shared = r[4], left = r[5];
        var buttons = ProButtons.None;
        void Map(byte value, int mask, ProButtons button) { if ((value & mask) != 0) buttons |= button; }
        Map(right, 0x01, ProButtons.Y); Map(right, 0x02, ProButtons.X); Map(right, 0x04, ProButtons.B); Map(right, 0x08, ProButtons.A);
        Map(right, 0x10, ProButtons.SRRight); Map(right, 0x20, ProButtons.SLRight); Map(right, 0x40, ProButtons.R); Map(right, 0x80, ProButtons.ZR);
        Map(shared, 0x01, ProButtons.Minus); Map(shared, 0x02, ProButtons.Plus); Map(shared, 0x04, ProButtons.RightStick);
        Map(shared, 0x08, ProButtons.LeftStick); Map(shared, 0x10, ProButtons.Home); Map(shared, 0x20, ProButtons.Capture);
        Map(left, 0x01, ProButtons.Down); Map(left, 0x02, ProButtons.Up); Map(left, 0x04, ProButtons.Right); Map(left, 0x08, ProButtons.Left);
        Map(left, 0x10, ProButtons.SRLeft); Map(left, 0x20, ProButtons.SLLeft); Map(left, 0x40, ProButtons.L); Map(left, 0x80, ProButtons.ZL);

        var (lx, ly) = StickCalibration.Unpack12(r.Slice(6, 3));
        var (rx, ry) = StickCalibration.Unpack12(r.Slice(9, 3));

        Motion? motion = null;
        if (r[0] == InputFull && r.Length >= 49)
        {
            // Neueste der drei Proben (Byte 37–48).
            var s = r.Slice(37, 12);
            if (S16(s, 0) != 0 || S16(s, 1) != 0 || S16(s, 2) != 0)
                motion = imu.ToSwitch2Axes(S16(s, 0), S16(s, 1), S16(s, 2), S16(s, 3), S16(s, 4), S16(s, 5),
                    kind == ControllerKind.JoyCon1Right);
        }

        int level = (power >> 5) & 0x07; // 0, 2, 4, 6, 8 (Bit 4 = Laden)
        state = new ControllerState
        {
            Kind = kind,
            Buttons = buttons,
            LeftX = lx, LeftY = ly, RightX = rx, RightY = ry,
            Motion = motion,
            BatteryPercent = Math.Clamp(level * 2 * 100 / 8, 0, 100),
            Charging = (power & 0x10) != 0,
        };
        return true;
    }
}

/// <summary>IMU-Werkskalibrierung eines Switch-1-Controllers (Rohwerte aus dem SPI-Flash).</summary>
public readonly record struct ImuCalibration(
    short AccelOriginX, short AccelOriginY, short AccelOriginZ,
    short AccelSensX, short AccelSensY, short AccelSensZ,
    short GyroOriginX, short GyroOriginY, short GyroOriginZ,
    short GyroSensX, short GyroSensY, short GyroSensZ)
{
    public static ImuCalibration Default { get; } = new(0, 0, 0, 16384, 16384, 16384, 0, 0, 0, 13371, 13371, 13371);

    // Zieleinheiten wie Switch 2: Gyro 32767 ≙ 2000 °/s, Beschleunigung 4096 ≙ 1 g.
    private const float Switch2GyroPerDps = 32767f / 2000f;

    private static short Clamp(float v) => (short)Math.Clamp(MathF.Round(v), -32768f, 32767f);

    /// <summary>
    /// Switch-1-Rohachsen → Switch-2-Rohachsen. SDL gibt Switch 1 als (−Y, Z, −X) und Switch 2 als
    /// (X, Z, −Y) aus; gleichgesetzt ergibt das X₂ = −Y₁, Y₂ = X₁, Z₂ = Z₁.
    /// Der rechte Joy-Con hat gespiegelte Achsen (SDL dreht X und Z der Ausgabe um).
    /// </summary>
    public Motion ToSwitch2Axes(short ax, short ay, short az, short gx, short gy, short gz, bool rightJoyCon)
    {
        float Acc(short v, short sens, short origin) => v * 4f * 4096f / (sens - origin);
        float Gyr(short v, short sens, short origin) => (v - origin) * 936f / (sens - origin) * Switch2GyroPerDps;

        float ax1 = Acc(ax, AccelSensX, AccelOriginX), ay1 = Acc(ay, AccelSensY, AccelOriginY), az1 = Acc(az, AccelSensZ, AccelOriginZ);
        float gx1 = Gyr(gx, GyroSensX, GyroOriginX), gy1 = Gyr(gy, GyroSensY, GyroOriginY), gz1 = Gyr(gz, GyroSensZ, GyroOriginZ);

        float ax2 = -ay1, ay2 = ax1, az2 = az1;
        float gx2 = -gy1, gy2 = gx1, gz2 = gz1;
        if (rightJoyCon)
        {
            // SDL: Ausgabe[0] (= X₂) und Ausgabe[1] (= Z₂) umdrehen.
            ax2 = -ax2; az2 = -az2;
            gx2 = -gx2; gz2 = -gz2;
        }
        return new Motion(Clamp(ax2), Clamp(ay2), Clamp(az2), Clamp(gx2), Clamp(gy2), Clamp(gz2));
    }
}

/// <summary>Switch-1-HD-Rumble (4 Byte je Seite). Kodierung wie SDL (Tabellen aus dekuNukem).</summary>
public static class Switch1Rumble
{

    /// <summary>Frame aus großem (tief) und kleinem (hoch) Motor, Stärke 0–1.</summary>
    public static byte[] Frame(byte largeMotor, byte smallMotor, float strength)
    {
        strength = Math.Clamp(strength, 0f, 1f);
        ushort lowAmp = (ushort)(largeMotor / 255f * strength * 65535f);
        ushort highAmp = (ushort)(smallMotor / 255f * strength * 65535f);
        if (lowAmp == 0 && highAmp == 0)
            return [.. Switch1.NeutralRumble];
        // Frequenz-Codes wie SDL (HIDAPI_DriverSwitch_ActuallyRumbleJoystick).
        const ushort hf = 0x0074;
        const byte lf = 0x3D;
        byte hfa = Encode(Hfa, highAmp);
        ushort lfa = Encode(Lfa, lowAmp);
        return
        [
            (byte)(hf & 0xFF),
            (byte)(hfa | ((hf >> 8) & 0x01)),
            (byte)(lf | ((lfa >> 8) & 0x80)),
            (byte)(lfa & 0xFF),
        ];
    }

    private static T Encode<T>((ushort Max, T Code)[] table, ushort amplitude)
    {
        foreach (var (max, code) in table)
            if (amplitude <= max)
                return code;
        return table[^1].Code;
    }

    private static readonly ushort[] AmplitudeSteps =
    [
        0, 514, 775, 921, 1096, 1303, 1550, 1843, 2192, 2606, 3100, 3686, 4383, 5213, 6199, 7372, 7698, 8039, 8395,
        8767, 9155, 9560, 9984, 10426, 10887, 11369, 11873, 12398, 12947, 13520, 14119, 14744, 15067, 15397, 15734,
        16079, 16431, 16790, 17158, 17534, 17918, 18310, 18711, 19121, 19540, 19967, 20405, 20851, 21308, 21775,
        22251, 22739, 23236, 23745, 24265, 24797, 25340, 25894, 26462, 27041, 27633, 28238, 28856, 29488, 30134,
        30794, 31468, 32157, 32861, 33581, 34316, 35068, 35836, 36620, 37422, 38242, 39079, 39935, 40809, 41703,
        42616, 43549, 44503, 45477, 46473, 47491, 48531, 49593, 50679, 51789, 52923, 54082, 55266, 56476, 57713,
        58977, 60268, 61588, 62936, 64315, 65535,
    ];

    private static readonly (ushort Max, byte Code)[] Hfa = BuildHfa();
    private static readonly (ushort Max, ushort Code)[] Lfa = BuildLfa();

    // Hoch: Code = 2·i. Tief: Code = 0x40 + i/2, bei ungeradem i zusätzlich 0x8000.
    private static (ushort, byte)[] BuildHfa() =>
        AmplitudeSteps.Select((a, i) => (a, (byte)(i * 2))).ToArray();

    private static (ushort, ushort)[] BuildLfa() =>
        AmplitudeSteps.Select((a, i) => (a, (ushort)((0x40 + i / 2) | (i % 2 == 1 ? 0x8000 : 0)))).ToArray();
}
