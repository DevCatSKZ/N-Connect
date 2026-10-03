namespace Switch2Pro.Protocol;

[Flags]
public enum XButtons : ushort
{
    None = 0,
    Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008,
    Start = 0x0010, Back = 0x0020, LS = 0x0040, RS = 0x0080,
    LB = 0x0100, RB = 0x0200, Guide = 0x0400,
    A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000,
}

/// <summary>Neutraler Gamepad-Zustand im Xbox-Schema (Achsen −32768…32767, Y positiv = oben).</summary>
public readonly record struct GamepadState(
    XButtons Buttons, byte LeftTrigger, byte RightTrigger,
    short LeftX, short LeftY, short RightX, short RightY,
    bool Touchpad);

public static class Mapping
{
    /// <summary>Standardziel jeder Controller-Taste je nach Belegung (ohne freie Umbelegung).</summary>
    public static ExtraButtonTarget DefaultTarget(ProButtons button, FaceButtonLayout layout) => button switch
    {
        ProButtons.A => layout == FaceButtonLayout.Xbox ? ExtraButtonTarget.B : ExtraButtonTarget.A,
        ProButtons.B => layout == FaceButtonLayout.Xbox ? ExtraButtonTarget.A : ExtraButtonTarget.B,
        ProButtons.X => layout == FaceButtonLayout.Xbox ? ExtraButtonTarget.Y : ExtraButtonTarget.X,
        ProButtons.Y => layout == FaceButtonLayout.Xbox ? ExtraButtonTarget.X : ExtraButtonTarget.Y,
        ProButtons.L => ExtraButtonTarget.LB,
        ProButtons.R => ExtraButtonTarget.RB,
        ProButtons.ZL => ExtraButtonTarget.LT,
        ProButtons.ZR => ExtraButtonTarget.RT,
        ProButtons.Minus => ExtraButtonTarget.Back,
        ProButtons.Plus => ExtraButtonTarget.Start,
        ProButtons.Home => ExtraButtonTarget.Guide,
        ProButtons.LeftStick => ExtraButtonTarget.LS,
        ProButtons.RightStick => ExtraButtonTarget.RS,
        ProButtons.Up => ExtraButtonTarget.Up,
        ProButtons.Down => ExtraButtonTarget.Down,
        ProButtons.Left => ExtraButtonTarget.Left,
        ProButtons.Right => ExtraButtonTarget.Right,
        ProButtons.Capture => ExtraButtonTarget.Touchpad,
        _ => ExtraButtonTarget.None, // C, GL, GR, Headset
    };

    private static readonly ProButtons[] AllButtons =
        Enum.GetValues<ProButtons>().Where(b => b != ProButtons.None).ToArray();

    /// <summary>Switch-Zustand → Xbox-Schema (gilt auch als Grundlage für DualShock 4).</summary>
    public static GamepadState ToGamepad(ControllerState s, Settings settings, StickCalibration left, StickCalibration right)
    {
        var b = XButtons.None;
        bool lt = false, rt = false, touchpad = false;
        foreach (var button in AllButtons)
        {
            if (!s.Has(button))
                continue;
            var target = settings.Remap.TryGetValue(button, out var custom) ? custom : DefaultTarget(button, settings.Layout);
            switch (target)
            {
                case ExtraButtonTarget.None: break;
                case ExtraButtonTarget.LT: lt = true; break;
                case ExtraButtonTarget.RT: rt = true; break;
                case ExtraButtonTarget.Touchpad: touchpad = true; break;
                default: b |= Enum.Parse<XButtons>(target.ToString()); break;
            }
        }

        var (lx, ly) = Stick(s.LeftX, s.LeftY, left, settings.StickDeadzone);
        var (rx, ry) = Stick(s.RightX, s.RightY, right, settings.StickDeadzone);
        return new GamepadState(b, (byte)(lt ? 255 : 0), (byte)(rt ? 255 : 0), lx, ly, rx, ry, touchpad);
    }

    /// <summary>Kalibrieren, radiale Totzone mit weichem Übergang, Kreis begrenzen.</summary>
    public static (short X, short Y) Stick(int rawX, int rawY, StickCalibration cal, float deadzone)
    {
        float x = cal.X.Normalize(rawX), y = cal.Y.Normalize(rawY);
        float mag = MathF.Sqrt(x * x + y * y);
        if (mag <= deadzone || mag == 0f)
            return (0, 0);
        float scaled = Math.Min(1f, (mag - deadzone) / (1f - deadzone));
        float k = scaled / mag;
        return (ToShort(x * k), ToShort(y * k));
    }

    private static short ToShort(float v) => (short)Math.Clamp(MathF.Round(v * 32767f), -32768f, 32767f);

    /// <summary>
    /// Baut den 63-Byte-Bericht DS4_REPORT_EX für ViGEmBus (DualShock 4 ohne Report-ID).
    /// Bewegungsdaten: Switch-Rohachsen → SDL-/DS4-Achsen wie in SDL (x, z, −y),
    /// Gyro 2000 °/s ≙ 32767 → DS4 16 LSB pro °/s; Beschleunigung 4096 → 8192 LSB pro g.
    /// </summary>
    public static byte[] ToDs4Report(GamepadState g, ControllerState s, GyroBias bias, ushort timestamp, ref byte touchCounter)
    {
        var r = new byte[63];
        r[0] = AxisToByte(g.LeftX, invert: false);
        r[1] = AxisToByte(g.LeftY, invert: true);
        r[2] = AxisToByte(g.RightX, invert: false);
        r[3] = AxisToByte(g.RightY, invert: true);

        int buttons = DpadHat(g.Buttons);
        if (g.Buttons.HasFlag(XButtons.X)) buttons |= 1 << 4;   // Viereck
        if (g.Buttons.HasFlag(XButtons.A)) buttons |= 1 << 5;   // Kreuz
        if (g.Buttons.HasFlag(XButtons.B)) buttons |= 1 << 6;   // Kreis
        if (g.Buttons.HasFlag(XButtons.Y)) buttons |= 1 << 7;   // Dreieck
        if (g.Buttons.HasFlag(XButtons.LB)) buttons |= 1 << 8;
        if (g.Buttons.HasFlag(XButtons.RB)) buttons |= 1 << 9;
        if (g.LeftTrigger > 0) buttons |= 1 << 10;
        if (g.RightTrigger > 0) buttons |= 1 << 11;
        if (g.Buttons.HasFlag(XButtons.Back)) buttons |= 1 << 12;  // Share
        if (g.Buttons.HasFlag(XButtons.Start)) buttons |= 1 << 13; // Options
        if (g.Buttons.HasFlag(XButtons.LS)) buttons |= 1 << 14;
        if (g.Buttons.HasFlag(XButtons.RS)) buttons |= 1 << 15;
        r[4] = (byte)buttons;
        r[5] = (byte)(buttons >> 8);
        r[6] = (byte)((g.Buttons.HasFlag(XButtons.Guide) ? 1 : 0) | (g.Touchpad ? 2 : 0));
        r[7] = g.LeftTrigger;
        r[8] = g.RightTrigger;
        r[9] = (byte)timestamp;
        r[10] = (byte)(timestamp >> 8);
        r[11] = 0xFF; // Temperatur/Batterie: unbenutzt

        if (s.Motion is { } m)
        {
            const float gyroScale = 16f * 2000f / 32767f;
            Put16(r, 12, (m.GyroX - bias.X) * gyroScale);
            Put16(r, 14, (m.GyroZ - bias.Z) * gyroScale);
            Put16(r, 16, -(m.GyroY - bias.Y) * gyroScale);
            Put16(r, 18, m.AccelX * 2f);
            Put16(r, 20, m.AccelZ * 2f);
            Put16(r, 22, -m.AccelY * 2f);
        }
        else
        {
            Put16(r, 20, 8192f); // ruhig liegend: 1 g nach oben, damit nichts „driftet“
        }

        // Akkuanzeige (Bit 4 = Kabel, Rest = Stufe 0–10 bzw. 0–11 beim Laden).
        int level = s.BatteryPercent < 0 ? 10 : Math.Clamp(s.BatteryPercent / 10, 0, 10);
        r[29] = (byte)(s.Charging ? 0x10 | Math.Min(11, level) : level);

        // Touchpad: kein Finger (Bit 7 gesetzt), sonst sehen Spiele Phantom-Berührungen.
        r[32] = 1;
        r[33] = touchCounter++;
        r[34] = 0x80;
        r[38] = 0x80;
        return r;
    }

    private static byte AxisToByte(short v, bool invert)
    {
        // DS4: 0 = links/oben, 128 = Mitte, 255 = rechts/unten.
        int b = invert ? (32768 - v) >> 8 : (v + 32768) >> 8;
        return (byte)Math.Min(255, b);
    }

    private static int DpadHat(XButtons b)
    {
        bool up = b.HasFlag(XButtons.Up), down = b.HasFlag(XButtons.Down);
        bool left = b.HasFlag(XButtons.Left), right = b.HasFlag(XButtons.Right);
        return (up, right, down, left) switch
        {
            (true, false, false, false) => 0,
            (true, true, false, false) => 1,
            (false, true, false, false) => 2,
            (false, true, true, false) => 3,
            (false, false, true, false) => 4,
            (false, false, true, true) => 5,
            (false, false, false, true) => 6,
            (true, false, false, true) => 7,
            _ => 8,
        };
    }

    private static void Put16(byte[] r, int offset, float value)
    {
        short v = (short)Math.Clamp(MathF.Round(value), -32768f, 32767f);
        r[offset] = (byte)v;
        r[offset + 1] = (byte)(v >> 8);
    }
}
