namespace Switch2Pro.Protocol;

/// <summary>
/// HD-Rumble-2 für den Pro Controller 2 über Bluetooth.
/// Paket (33 Byte) an <see cref="Gatt.ProRumbleOutput"/>: Byte 0 = 0x00, dann je Seite 16 Byte:
/// Zählerbyte (0x50 | n) + drei 5-Byte-Frames. Frame (40 Bit, LE):
/// tiefe Frequenz (Bit 0–9), tiefe Amplitude (10–19), hohe Frequenz (20–29), hohe Amplitude (30–39).
/// Format aus NS2Pro-Bridge-Windows (MIT) / joycon2cpp übernommen.
/// </summary>
public static class Rumble
{
    public const int PacketSize = 33;
    private const int FrequencyMax = 0x1FF;
    private const int AmplitudeMax = 0x3FF;

    // Mittelwerte, bei denen die Linearmotoren angenehm „brummen“ (tief) bzw. „surren“ (hoch).
    private const int LowFrequency = 160;
    private const int HighFrequency = 320;
    private const int NeutralLowFrequency = 0x0E1;
    private const int NeutralHighFrequency = 0x1E1;

    /// <summary>Höchste Amplitude bei Stärke 1.0 (gut 3/4 des Bereichs; mehr klappert).</summary>
    private const int MaxAmplitude = 800;

    public static byte[] EncodeFrame(int lowFrequency, int lowAmplitude, int highFrequency, int highAmplitude)
    {
        ulong v = (ulong)(uint)Math.Clamp(lowFrequency, 0, FrequencyMax)
                  | (ulong)(uint)Math.Clamp(lowAmplitude, 0, AmplitudeMax) << 10
                  | (ulong)(uint)Math.Clamp(highFrequency, 0, FrequencyMax) << 20
                  | (ulong)(uint)Math.Clamp(highAmplitude, 0, AmplitudeMax) << 30;
        return [(byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24), (byte)(v >> 32)];
    }

    /// <summary>
    /// Baut ein Paket aus den beiden XInput-Motoren (0–255): der große Motor wird zum tiefen,
    /// der kleine zum hohen Frequenzband. <paramref name="strength"/> 0–1 skaliert alles.
    /// Links/rechts bekommen dasselbe Signal, wie bei einem Xbox-Controller üblich, aber der
    /// große Motor betont links und der kleine rechts.
    /// </summary>
    public static byte[] BuildPacket(byte largeMotor, byte smallMotor, int counter, float strength = 1f)
    {
        strength = Math.Clamp(strength, 0f, 1f);
        int Amp(byte motor, float weight) =>
            (int)MathF.Round(MathF.Pow(motor / 255f, 1.2f) * MaxAmplitude * strength * weight);

        var neutral = EncodeFrame(NeutralLowFrequency, 0, NeutralHighFrequency, 0);
        var packet = new byte[PacketSize];
        byte sequence = (byte)(0x50 | (counter & 0x0F));
        Span<(int Offset, float LargeWeight, float SmallWeight)> sides = [(1, 1f, 0.75f), (17, 0.75f, 1f)];
        foreach (var (offset, largeWeight, smallWeight) in sides)
        {
            int low = Amp(largeMotor, largeWeight), high = Amp(smallMotor, smallWeight);
            var frame = low == 0 && high == 0 ? neutral : EncodeFrame(LowFrequency, low, HighFrequency, high);
            packet[offset] = sequence;
            frame.CopyTo(packet, offset + 1);
            // Alle drei Frames gleich: falls ein Paket verloren geht, läuft das Signal weich weiter.
            frame.CopyTo(packet, offset + 6);
            frame.CopyTo(packet, offset + 11);
        }
        return packet;
    }

    public static byte[] StopPacket(int counter) => BuildPacket(0, 0, counter);
}
