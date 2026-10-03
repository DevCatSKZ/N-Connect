namespace Switch2Pro.Protocol;

/// <summary>Was an der Wii-Fernbedienung steckt (Kennung im Erweiterungsregister 0xA400FA).</summary>
public enum WiiExtension
{
    None,
    Nunchuk,
    Classic,
    /// <summary>Wii U Pro Controller (meldet sich als Fernbedienung mit dieser „Erweiterung“).</summary>
    WiiUPro,
    /// <summary>Etwas anderes (z. B. MotionPlus, Balance Board) – wird ignoriert.</summary>
    Other,
}

/// <summary>
/// Protokoll der Wii-Fernbedienung und des Wii U Pro Controllers (Bluetooth-HID). Nach der öffentlichen
/// Dokumentation auf WiiBrew („Wiimote“, „Extension Controllers“) und dem Linux-Treiber hid-wiimote:
/// Ausgabeberichte 0x11 (LEDs), 0x12 (Berichtsmodus), 0x15 (Status), 0x16/0x17 (Speicher schreiben/lesen);
/// Eingaben 0x20 (Status), 0x21 (Speicher), 0x22 (Bestätigung), 0x30–0x3D (Daten). Bit 0 des ersten
/// Datenbytes jedes Ausgabeberichts schaltet die Vibration.
/// </summary>
public static class Wii
{
    public const ushort ProductRemote = 0x0306;
    /// <summary>Fernbedienung Plus (-TR) und Wii U Pro Controller.</summary>
    public const ushort ProductRemotePlus = 0x0330;

    public const byte ReportLeds = 0x11, ReportMode = 0x12, ReportStatusRequest = 0x15, ReportWrite = 0x16, ReportRead = 0x17;
    public const byte InputStatus = 0x20, InputRead = 0x21, InputAck = 0x22;
    /// <summary>Tasten + Beschleunigung + 16 Byte Erweiterung.</summary>
    public const byte ModeButtonsAccelExt = 0x35;
    /// <summary>Tasten + 19 Byte Erweiterung (Wii U Pro).</summary>
    public const byte ModeButtonsExt19 = 0x34;

    public const uint RegExtensionInit1 = 0xA400F0, RegExtensionInit2 = 0xA400FB, RegExtensionId = 0xA400FA;

    public static byte[] Leds(int playerIndex, bool rumble)
    {
        // Vier LEDs (Bit 4–7); Spieler 5–8 wie an der Konsole mit Mustern.
        byte[] patterns = [0x10, 0x20, 0x40, 0x80, 0x90, 0xA0, 0xC0, 0xF0];
        byte leds = playerIndex >= 0 ? patterns[playerIndex % 8] : (byte)0;
        return [ReportLeds, (byte)(leds | (rumble ? 1 : 0))];
    }

    /// <summary>Daten dauernd senden (Bit 2), im gewünschten Format.</summary>
    public static byte[] SetMode(byte mode, bool rumble) => [ReportMode, (byte)(0x04 | (rumble ? 1 : 0)), mode];

    public static byte[] StatusRequest(bool rumble) => [ReportStatusRequest, (byte)(rumble ? 1 : 0)];

    /// <summary>Registerbereich schreiben (max. 16 Byte).</summary>
    public static byte[] WriteRegister(uint address, ReadOnlySpan<byte> data, bool rumble)
    {
        var r = new byte[22];
        r[0] = ReportWrite;
        r[1] = (byte)(0x04 | (rumble ? 1 : 0));
        r[2] = (byte)(address >> 16);
        r[3] = (byte)(address >> 8);
        r[4] = (byte)address;
        r[5] = (byte)Math.Min(16, data.Length);
        data[..r[5]].CopyTo(r.AsSpan(6));
        return r;
    }

    public static byte[] ReadRegister(uint address, ushort size, bool rumble) =>
    [
        ReportRead, (byte)(0x04 | (rumble ? 1 : 0)),
        (byte)(address >> 16), (byte)(address >> 8), (byte)address, (byte)(size >> 8), (byte)size,
    ];

    /// <summary>Erweiterung aus den 6 Kennungs-Bytes (unverschlüsselt nach der Initialisierung mit 0x55/0x00).</summary>
    public static WiiExtension ExtensionFromId(ReadOnlySpan<byte> id)
    {
        if (id.Length < 6 || id[2] != 0xA4 || id[3] != 0x20)
            return id.Length >= 6 && id.IndexOfAnyExcept((byte)0xFF) < 0 ? WiiExtension.None : WiiExtension.Other;
        return (id[4], id[5]) switch
        {
            (0x00, 0x00) => WiiExtension.Nunchuk,
            (0x01, 0x01) => WiiExtension.Classic,  // auch Classic Controller Pro
            (0x01, 0x20) => WiiExtension.WiiUPro,
            _ => WiiExtension.Other,
        };
    }

    /// <summary>Statusbericht 0x20: Erweiterung gesteckt (Bit 1), Akku (0–200 ≙ voll).</summary>
    public static bool TryParseStatus(ReadOnlySpan<byte> r, out bool extension, out int batteryPercent)
    {
        extension = false;
        batteryPercent = -1;
        if (r.Length < 7 || r[0] != InputStatus)
            return false;
        extension = (r[3] & 0x02) != 0;
        batteryPercent = Math.Clamp(r[6] * 100 / 200, 0, 100);
        return true;
    }

    /// <summary>Antwort 0x21 auf eine Speicher-Leseanfrage: Fehler (0 = ok) und Daten.</summary>
    public static bool TryParseRead(ReadOnlySpan<byte> r, out int error, out ushort addressLow, out byte[] data)
    {
        error = -1;
        addressLow = 0;
        data = [];
        if (r.Length < 22 || r[0] != InputRead)
            return false;
        int size = (r[3] >> 4) + 1;
        error = r[3] & 0x0F;
        addressLow = (ushort)(r[4] << 8 | r[5]);
        data = r.Slice(6, Math.Min(size, 16)).ToArray();
        return true;
    }

    /// <summary>
    /// Datenbericht (0x30–0x37, 0x3D) in einen Zustand übersetzen. Ohne Erweiterung wird die Fernbedienung quer
    /// gehalten (Steuerkreuz gedreht, 1/2 = linke/untere Taste); mit Nunchuk senkrecht. <paramref name="battery"/>
    /// kommt aus dem letzten Statusbericht.
    /// </summary>
    public static bool TryParseData(ReadOnlySpan<byte> r, WiiExtension ext, int battery, out ControllerState state)
    {
        state = new ControllerState();
        if (r.Length < 3 || r[0] < 0x30 || r[0] > 0x3F)
            return false;
        byte mode = r[0];
        bool hasCore = mode != 0x3D && mode != 0x3E && mode != 0x3F;
        ReadOnlySpan<byte> accel = mode is 0x31 or 0x33 or 0x35 or 0x37 && r.Length >= 6 ? r.Slice(3, 3) : default;
        int extOffset = mode switch { 0x32 or 0x34 => 3, 0x35 => 6, 0x36 => 13, 0x37 => 16, 0x3D => 1, _ => -1 };
        ReadOnlySpan<byte> e = extOffset > 0 && r.Length > extOffset ? r[extOffset..] : default;

        if (ext == WiiExtension.WiiUPro)
            return TryParseWiiUPro(e, out state);

        var b = ProButtons.None;
        int lx = 2048, ly = 2048, rx = 2048, ry = 2048, lt = -1, rt = -1;
        if (hasCore)
        {
            byte b0 = r[1], b1 = r[2];
            bool left = (b0 & 0x01) != 0, right = (b0 & 0x02) != 0, down = (b0 & 0x04) != 0, up = (b0 & 0x08) != 0;
            if (ext == WiiExtension.None)
            {
                // Quer gehalten (Steuerkreuz links): „hoch“ der Fernbedienung zeigt nach links usw.
                if (up) b |= ProButtons.Left;
                if (down) b |= ProButtons.Right;
                if (left) b |= ProButtons.Down;
                if (right) b |= ProButtons.Up;
            }
            else
            {
                if (up) b |= ProButtons.Up;
                if (down) b |= ProButtons.Down;
                if (left) b |= ProButtons.Left;
                if (right) b |= ProButtons.Right;
            }
            if ((b0 & 0x10) != 0) b |= ProButtons.Plus;
            if ((b1 & 0x01) != 0) b |= ProButtons.B;      // 2
            if ((b1 & 0x02) != 0) b |= ProButtons.Y;      // 1
            if ((b1 & 0x04) != 0) b |= ProButtons.ZR;     // B (Abzug)
            if ((b1 & 0x08) != 0) b |= ProButtons.A;
            if ((b1 & 0x10) != 0) b |= ProButtons.Minus;
            if ((b1 & 0x80) != 0) b |= ProButtons.Home;
        }

        if (ext == WiiExtension.Nunchuk && e.Length >= 6)
        {
            // Stick 8 Bit (Mitte ≈ 128) → 12 Bit wie bei den Switch-Controllern.
            lx = e[0] << 4;
            ly = e[1] << 4;
            if ((e[5] & 0x01) == 0) b |= ProButtons.ZL;   // Z (0 = gedrückt)
            if ((e[5] & 0x02) == 0) b |= ProButtons.L;    // C
        }
        else if (ext == WiiExtension.Classic && e.Length >= 6)
        {
            // Format 1 (WiiBrew „Classic Controller“): Sticks 6/5 Bit, Trigger 5 Bit, Tasten 0 = gedrückt.
            int clx = e[0] & 0x3F, cly = e[1] & 0x3F;
            int crx = ((e[0] >> 3) & 0x18) | ((e[1] >> 5) & 0x06) | ((e[2] >> 7) & 0x01);
            int cry = e[2] & 0x1F;
            lt = ((e[2] >> 2) & 0x18) | ((e[3] >> 5) & 0x07);
            rt = e[3] & 0x1F;
            lx = clx << 6; ly = cly << 6; rx = crx << 7; ry = cry << 7;
            b |= ClassicButtons(e[4], e[5]);
        }

        Motion? motion = null;
        if (accel.Length == 3)
        {
            // 8-Bit-Beschleunigung, Ruhe ≈ 0x80, 1 g ≈ 26 Schritte → 4096 ≙ 1 g (wie Switch 2).
            static short A(byte v) => (short)Math.Clamp((v - 128) * 4096 / 26, -32768, 32767);
            motion = new Motion(A(accel[0]), A(accel[1]), A(accel[2]), 0, 0, 0);
        }

        state = new ControllerState
        {
            Kind = ControllerKind.WiiRemote,
            Buttons = b,
            LeftX = lx, LeftY = ly, RightX = rx, RightY = ry,
            LeftTrigger = lt < 0 ? -1 : lt * 255 / 31,
            RightTrigger = rt < 0 ? -1 : rt * 255 / 31,
            Motion = motion,
            BatteryPercent = battery,
        };
        return true;
    }

    /// <summary>Tasten des Classic Controllers und Wii U Pro Controllers (zwei Bytes, 0 = gedrückt).</summary>
    private static ProButtons ClassicButtons(byte e4, byte e5)
    {
        var b = ProButtons.None;
        void Bit(byte v, int mask, ProButtons button) { if ((v & mask) == 0) b |= button; }
        Bit(e4, 0x02, ProButtons.R); Bit(e4, 0x04, ProButtons.Plus); Bit(e4, 0x08, ProButtons.Home);
        Bit(e4, 0x10, ProButtons.Minus); Bit(e4, 0x20, ProButtons.L); Bit(e4, 0x40, ProButtons.Down); Bit(e4, 0x80, ProButtons.Right);
        Bit(e5, 0x01, ProButtons.Up); Bit(e5, 0x02, ProButtons.Left); Bit(e5, 0x04, ProButtons.ZR); Bit(e5, 0x08, ProButtons.X);
        Bit(e5, 0x10, ProButtons.A); Bit(e5, 0x20, ProButtons.Y); Bit(e5, 0x40, ProButtons.B); Bit(e5, 0x80, ProButtons.ZL);
        return b;
    }

    /// <summary>
    /// Wii U Pro Controller (Erweiterungsdaten, mind. 11 Byte): vier 12-Bit-Sticks (LX, RX, LY, RY, je 16 Bit LE),
    /// Tasten wie Classic Controller, Byte 10: Stickklicks (0 = gedrückt), Akku (Bit 4–6), Laden (Bit 2, 0 = lädt).
    /// </summary>
    public static bool TryParseWiiUPro(ReadOnlySpan<byte> e, out ControllerState state)
    {
        state = new ControllerState();
        if (e.Length < 11)
            return false;
        static int U12(ReadOnlySpan<byte> d, int o) => (d[o] | d[o + 1] << 8) & 0x0FFF;
        var b = ClassicButtons(e[8], e[9]);
        if ((e[10] & 0x01) == 0) b |= ProButtons.RightStick;
        if ((e[10] & 0x02) == 0) b |= ProButtons.LeftStick;
        int level = (e[10] >> 4) & 0x07; // 0–4
        state = new ControllerState
        {
            Kind = ControllerKind.WiiUPro,
            Buttons = b,
            LeftX = U12(e, 0), RightX = U12(e, 2), LeftY = U12(e, 4), RightY = U12(e, 6),
            BatteryPercent = Math.Clamp(level * 25, 0, 100),
            Charging = (e[10] & 0x04) == 0,
        };
        return true;
    }

    /// <summary>Stick-Kalibrierung je Art: Nunchuk/Classic auf 12 Bit hochgerechnet, Wii U Pro nativ 12 Bit.</summary>
    public static DeviceCalibration DefaultCalibration(WiiExtension ext)
    {
        var axis = ext switch
        {
            WiiExtension.WiiUPro => new AxisCalibration(2048, 1150, 1150),
            WiiExtension.Classic => new AxisCalibration(2048, 1600, 1600),
            _ => new AxisCalibration(2048, 1600, 1600), // Nunchuk: Mitte 128 (×16), Ausschlag ±100 (×16)
        };
        var stick = new StickCalibration(axis, axis);
        // Wii-Achse Y: oben = größer (wie die Switch-Controller).
        return new DeviceCalibration { Left = stick, Right = stick };
    }
}
