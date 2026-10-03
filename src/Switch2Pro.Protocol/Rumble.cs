namespace Switch2Pro.Protocol;

/// <summary>
/// HD-Rumble-2 für den Pro Controller 2. Aufbau wie in SDL (SDL_hidapi_switch2.c, UpdateRumble),
/// dort mit echter Hardware erprobt:
/// je Seite 16 Byte = Zählerbyte (0x50 | n) + 5-Byte-Frame (Rest 0); rechte Seite ab Byte 0x11.
/// Frame (40 Bit, LE): hohe Frequenz (Bit 0–9), hohe Amplitude (10–19),
/// tiefe Frequenz (20–29), tiefe Amplitude (30–39).
/// Bluetooth: 33 Byte an <see cref="Gatt.ProRumbleOutput"/>, Byte 0 = 0x00.
/// USB: HID-Ausgabebericht 0x02 (64 Byte), Byte 0 = Report-ID.
/// </summary>
public static class Rumble
{
    public const int PacketSize = 33;
    public const int UsbReportSize = 64;
    public const byte UsbReportId = 0x02;

    private const int FieldMax = 0x3FF;
    private const int HighFrequency = 0x187;
    private const int LowFrequency = 0x112;

    /// <summary>
    /// Höchste Amplitude bei Stärke 1.0: wie SDL (29000 von 65535 ≙ 453 von 1023).
    /// Mehr kann laut SDL den Linearmotoren schaden.
    /// </summary>
    public const int MaxAmplitude = 29000 >> 6;

    public static byte[] EncodeFrame(int highFrequency, int highAmplitude, int lowFrequency, int lowAmplitude)
    {
        ulong v = (ulong)(uint)Math.Clamp(highFrequency, 0, FieldMax)
                  | (ulong)(uint)Math.Clamp(highAmplitude, 0, FieldMax) << 10
                  | (ulong)(uint)Math.Clamp(lowFrequency, 0, FieldMax) << 20
                  | (ulong)(uint)Math.Clamp(lowAmplitude, 0, FieldMax) << 30;
        return [(byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24), (byte)(v >> 32)];
    }

    /// <summary>
    /// Frame aus den beiden XInput-Motoren (0–255): großer Motor → tiefes Band, kleiner → hohes Band.
    /// <paramref name="strength"/> 0–1 skaliert alles.
    /// </summary>
    public static byte[] Frame(byte largeMotor, byte smallMotor, float strength = 1f)
    {
        strength = Math.Clamp(strength, 0f, 1f);
        int Amp(byte motor) => (int)MathF.Round(motor / 255f * MaxAmplitude * strength);
        return EncodeFrame(HighFrequency, Amp(smallMotor), LowFrequency, Amp(largeMotor));
    }

    private static void Fill(Span<byte> packet, byte[] frame, int counter)
    {
        byte sequence = (byte)(0x50 | (counter & 0x0F));
        foreach (int offset in (ReadOnlySpan<int>)[0x01, 0x11])
        {
            packet[offset] = sequence;
            frame.CopyTo(packet[(offset + 1)..]);
        }
    }

    /// <summary>Bluetooth-Paket (33 Byte).</summary>
    public static byte[] BuildPacket(byte largeMotor, byte smallMotor, int counter, float strength = 1f)
    {
        var packet = new byte[PacketSize];
        Fill(packet, Frame(largeMotor, smallMotor, strength), counter);
        return packet;
    }

    public static byte[] StopPacket(int counter) => BuildPacket(0, 0, counter);

    /// <summary>
    /// Bluetooth-Paket je Controller-Art: Joy-Con 2 haben einen Motor (17 Byte, wie switch2controllerpc),
    /// Pro Controller 2 und GameCube-Controller zwei Seiten (33 Byte).
    /// </summary>
    public static byte[] BuildPacket(ControllerKind kind, byte largeMotor, byte smallMotor, int counter, float strength)
    {
        if (!kind.IsJoyCon())
            return BuildPacket(largeMotor, smallMotor, counter, strength);
        var packet = new byte[17];
        packet[1] = (byte)(0x50 | (counter & 0x0F));
        Frame(largeMotor, smallMotor, strength).CopyTo(packet, 2);
        return packet;
    }

    /// <summary>
    /// GameCube-Controller: einfacher Motor (nur an/aus, HID-Bericht 0x03 über USB). Die Stärke entsteht durch
    /// schnelles An- und Ausschalten: ein Fehlerspeicher verteilt die An-Phasen gleichmäßig (Fehlerdiffusion).
    /// Byte 2: 1 = an, 0 = aus, 2 = Stopp.
    /// </summary>
    public sealed class GameCubeMotor
    {
        private float _error;

        public byte[] NextReport(float strength, int counter)
        {
            var report = new byte[UsbReportSize];
            report[0] = 0x03;
            report[1] = (byte)(0x50 | (counter & 0x0F));
            strength = Math.Clamp(strength, 0f, 1f);
            if (strength <= 0f)
            {
                report[2] = 2;
                _error = 0;
                return report;
            }
            _error += strength;
            if (_error >= 1f)
            {
                report[2] = 1;
                _error -= 1f;
            }
            return report;
        }
    }

    /// <summary>USB: HID-Ausgabebericht 0x02 (64 Byte).</summary>
    public static byte[] BuildUsbReport(byte largeMotor, byte smallMotor, int counter, float strength = 1f)
    {
        var report = new byte[UsbReportSize];
        report[0] = UsbReportId;
        Fill(report, Frame(largeMotor, smallMotor, strength), counter);
        return report;
    }
}
