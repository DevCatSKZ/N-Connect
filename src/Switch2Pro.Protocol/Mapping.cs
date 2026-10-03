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

/// <summary>
/// Eingabe eines (virtuellen) Controllers nach Kalibrierung und Ausrichtung – für alle Controller-Arten gleich:
/// Tasten im Schema eines Pro Controllers (B unten, A rechts, Y links, X oben), Sticks −1…1 (Y oben positiv),
/// analoge Trigger 0…1 (oder null), Bewegungsdaten ohne Nullpunktfehler in Switch-2-Rohachsen.
/// </summary>
public sealed record PadInput
{
    public ControllerKind Kind { get; init; }
    public ProButtons Buttons { get; init; }
    public float LeftX { get; init; }
    public float LeftY { get; init; }
    public float RightX { get; init; }
    public float RightY { get; init; }
    public float? LeftTrigger { get; init; }
    public float? RightTrigger { get; init; }
    public Motion? Motion { get; init; }
    public int BatteryPercent { get; init; } = -1;
    public bool Charging { get; init; }

    public bool Has(ProButtons b) => (Buttons & b) != 0;
}

public static class Mapping
{
    // ---------- 1. Normalisieren: jede Controller-Art → einheitliche Eingabe ----------

    /// <summary>
    /// Kalibriert und richtet aus. Ein einzelner Joy-Con wird quer gehalten (wie an der Konsole):
    /// Stick und Tasten werden gedreht, SL/SR werden zu L/R. Geometrie wie SDL (Switch 1, erprobt).
    /// </summary>
    public static PadInput Normalize(ControllerState s, DeviceCalibration cal, bool sideways = true)
    {
        float lx = cal.Left.X.Normalize(s.LeftX), ly = cal.Left.Y.Normalize(s.LeftY);
        float rx = cal.Right.X.Normalize(s.RightX), ry = cal.Right.Y.Normalize(s.RightY);
        var motion = s.Motion is { } m ? Unbias(m, cal.Gyro) : (Motion?)null;
        var input = new PadInput
        {
            Kind = s.Kind,
            Buttons = s.Buttons,
            LeftX = lx, LeftY = ly, RightX = rx, RightY = ry,
            Motion = motion,
            BatteryPercent = s.BatteryPercent,
            Charging = s.Charging,
        };

        if (s.Kind == ControllerKind.GameCube2 && s.LeftTrigger >= 0 && s.RightTrigger >= 0)
        {
            return input with
            {
                LeftTrigger = Trigger(s.LeftTrigger, cal.TriggerZeroLeft),
                RightTrigger = Trigger(s.RightTrigger, cal.TriggerZeroRight),
            };
        }

        // Ring-Con: zusammendrücken = rechter Trigger, auseinanderziehen = linker Trigger (analog).
        if (s.RingFlex is { } flex)
            input = input with { RightTrigger = Math.Max(0f, flex), LeftTrigger = Math.Max(0f, -flex) };

        if (s.Kind.IsClassic())
            return NormalizeClassic(s.Kind, input);

        // Wii Classic Controller: Die analogen L/R-Werte werden nicht weitergegeben – L/R sind schon LB/RB und
        // ZL/ZR die Trigger; sonst löste L zugleich LB und LT aus (beim Classic Controller Pro bei jedem Druck).

        if (!sideways || !s.Kind.IsJoyCon())
            return input;

        if (s.Kind.IsLeftJoyCon())
        {
            // Quer, Schiene oben: Gerät +X zeigt nach oben, +Y nach links.
            return input with
            {
                Buttons = Translate(s.Buttons, LeftSideways),
                LeftX = -ly, LeftY = lx, RightX = 0, RightY = 0,
                Motion = motion is { } lm ? lm with { AccelX = Neg(lm.AccelY), AccelY = lm.AccelX, GyroX = Neg(lm.GyroY), GyroY = lm.GyroX } : null,
            };
        }
        // Rechter Joy-Con quer: Gerät +X zeigt nach unten, +Y nach rechts. Sein Stick wird der linke Stick.
        return input with
        {
            Buttons = Translate(s.Buttons, RightSideways),
            LeftX = ry, LeftY = -rx, RightX = 0, RightY = 0,
            Motion = motion is { } rm ? rm with { AccelX = rm.AccelY, AccelY = Neg(rm.AccelX), GyroX = rm.GyroY, GyroY = Neg(rm.GyroX) } : null,
        };
    }

    /// <summary>
    /// Zwei Joy-Con zu einem Controller: linker Stick vom linken, rechter vom rechten, Gyro vom gewählten
    /// Joy-Con (Standard wie an der Switch: rechts; fehlen dessen Daten, vom anderen).
    /// </summary>
    public static PadInput Merge(ControllerState left, DeviceCalibration leftCal, ControllerState right, DeviceCalibration rightCal,
        GyroSource gyro = GyroSource.Right)
    {
        var l = Normalize(left, leftCal, sideways: false);
        var r = Normalize(right, rightCal, sideways: false);
        const ProButtons rails = ProButtons.SLLeft | ProButtons.SRLeft | ProButtons.SLRight | ProButtons.SRRight;
        int battery = l.BatteryPercent < 0 ? r.BatteryPercent : r.BatteryPercent < 0 ? l.BatteryPercent : Math.Min(l.BatteryPercent, r.BatteryPercent);
        return new PadInput
        {
            Kind = ControllerKind.JoyConPair,
            Buttons = (l.Buttons | r.Buttons) & ~rails,
            LeftX = l.LeftX, LeftY = l.LeftY,
            RightX = r.RightX, RightY = r.RightY,
            Motion = gyro == GyroSource.Left ? l.Motion ?? r.Motion : r.Motion ?? l.Motion,
            LeftTrigger = r.LeftTrigger,   // Ring-Con am rechten Joy-Con
            RightTrigger = r.RightTrigger,
            BatteryPercent = battery,
            Charging = l.Charging && r.Charging,
        };
    }

    /// <summary>
    /// Nintendo-Switch-Online-Controller in die einheitliche Anordnung bringen (Bits wie hid-nintendo, Linux):
    /// NES/SNES wie beschriftet (A rechts, B unten …), ohne Sticks. N64: A unten, B links, Z = ZL, ZR = ZR,
    /// C-Tasten werden der rechte Stick (wie in Emulatoren üblich). Mega Drive nach Position: A links, B unten,
    /// C rechts, Y oben, X/Z = Schultertasten, MODE = −.
    /// </summary>
    private static PadInput NormalizeClassic(ControllerKind kind, PadInput input)
    {
        var b = input.Buttons;
        switch (kind)
        {
            case ControllerKind.NesController:
            case ControllerKind.SnesController:
                return input with { LeftX = 0, LeftY = 0, RightX = 0, RightY = 0, Motion = null };
            case ControllerKind.N64Controller:
            {
                // Rohbits: Y = C-hoch, ZR = C-runter, X = C-links, − = C-rechts, linker Stickklick = ZR.
                float cx = (b.HasFlag(ProButtons.Minus) ? 1 : 0) - (b.HasFlag(ProButtons.X) ? 1 : 0);
                float cy = (b.HasFlag(ProButtons.Y) ? 1 : 0) - (b.HasFlag(ProButtons.ZR) ? 1 : 0);
                var t = Translate(b,
                [
                    (ProButtons.A, ProButtons.B), (ProButtons.B, ProButtons.Y), (ProButtons.ZL, ProButtons.ZL),
                    (ProButtons.LeftStick, ProButtons.ZR), (ProButtons.L, ProButtons.L), (ProButtons.R, ProButtons.R),
                    (ProButtons.Plus, ProButtons.Plus), (ProButtons.Home, ProButtons.Home), (ProButtons.Capture, ProButtons.Capture),
                    (ProButtons.Up, ProButtons.Up), (ProButtons.Down, ProButtons.Down), (ProButtons.Left, ProButtons.Left),
                    (ProButtons.Right, ProButtons.Right),
                ]);
                return input with { Buttons = t, RightX = cx, RightY = cy, Motion = null };
            }
            case ControllerKind.MegaDrive:
            {
                var t = Translate(b,
                [
                    (ProButtons.A, ProButtons.Y), (ProButtons.B, ProButtons.B), (ProButtons.R, ProButtons.A),
                    (ProButtons.Y, ProButtons.X), (ProButtons.X, ProButtons.L), (ProButtons.L, ProButtons.R),
                    (ProButtons.ZR, ProButtons.Minus), (ProButtons.Plus, ProButtons.Plus), (ProButtons.Home, ProButtons.Home),
                    (ProButtons.Capture, ProButtons.Capture),
                    (ProButtons.Up, ProButtons.Up), (ProButtons.Down, ProButtons.Down), (ProButtons.Left, ProButtons.Left),
                    (ProButtons.Right, ProButtons.Right),
                ]);
                return input with { Buttons = t, LeftX = 0, LeftY = 0, RightX = 0, RightY = 0, Motion = null };
            }
            default:
                return input;
        }
    }

    private static readonly (ProButtons From, ProButtons To)[] LeftSideways =
    [
        (ProButtons.Left, ProButtons.B), (ProButtons.Down, ProButtons.A),
        (ProButtons.Up, ProButtons.Y), (ProButtons.Right, ProButtons.X),
        (ProButtons.SLLeft, ProButtons.L), (ProButtons.SRLeft, ProButtons.R),
        (ProButtons.L, ProButtons.ZL), (ProButtons.ZL, ProButtons.ZR),
        (ProButtons.Minus, ProButtons.Plus), (ProButtons.Capture, ProButtons.Home),
        (ProButtons.LeftStick, ProButtons.LeftStick),
    ];

    private static readonly (ProButtons From, ProButtons To)[] RightSideways =
    [
        (ProButtons.A, ProButtons.B), (ProButtons.X, ProButtons.A),
        (ProButtons.B, ProButtons.Y), (ProButtons.Y, ProButtons.X),
        (ProButtons.SLRight, ProButtons.L), (ProButtons.SRRight, ProButtons.R),
        (ProButtons.R, ProButtons.ZL), (ProButtons.ZR, ProButtons.ZR),
        (ProButtons.Plus, ProButtons.Plus), (ProButtons.Home, ProButtons.Home),
        (ProButtons.RightStick, ProButtons.LeftStick), (ProButtons.C, ProButtons.C),
    ];

    private static ProButtons Translate(ProButtons buttons, (ProButtons From, ProButtons To)[] table)
    {
        var result = ProButtons.None;
        foreach (var (from, to) in table)
            if ((buttons & from) != 0)
                result |= to;
        return result;
    }

    private static float Trigger(int raw, int zero) => Math.Clamp((raw - zero) / (232f - zero), 0f, 1f);

    private static short Neg(short v) => v == short.MinValue ? short.MaxValue : (short)-v;

    private static Motion Unbias(Motion m, GyroBias bias)
    {
        static short C(float v) => (short)Math.Clamp(MathF.Round(v), -32768f, 32767f);
        return m with { GyroX = C(m.GyroX - bias.X), GyroY = C(m.GyroY - bias.Y), GyroZ = C(m.GyroZ - bias.Z) };
    }

    // ---------- 2. Belegung: einheitliche Eingabe → Xbox-Schema ----------

    /// <summary>Standardziel jeder Taste je nach Belegung (ohne freie Umbelegung).</summary>
    public static ExtraButtonTarget DefaultTarget(ProButtons button, FaceButtonLayout layout, ControllerKind kind = ControllerKind.Pro2)
    {
        // GameCube: Tasten nach Position (die Beschriftung passt zu keinem Schema), Z = RB,
        // ZL = LB, die digitalen Trigger-Klicks sind durch die analogen Trigger abgedeckt.
        // N64 und Mega Drive: Tasten sind schon nach Position angeordnet (siehe NormalizeClassic).
        if (kind is ControllerKind.N64Controller or ControllerKind.MegaDrive)
            layout = FaceButtonLayout.Xbox;
        if (kind == ControllerKind.GameCube2)
        {
            layout = FaceButtonLayout.Xbox;
            switch (button)
            {
                case ProButtons.ZR: return ExtraButtonTarget.RB;
                case ProButtons.ZL: return ExtraButtonTarget.LB;
                case ProButtons.R or ProButtons.L: return ExtraButtonTarget.None;
                case ProButtons.C: return ExtraButtonTarget.Back;
            }
        }
        return button switch
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
            _ => ExtraButtonTarget.None, // C, GL, GR, Headset, SL/SR
        };
    }

    private static readonly ProButtons[] AllButtons =
        Enum.GetValues<ProButtons>().Where(b => b != ProButtons.None).ToArray();

    /// <summary>
    /// Ergebnis der Belegung: Gamepad-Zustand, gehaltene Tastenkombinationen, Sonderaktionen und die Makros
    /// der gerade gedrückten Tasten (der Spieler startet sie bei jedem neuen Drücken).
    /// </summary>
    public sealed record Output(GamepadState Gamepad, HashSet<string> Keys, SpecialAction Specials, bool ShiftActive,
        IReadOnlyList<string> Macros);

    /// <summary>
    /// Wendet die Belegung an: geltendes Profil (Standard oder benannt), Shift-Ebene (solange eine Taste mit der
    /// Aktion „Shift“ gehalten wird), Turbo (im Takt von <see cref="Settings.TurboRate"/>), Totzone und Kennlinie
    /// der Sticks, Schwelle der analogen Trigger. <paramref name="nowMs"/> = Uhrzeit für den Turbo-Takt.
    /// </summary>
    public static Output Evaluate(PadInput p, Settings settings, long nowMs = -1)
    {
        if (nowMs < 0)
            nowMs = Environment.TickCount64;
        // Turbo: abwechselnd an/aus, beginnend mit „an“ (Halbperiode = 1000 / (2 × Rate) ms).
        // In double rechnen: float (24 Bit) wird bei Tagen Laufzeit zu ungenau, der Takt bliebe stehen.
        bool turboOn = (long)(nowMs * (double)settings.TurboRate * 2 / 1000) % 2 == 0;
        var macros = new List<string>();
        var profile = settings.CurrentProfile();
        bool shift = false;
        foreach (var button in AllButtons)
        {
            if (p.Has(button) && ActionFor(button, p.Kind, settings, profile, shift: false).Special.HasFlag(SpecialAction.Shift))
            {
                shift = true;
                break;
            }
        }

        var b = XButtons.None;
        bool lt = false, rt = false, touchpad = false;
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var specials = SpecialAction.None;
        foreach (var button in AllButtons)
        {
            if (!p.Has(button))
                continue;
            var action = ActionFor(button, p.Kind, settings, profile, shift);
            if (action.IsMacro)
            {
                macros.Add(action.Macro!);
                continue;
            }
            if (action.Turbo && !turboOn)
                continue; // Dauerfeuer: in der „aus“-Hälfte gilt die Taste als losgelassen
            if (action.IsKeyboard)
                keys.Add(action.Keys!);
            specials |= action.Special;
            switch (action.Target)
            {
                case ExtraButtonTarget.None: break;
                case ExtraButtonTarget.LT: lt = true; break;
                case ExtraButtonTarget.RT: rt = true; break;
                case ExtraButtonTarget.Touchpad: touchpad = true; break;
                default: b |= Enum.Parse<XButtons>(action.Target.ToString()); break;
            }
        }

        byte TriggerByte(bool digital, float? analog) =>
            digital ? (byte)255 : analog is { } a ? AnalogTrigger(a, settings.TriggerDeadzone, settings.TriggerFullAt) : (byte)0;

        float deadzone = settings.DeadzoneFor(p.Kind);
        var (lx, ly) = Stick(p.LeftX, p.LeftY, deadzone, settings.StickCurve);
        var (rx, ry) = Stick(p.RightX, p.RightY, deadzone, settings.StickCurve);
        var gamepad = new GamepadState(b, TriggerByte(lt, p.LeftTrigger), TriggerByte(rt, p.RightTrigger), lx, ly, rx, ry, touchpad);
        return new Output(gamepad, keys, specials, shift, macros);
    }

    /// <summary>Eine Gamepad-Taste zusätzlich drücken (z. B. aus einem Makro).</summary>
    public static GamepadState Press(GamepadState g, ExtraButtonTarget target) => target switch
    {
        ExtraButtonTarget.None => g,
        ExtraButtonTarget.LT => g with { LeftTrigger = 255 },
        ExtraButtonTarget.RT => g with { RightTrigger = 255 },
        ExtraButtonTarget.Touchpad => g with { Touchpad = true },
        _ => g with { Buttons = g.Buttons | Enum.Parse<XButtons>(target.ToString()) },
    };

    /// <summary>Analoger Trigger 0…1 → 0…255 mit Totzone am Anfang und „voll ab“-Schwelle.</summary>
    public static byte AnalogTrigger(float value, float deadzone, float fullAt)
    {
        value = Math.Clamp(value, 0f, 1f);
        if (value <= deadzone)
            return 0;
        float range = Math.Max(0.01f, fullAt - deadzone);
        return (byte)MathF.Round(Math.Clamp((value - deadzone) / range, 0f, 1f) * 255f);
    }

    /// <summary>
    /// Gyro → rechter Stick (Drehgeschwindigkeit wird zu Ausschlag, wie „Gyro als Joystick“ in Steam):
    /// Gieren (links/rechts drehen) = X, Nicken (kippen) = Y. Ab einer kleinen Drehung gilt mindestens der
    /// Mindestausschlag, damit die Totzone des Spiels überwunden wird. Ergebnis wird zum echten Stick addiert.
    /// </summary>
    public static (short X, short Y) GyroToStick(Motion m, Settings s, short stickX, short stickY)
    {
        const float DegPerRaw = 2000f / 32767f;
        const float RestDegPerSec = 1.5f; // darunter: Sensorrauschen, keine Bewegung
        float yaw = -m.GyroZ * DegPerRaw, pitch = m.GyroX * DegPerRaw;
        if (s.GyroStickInvertY)
            pitch = -pitch;
        float Axis(float rate)
        {
            float mag = MathF.Abs(rate);
            if (mag < RestDegPerSec)
                return 0f;
            float v = s.GyroStickAntiDeadzone + (1f - s.GyroStickAntiDeadzone) * Math.Min(1f, mag / s.GyroStickFullSpeed);
            return MathF.CopySign(Math.Min(1f, v), rate);
        }
        float x = stickX / 32767f + Axis(yaw), y = stickY / 32767f + Axis(pitch);
        return (ToShort(Math.Clamp(x, -1f, 1f)), ToShort(Math.Clamp(y, -1f, 1f)));
    }

    /// <summary>Einheitliche Eingabe → Xbox-Schema (gilt auch als Grundlage für DualShock 4).</summary>
    public static GamepadState ToGamepad(PadInput p, Settings settings) => Evaluate(p, settings).Gamepad;

    /// <summary>Tastenkombinationen (Hotkeys), die gerade gehalten werden sollen.</summary>
    public static HashSet<string> KeyboardActions(PadInput p, Settings settings) => Evaluate(p, settings).Keys;

    /// <summary>Aktion einer Taste im gerade geltenden Profil (normale Ebene).</summary>
    public static ButtonAction ActionFor(ProButtons button, ControllerKind kind, Settings settings) =>
        ActionFor(button, kind, settings, settings.CurrentProfile(), shift: false);

    /// <summary>
    /// Aktion einer Taste: auf der Shift-Ebene zuerst deren Belegung; dann die Belegung des Profils
    /// (benanntes Profil oder Standard) für die Controller-Art; sonst freie Umbelegung; sonst Standard.
    /// </summary>
    public static ButtonAction ActionFor(ProButtons button, ControllerKind kind, Settings settings, NamedProfile? profile, bool shift)
    {
        if (shift && Lookup(profile?.ShiftButtons ?? settings.ShiftProfiles, kind, button) is { } shifted)
            return shifted;
        if (Lookup(profile?.Buttons ?? settings.Profiles, kind, button) is { } mapped)
            return mapped;
        // Die alte freie Umbelegung gilt nicht für den GameCube-Controller (eigene Standardbelegung, z. B. C = Back).
        if (kind != ControllerKind.GameCube2 && settings.Remap.TryGetValue(button, out var custom))
            return ButtonAction.Gamepad(custom);
        return ButtonAction.Gamepad(DefaultTarget(button, settings.Layout, kind));
    }

    private static ButtonAction? Lookup(Dictionary<ControllerKind, Dictionary<ProButtons, string>> maps, ControllerKind kind, ProButtons button) =>
        maps.TryGetValue(kind, out var map) && map.TryGetValue(button, out var text) ? ButtonAction.Parse(text) : null;

    /// <summary>Kurzform für einen einzelnen Controller (z. B. Pro Controller).</summary>
    public static GamepadState ToGamepad(ControllerState s, Settings settings, StickCalibration left, StickCalibration right) =>
        ToGamepad(Normalize(s, new DeviceCalibration { Left = left, Right = right }), settings);

    /// <summary>Rohwerte kalibrieren und mit radialer Totzone abbilden.</summary>
    public static (short X, short Y) Stick(int rawX, int rawY, StickCalibration cal, float deadzone) =>
        Stick(cal.X.Normalize(rawX), cal.Y.Normalize(rawY), deadzone);

    /// <summary>Radiale Totzone mit weichem Übergang, auf den Kreis begrenzt; Kennlinie als Exponent (1 = linear).</summary>
    public static (short X, short Y) Stick(float x, float y, float deadzone, float curve = 1f)
    {
        float mag = MathF.Sqrt(x * x + y * y);
        if (mag <= deadzone || mag == 0f)
            return (0, 0);
        float scaled = Math.Min(1f, (mag - deadzone) / (1f - deadzone));
        if (curve != 1f)
            scaled = MathF.Pow(scaled, curve);
        float k = scaled / mag;
        return (ToShort(x * k), ToShort(y * k));
    }

    private static short ToShort(float v) => (short)Math.Clamp(MathF.Round(v * 32767f), -32768f, 32767f);

    // ---------- 3. DualShock-4-Bericht ----------

    /// <summary>
    /// Baut den 63-Byte-Bericht DS4_REPORT_EX für ViGEmBus (DualShock 4 ohne Report-ID).
    /// Bewegungsdaten: Switch-2-Rohachsen → SDL-/DS4-Achsen wie in SDL (x, z, −y),
    /// Gyro 2000 °/s ≙ 32767 → DS4 16 LSB pro °/s; Beschleunigung 4096 → 8192 LSB pro g.
    /// <paramref name="timestamp"/> in DS4-Einheiten (5,33 µs).
    /// </summary>
    public static byte[] ToDs4Report(GamepadState g, PadInput p, ushort timestamp, ref byte touchCounter)
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

        if (p.Motion is { } m)
        {
            const float gyroScale = 16f * 2000f / 32767f;
            Put16(r, 12, m.GyroX * gyroScale);
            Put16(r, 14, m.GyroZ * gyroScale);
            Put16(r, 16, -m.GyroY * gyroScale);
            Put16(r, 18, m.AccelX * 2f);
            Put16(r, 20, m.AccelZ * 2f);
            Put16(r, 22, -m.AccelY * 2f);
        }
        else
        {
            Put16(r, 20, 8192f); // ruhig liegend: 1 g nach oben, damit nichts „driftet“
        }

        // Akkuanzeige (Bit 4 = Kabel, Rest = Stufe 0–10 bzw. 0–11 beim Laden).
        int level = p.BatteryPercent < 0 ? 10 : Math.Clamp(p.BatteryPercent / 10, 0, 10);
        r[29] = (byte)(p.Charging ? 0x10 | Math.Min(11, level) : level);

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
