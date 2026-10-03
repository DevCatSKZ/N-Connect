namespace Switch2Pro.Protocol;

[Flags]
public enum ProButtons : uint
{
    None = 0,
    B = 1 << 0,
    A = 1 << 1,
    Y = 1 << 2,
    X = 1 << 3,
    L = 1 << 4,
    R = 1 << 5,
    ZL = 1 << 6,
    ZR = 1 << 7,
    Minus = 1 << 8,
    Plus = 1 << 9,
    LeftStick = 1 << 10,
    RightStick = 1 << 11,
    Home = 1 << 12,
    Capture = 1 << 13,
    C = 1 << 14,
    GL = 1 << 15,
    GR = 1 << 16,
    Up = 1 << 17,
    Down = 1 << 18,
    Left = 1 << 19,
    Right = 1 << 20,
    Headset = 1 << 21,
    /// <summary>SL/SR an der Schiene des linken Joy-Con.</summary>
    SLLeft = 1 << 22,
    SRLeft = 1 << 23,
    /// <summary>SL/SR an der Schiene des rechten Joy-Con.</summary>
    SLRight = 1 << 24,
    SRRight = 1 << 25,
}

/// <summary>Alle unterstützten Controller.</summary>
public enum ControllerKind
{
    Unknown,
    Pro2,
    JoyCon2Left,
    JoyCon2Right,
    GameCube2,
    Pro1,
    JoyCon1Left,
    JoyCon1Right,
    /// <summary>Zwei Joy-Con, zu einem Controller zusammengefasst.</summary>
    JoyConPair,
}

public static class ControllerKinds
{
    public static ControllerKind FromSwitch2ProductId(int pid) => pid switch
    {
        0x2069 => ControllerKind.Pro2,
        0x2067 => ControllerKind.JoyCon2Left,
        0x2066 => ControllerKind.JoyCon2Right,
        0x2073 => ControllerKind.GameCube2,
        _ => ControllerKind.Unknown,
    };

    public static ControllerKind FromSwitch1ProductId(int pid) => pid switch
    {
        Switch1.ProControllerProductId => ControllerKind.Pro1,
        Switch1.JoyConLeftProductId => ControllerKind.JoyCon1Left,
        Switch1.JoyConRightProductId => ControllerKind.JoyCon1Right,
        _ => ControllerKind.Unknown,
    };

    public static bool IsJoyCon(this ControllerKind k) =>
        k is ControllerKind.JoyCon1Left or ControllerKind.JoyCon1Right or ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right;

    public static bool IsLeftJoyCon(this ControllerKind k) => k is ControllerKind.JoyCon1Left or ControllerKind.JoyCon2Left;

    public static bool IsSwitch2(this ControllerKind k) =>
        k is ControllerKind.Pro2 or ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right or ControllerKind.GameCube2;

    /// <summary>Name, wie ihn Windows und die App anzeigen.</summary>
    public static string DisplayName(this ControllerKind k) => k switch
    {
        ControllerKind.Pro2 => "Nintendo Switch 2 Pro Controller",
        ControllerKind.JoyCon2Left => "Nintendo Switch 2 Joy-Con (L)",
        ControllerKind.JoyCon2Right => "Nintendo Switch 2 Joy-Con (R)",
        ControllerKind.GameCube2 => "Nintendo GameCube Controller (Switch 2)",
        ControllerKind.Pro1 => "Nintendo Switch Pro Controller",
        ControllerKind.JoyCon1Left => "Nintendo Switch Joy-Con (L)",
        ControllerKind.JoyCon1Right => "Nintendo Switch Joy-Con (R)",
        ControllerKind.JoyConPair => "Nintendo Joy-Con (L+R)",
        _ => "Nintendo Controller",
    };
}

/// <summary>Bewegungsdaten in Rohwerten der Sensorachsen (Gyro ±2000 °/s, Beschl. ±8 g auf ±32767).</summary>
public readonly record struct Motion(short AccelX, short AccelY, short AccelZ, short GyroX, short GyroY, short GyroZ);

public sealed record ControllerState
{
    public ControllerKind Kind { get; init; }
    public ProButtons Buttons { get; init; }
    /// <summary>Unkalibrierte 12-Bit-Rohwerte (0–4095), Y wächst nach oben.</summary>
    public int LeftX { get; init; } = 2048;
    public int LeftY { get; init; } = 2048;
    public int RightX { get; init; } = 2048;
    public int RightY { get; init; } = 2048;
    /// <summary>Analoge Trigger 0–255 (nur GameCube-Controller), sonst −1.</summary>
    public int LeftTrigger { get; init; } = -1;
    public int RightTrigger { get; init; } = -1;
    public Motion? Motion { get; init; }
    /// <summary>Maussensor der Joy-Con 2 (sonst null).</summary>
    public OpticalMouse? Mouse { get; init; }
    /// <summary>Akkuspannung in mV (nur Switch 2), sonst 0.</summary>
    public int BatteryMillivolts { get; init; }
    /// <summary>Akkustand 0–100 % oder −1, wenn unbekannt.</summary>
    public int BatteryPercent { get; init; } = -1;
    public bool Charging { get; init; }

    public bool Has(ProButtons b) => (Buttons & b) != 0;
}

/// <summary>
/// Optischer Sensor der Joy-Con 2 (Bericht 0x05 ab 0x10): X/Y sind fortlaufende 16-Bit-Zähler,
/// dazu Rauheit und Abstand der Oberfläche. Liegt der Joy-Con auf einer Fläche, ist der Abstand klein.
/// </summary>
public readonly record struct OpticalMouse(ushort X, ushort Y, ushort Roughness, ushort Distance)
{
    /// <summary>
    /// Auf einer Oberfläche? Schwellen wie in der Referenz Switch2Connect (Stufe „normal“):
    /// Abstand 1 … 999 und Rauheit unter 4000.
    /// </summary>
    public bool OnSurface => Distance != 0 && Distance < 1000 && Roughness < 4000;

    /// <summary>Kürzeste vorzeichenbehaftete Differenz zweier 16-Bit-Zählerstände (Überlauf beachtet).</summary>
    public static int Delta(ushort previous, ushort current) => ((current - previous + 0x8000) & 0xFFFF) - 0x8000;
}
