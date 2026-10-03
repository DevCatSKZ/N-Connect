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
}

/// <summary>
/// Was eine Controller-Taste auslösen soll: eine Taste des virtuellen Gamepads, eine Tastaturtaste bzw.
/// Tastenkombination (Hotkey), eine Sonderaktion (Maus, Gyro-Maus, Shift) oder nichts. Gespeichert als Text,
/// z. B. "A", "LT", "None", "Key:F5", "Key:Ctrl+Shift+S", "Key:Win+Print", "MouseLeft", "GyroMouse", "Shift".
/// </summary>
public readonly record struct ButtonAction(ExtraButtonTarget Target, string? Keys, SpecialAction Special = SpecialAction.None)
{
    public const string KeyPrefix = "Key:";

    public static ButtonAction Gamepad(ExtraButtonTarget target) => new(target, null);
    public static ButtonAction Keyboard(string keys) => new(ExtraButtonTarget.None, keys);
    public static ButtonAction Do(SpecialAction special) => new(ExtraButtonTarget.None, null, special);
    public static ButtonAction Nothing { get; } = new(ExtraButtonTarget.None, null);

    public bool IsKeyboard => Keys is not null;
    public bool IsSpecial => Special != SpecialAction.None;

    public override string ToString() => IsKeyboard ? KeyPrefix + Keys : IsSpecial ? Special.ToString() : Target.ToString();

    public static ButtonAction Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Nothing;
        text = text.Trim();
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
