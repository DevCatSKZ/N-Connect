namespace Switch2Pro.Protocol;

/// <summary>
/// Lizenzierte Kabel-Gamepads für die Switch (HORI, PowerA, PDP): einfache USB-HID-Gamepads ohne das
/// Nintendo-Protokoll – kein Gyro, keine Vibration. Alle senden denselben 7-Byte-Bericht (wie SDL
/// „Switch input-only controller“, ohne Report-ID):
/// <c>[0]</c> Y B A X L R ZL ZR (Bit 0–7), <c>[1]</c> − + LS RS HOME Aufnahme (Bit 0–5),
/// <c>[2]</c> Steuerkreuz 0–7 (0 = oben, im Uhrzeigersinn; 8/15 = losgelassen), <c>[3..6]</c> LX LY RX RY (0–255,
/// Mitte 128, Y wächst nach unten).
/// </summary>
public static class WiredSwitchPad
{
    /// <summary>
    /// Bekannte Geräte (USB-Hersteller, -Produkt, Anzeigename). Namen bewusst allgemein nach Hersteller – die
    /// Produktbezeichnungen wechseln je Modell. Weitere Geräte desselben Formats lassen sich hier ergänzen.
    /// </summary>
    public static readonly IReadOnlyList<(ushort Vendor, ushort Product, string Name)> Known =
    [
        (0x0F0D, 0x00C1, "HORIPAD für Nintendo Switch"),
        (0x0F0D, 0x0092, "HORI Pokkén Tournament DX Pro Pad"),
        (0x20D6, 0xA711, "PowerA Switch-Controller (Kabel)"),
        (0x20D6, 0xA712, "PowerA Switch-Controller (Kabel)"),
        (0x20D6, 0xA713, "PowerA Switch-Controller (Kabel)"),
        (0x20D6, 0xA714, "PowerA Switch-Controller (Kabel)"),
        (0x20D6, 0xA715, "PowerA Switch-Controller (Kabel)"),
        (0x20D6, 0xA716, "PowerA Switch-Controller (Kabel)"),
        (0x0E6F, 0x0180, "PDP Switch-Controller (Kabel)"),
        (0x0E6F, 0x0181, "PDP Switch-Controller (Kabel)"),
        (0x0E6F, 0x0184, "PDP Switch-Controller (Kabel)"),
        (0x0E6F, 0x0185, "PDP Switch-Controller (Kabel)"),
        (0x0E6F, 0x0186, "PDP Switch-Controller (Kabel)"),
        (0x0E6F, 0x0187, "PDP Switch-Controller (Kabel)"),
        (0x0E6F, 0x0188, "PDP Switch-Controller (Kabel)"),
    ];

    /// <summary>Name eines bekannten Geräts aus einem HID-Gerätepfad (…vid_0f0d&amp;pid_00c1…), sonst null.</summary>
    public static string? NameFromHidPath(string path)
    {
        foreach (var (vendor, product, name) in Known)
            if (path.Contains($"vid_{vendor:x4}&pid_{product:x4}", StringComparison.OrdinalIgnoreCase))
                return name;
        return null;
    }

    /// <summary>Kalibrierung der 8-Bit-Sticks auf den 12-Bit-Bereich (Mitte 128 → 2056, voller Ausschlag ±2000).</summary>
    public static DeviceCalibration Calibration { get; } = new()
    {
        Left = new StickCalibration(new AxisCalibration(2056, 2000, 2000), new AxisCalibration(2056, 2000, 2000)),
        Right = new StickCalibration(new AxisCalibration(2056, 2000, 2000), new AxisCalibration(2056, 2000, 2000)),
    };

    /// <summary>Bericht zerlegen (ohne Report-ID; Windows' vorangestellte 0 vorher abschneiden).</summary>
    public static bool TryParse(ReadOnlySpan<byte> r, out ControllerState state)
    {
        state = new ControllerState();
        if (r.Length < 7 || (r[2] & 0x0F) > 8 && (r[2] & 0x0F) != 0x0F)
            return false;
        var buttons = ProButtons.None;
        void Map(byte value, int mask, ProButtons button) { if ((value & mask) != 0) buttons |= button; }
        Map(r[0], 0x01, ProButtons.Y); Map(r[0], 0x02, ProButtons.B); Map(r[0], 0x04, ProButtons.A); Map(r[0], 0x08, ProButtons.X);
        Map(r[0], 0x10, ProButtons.L); Map(r[0], 0x20, ProButtons.R); Map(r[0], 0x40, ProButtons.ZL); Map(r[0], 0x80, ProButtons.ZR);
        Map(r[1], 0x01, ProButtons.Minus); Map(r[1], 0x02, ProButtons.Plus); Map(r[1], 0x04, ProButtons.LeftStick);
        Map(r[1], 0x08, ProButtons.RightStick); Map(r[1], 0x10, ProButtons.Home); Map(r[1], 0x20, ProButtons.Capture);
        buttons |= (r[2] & 0x0F) switch
        {
            0 => ProButtons.Up,
            1 => ProButtons.Up | ProButtons.Right,
            2 => ProButtons.Right,
            3 => ProButtons.Down | ProButtons.Right,
            4 => ProButtons.Down,
            5 => ProButtons.Down | ProButtons.Left,
            6 => ProButtons.Left,
            7 => ProButtons.Up | ProButtons.Left,
            _ => ProButtons.None,
        };
        static int X(byte v) => v * 4095 / 255;
        static int Y(byte v) => (255 - v) * 4095 / 255; // nach oben wachsend wie bei Nintendo
        state = new ControllerState
        {
            Kind = ControllerKind.Pro1,
            Buttons = buttons,
            LeftX = X(r[3]), LeftY = Y(r[4]), RightX = X(r[5]), RightY = Y(r[6]),
        };
        return true;
    }
}
