namespace Switch2Pro.Protocol;

/// <summary>Nachrichtenart zwischen N-Connect und einem Funkadapter (ESP32/nRF52840) über USB (siehe docs/ADAPTER-PROTOKOLL.md).</summary>
public enum AdapterMessage : byte
{
    // Adapter → PC
    /// <summary>Adapter meldet sich: Payload = Protokollversion (1 Byte) + Firmware-Text (UTF-8).</summary>
    Hello = 0x08,
    /// <summary>Controller verbunden: Slot in <c>slot</c>, Payload = PID (2 Byte LE) + Adresse (6 Byte, höchstes Byte zuerst).</summary>
    Connected = 0x10,
    /// <summary>Controller getrennt: <c>slot</c>.</summary>
    Disconnected = 0x11,
    /// <summary>Eingabebericht: <c>slot</c>, Payload = roher Switch-2-Bericht 0x05 (wie über BLE, ohne Report-ID).</summary>
    Input = 0x12,
    /// <summary>Klartext-Protokollzeile des Adapters (UTF-8) – nur zur Fehlersuche.</summary>
    Log = 0x1F,

    // PC → Adapter
    /// <summary>Host-Adresse setzen und Suche starten: Payload = Adresse (6 Byte, niedrigstes Byte zuerst).</summary>
    SetHost = 0x01,
    /// <summary>Lebenszeichen anfordern (Adapter antwortet mit <see cref="Hello"/>).</summary>
    Ping = 0x02,
    /// <summary>Vibration: <c>slot</c>, Payload = großer Motor, kleiner Motor (je 1 Byte 0–255).</summary>
    Rumble = 0x20,
    /// <summary>Spieler-LED: <c>slot</c>, Payload = Muster (1 Byte, 1–8).</summary>
    PlayerLed = 0x21,
}

/// <summary>Ein entschlüsselter Rahmen: Art, Slot (0–7) und Nutzdaten.</summary>
public readonly record struct AdapterFrame(AdapterMessage Type, byte Slot, byte[] Payload);

/// <summary>
/// Rahmenformat für die USB-Verbindung zum Funkadapter. SLIP-Rahmung (RFC 1055: END 0xC0, ESC 0xDB) begrenzt jeden
/// Rahmen; darin stehen Art, Slot, Nutzdaten und eine CRC16-CCITT über (Art | Slot | Nutzdaten). Einfach in C und in
/// C# gleich umzusetzen; defekte Rahmen werden verworfen, die Verbindung synchronisiert sich am nächsten 0xC0 neu.
/// </summary>
public static class AdapterProtocol
{
    public const byte ProtocolVersion = 1;
    private const byte End = 0xC0;
    private const byte Esc = 0xDB;
    private const byte EscEnd = 0xDC;
    private const byte EscEsc = 0xDD;

    /// <summary>CRC16-CCITT (Polynom 0x1021, Startwert 0xFFFF) – wie in vielen Mikrocontroller-Bibliotheken.</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }

    /// <summary>Einen Rahmen bauen: Art | Slot | Nutzdaten | CRC16 (großes Byte zuerst), SLIP-kodiert mit 0xC0 am Ende.</summary>
    public static byte[] Encode(AdapterMessage type, byte slot, ReadOnlySpan<byte> payload = default)
    {
        Span<byte> body = payload.Length + 2 <= 256 ? stackalloc byte[payload.Length + 2] : new byte[payload.Length + 2];
        body[0] = (byte)type;
        body[1] = slot;
        payload.CopyTo(body[2..]);
        ushort crc = Crc16(body[..(payload.Length + 2)]);
        var packet = new byte[payload.Length + 4];
        body[..(payload.Length + 2)].CopyTo(packet);
        packet[^2] = (byte)(crc >> 8);
        packet[^1] = (byte)crc;

        var output = new List<byte>(packet.Length + 4) { End };
        foreach (byte b in packet)
        {
            switch (b)
            {
                case End: output.Add(Esc); output.Add(EscEnd); break;
                case Esc: output.Add(Esc); output.Add(EscEsc); break;
                default: output.Add(b); break;
            }
        }
        output.Add(End);
        return [.. output];
    }

    public static byte[] Encode(AdapterMessage type, byte slot, byte[] payload) => Encode(type, slot, payload.AsSpan());

    /// <summary>Host-Adresse für <see cref="AdapterMessage.SetHost"/>: 6 Byte, niedrigstes Byte zuerst (wie in der Werbung).</summary>
    public static byte[] SetHost(string hostAddress)
    {
        if (!BtAddress.TryNormalize(hostAddress, out var a))
            throw new ArgumentException($"Ungültige Adresse: {hostAddress}", nameof(hostAddress));
        byte[] le = a.Split(':').Select(p => Convert.ToByte(p, 16)).Reverse().ToArray();
        return Encode(AdapterMessage.SetHost, 0, le);
    }

    /// <summary>PID und Adresse aus einer <see cref="AdapterMessage.Connected"/>-Nachricht lesen.</summary>
    public static bool TryReadConnected(ReadOnlySpan<byte> payload, out ControllerKind kind, out string address)
    {
        kind = ControllerKind.Unknown;
        address = "";
        if (payload.Length < 8)
            return false;
        int pid = payload[0] | (payload[1] << 8);
        kind = ControllerKinds.FromSwitch2ProductId(pid);
        address = BtAddress.Format(payload.Slice(2, 6)); // höchstes Byte zuerst
        return true;
    }

    /// <summary>Nutzlast für <see cref="AdapterMessage.Connected"/> (für die Adapterseite / Tests).</summary>
    public static byte[] ConnectedPayload(int productId, string address)
    {
        byte[] addr = AddressBytesBigEndian(address);
        return [(byte)productId, (byte)(productId >> 8), .. addr];
    }

    private static byte[] AddressBytesBigEndian(string address)
    {
        if (!BtAddress.TryNormalize(address, out var a))
            throw new ArgumentException($"Ungültige Adresse: {address}", nameof(address));
        return a.Split(':').Select(p => Convert.ToByte(p, 16)).ToArray();
    }
}

/// <summary>
/// Liest den USB-Bytestrom vom Adapter und gibt vollständige, CRC-geprüfte Rahmen zurück. Zustandsbehaftet: Bytes
/// hineinfüttern (<see cref="Push"/>), fertige Rahmen kommen zurück. Defekte Rahmen (CRC falsch, zu kurz) werden
/// verworfen; ein unvollständiger Rahmen wartet auf weitere Bytes.
/// </summary>
public sealed class AdapterFrameReader
{
    private const byte End = 0xC0;
    private const byte Esc = 0xDB;
    private const byte EscEnd = 0xDC;
    private const byte EscEsc = 0xDD;
    private readonly List<byte> _buffer = new(128);
    private bool _escaped;
    private bool _overflow;
    private readonly int _maxFrame;

    /// <param name="maxFramePayload">Obergrenze für einen Rahmen (Schutz gegen Müll ohne 0xC0).</param>
    public AdapterFrameReader(int maxFramePayload = 512) => _maxFrame = maxFramePayload + 4;

    public IEnumerable<AdapterFrame> Push(ReadOnlySpan<byte> data)
    {
        var frames = new List<AdapterFrame>();
        foreach (byte b in data)
        {
            if (b == End)
            {
                if (!_overflow && _buffer.Count > 0 && TryFinish(out var frame))
                    frames.Add(frame);
                _buffer.Clear();
                _escaped = false;
                _overflow = false;
                continue;
            }
            if (_overflow)
                continue;
            byte value = b;
            if (_escaped)
            {
                value = b switch { EscEnd => End, EscEsc => Esc, _ => b };
                _escaped = false;
            }
            else if (b == Esc)
            {
                _escaped = true;
                continue;
            }
            _buffer.Add(value);
            if (_buffer.Count > _maxFrame)
                _overflow = true; // bis zum nächsten 0xC0 verwerfen
        }
        return frames;
    }

    private bool TryFinish(out AdapterFrame frame)
    {
        frame = default;
        int n = _buffer.Count;
        if (n < 4) // Art + Slot + CRC16
            return false;
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer);
        ushort crc = (ushort)((span[n - 2] << 8) | span[n - 1]);
        if (AdapterProtocol.Crc16(span[..(n - 2)]) != crc)
            return false;
        frame = new AdapterFrame((AdapterMessage)span[0], span[1], span[2..(n - 2)].ToArray());
        return true;
    }
}
