namespace Switch2Pro.Protocol;

/// <summary>Kalibrierung einer Stick-Achse: Mitte und Ausschlag nach oben/unten (12 Bit).</summary>
public readonly record struct AxisCalibration(int Neutral, int Max, int Min)
{
    /// <summary>Rohwert (0–4095) → −1 … +1.</summary>
    public float Normalize(int raw)
    {
        float v = raw - Neutral;
        v = v < 0 ? v / Min : v / Max;
        return Math.Clamp(v, -1f, 1f);
    }
}

public readonly record struct StickCalibration(AxisCalibration X, AxisCalibration Y)
{
    /// <summary>Notwert, falls die Kalibrierung nicht gelesen werden konnte.</summary>
    public static StickCalibration Default { get; } =
        new(new AxisCalibration(2048, 1300, 1300), new AxisCalibration(2048, 1300, 1300));

    /// <summary>
    /// Liest 9 Byte = sechs gepackte 12-Bit-Werte: Mitte X/Y, Max X/Y, Min X/Y
    /// (gleiches Format wie SDL, SDL_hidapi_switch2.c: ParseStickCalibration).
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out StickCalibration calibration)
    {
        calibration = Default;
        if (data.Length < 9)
            return false;
        var (cx, cy) = Unpack12(data[0..3]);
        var (maxX, maxY) = Unpack12(data[3..6]);
        var (minX, minY) = Unpack12(data[6..9]);
        // Ungültig (unbeschrieben = FF FF FF … oder 0) → Notwert.
        if (cx is 0 or 0xFFF || cy is 0 or 0xFFF || maxX < 200 || maxY < 200 || minX < 200 || minY < 200
            || maxX == 0xFFF || minX == 0xFFF)
            return false;
        calibration = new StickCalibration(new AxisCalibration(cx, maxX, minX), new AxisCalibration(cy, maxY, minY));
        return true;
    }

    public static (int A, int B) Unpack12(ReadOnlySpan<byte> b) =>
        (b[0] | ((b[1] & 0x0F) << 8), (b[1] >> 4) | (b[2] << 4));
}

/// <summary>Gyro-Nullpunkt in Rohwerten (LSB) der drei Sensorachsen.</summary>
public readonly record struct GyroBias(float X, float Y, float Z)
{
    // 2000 °/s Vollausschlag bei ±32767.
    private const float RawPerRadPerSecond = 32767f / 2000f * (180f / MathF.PI);

    /// <summary>Werkswerte: drei float32 (rad/s) ab Offset 4 des Blocks bei 0x13040.</summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out GyroBias bias)
    {
        bias = default;
        if (data.Length < 16)
            return false;
        float x = BitConverter.ToSingle(data[4..8]);
        float y = BitConverter.ToSingle(data[8..12]);
        float z = BitConverter.ToSingle(data[12..16]);
        // Plausibilität: Nullpunktfehler liegen weit unter 1 rad/s.
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)
            || MathF.Abs(x) > 1f || MathF.Abs(y) > 1f || MathF.Abs(z) > 1f)
            return false;
        bias = new GyroBias(x * RawPerRadPerSecond, y * RawPerRadPerSecond, z * RawPerRadPerSecond);
        return true;
    }
}

/// <summary>Alle Kalibrierwerte eines Controllers.</summary>
public sealed record DeviceCalibration
{
    public StickCalibration Left { get; init; } = StickCalibration.Default;
    public StickCalibration Right { get; init; } = StickCalibration.Default;
    /// <summary>Gyro-Nullpunkt in Switch-2-Rohwerten (Switch 1: bereits in den Bewegungsdaten verrechnet).</summary>
    public GyroBias Gyro { get; init; }
    /// <summary>Ruhewert der analogen Trigger (GameCube-Controller, Block 0x13140).</summary>
    public int TriggerZeroLeft { get; init; } = 30;
    public int TriggerZeroRight { get; init; } = 30;

    public static DeviceCalibration Default { get; } = new();
}
