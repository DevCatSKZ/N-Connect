using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Switch2Pro.Protocol;

/// <summary>
/// Virtueller Xbox-One-S-Controller (Bluetooth, 045E:02FD) als HID-Gerät – Ausgabe unter Android über UHID
/// (Shell-Befehl <c>/system/bin/hid</c>, mit Root oder Shizuku). Der Kernel bindet ihn ohne Feature-Reports
/// (anders als DualShock 4/DualSense, deren Treiber beim Erkennen scheitern), Android meldet ein Gamepad mit
/// Vibration. Deskriptor und Berichtsaufbau stammen aus den Android-CTS-Tests <c>microsoft_xboxones_*</c>;
/// am Pixel 10a (Android 17) geprüft.
/// </summary>
/// <remarks>
/// Eingabebericht, 17 Byte: [0] Report-ID 0x01 | [1–8] LX, LY, RX, RY (u16 LE, Mitte 0x7FFF, Y nach unten positiv)
/// | [9–12] LT, RT (10 Bit) | [13] Steuerkreuz (0 = neutral, 1 = oben, im Uhrzeigersinn bis 8 = oben links)
/// | [14] A 01, B 02, X 08, Y 10, LB 40, RB 80 | [15] Start 08, Xbox 10, LS 20, RS 40 | [16] Zurück 01.
/// </remarks>
public static class XboxBtReport
{
    public const ushort VendorId = 0x045E;
    public const ushort ProductId = 0x02FD;
    public const int Length = 17;

    /// <summary>HID-Deskriptor des Xbox One S Controllers über Bluetooth (334 Byte).</summary>
    public static ReadOnlySpan<byte> Descriptor => new byte[]
    {
        0x05, 0x01, 0x09, 0x05, 0xA1, 0x01, 0x85, 0x01, 0x09, 0x01, 0xA1, 0x00, 0x09, 0x30, 0x09, 0x31,
        0x15, 0x00, 0x27, 0xFF, 0xFF, 0x00, 0x00, 0x95, 0x02, 0x75, 0x10, 0x81, 0x02, 0xC0, 0x09, 0x01,
        0xA1, 0x00, 0x09, 0x32, 0x09, 0x35, 0x15, 0x00, 0x27, 0xFF, 0xFF, 0x00, 0x00, 0x95, 0x02, 0x75,
        0x10, 0x81, 0x02, 0xC0, 0x05, 0x02, 0x09, 0xC5, 0x15, 0x00, 0x26, 0xFF, 0x03, 0x95, 0x01, 0x75,
        0x0A, 0x81, 0x02, 0x15, 0x00, 0x25, 0x00, 0x75, 0x06, 0x95, 0x01, 0x81, 0x03, 0x05, 0x02, 0x09,
        0xC4, 0x15, 0x00, 0x26, 0xFF, 0x03, 0x95, 0x01, 0x75, 0x0A, 0x81, 0x02, 0x15, 0x00, 0x25, 0x00,
        0x75, 0x06, 0x95, 0x01, 0x81, 0x03, 0x05, 0x01, 0x09, 0x39, 0x15, 0x01, 0x25, 0x08, 0x35, 0x00,
        0x46, 0x3B, 0x01, 0x66, 0x14, 0x00, 0x75, 0x04, 0x95, 0x01, 0x81, 0x42, 0x75, 0x04, 0x95, 0x01,
        0x15, 0x00, 0x25, 0x00, 0x35, 0x00, 0x45, 0x00, 0x65, 0x00, 0x81, 0x03, 0x05, 0x09, 0x19, 0x01,
        0x29, 0x0F, 0x15, 0x00, 0x25, 0x01, 0x75, 0x01, 0x95, 0x0F, 0x81, 0x02, 0x15, 0x00, 0x25, 0x00,
        0x75, 0x01, 0x95, 0x01, 0x81, 0x03, 0x05, 0x0C, 0x0A, 0x24, 0x02, 0x15, 0x00, 0x25, 0x01, 0x95,
        0x01, 0x75, 0x01, 0x81, 0x02, 0x15, 0x00, 0x25, 0x00, 0x75, 0x07, 0x95, 0x01, 0x81, 0x03, 0x05,
        0x0C, 0x09, 0x01, 0x85, 0x02, 0xA1, 0x01, 0x05, 0x0C, 0x0A, 0x23, 0x02, 0x15, 0x00, 0x25, 0x01,
        0x95, 0x01, 0x75, 0x01, 0x81, 0x02, 0x15, 0x00, 0x25, 0x00, 0x75, 0x07, 0x95, 0x01, 0x81, 0x03,
        0xC0, 0x05, 0x0F, 0x09, 0x21, 0x85, 0x03, 0xA1, 0x02, 0x09, 0x97, 0x15, 0x00, 0x25, 0x01, 0x75,
        0x04, 0x95, 0x01, 0x91, 0x02, 0x15, 0x00, 0x25, 0x00, 0x75, 0x04, 0x95, 0x01, 0x91, 0x03, 0x09,
        0x70, 0x15, 0x00, 0x25, 0x64, 0x75, 0x08, 0x95, 0x04, 0x91, 0x02, 0x09, 0x50, 0x66, 0x01, 0x10,
        0x55, 0x0E, 0x15, 0x00, 0x26, 0xFF, 0x00, 0x75, 0x08, 0x95, 0x01, 0x91, 0x02, 0x09, 0xA7, 0x15,
        0x00, 0x26, 0xFF, 0x00, 0x75, 0x08, 0x95, 0x01, 0x91, 0x02, 0x65, 0x00, 0x55, 0x00, 0x09, 0x7C,
        0x15, 0x00, 0x26, 0xFF, 0x00, 0x75, 0x08, 0x95, 0x01, 0x91, 0x02, 0xC0, 0x05, 0x06, 0x09, 0x20,
        0x85, 0x04, 0x15, 0x00, 0x26, 0xFF, 0x00, 0x75, 0x08, 0x95, 0x01, 0x81, 0x02, 0xC0,
    };

    /// <summary>Baut den Eingabebericht aus einem Gamepad-Zustand im Xbox-Schema.</summary>
    public static byte[] Build(GamepadState g)
    {
        var r = new byte[Length];
        Build(g, r);
        return r;
    }

    /// <summary>
    /// Wie <see cref="Build(GamepadState)"/>, schreibt aber in einen vorhandenen Puffer (mindestens
    /// <see cref="Length"/> Byte) – für den heißen Pfad ohne Speicherreservierung pro Bericht.
    /// </summary>
    public static void Build(GamepadState g, Span<byte> r)
    {
        r[0] = 0x01;
        Put(r, 1, AxisX(g.LeftX));
        Put(r, 3, AxisY(g.LeftY));
        Put(r, 5, AxisX(g.RightX));
        Put(r, 7, AxisY(g.RightY));
        Put(r, 9, Trigger(g.LeftTrigger));
        Put(r, 11, Trigger(g.RightTrigger));
        r[13] = Hat(g.Buttons);
        var b = g.Buttons;
        r[14] = (byte)((b.HasFlag(XButtons.A) ? 0x01 : 0) | (b.HasFlag(XButtons.B) ? 0x02 : 0)
                       | (b.HasFlag(XButtons.X) ? 0x08 : 0) | (b.HasFlag(XButtons.Y) ? 0x10 : 0)
                       | (b.HasFlag(XButtons.LB) ? 0x40 : 0) | (b.HasFlag(XButtons.RB) ? 0x80 : 0));
        r[15] = (byte)((b.HasFlag(XButtons.Start) ? 0x08 : 0) | (b.HasFlag(XButtons.Guide) ? 0x10 : 0)
                       | (b.HasFlag(XButtons.LS) ? 0x20 : 0) | (b.HasFlag(XButtons.RS) ? 0x40 : 0));
        r[16] = (byte)(b.HasFlag(XButtons.Back) ? 0x01 : 0);
    }

    /// <summary>Bericht ohne Eingabe (Sticks mittig, nichts gedrückt).</summary>
    public static byte[] Neutral() => Build(default);

    private static void Put(Span<byte> r, int offset, ushort value)
    {
        r[offset] = (byte)value;
        r[offset + 1] = (byte)(value >> 8);
    }

    /// <summary>−32768…32767 (rechts positiv) → 0…65534, Mitte 0x7FFF.</summary>
    private static ushort AxisX(short v) => (ushort)Math.Clamp(32767 + v, 0, 65535);

    /// <summary>Xbox-Schema hat Y oben positiv, der Bericht Y unten positiv.</summary>
    private static ushort AxisY(short v) => (ushort)Math.Clamp(32767 - v, 0, 65535);

    private static ushort Trigger(byte t) => (ushort)((t * 1023 + 127) / 255);

    private static byte Hat(XButtons b)
    {
        bool up = b.HasFlag(XButtons.Up), down = b.HasFlag(XButtons.Down);
        bool left = b.HasFlag(XButtons.Left), right = b.HasFlag(XButtons.Right);
        if (up && down) up = down = false;
        if (left && right) left = right = false;
        return (up, right, down, left) switch
        {
            (true, false, false, false) => 1,
            (true, true, false, false) => 2,
            (false, true, false, false) => 3,
            (false, true, true, false) => 4,
            (false, false, true, false) => 5,
            (false, false, true, true) => 6,
            (false, false, false, true) => 7,
            (true, false, false, true) => 8,
            _ => 0,
        };
    }
}

/// <summary>
/// Befehle für den Android-Shell-Befehl <c>hid</c> (AOSP <c>frameworks/base/cmds/hid</c>): eine JSON-Zeile je
/// Ereignis über stdin. Damit legt SKZ Connect unter Android ein virtuelles HID-Gerät an und füttert es.
/// Zahlen immer kulturunabhängig (manche Sprachen schreiben negative Zahlen mit Unicode-Minus – ungültiges JSON).
/// </summary>
public static class UhidCommand
{
    /// <summary>Gerät anmelden. <paramref name="bus"/>: "bluetooth" oder "usb".</summary>
    public static string Register(int id, string name, ushort vendorId, ushort productId, string bus,
        ReadOnlySpan<byte> descriptor)
    {
        var sb = new StringBuilder(descriptor.Length * 4 + 160);
        sb.Append("{\"id\":").Append(id.ToString(CultureInfo.InvariantCulture)).Append(",\"command\":\"register\",\"name\":\"").Append(Escape(name))
          .Append("\",\"vid\":").Append(vendorId).Append(",\"pid\":").Append(productId)
          .Append(",\"bus\":\"").Append(bus).Append("\",\"descriptor\":");
        AppendBytes(sb, descriptor);
        return sb.Append('}').ToString();
    }

    /// <summary>Eingabebericht senden.</summary>
    public static string Report(int id, ReadOnlySpan<byte> report)
    {
        var sb = new StringBuilder(report.Length * 4 + 40);
        sb.Append("{\"id\":").Append(id.ToString(CultureInfo.InvariantCulture)).Append(",\"command\":\"report\",\"report\":");
        AppendBytes(sb, report);
        return sb.Append('}').ToString();
    }

    /// <summary>
    /// Die „Bericht senden“-Zeile (wie <see cref="Report"/>, plus Zeilenumbruch) direkt als ASCII in einen
    /// Byte-Puffer schreiben. Rückgabe: geschriebene Länge. Im Dauerbetrieb ohne Speicherreservierung –
    /// bei ~90 Eingaben/s je Controller würden sonst laufend Zeichenketten im Speicher landen.
    /// Jede <paramref name="id"/> wird so geschrieben wie bei <see cref="Report"/> (auch mehrstellig oder negativ);
    /// der Puffer muss mindestens <see cref="ReportLineCapacity"/> Byte groß sein, sonst ArgumentException.
    /// </summary>
    public static int WriteReportLine(int id, ReadOnlySpan<byte> report, Span<byte> buffer)
    {
        if (buffer.Length < ReportLineCapacity(report.Length))
            throw new ArgumentException($"Puffer zu klein: {buffer.Length} statt {ReportLineCapacity(report.Length)} Byte.", nameof(buffer));
        ReadOnlySpan<byte> head = "{\"id\":"u8;
        ReadOnlySpan<byte> mid = ",\"command\":\"report\",\"report\":["u8;
        int pos = 0;
        head.CopyTo(buffer);
        pos += head.Length;
        pos += WriteInt(buffer[pos..], id);
        mid.CopyTo(buffer[pos..]);
        pos += mid.Length;
        for (int i = 0; i < report.Length; i++)
        {
            if (i > 0)
                buffer[pos++] = (byte)',';
            pos += WriteInt(buffer[pos..], report[i]);
        }
        buffer[pos++] = (byte)']';
        buffer[pos++] = (byte)'}';
        buffer[pos++] = (byte)'\n';
        return pos;
    }

    /// <summary>
    /// Puffergröße, die <see cref="WriteReportLine"/> höchstens braucht: Kopf und Ende (39 Byte), die längste ID
    /// („-2147483648“, 11 Byte) und je Berichtsbyte bis zu 3 Ziffern plus Komma.
    /// </summary>
    public static int ReportLineCapacity(int reportLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reportLength);
        return reportLength * 4 + 50;
    }

    /// <summary>Ganze Zahl als ASCII-Ziffern schreiben (wie StringBuilder.Append, ohne Speicherreservierung). Rückgabe: geschriebene Länge.</summary>
    private static int WriteInt(Span<byte> buffer, int value)
    {
        if (!Utf8Formatter.TryFormat(value, buffer, out int written))
            throw new ArgumentException("Puffer zu klein.", nameof(buffer));
        return written;
    }

    /// <summary>Virtuellen Xbox-Controller anmelden.</summary>
    public static string RegisterXbox(int id, string name) =>
        Register(id, name, XboxBtReport.VendorId, XboxBtReport.ProductId, "bluetooth", XboxBtReport.Descriptor);

    private static void AppendBytes(StringBuilder sb, ReadOnlySpan<byte> data)
    {
        sb.Append('[');
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(data[i]);
        }
        sb.Append(']');
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}