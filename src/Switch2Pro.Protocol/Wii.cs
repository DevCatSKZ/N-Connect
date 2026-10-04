namespace Switch2Pro.Protocol;

/// <summary>Was an der Wii-Fernbedienung steckt (Kennung im Erweiterungsregister 0xA400FA).</summary>
public enum WiiExtension
{
    None,
    Nunchuk,
    Classic,
    /// <summary>Wii U Pro Controller (meldet sich als Fernbedienung mit dieser „Erweiterung“).</summary>
    WiiUPro,
    /// <summary>Etwas anderes (z. B. Balance Board) – wird ignoriert.</summary>
    Other,
    /// <summary>MotionPlus aktiv, ohne weitere Erweiterung.</summary>
    MotionPlus,
    /// <summary>MotionPlus aktiv, Nunchuk dahinter (Durchreichen: Daten wechseln sich ab).</summary>
    MotionPlusNunchuk,
    /// <summary>MotionPlus aktiv, Classic Controller dahinter (Durchreichen).</summary>
    MotionPlusClassic,
}

/// <summary>
/// Protokoll der Wii-Fernbedienung und des Wii U Pro Controllers (Bluetooth-HID). Nach der öffentlichen
/// Dokumentation auf WiiBrew („Wiimote“, „Extension Controllers“, „Wii Motion Plus“, „IR Camera“) und dem
/// Linux-Treiber hid-wiimote: Ausgabeberichte 0x11 (LEDs), 0x12 (Berichtsmodus), 0x13/0x1A (IR-Kamera),
/// 0x15 (Status), 0x16/0x17 (Speicher schreiben/lesen); Eingaben 0x20 (Status), 0x21 (Speicher),
/// 0x22 (Bestätigung), 0x30–0x3D (Daten). Bit 0 des ersten Datenbytes jedes Ausgabeberichts schaltet die Vibration.
/// </summary>
public static class Wii
{
    public const ushort ProductRemote = 0x0306;
    /// <summary>Fernbedienung Plus (-TR) und Wii U Pro Controller.</summary>
    public const ushort ProductRemotePlus = 0x0330;

    public const byte ReportLeds = 0x11, ReportMode = 0x12, ReportIrPixelClock = 0x13, ReportStatusRequest = 0x15,
        ReportWrite = 0x16, ReportRead = 0x17, ReportIrLogic = 0x1A;
    public const byte InputStatus = 0x20, InputRead = 0x21, InputAck = 0x22;
    /// <summary>Tasten + Beschleunigung + 16 Byte Erweiterung.</summary>
    public const byte ModeButtonsAccelExt = 0x35;
    /// <summary>Tasten + 19 Byte Erweiterung (Wii U Pro).</summary>
    public const byte ModeButtonsExt19 = 0x34;
    /// <summary>Tasten + Beschleunigung + 10 Byte IR (einfach) + 6 Byte Erweiterung.</summary>
    public const byte ModeButtonsAccelIrExt = 0x37;

    public const uint RegExtensionInit1 = 0xA400F0, RegExtensionInit2 = 0xA400FB, RegExtensionId = 0xA400FA;
    public const uint RegMotionPlusInit = 0xA600F0, RegMotionPlusId = 0xA600FA, RegMotionPlusActivate = 0xA600FE;

    public static byte[] Leds(int playerIndex, bool rumble)
    {
        // Vier LEDs (Bit 4–7); Spieler 5–8 wie an der Konsole mit Mustern.
        byte[] patterns = [0x10, 0x20, 0x40, 0x80, 0x90, 0xA0, 0xC0, 0xF0];
        byte leds = playerIndex >= 0 ? patterns[playerIndex % 8] : (byte)0;
        return [ReportLeds, (byte)(leds | (rumble ? 1 : 0))];
    }

    /// <summary>Daten dauernd senden (Bit 2), im gewünschten Format.</summary>
    /// <summary>
    /// Datenformat einstellen. <paramref name="continuous"/>: 100 Berichte/s auch ohne Änderung (Bit 0x04); sonst nur
    /// bei Änderung – spart Funkzeit für andere Controller, ohne Verzögerung (Änderungen werden sofort gesendet).
    /// </summary>
    public static byte[] SetMode(byte mode, bool rumble, bool continuous = true) =>
        [ReportMode, (byte)((continuous ? 0x04 : 0) | (rumble ? 1 : 0)), mode];

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

    // ---------- Erweiterungen ----------

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
            (0x04, 0x05) => WiiExtension.MotionPlus,
            (0x05, 0x05) => WiiExtension.MotionPlusNunchuk,
            (0x07, 0x05) => WiiExtension.MotionPlusClassic,
            _ => WiiExtension.Other,
        };
    }

    /// <summary>
    /// Inaktives MotionPlus (Register 0xA600FA nach 0x55 → 0xA600F0): Kennung …A6 20 00 05 bzw. bei
    /// „MotionPlus Inside“ …A6 20 04 05.
    /// </summary>
    public static bool IsInactiveMotionPlus(ReadOnlySpan<byte> id) =>
        id.Length >= 6 && id[2] == 0xA6 && id[3] == 0x20 && id[5] == 0x05;

    /// <summary>MotionPlus einschalten: 0x04 allein, 0x05 mit Nunchuk, 0x07 mit Classic Controller dahinter.</summary>
    public static byte MotionPlusMode(WiiExtension behind) => behind switch
    {
        WiiExtension.Nunchuk => 0x05,
        WiiExtension.Classic => 0x07,
        _ => 0x04,
    };

    public static bool HasMotionPlus(this WiiExtension e) =>
        e is WiiExtension.MotionPlus or WiiExtension.MotionPlusNunchuk or WiiExtension.MotionPlusClassic;

    /// <summary>Nunchuk vorhanden (direkt oder hinter dem MotionPlus) – dann wird die Fernbedienung senkrecht gehalten.</summary>
    public static bool HasNunchuk(this WiiExtension e) => e is WiiExtension.Nunchuk or WiiExtension.MotionPlusNunchuk;

    public static bool HasClassic(this WiiExtension e) => e is WiiExtension.Classic or WiiExtension.MotionPlusClassic;

    // ---------- IR-Kamera (Zeiger über die Sensorleiste) ----------

    /// <summary>IR-Kamera einschalten (Pixeltakt und Logik).</summary>
    public static byte[] IrPixelClock(bool rumble) => [ReportIrPixelClock, (byte)(0x04 | (rumble ? 1 : 0))];
    public static byte[] IrLogic(bool rumble) => [ReportIrLogic, (byte)(0x04 | (rumble ? 1 : 0))];

    /// <summary>
    /// Registerschritte der IR-Kamera (WiiBrew „IR Camera → Initialization“): Einstellungen freigeben, Empfindlichkeit
    /// (Stufe 3, empfohlen), Modus „einfach“ (10 Byte für 4 Punkte), fertig.
    /// </summary>
    public static IEnumerable<byte[]> IrSetup(bool rumble)
    {
        yield return WriteRegister(0xB00030, [0x08], rumble);
        yield return WriteRegister(0xB00000, [0x02, 0x00, 0x00, 0x71, 0x01, 0x00, 0xAA, 0x00, 0x64], rumble);
        yield return WriteRegister(0xB0001A, [0x63, 0x03], rumble);
        yield return WriteRegister(0xB00033, [0x01], rumble);
        yield return WriteRegister(0xB00030, [0x08], rumble);
    }

    /// <summary>
    /// IR-Punkte im einfachen Format (10 Byte, je 2 Punkte in 5 Byte): X/Y 10 Bit (1024 × 768), 0x3FF = kein Punkt.
    /// </summary>
    public static List<(int X, int Y)> ParseIrBasic(ReadOnlySpan<byte> ir)
    {
        var points = new List<(int, int)>();
        for (int pair = 0; pair + 5 <= Math.Min(ir.Length, 10); pair += 5)
        {
            var b = ir.Slice(pair, 5);
            int x1 = b[0] | (b[2] & 0x30) << 4, y1 = b[1] | (b[2] & 0xC0) << 2;
            int x2 = b[3] | (b[2] & 0x03) << 8, y2 = b[4] | (b[2] & 0x0C) << 6;
            if (x1 < 1023 && y1 < 1023) points.Add((x1, y1));
            if (x2 < 1023 && y2 < 1023) points.Add((x2, y2));
        }
        return points;
    }

    /// <summary>
    /// Zeigerposition 0…1 (links oben = 0,0) aus den IR-Punkten: Mitte der beiden Lichtpunkte der Sensorleiste,
    /// gespiegelt (die Kamera sieht das Bild seitenverkehrt). null, wenn kein Punkt sichtbar ist.
    /// </summary>
    public static (float X, float Y)? Pointer(IReadOnlyList<(int X, int Y)> points)
    {
        if (points.Count == 0)
            return null;
        float x, y;
        if (points.Count >= 2)
        {
            // Die zwei am weitesten auseinanderliegenden Punkte = die beiden Enden der Sensorleiste.
            var (a, b) = (points[0], points[1]);
            double best = -1;
            for (int i = 0; i < points.Count; i++)
                for (int j = i + 1; j < points.Count; j++)
                {
                    double d = Math.Pow(points[i].X - points[j].X, 2) + Math.Pow(points[i].Y - points[j].Y, 2);
                    if (d > best) (best, a, b) = (d, points[i], points[j]);
                }
            x = (a.X + b.X) / 2f;
            y = (a.Y + b.Y) / 2f;
        }
        else
        {
            (x, y) = (points[0].X, points[0].Y);
        }
        return (Math.Clamp(1f - x / 1023f, 0f, 1f), Math.Clamp(y / 767f, 0f, 1f));
    }

    // ---------- Status, Speicher ----------

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

    /// <summary>Ein Datenbericht ohne Zustand (für einfache Fälle und Tests); im Betrieb <see cref="WiiParser"/> verwenden.</summary>
    public static bool TryParseData(ReadOnlySpan<byte> r, WiiExtension ext, int battery, out ControllerState state) =>
        new WiiParser().TryParse(r, ext, battery, out state);

    /// <summary>Tasten des Classic Controllers und Wii U Pro Controllers (zwei Bytes, 0 = gedrückt).</summary>
    internal static ProButtons ClassicButtons(byte e4, byte e5)
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
            _ => new AxisCalibration(2048, 1600, 1600), // Nunchuk: Mitte 128 (×16), Ausschlag ±100 (×16); Classic ähnlich
        };
        var stick = new StickCalibration(axis, axis);
        return new DeviceCalibration { Left = stick, Right = stick };
    }
}

/// <summary>
/// Datenberichte der Wii-Fernbedienung mit Gedächtnis: Beim Durchreichen (MotionPlus + Nunchuk/Classic) wechseln
/// sich Gyro- und Erweiterungsdaten ab – die jeweils andere Hälfte wird vom letzten Bericht übernommen. Außerdem
/// wird der Gyro-Nullpunkt laufend nachgeführt, solange die Fernbedienung ruhig liegt.
/// </summary>
public sealed class WiiParser
{
    // Letzter Stand der Erweiterung (beim Durchreichen nur jeder zweite Bericht).
    private ProButtons _extButtons;
    private int _lx = 2048, _ly = 2048, _rx = 2048, _ry = 2048;
    private (float Yaw, float Roll, float Pitch)? _gyro;
    private readonly GyroZero _zero = new();

    public bool TryParse(ReadOnlySpan<byte> r, WiiExtension ext, int battery, out ControllerState state)
    {
        state = new ControllerState();
        if (r.Length < 3 || r[0] < 0x30 || r[0] > 0x3F)
            return false;
        byte mode = r[0];
        bool hasCore = mode != 0x3D && mode != 0x3E && mode != 0x3F;
        ReadOnlySpan<byte> accel = mode is 0x31 or 0x33 or 0x35 or 0x37 && r.Length >= 6 ? r.Slice(3, 3) : default;
        ReadOnlySpan<byte> ir = mode == 0x37 && r.Length >= 16 ? r.Slice(6, 10) : mode == 0x36 && r.Length >= 13 ? r.Slice(3, 10) : default;
        int extOffset = mode switch { 0x32 or 0x34 => 3, 0x35 => 6, 0x36 => 13, 0x37 => 16, 0x3D => 1, _ => -1 };
        ReadOnlySpan<byte> e = extOffset > 0 && r.Length > extOffset ? r[extOffset..] : default;

        if (ext == WiiExtension.WiiUPro)
            return Wii.TryParseWiiUPro(e, out state);

        var b = ProButtons.None;
        if (hasCore)
        {
            byte b0 = r[1], b1 = r[2];
            bool left = (b0 & 0x01) != 0, right = (b0 & 0x02) != 0, down = (b0 & 0x04) != 0, up = (b0 & 0x08) != 0;
            if (!ext.HasNunchuk() && !ext.HasClassic())
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

        if (e.Length >= 6)
            ParseExtension(e, ext);
        if (ext.HasNunchuk() || ext.HasClassic())
            b |= _extButtons;

        Motion? motion = null;
        if (accel.Length == 3)
        {
            // 8-Bit-Beschleunigung, Ruhe ≈ 0x80, 1 g ≈ 26 Schritte → 4096 ≙ 1 g (wie Switch 2).
            static short A(byte v) => (short)Math.Clamp((v - 128) * 4096 / 26, -32768, 32767);
            short gx = 0, gy = 0, gz = 0;
            if (_gyro is { } g)
            {
                // Grad/s → Rohwerte wie Switch 2 (2000 °/s ≙ 32767): Nicken = X, Gieren = Z, Rollen = −Y
                // (Achsen wie in Mapping.ToDs4Report). Nullpunkt laufend nachgeführt.
                var (yaw, roll, pitch) = _zero.Apply(g);
                static short R(float dps) => (short)Math.Clamp(dps * 32767f / 2000f, -32768f, 32767f);
                (gx, gy, gz) = (R(pitch), R(-roll), R(yaw));
            }
            motion = new Motion(A(accel[0]), A(accel[1]), A(accel[2]), gx, gy, gz);
        }

        (float X, float Y)? pointer = ir.Length >= 10 ? Wii.Pointer(Wii.ParseIrBasic(ir)) : null;

        bool classic = ext.HasClassic();
        state = new ControllerState
        {
            Kind = ControllerKind.WiiRemote,
            Buttons = b,
            LeftX = ext.HasNunchuk() || classic ? _lx : 2048, LeftY = ext.HasNunchuk() || classic ? _ly : 2048,
            RightX = classic ? _rx : 2048, RightY = classic ? _ry : 2048,
            Motion = motion,
            BatteryPercent = battery,
            Pointer = pointer,
        };
        return true;
    }

    /// <summary>Erweiterungsbytes übernehmen: Nunchuk, Classic, MotionPlus (allein oder durchgereicht).</summary>
    private void ParseExtension(ReadOnlySpan<byte> e, WiiExtension ext)
    {
        switch (ext)
        {
            case WiiExtension.Nunchuk:
                Nunchuk(e[0], e[1], (e[5] & 0x02) == 0, (e[5] & 0x01) == 0);
                break;
            case WiiExtension.Classic:
                Classic(e, passThrough: false);
                break;
            case WiiExtension.MotionPlus:
            case WiiExtension.MotionPlusNunchuk:
            case WiiExtension.MotionPlusClassic:
                if ((e[5] & 0x02) != 0)
                {
                    // MotionPlus-Daten: 14 Bit je Achse, Mitte 8192; „langsam“-Bit → ±440 °/s, sonst ±2000 °/s.
                    int yaw = e[0] | (e[3] & 0xFC) << 6, roll = e[1] | (e[4] & 0xFC) << 6, pitch = e[2] | (e[5] & 0xFC) << 6;
                    static float Dps(int v, bool slow) => (v - 8192) / 8192f * (slow ? 440f : 2000f);
                    _gyro = (Dps(yaw, (e[3] & 0x02) != 0), Dps(roll, (e[4] & 0x02) != 0), Dps(pitch, (e[3] & 0x01) != 0));
                }
                else if (ext == WiiExtension.MotionPlusNunchuk)
                {
                    // Durchgereichter Nunchuk: C = Bit 3, Z = Bit 2 von Byte 5 (0 = gedrückt).
                    Nunchuk(e[0], e[1], (e[5] & 0x08) == 0, (e[5] & 0x04) == 0);
                }
                else if (ext == WiiExtension.MotionPlusClassic)
                {
                    Classic(e, passThrough: true);
                }
                break;
        }
    }

    private void Nunchuk(byte sx, byte sy, bool c, bool z)
    {
        // Stick 8 Bit (Mitte ≈ 128) → 12 Bit wie bei den Switch-Controllern.
        _lx = sx << 4;
        _ly = sy << 4;
        _extButtons = (c ? ProButtons.L : ProButtons.None) | (z ? ProButtons.ZL : ProButtons.None);
    }

    /// <summary>
    /// Classic Controller, Format 1. Beim Durchreichen fehlt jeweils das niedrigste Bit von LX/LY; die Steuerkreuz-
    /// Tasten hoch/links stehen dann in Bit 0 von Byte 0 bzw. 1 (WiiBrew „Classic Controller pass-through“).
    /// </summary>
    private void Classic(ReadOnlySpan<byte> e, bool passThrough)
    {
        int clx = passThrough ? e[0] & 0x3E : e[0] & 0x3F, cly = passThrough ? e[1] & 0x3E : e[1] & 0x3F;
        int crx = ((e[0] >> 3) & 0x18) | ((e[1] >> 5) & 0x06) | ((e[2] >> 7) & 0x01);
        int cry = e[2] & 0x1F;
        _lx = clx << 6; _ly = cly << 6; _rx = crx << 7; _ry = cry << 7;
        var buttons = Wii.ClassicButtons(e[4], passThrough ? (byte)(e[5] | 0x03) : e[5]);
        if (passThrough)
        {
            if ((e[0] & 0x01) == 0) buttons |= ProButtons.Up;
            if ((e[1] & 0x01) == 0) buttons |= ProButtons.Left;
        }
        _extButtons = buttons;
    }

    /// <summary>
    /// Gyro-Nullpunkt: Das MotionPlus hat keinen festen Nullpunkt. Liegt die Fernbedienung ruhig (wenig Schwankung über
    /// ~0,5 s), wird der Mittelwert als neuer Nullpunkt übernommen – wie die „kontinuierliche Kalibrierung“ in Steam.
    /// </summary>
    private sealed class GyroZero
    {
        private const int Window = 50;
        private readonly Queue<(float Y, float R, float P)> _samples = new();
        private (float Y, float R, float P) _bias;
        private bool _hasBias;

        public (float Yaw, float Roll, float Pitch) Apply((float Yaw, float Roll, float Pitch) g)
        {
            _samples.Enqueue(g);
            if (_samples.Count > Window)
                _samples.Dequeue();
            if (_samples.Count == Window)
            {
                float my = _samples.Average(s => s.Y), mr = _samples.Average(s => s.R), mp = _samples.Average(s => s.P);
                float spread = _samples.Max(s => Math.Max(Math.Abs(s.Y - my), Math.Max(Math.Abs(s.R - mr), Math.Abs(s.P - mp))));
                if (spread < 3f) // ruhig: höchstens ±3 °/s Schwankung
                {
                    _bias = (my, mr, mp);
                    _hasBias = true;
                }
            }
            return _hasBias ? (g.Yaw - _bias.Y, g.Roll - _bias.R, g.Pitch - _bias.P) : (0f, 0f, 0f);
        }
    }
}
