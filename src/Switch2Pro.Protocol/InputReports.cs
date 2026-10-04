namespace Switch2Pro.Protocol;

/// <summary>
/// Zerlegt die Eingabeberichte. Über Bluetooth fehlt das Report-ID-Byte;
/// Aufbau laut ndeadly/switch2_controller_research, hid_reports.md.
/// </summary>
public static class InputReports
{
    public const int Report05MinLength = 0x10;
    public const int Report09MinLength = 0x0B;

    /// <summary>Bericht 0x05 (Merkmal <see cref="Gatt.InputReportCommon"/>).</summary>
    public static bool TryParseReport05(ReadOnlySpan<byte> r, out ControllerState state, ControllerKind kind = ControllerKind.Pro2)
    {
        state = new ControllerState();
        if (r.Length < Report05MinLength)
            return false;

        byte b0 = r[4], b1 = r[5], b2 = r[6], b3 = r[7];
        var buttons = ProButtons.None;
        void Map(byte value, int mask, ProButtons button) { if ((value & mask) != 0) buttons |= button; }
        Map(b0, 0x80, ProButtons.ZR); Map(b0, 0x40, ProButtons.R); Map(b0, 0x20, ProButtons.SLRight); Map(b0, 0x10, ProButtons.SRRight);
        Map(b0, 0x08, ProButtons.A); Map(b0, 0x04, ProButtons.B); Map(b0, 0x02, ProButtons.X); Map(b0, 0x01, ProButtons.Y);
        Map(b1, 0x40, ProButtons.C); Map(b1, 0x20, ProButtons.Capture); Map(b1, 0x10, ProButtons.Home);
        Map(b1, 0x08, ProButtons.LeftStick); Map(b1, 0x04, ProButtons.RightStick);
        Map(b1, 0x02, ProButtons.Plus); Map(b1, 0x01, ProButtons.Minus);
        Map(b2, 0x80, ProButtons.ZL); Map(b2, 0x40, ProButtons.L); Map(b2, 0x20, ProButtons.SLLeft); Map(b2, 0x10, ProButtons.SRLeft);
        Map(b2, 0x08, ProButtons.Left); Map(b2, 0x04, ProButtons.Right); Map(b2, 0x02, ProButtons.Up); Map(b2, 0x01, ProButtons.Down);
        Map(b3, 0x10, ProButtons.Headset); Map(b3, 0x02, ProButtons.GL); Map(b3, 0x01, ProButtons.GR);

        var (lx, ly) = StickCalibration.Unpack12(r.Slice(0x0A, 3));
        var (rx, ry) = StickCalibration.Unpack12(r.Slice(0x0D, 3));

        int millivolts = r.Length >= 0x21 ? r[0x1F] | (r[0x20] << 8) : 0;
        Motion? motion = null;
        if (r.Length >= 0x3C)
        {
            var m = new Motion(S16(r, 0x30), S16(r, 0x32), S16(r, 0x34), S16(r, 0x36), S16(r, 0x38), S16(r, 0x3A));
            if (m != default)
                motion = m;
        }

        OpticalMouse? mouse = null;
        if (kind is ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right && r.Length >= 0x18)
            mouse = new OpticalMouse(U16(r, 0x10), U16(r, 0x12), U16(r, 0x14), U16(r, 0x16));

        bool gameCube = kind == ControllerKind.GameCube2 && r.Length >= 0x3E;
        state = new ControllerState
        {
            Kind = kind,
            Buttons = buttons,
            LeftX = lx, LeftY = ly, RightX = rx, RightY = ry,
            LeftTrigger = gameCube ? r[0x3C] : -1,
            RightTrigger = gameCube ? r[0x3D] : -1,
            Motion = motion,
            Mouse = mouse,
            BatteryMillivolts = millivolts,
            BatteryPercent = millivolts > 0 ? BatteryPercentFromMillivolts(millivolts, kind) : -1,
            Charging = r.Length > 0x21 && r[0x21] != 0 && r[0x21] != 0x20,
        };
        return true;
    }

    /// <summary>Bericht 0x09 (Merkmal <see cref="Gatt.InputReportPro"/>), ohne Bewegungsdaten.</summary>
    public static bool TryParseReport09(ReadOnlySpan<byte> r, out ControllerState state)
    {
        state = new ControllerState();
        if (r.Length < Report09MinLength)
            return false;

        byte b0 = r[2], b1 = r[3], b2 = r[4];
        var buttons = ProButtons.None;
        void Map(byte value, int mask, ProButtons button) { if ((value & mask) != 0) buttons |= button; }
        Map(b0, 0x80, ProButtons.RightStick); Map(b0, 0x40, ProButtons.Plus); Map(b0, 0x20, ProButtons.ZR); Map(b0, 0x10, ProButtons.R);
        Map(b0, 0x08, ProButtons.X); Map(b0, 0x04, ProButtons.Y); Map(b0, 0x02, ProButtons.A); Map(b0, 0x01, ProButtons.B);
        Map(b1, 0x80, ProButtons.LeftStick); Map(b1, 0x40, ProButtons.Minus); Map(b1, 0x20, ProButtons.ZL); Map(b1, 0x10, ProButtons.L);
        Map(b1, 0x08, ProButtons.Up); Map(b1, 0x04, ProButtons.Left); Map(b1, 0x02, ProButtons.Right); Map(b1, 0x01, ProButtons.Down);
        Map(b2, 0x10, ProButtons.C); Map(b2, 0x08, ProButtons.GL); Map(b2, 0x04, ProButtons.GR);
        Map(b2, 0x02, ProButtons.Capture); Map(b2, 0x01, ProButtons.Home);

        var (lx, ly) = StickCalibration.Unpack12(r.Slice(0x05, 3));
        var (rx, ry) = StickCalibration.Unpack12(r.Slice(0x08, 3));
        byte power = r[1];

        state = new ControllerState
        {
            Buttons = buttons,
            LeftX = lx, LeftY = ly, RightX = rx, RightY = ry,
            BatteryPercent = Math.Min(9, (power >> 2) & 0x0F) * 100 / 9,
            Charging = (power & 0x02) != 0,
        };
        return true;
    }

    /// <summary>
    /// Akkustand aus der gemeldeten Spannung. Pro Controller 2/GameCube: Li-Ion-Kennlinie (3,30 V leer … 4,15 V voll).
    /// Joy-Con 2 melden deutlich niedrigere Werte (gemessen: geladen ~3,3 V); Schwellen wie die Referenz
    /// Switch2Connect (über 3,25 V „hoch“, über 3,125 V „mittel“) – Prozentwerte daher nur geschätzt.
    /// </summary>
    public static int BatteryPercentFromMillivolts(int mv, ControllerKind kind = ControllerKind.Pro2)
    {
        bool joyCon = kind is ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right;
        ReadOnlySpan<int> volts = joyCon ? [3050, 3125, 3200, 3250, 3300, 3360] : [3300, 3600, 3700, 3800, 3900, 4000, 4150];
        ReadOnlySpan<int> pct = joyCon ? [0, 10, 35, 60, 85, 100] : [0, 10, 25, 45, 65, 82, 100];
        if (mv <= volts[0]) return 0;
        for (int i = 1; i < volts.Length; i++)
        {
            if (mv <= volts[i])
            {
                // Kaufmännisch gerundet (3707 mV ≈ 26,4 % → 26, 3704 mV ≈ 25,8 % → 26 statt abgeschnitten 25)
                int num = (mv - volts[i - 1]) * (pct[i] - pct[i - 1]), den = volts[i] - volts[i - 1];
                return pct[i - 1] + (2 * num + den) / (2 * den);
            }
        }
        return 100;
    }

    private static short S16(ReadOnlySpan<byte> r, int offset) => (short)(r[offset] | (r[offset + 1] << 8));

    private static ushort U16(ReadOnlySpan<byte> r, int offset) => (ushort)(r[offset] | (r[offset + 1] << 8));
}
