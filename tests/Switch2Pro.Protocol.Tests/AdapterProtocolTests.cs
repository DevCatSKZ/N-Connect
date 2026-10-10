using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>USB-Protokoll zum Funkadapter (ESP32/nRF52840): Rahmen bauen, lesen, CRC, SLIP-Escapes, Nutzlasten.</summary>
public class AdapterProtocolTests
{
    private static List<AdapterFrame> RoundTrip(params byte[][] frames)
    {
        var reader = new AdapterFrameReader();
        var all = new List<AdapterFrame>();
        foreach (var f in frames)
            all.AddRange(reader.Push(f));
        return all;
    }

    [Fact]
    public void Encode_Decode_EinRahmen()
    {
        byte[] wire = AdapterProtocol.Encode(AdapterMessage.Input, 3, [0x11, 0x22, 0x33]);
        var frames = RoundTrip(wire);
        var f = Assert.Single(frames);
        Assert.Equal(AdapterMessage.Input, f.Type);
        Assert.Equal(3, f.Slot);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33 }, f.Payload);
    }

    [Fact]
    public void Escapes_WennNutzdatenRahmenzeichenEnthalten()
    {
        // 0xC0 (END) und 0xDB (ESC) müssen maskiert und korrekt zurückgewandelt werden.
        byte[] payload = { 0xC0, 0xDB, 0xC0, 0x00, 0xDB };
        byte[] wire = AdapterProtocol.Encode(AdapterMessage.Rumble, 1, payload);
        Assert.DoesNotContain(wire[1..^1], b => b == 0xC0); // innen kein unmaskiertes END
        var f = Assert.Single(RoundTrip(wire));
        Assert.Equal(payload, f.Payload);
    }

    [Fact]
    public void ByteweiseGefuettert_ErgibtDenselbenRahmen()
    {
        byte[] wire = AdapterProtocol.Encode(AdapterMessage.Connected, 0, AdapterProtocol.ConnectedPayload(0x2069, "AA:BB:CC:DD:EE:FF"));
        var reader = new AdapterFrameReader();
        var frames = new List<AdapterFrame>();
        foreach (byte b in wire)
            frames.AddRange(reader.Push(new[] { b }));
        var f = Assert.Single(frames);
        Assert.True(AdapterProtocol.TryReadConnected(f.Payload, out var kind, out var addr));
        Assert.Equal(ControllerKind.Pro2, kind);
        Assert.Equal("AA:BB:CC:DD:EE:FF", addr);
    }

    [Fact]
    public void ZweiRahmenHintereinander()
    {
        var reader = new AdapterFrameReader();
        var bytes = new List<byte>();
        bytes.AddRange(AdapterProtocol.Encode(AdapterMessage.Disconnected, 2));
        bytes.AddRange(AdapterProtocol.Encode(AdapterMessage.Ping, 0));
        var frames = reader.Push(bytes.ToArray()).ToList();
        Assert.Equal(2, frames.Count);
        Assert.Equal(AdapterMessage.Disconnected, frames[0].Type);
        Assert.Equal(2, frames[0].Slot);
        Assert.Equal(AdapterMessage.Ping, frames[1].Type);
    }

    [Fact]
    public void DefekteCrc_WirdVerworfen_NaechsterRahmenKommtAn()
    {
        byte[] bad = AdapterProtocol.Encode(AdapterMessage.Input, 0, [1, 2, 3]);
        bad[4] ^= 0xFF; // ein Nutzdatenbyte verfälschen (Kopf 0xC0|Art|Slot|Daten…; 0x12,0x00,… werden nie maskiert)
        byte[] good = AdapterProtocol.Encode(AdapterMessage.Ping, 0);
        var frames = RoundTrip(bad, good);
        var f = Assert.Single(frames);
        Assert.Equal(AdapterMessage.Ping, f.Type);
    }

    [Fact]
    public void Muell_VorEinemGueltigenRahmen_Resynchronisiert()
    {
        var reader = new AdapterFrameReader();
        reader.Push(new byte[] { 0x11, 0x22, 0x33, 0x44 }); // kein 0xC0 – Müll
        var frames = reader.Push(AdapterProtocol.Encode(AdapterMessage.Hello, 0, [AdapterProtocol.ProtocolVersion])).ToList();
        var f = Assert.Single(frames);
        Assert.Equal(AdapterMessage.Hello, f.Type);
        Assert.Equal(AdapterProtocol.ProtocolVersion, f.Payload[0]);
    }

    [Fact]
    public void SetHost_SchreibtAdresseNiedrigstesByteZuerst()
    {
        byte[] wire = AdapterProtocol.SetHost("98:B6:E9:01:02:03");
        var f = Assert.Single(RoundTrip(wire));
        Assert.Equal(AdapterMessage.SetHost, f.Type);
        Assert.Equal(new byte[] { 0x03, 0x02, 0x01, 0xE9, 0xB6, 0x98 }, f.Payload);
    }

    [Fact]
    public void Crc16_CcittReferenzwert()
    {
        // „123456789“ → 0x29B1 (bekannter Prüfwert für CRC-16/CCITT-FALSE).
        Assert.Equal(0x29B1, AdapterProtocol.Crc16("123456789"u8));
    }

    [Fact]
    public void Ueberlanger_MuellBlock_SprengtNichtDenRahmen()
    {
        var reader = new AdapterFrameReader(maxFramePayload: 64);
        reader.Push(new byte[5000]); // viel Müll ohne 0xC0
        var frames = reader.Push(AdapterProtocol.Encode(AdapterMessage.Ping, 0)).ToList();
        Assert.Single(frames);
    }
}
