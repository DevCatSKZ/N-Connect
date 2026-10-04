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
    /// <summary>Nintendo Switch Online: NES-Controller (L oder R, auch Famicom).</summary>
    NesController,
    /// <summary>Nintendo Switch Online: SNES-Controller.</summary>
    SnesController,
    /// <summary>Nintendo Switch Online: Nintendo-64-Controller.</summary>
    N64Controller,
    /// <summary>Nintendo Switch Online: SEGA Mega Drive / Genesis-Controller.</summary>
    MegaDrive,
    /// <summary>Wii-Fernbedienung (auch Plus), ggf. mit Nunchuk oder Classic Controller.</summary>
    WiiRemote,
    /// <summary>Wii U Pro Controller.</summary>
    WiiUPro,
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
        ControllerKind.NesController => "NES Controller (Nintendo Switch Online)",
        ControllerKind.SnesController => "SNES Controller (Nintendo Switch Online)",
        ControllerKind.N64Controller => "Nintendo 64 Controller (Nintendo Switch Online)",
        ControllerKind.MegaDrive => "SEGA Mega Drive Controller (Nintendo Switch Online)",
        ControllerKind.WiiRemote => "Wii-Fernbedienung",
        ControllerKind.WiiUPro => "Wii U Pro Controller",
        _ => "Nintendo Controller",
    };

    /// <summary>Nintendo-Switch-Online-Controller (Switch-1-Protokoll, eigene Tastenanordnung).</summary>
    public static bool IsClassic(this ControllerKind k) =>
        k is ControllerKind.NesController or ControllerKind.SnesController or ControllerKind.N64Controller or ControllerKind.MegaDrive;

    /// <summary>
    /// Art aus dem Gerätetyp der Antwort auf „Geräteinfo“ (Switch-1-Protokoll, Byte 2): NES-Controller melden
    /// sich mit der Produkt-ID eines Joy-Con und sind nur hieran zu erkennen. Typen nach hid-nintendo (Linux).
    /// </summary>
    public static ControllerKind FromSwitch1DeviceType(byte type, ControllerKind fallback) => type switch
    {
        0x07 or 0x08 or 0x09 or 0x0A => ControllerKind.NesController, // Famicom/NES links/rechts
        0x0B => ControllerKind.SnesController,
        0x0C => ControllerKind.N64Controller,
        0x0D => ControllerKind.MegaDrive,
        _ => fallback,
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
    /// <summary>Ring-Con (am rechten Joy-Con der Switch 1): Biegung −1 (auseinanderziehen) … +1 (zusammendrücken).</summary>
    public float? RingFlex { get; init; }
    /// <summary>Wii-Zeiger (Sensorleiste): Position 0…1 auf dem Bildschirm (links oben = 0,0); null = nicht sichtbar.</summary>
    public (float X, float Y)? Pointer { get; init; }
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
