namespace Switch2Pro.Protocol;

/// <summary>Aktionen, die weder Gamepad- noch Tastaturtaste sind.</summary>
[Flags]
public enum SpecialAction
{
    None = 0,
    MouseLeft = 1, MouseRight = 2, MouseMiddle = 4,
    /// <summary>Gyro-Maus, solange die Taste gehalten wird.</summary>
    GyroMouse = 8,
    /// <summary>Gyro-Maus ein/aus (bei jedem Drücken).</summary>
    GyroMouseToggle = 16,
    /// <summary>Shift-Ebene, solange die Taste gehalten wird (andere Tasten bekommen ihre zweite Belegung).</summary>
    Shift = 32,
    /// <summary>Gyro steuert den rechten Stick, solange die Taste gehalten wird.</summary>
    GyroStick = 64,
    /// <summary>Gyro-Stick ein/aus (bei jedem Drücken).</summary>
    GyroStickToggle = 128,
    /// <summary>
    /// „Ratchet“ (JoyShockMapper GYRO_OFF): Gyro aus, solange gehalten – Controller zurückführen, ohne dass sich das
    /// Ziel bewegt (wie die Maus anheben). Gilt für Gyro-Stick und Gyro-Maus.
    /// </summary>
    GyroPause = 256,
}

/// <summary>
/// Was eine Controller-Taste auslösen soll: eine Taste des virtuellen Gamepads, eine Tastaturtaste bzw.
/// Tastenkombination (Hotkey), eine Sonderaktion (Maus, Gyro, Shift), ein Makro (Tastenfolge) oder nichts.
/// Gamepad- und Tastatur-Aktionen lassen sich mit „Turbo:“ zu Dauerfeuer machen. Gespeichert als Text, z. B.
/// "A", "LT", "None", "Key:F5", "Key:Ctrl+Shift+S", "MouseLeft", "GyroStick", "Shift", "Turbo:A",
/// "Turbo:Key:Space", "Macro:A 80, Pause 40, A 80".
/// </summary>
public readonly record struct ButtonAction(ExtraButtonTarget Target, string? Keys, SpecialAction Special = SpecialAction.None,
    bool Turbo = false, string? Macro = null)
{
    public const string KeyPrefix = "Key:";
    public const string TurboPrefix = "Turbo:";
    public const string MacroPrefix = "Macro:";

    public static ButtonAction Gamepad(ExtraButtonTarget target) => new(target, null);
    public static ButtonAction Keyboard(string keys) => new(ExtraButtonTarget.None, keys);
    public static ButtonAction Do(SpecialAction special) => new(ExtraButtonTarget.None, null, special);
    public static ButtonAction Play(string macro) => new(ExtraButtonTarget.None, null, Macro: macro);
    public static ButtonAction Nothing { get; } = new(ExtraButtonTarget.None, null);

    public bool IsKeyboard => Keys is not null;
    public bool IsSpecial => Special != SpecialAction.None;
    public bool IsMacro => Macro is not null;

    public override string ToString()
    {
        if (IsMacro)
            return MacroPrefix + Macro;
        string inner = IsKeyboard ? KeyPrefix + Keys : IsSpecial ? Special.ToString() : Target.ToString();
        return Turbo ? TurboPrefix + inner : inner;
    }

    public static ButtonAction Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Nothing;
        text = text.Trim();
        if (text.StartsWith(MacroPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var script = text[MacroPrefix.Length..].Trim();
            return MacroScript.TryParse(script, out _) ? Play(script) : Nothing;
        }
        if (text.StartsWith(TurboPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Turbo nur für Gamepad- und Tastatur-Aktionen (Sonderaktionen wie Shift ergeben als Dauerfeuer keinen Sinn).
            var inner = Parse(text[TurboPrefix.Length..]);
            return inner.IsSpecial || inner.IsMacro || inner.Turbo || inner == Nothing ? Nothing : inner with { Turbo = true };
        }
        if (text.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var keys = text[KeyPrefix.Length..].Trim();
            return keys.Length == 0 ? Nothing : Keyboard(keys);
        }
        if (Enum.TryParse<ExtraButtonTarget>(text, ignoreCase: true, out var target) && Enum.IsDefined(target))
            return Gamepad(target);
        if (Enum.TryParse<SpecialAction>(text, ignoreCase: true, out var special) && Enum.IsDefined(special) && special != SpecialAction.None)
            return Do(special);
        return Nothing;
    }
}

public static class ControllerButtons
{
    private static readonly ProButtons[] Dpad = [ProButtons.Up, ProButtons.Down, ProButtons.Left, ProButtons.Right];
    private static readonly ProButtons[] Face = [ProButtons.A, ProButtons.B, ProButtons.X, ProButtons.Y];

    /// <summary>
    /// Tasten, die beim jeweiligen Controller wirklich vorhanden sind – in den Namen, unter denen sie
    /// nach dem Ausrichten ankommen (ein einzelner, quer gehaltener Joy-Con liefert z. B. A/B/X/Y aus seinen
    /// Richtungstasten und L/R aus SL/SR).
    /// </summary>
    public static IReadOnlyList<ProButtons> For(ControllerKind kind) => kind switch
    {
        ControllerKind.Pro2 =>
        [
            .. Face, ProButtons.L, ProButtons.R, ProButtons.ZL, ProButtons.ZR, ProButtons.Minus, ProButtons.Plus,
            ProButtons.LeftStick, ProButtons.RightStick, ProButtons.Home, ProButtons.Capture, ProButtons.C,
            ProButtons.GL, ProButtons.GR, .. Dpad,
        ],
        ControllerKind.Pro1 =>
        [
            .. Face, ProButtons.L, ProButtons.R, ProButtons.ZL, ProButtons.ZR, ProButtons.Minus, ProButtons.Plus,
            ProButtons.LeftStick, ProButtons.RightStick, ProButtons.Home, ProButtons.Capture, .. Dpad,
        ],
        ControllerKind.JoyConPair =>
        [
            .. Face, ProButtons.L, ProButtons.R, ProButtons.ZL, ProButtons.ZR, ProButtons.Minus, ProButtons.Plus,
            ProButtons.LeftStick, ProButtons.RightStick, ProButtons.Home, ProButtons.Capture, ProButtons.C, ProButtons.GL, ProButtons.GR, .. Dpad,
        ],
        // Einzelner Joy-Con quer: siehe Mapping.Normalize (LeftSideways/RightSideways).
        ControllerKind.JoyCon1Left or ControllerKind.JoyCon2Left =>
        [
            .. Face, ProButtons.L, ProButtons.R, ProButtons.ZL, ProButtons.ZR, ProButtons.Plus, ProButtons.Home,
            ProButtons.LeftStick,
        ],
        ControllerKind.JoyCon1Right =>
        [
            .. Face, ProButtons.L, ProButtons.R, ProButtons.ZL, ProButtons.ZR, ProButtons.Plus, ProButtons.Home,
            ProButtons.LeftStick,
        ],
        ControllerKind.JoyCon2Right =>
        [
            .. Face, ProButtons.L, ProButtons.R, ProButtons.ZL, ProButtons.ZR, ProButtons.Plus, ProButtons.Home,
            ProButtons.LeftStick, ProButtons.C,
        ],
        ControllerKind.GameCube2 =>
        [
            .. Face, ProButtons.ZR, ProButtons.ZL, ProButtons.L, ProButtons.R, ProButtons.Plus, ProButtons.Home,
            ProButtons.Capture, ProButtons.C, .. Dpad,
        ],
        ControllerKind.NesController => [ProButtons.A, ProButtons.B, ProButtons.L, ProButtons.R, ProButtons.Minus, ProButtons.Plus, .. Dpad],
        ControllerKind.SnesController =>
        [
            .. Face, ProButtons.L, ProButtons.R, ProButtons.ZL, ProButtons.ZR, ProButtons.Minus, ProButtons.Plus, .. Dpad,
        ],
        // N64 nach NormalizeClassic: A = B-Position, B = Y-Position; C-Tasten sind der rechte Stick.
        ControllerKind.N64Controller =>
        [
            ProButtons.B, ProButtons.Y, ProButtons.ZL, ProButtons.ZR, ProButtons.L, ProButtons.R, ProButtons.Plus,
            ProButtons.Home, ProButtons.Capture, .. Dpad,
        ],
        ControllerKind.MegaDrive =>
        [
            ProButtons.Y, ProButtons.B, ProButtons.A, ProButtons.L, ProButtons.X, ProButtons.R, ProButtons.Minus, ProButtons.Plus,
            ProButtons.Home, ProButtons.Capture, .. Dpad,
        ],
        // Wii-Fernbedienung (siehe WiiReports): quer gehalten 1/2 = Y/B-Position, A, B (Abzug), +/−, HOME;
        // mit Nunchuk C/Z, mit Classic Controller alle Tasten wie beim Pro Controller.
        ControllerKind.WiiRemote =>
        [
            .. Face, ProButtons.L, ProButtons.R, ProButtons.ZL, ProButtons.ZR, ProButtons.Minus, ProButtons.Plus, ProButtons.Home, .. Dpad,
        ],
        ControllerKind.WiiUPro =>
        [
            .. Face, ProButtons.L, ProButtons.R, ProButtons.ZL, ProButtons.ZR, ProButtons.Minus, ProButtons.Plus,
            ProButtons.LeftStick, ProButtons.RightStick, ProButtons.Home, .. Dpad,
        ],
        ControllerKind.DualShock4 =>
        [
            .. Face, ProButtons.L, ProButtons.R, ProButtons.ZL, ProButtons.ZR, ProButtons.Minus, ProButtons.Plus,
            ProButtons.LeftStick, ProButtons.RightStick, ProButtons.Home, ProButtons.Capture, .. Dpad,
        ],
        ControllerKind.DualSense =>
        [
            .. Face, ProButtons.L, ProButtons.R, ProButtons.ZL, ProButtons.ZR, ProButtons.Minus, ProButtons.Plus,
            ProButtons.LeftStick, ProButtons.RightStick, ProButtons.Home, ProButtons.Capture, ProButtons.Headset,
            ProButtons.C, ProButtons.GL, ProButtons.GR, .. Dpad,
        ],
        ControllerKind.XboxController =>
        [
            .. Face, ProButtons.L, ProButtons.R, ProButtons.Minus, ProButtons.Plus,
            ProButtons.LeftStick, ProButtons.RightStick, ProButtons.Home, .. Dpad,
        ],
        _ => Enum.GetValues<ProButtons>().Where(b => b != ProButtons.None).ToArray(),
    };

    /// <summary>Beschriftung einer Taste so, wie sie auf dem jeweiligen Controller steht.</summary>
    public static string Label(ProButtons button, ControllerKind kind)
    {
        if (kind == ControllerKind.GameCube2)
        {
            switch (button)
            {
                // Nach Position: die große untere Taste ist bei GameCube „A“ usw.
                case ProButtons.B: return "A (groß, unten)";
                case ProButtons.Y: return "B (links)";
                case ProButtons.A: return "X (rechts)";
                case ProButtons.X: return "Y (oben)";
                case ProButtons.ZR: return "Z";
                case ProButtons.ZL: return "ZL";
                case ProButtons.L: return "L (ganz durchgedrückt)";
                case ProButtons.R: return "R (ganz durchgedrückt)";
                case ProButtons.Plus: return "START/PAUSE";
            }
        }
        switch (kind, button)
        {
            case (ControllerKind.NesController or ControllerKind.SnesController, ProButtons.Minus): return "SELECT";
            case (ControllerKind.NesController or ControllerKind.SnesController, ProButtons.Plus): return "START";
            case (ControllerKind.N64Controller, ProButtons.B): return "A";
            case (ControllerKind.N64Controller, ProButtons.Y): return "B";
            case (ControllerKind.N64Controller, ProButtons.ZL): return "Z";
            case (ControllerKind.N64Controller, ProButtons.Plus): return "START";
            case (ControllerKind.MegaDrive, ProButtons.Y): return "A";
            case (ControllerKind.MegaDrive, ProButtons.B): return "B";
            case (ControllerKind.MegaDrive, ProButtons.A): return "C";
            case (ControllerKind.MegaDrive, ProButtons.L): return "X";
            case (ControllerKind.MegaDrive, ProButtons.X): return "Y";
            case (ControllerKind.MegaDrive, ProButtons.R): return "Z";
            case (ControllerKind.MegaDrive, ProButtons.Minus): return "MODE";
            case (ControllerKind.MegaDrive, ProButtons.Plus): return "START";
            case (ControllerKind.WiiRemote, ProButtons.Y): return "1 (quer) / Classic Y";
            case (ControllerKind.WiiRemote, ProButtons.B): return "2 (quer) / Classic B";
            case (ControllerKind.WiiRemote, ProButtons.A): return "A";
            case (ControllerKind.WiiRemote, ProButtons.ZR): return "B (Abzug) / Classic ZR";
            case (ControllerKind.WiiRemote, ProButtons.ZL): return "Nunchuk Z / Classic ZL";
            case (ControllerKind.WiiRemote, ProButtons.L): return "Nunchuk C / Classic L";
            case (ControllerKind.WiiRemote, ProButtons.R): return "Classic R";
            case (ControllerKind.WiiRemote, ProButtons.X): return "Classic X";
        }
        if (kind.IsPlayStation())
        {
            bool ds5 = kind == ControllerKind.DualSense;
            switch (button)
            {
                case ProButtons.B: return "Kreuz (unten)";
                case ProButtons.A: return "Kreis (rechts)";
                case ProButtons.Y: return "Viereck (links)";
                case ProButtons.X: return "Dreieck (oben)";
                case ProButtons.L: return "L1";
                case ProButtons.R: return "R1";
                case ProButtons.ZL: return "L2";
                case ProButtons.ZR: return "R2";
                case ProButtons.Minus: return ds5 ? "Create" : "Share";
                case ProButtons.Plus: return "Options";
                case ProButtons.LeftStick: return "L3 (Stick drücken)";
                case ProButtons.RightStick: return "R3 (Stick drücken)";
                case ProButtons.Home: return "PS-Taste";
                case ProButtons.Capture: return "Touchpad-Klick";
                case ProButtons.Headset: return "Mikro stumm";
                case ProButtons.C: return "Fn links (Edge)";
                case ProButtons.GL: return "Rücktaste links (Edge)";
                case ProButtons.GR: return "Rücktaste rechts (Edge)";
            }
        }
        if (kind.IsXbox())
        {
            switch (button)
            {
                case ProButtons.B: return "A";
                case ProButtons.A: return "B";
                case ProButtons.Y: return "X";
                case ProButtons.X: return "Y";
                case ProButtons.L: return "LB";
                case ProButtons.R: return "RB";
                case ProButtons.ZL: return "LT";
                case ProButtons.ZR: return "RT";
                case ProButtons.Minus: return "Ansicht/Zurück";
                case ProButtons.Plus: return "Menü/Start";
                case ProButtons.LeftStick: return "Linker Stick drücken";
                case ProButtons.RightStick: return "Rechter Stick drücken";
                case ProButtons.Home: return "Xbox-Taste";
            }
        }
        if (kind.IsJoyCon() && kind != ControllerKind.JoyConPair)
        {
            switch (button)
            {
                case ProButtons.B: return "untere Taste (quer)";
                case ProButtons.A: return "rechte Taste (quer)";
                case ProButtons.Y: return "linke Taste (quer)";
                case ProButtons.X: return "obere Taste (quer)";
                case ProButtons.L: return "SL";
                case ProButtons.R: return "SR";
                case ProButtons.ZL: return kind.IsLeftJoyCon() ? "L" : "R";
                case ProButtons.ZR: return kind.IsLeftJoyCon() ? "ZL" : "ZR";
                case ProButtons.Plus: return kind.IsLeftJoyCon() ? "−" : "+";
                case ProButtons.Home: return kind.IsLeftJoyCon() ? "Aufnahme" : "HOME";
                case ProButtons.LeftStick: return "Stick drücken";
            }
        }
        return button switch
        {
            ProButtons.Minus => "− (Minus)",
            ProButtons.Plus => "+ (Plus)",
            ProButtons.Home => "HOME",
            ProButtons.Capture => "Aufnahme",
            ProButtons.C => "C",
            ProButtons.GL => "GL (Rücktaste links)",
            ProButtons.GR => "GR (Rücktaste rechts)",
            ProButtons.LeftStick => "Linker Stick drücken",
            ProButtons.RightStick => "Rechter Stick drücken",
            ProButtons.Up => "Steuerkreuz hoch",
            ProButtons.Down => "Steuerkreuz runter",
            ProButtons.Left => "Steuerkreuz links",
            ProButtons.Right => "Steuerkreuz rechts",
            _ => button.ToString(),
        };
    }
}
