using System.Buffers.Binary;

namespace Switch2Pro.Protocol;

/// <summary>
/// Cemuhook-/DSU-Protokoll (UDP, Port 26760): liefert Bewegungsdaten (Gyro, Beschleunigung) und Tasten an
/// Emulatoren wie Cemu, Dolphin, Citra und Yuzu-Nachfolger. Paketaufbau nach der offenen Protokollbeschreibung
/// (Header „DSUS“/„DSUC“, Version 1001, CRC32 über das ganze Paket mit genulltem CRC-Feld).
/// </summary>
public static class Dsu
{
    public const int Port = 26760;
    public const ushort ProtocolVersion = 1001;
    public const uint MsgVersion = 0x100000;
    public const uint MsgPortInfo = 0x100001;
    public const uint MsgPadData = 0x100002;
    public const int MaxSlots = 4;

    /// <summary>Nachrichtentyp einer Client-Anfrage, oder null, wenn kein gültiges „DSUC“-Paket.</summary>
    public static uint? ParseRequest(ReadOnlySpan<byte> p, out ReadOnlySpan<byte> body)
    {
        body = default;
        if (p.Length < 20 || p[0] != 'D' || p[1] != 'S' || p[2] != 'U' || p[3] != 'C')
            return null;
        if (BinaryPrimitives.ReadUInt16LittleEndian(p[4..]) > ProtocolVersion)
            return null;
        int length = BinaryPrimitives.ReadUInt16LittleEndian(p[6..]);
        if (length < 4 || p.Length < 16 + length) // Länge zählt ab der Client-ID, mindestens der Nachrichtentyp
            return null;
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(p[8..]);
        var copy = p[..(16 + length)].ToArray();
        copy[8] = copy[9] = copy[10] = copy[11] = 0;
        if (Crc32(copy) != crc)
            return null;
        body = p[20..(16 + length)];
        return BinaryPrimitives.ReadUInt32LittleEndian(p[16..]);
    }

    /// <summary>Slots, nach denen eine Port-Info-Anfrage fragt.</summary>
    public static IEnumerable<int> RequestedSlots(ReadOnlySpan<byte> body)
    {
        var slots = new List<int>();
        if (body.Length < 4)
            return slots;
        int count = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(body), 0, MaxSlots);
        for (int i = 0; i < count && 4 + i < body.Length; i++)
            slots.Add(body[4 + i]);
        return slots;
    }

    /// <summary>Vollständiges Server-Paket mit Kopf und CRC.</summary>
    private static byte[] Packet(uint serverId, uint message, ReadOnlySpan<byte> payload)
    {
        var p = new byte[20 + payload.Length];
        p[0] = (byte)'D'; p[1] = (byte)'S'; p[2] = (byte)'U'; p[3] = (byte)'S';
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4), ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6), (ushort)(p.Length - 16));
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(12), serverId);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(16), message);
        payload.CopyTo(p.AsSpan(20));
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8), Crc32(p));
        return p;
    }

    public static byte[] VersionResponse(uint serverId)
    {
        Span<byte> v = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(v, ProtocolVersion);
        return Packet(serverId, MsgVersion, v);
    }

    /// <summary>Beschreibung eines Slots (11 Byte): Slot, Zustand, Modell, Verbindung, MAC, Akku.</summary>
    private static void SharedInfo(Span<byte> s, int slot, bool connected, ulong mac, int batteryPercent, bool charging)
    {
        s[0] = (byte)slot;
        s[1] = connected ? (byte)2 : (byte)0;   // 2 = verbunden
        s[2] = connected ? (byte)2 : (byte)0;   // 2 = voller Gyro
        s[3] = connected ? (byte)2 : (byte)0;   // 2 = Bluetooth
        for (int i = 0; i < 6; i++)
            s[4 + i] = (byte)(mac >> (8 * (5 - i)));
        s[10] = !connected ? (byte)0
            : charging ? (byte)0xEE
            : batteryPercent < 0 ? (byte)0x05
            : batteryPercent <= 10 ? (byte)0x01 : batteryPercent <= 25 ? (byte)0x02
            : batteryPercent <= 60 ? (byte)0x03 : batteryPercent <= 90 ? (byte)0x04 : (byte)0x05;
    }

    public static byte[] PortInfo(uint serverId, int slot, bool connected, ulong mac, int batteryPercent, bool charging)
    {
        var payload = new byte[12];
        SharedInfo(payload, slot, connected, mac, batteryPercent, charging);
        return Packet(serverId, MsgPortInfo, payload);
    }

    /// <summary>
    /// Datenpaket eines Slots. Bewegungsdaten in den Achsen eines DualShock 4 (wie <see cref="Mapping.ToDs4Report"/>):
    /// Beschleunigung in g, Gyro in °/s (Nicken, Gieren, Rollen).
    /// </summary>
    public static byte[] PadData(uint serverId, int slot, ulong mac, uint packetNumber, GamepadState g, PadInput p, ulong timestampMicros)
    {
        var d = new byte[80];
        SharedInfo(d, slot, true, mac, p.BatteryPercent, p.Charging);
        d[11] = 1; // verbunden
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(12), packetNumber);

        bool On(XButtons b) => (g.Buttons & b) != 0;
        byte b1 = 0, b2 = 0;
        if (On(XButtons.Left)) b1 |= 0x80;
        if (On(XButtons.Down)) b1 |= 0x40;
        if (On(XButtons.Right)) b1 |= 0x20;
        if (On(XButtons.Up)) b1 |= 0x10;
        if (On(XButtons.Start)) b1 |= 0x08;
        if (On(XButtons.RS)) b1 |= 0x04;
        if (On(XButtons.LS)) b1 |= 0x02;
        if (On(XButtons.Back)) b1 |= 0x01;
        if (On(XButtons.X)) b2 |= 0x80;   // Viereck
        if (On(XButtons.A)) b2 |= 0x40;   // Kreuz
        if (On(XButtons.B)) b2 |= 0x20;   // Kreis
        if (On(XButtons.Y)) b2 |= 0x10;   // Dreieck
        if (On(XButtons.RB)) b2 |= 0x08;
        if (On(XButtons.LB)) b2 |= 0x04;
        if (g.RightTrigger > 0) b2 |= 0x02;
        if (g.LeftTrigger > 0) b2 |= 0x01;
        d[16] = b1;
        d[17] = b2;
        d[18] = On(XButtons.Guide) ? (byte)1 : (byte)0;
        d[19] = g.Touchpad ? (byte)1 : (byte)0;
        d[20] = Axis(g.LeftX, false);
        d[21] = Axis(g.LeftY, false);
        d[22] = Axis(g.RightX, false);
        d[23] = Axis(g.RightY, false);
        d[24] = On(XButtons.Left) ? (byte)255 : (byte)0;
        d[25] = On(XButtons.Down) ? (byte)255 : (byte)0;
        d[26] = On(XButtons.Right) ? (byte)255 : (byte)0;
        d[27] = On(XButtons.Up) ? (byte)255 : (byte)0;
        d[28] = On(XButtons.X) ? (byte)255 : (byte)0;
        d[29] = On(XButtons.A) ? (byte)255 : (byte)0;
        d[30] = On(XButtons.B) ? (byte)255 : (byte)0;
        d[31] = On(XButtons.Y) ? (byte)255 : (byte)0;
        d[32] = On(XButtons.RB) ? (byte)255 : (byte)0;
        d[33] = On(XButtons.LB) ? (byte)255 : (byte)0;
        d[34] = g.RightTrigger;
        d[35] = g.LeftTrigger;
        // 36–47: zwei Touchpunkte, ungenutzt
        BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(48), timestampMicros);
        if (p.Motion is { } m)
        {
            const float gyroDps = 2000f / 32767f;
            const float accelG = 1f / 4096f;
            Float(d, 56, m.AccelX * accelG);
            Float(d, 60, m.AccelZ * accelG);
            Float(d, 64, -m.AccelY * accelG);
            Float(d, 68, m.GyroX * gyroDps);
            Float(d, 72, m.GyroZ * gyroDps);
            Float(d, 76, -m.GyroY * gyroDps);
        }
        return Packet(serverId, MsgPadData, d);
    }

    /// <summary>CRC-32 (IEEE 802.3, wie zlib), über das ganze Paket mit genulltem CRC-Feld.</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>DSU-Sticks: 0–255, 128 = Mitte, Y nach oben positiv (wie Cemuhook/DS4Windows).</summary>
    private static byte Axis(short v, bool invert)
    {
        int b = ((invert ? -v : v) + 32768) >> 8;
        return (byte)Math.Clamp(b, 0, 255);
    }

    private static void Float(byte[] d, int offset, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(offset), value);
}
