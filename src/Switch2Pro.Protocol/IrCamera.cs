namespace Switch2Pro.Protocol;

/// <summary>Auflösungen der IR-Kamera (rechter Joy-Con der Switch 1).</summary>
public enum IrResolution
{
    Size40x30,
    Size80x60,
    Size160x120,
    Size320x240,
}

/// <summary>
/// IR-Kamera im rechten Joy-Con (Switch 1): Bild in Graustufen, in Stücken zu 300 Byte übertragen. Ablauf
/// (öffentlich dokumentiert, u. a. im Emulator yuzu/Citron): MCU in den IR-Modus, Bildübertragung konfigurieren
/// (MCU-Befehl 0x23), Kameraregister schreiben, dann jedes Stück mit einer Quittung (Bericht 0x11) anfordern.
/// Stücke kommen in Bericht 0x31: Art 0x03 an Byte 49, Stücknummer an Byte 52, Bilddaten ab Byte 59.
/// </summary>
public static class IrCamera
{
    public const byte ModeIr = 5;
    public const byte ReportIrData = 0x03;
    public const int FragmentSize = 300;

    public static (int Width, int Height, byte Code, byte LastFragment) Format(IrResolution r) => r switch
    {
        IrResolution.Size80x60 => (80, 60, 0x64, 0x0F),
        IrResolution.Size160x120 => (160, 120, 0x50, 0x3F),
        IrResolution.Size320x240 => (320, 240, 0x00, 0xFF),
        _ => (40, 30, 0x69, 0x03),
    };

    /// <summary>MCU-Konfiguration 0x21-Argumente: Bildübertragung (Modus 0x07) mit Anzahl Stücke, MCU-Version 5.18.</summary>
    public static byte[] Configure(IrResolution r)
    {
        var c = new byte[38];
        c[0] = 0x23;                 // IR konfigurieren
        c[1] = 0x01;                 // Gerätemodus setzen
        c[2] = 0x07;                 // Bildübertragung
        c[3] = Format(r).LastFragment;
        c[4] = 0x00; c[5] = 0x05;    // MCU-Hauptversion 0x0500 (LE)
        c[6] = 0x00; c[7] = 0x18;    // Nebenversion 0x1800 (LE)
        c[37] = Nfc.Crc8(c.AsSpan(1, 36));
        return c;
    }

    /// <summary>Kameraregister schreiben (bis 9 Stück je Paket): (Adresse als Seite/Register, Wert).</summary>
    private static byte[] Registers(params (ushort Address, byte Value)[] regs)
    {
        var c = new byte[38];
        c[0] = 0x23;                 // IR konfigurieren
        c[1] = 0x04;                 // Register schreiben
        c[2] = (byte)regs.Length;
        for (int i = 0; i < regs.Length; i++)
        {
            c[3 + i * 3] = (byte)regs[i].Address;
            c[4 + i * 3] = (byte)(regs[i].Address >> 8);
            c[5 + i * 3] = regs[i].Value;
        }
        c[37] = Nfc.Crc8(c.AsSpan(1, 36));
        return c;
    }

    // Standardwerte wie in der Dokumentation: Belichtung 0x2490, beide LED-Gruppen, Filter an, Verstärkung 1.
    private const ushort Exposure = 0x2490, LedIntensity = 0x0F10;
    private const byte DigitalGain = 0x01;
    private const uint Denoise = 0x012344;

    public static byte[] RegistersStep1(IrResolution r) => Registers(
        (0x2E00, Format(r).Code),                     // Auflösung
        (0x3001, (byte)(Exposure & 0xFF)),
        (0x3101, (byte)(Exposure >> 8)),
        (0x3201, 0x00),                               // Belichtung: kein Langzeitmodus
        (0x1000, 0x00),                               // LEDs: hell und gedimmt
        (0x2E01, (byte)((DigitalGain & 0x0F) << 4)),
        (0x2F01, (byte)((DigitalGain & 0xF0) >> 4)),
        (0x0E00, 0x03),                               // externer Lichtfilter an
        (0x4301, 0xC8));                              // Schwelle weiße Pixel

    public static byte[] RegistersStep2() => Registers(
        (0x1100, (byte)(LedIntensity >> 8)),
        (0x1200, (byte)(LedIntensity & 0xFF)),
        (0x2D00, 0x00),                               // Bild nicht gespiegelt
        (0x6701, (byte)((Denoise >> 16) & 0xFF)),
        (0x6801, (byte)((Denoise >> 8) & 0xFF)),
        (0x6901, (byte)(Denoise & 0xFF)),
        (0x0400, 0x2D),                               // Aktualisierungszeit
        (0x0700, 0x01));                              // Einstellungen übernehmen

    /// <summary>Quittung/Anforderung für das nächste Stück (MCU-Bericht mit Unterbefehl 0x03).</summary>
    public static byte[] Acknowledge(byte fragment, bool start = false)
    {
        var m = new byte[38];
        m[0] = (byte)(start ? 0x02 : 0x00);
        m[3] = fragment;
        m[36] = Nfc.Crc8(m.AsSpan(0, 36));
        m[37] = 0xFF;
        return m;
    }

    /// <summary>Stück erneut anfordern.</summary>
    public static byte[] Resend(byte fragment)
    {
        var m = new byte[38];
        m[1] = 0x01;
        m[2] = fragment;
        m[36] = Nfc.Crc8(m.AsSpan(0, 36));
        m[37] = 0xFF;
        return m;
    }
}

/// <summary>Setzt die Stücke zu einem Bild zusammen und sagt, welche Quittung als Nächstes fällig ist.</summary>
public sealed class IrFrameAssembler
{
    private readonly byte _last;
    private readonly byte[] _image;
    private byte _fragment;

    public int Width { get; }
    public int Height { get; }

    public IrFrameAssembler(IrResolution resolution)
    {
        var f = IrCamera.Format(resolution);
        (Width, Height, _last) = (f.Width, f.Height, f.LastFragment);
        _image = new byte[(_last + 1) * IrCamera.FragmentSize];
        _fragment = _last; // so dass als Nächstes Stück 0 erwartet wird
    }

    /// <summary>
    /// Bericht 0x31 verarbeiten. Liefert die zu sendende Anforderung und – wenn das letzte Stück eingetroffen ist –
    /// das fertige Bild (Breite × Höhe Graustufen).
    /// </summary>
    public (byte[] Request, byte[]? Image) Handle(ReadOnlySpan<byte> report)
    {
        byte next = (byte)((_fragment + 1) % (_last + 1));
        if (report.Length >= 59 + IrCamera.FragmentSize && report[0] == Nfc.InputMcu && report[Nfc.McuReportOffset] == IrCamera.ReportIrData)
        {
            byte got = report[52];
            if (got == next)
            {
                _fragment = got;
                report.Slice(59, IrCamera.FragmentSize).CopyTo(_image.AsSpan(IrCamera.FragmentSize * got));
                byte[]? done = got == _last ? _image[..(Width * Height)] : null;
                return (IrCamera.Acknowledge(got), done);
            }
            if (got == _fragment)
                return (IrCamera.Acknowledge(_fragment), null);
            return (IrCamera.Resend(next), null);
        }
        return (IrCamera.Acknowledge(_fragment), null);
    }
}
