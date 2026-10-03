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
}

/// <summary>Bewegungsdaten in Rohwerten der Sensorachsen (Gyro ±2000 °/s, Beschl. ±8 g auf ±32767).</summary>
public readonly record struct Motion(short AccelX, short AccelY, short AccelZ, short GyroX, short GyroY, short GyroZ);

public sealed record ControllerState
{
    public ProButtons Buttons { get; init; }
    /// <summary>Unkalibrierte 12-Bit-Rohwerte (0–4095), Y wächst nach oben.</summary>
    public int LeftX { get; init; } = 2048;
    public int LeftY { get; init; } = 2048;
    public int RightX { get; init; } = 2048;
    public int RightY { get; init; } = 2048;
    public Motion? Motion { get; init; }
    /// <summary>Akkuspannung in mV (nur Bericht 0x05), sonst 0.</summary>
    public int BatteryMillivolts { get; init; }
    /// <summary>Akkustand 0–100 % oder −1, wenn unbekannt.</summary>
    public int BatteryPercent { get; init; } = -1;
    public bool Charging { get; init; }

    public bool Has(ProButtons b) => (Buttons & b) != 0;
}
