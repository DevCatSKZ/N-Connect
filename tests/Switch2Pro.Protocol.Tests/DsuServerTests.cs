using System.Net;
using System.Net.Sockets;
using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>
/// Der gemeinsame DSU-Server über echtes UDP auf 127.0.0.1 – mit freiem Port, damit ein laufendes SKZ Connect (26760)
/// nicht stört. Antworten werden mit kurzer Frist erwartet; „keine Antwort“ zeigt sich daran, dass die nächste
/// Antwort schon die auf eine spätere Anfrage ist (der Server arbeitet Anfragen der Reihe nach ab).
/// </summary>
public sealed class DsuServerTests : IDisposable
{
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(2);
    private const ulong Mac = 0xAABBCCDDEEFF;

    private readonly List<string> _log = [];
    private readonly UdpClient _client = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly DsuServer _server;

    public DsuServerTests() =>
        _server = DsuServer.Open(0, network: false, Info, Warn) ?? throw new InvalidOperationException("DSU-Server nicht gestartet");

    public void Dispose()
    {
        _server.Dispose();
        _client.Dispose();
    }

    private void Info(string message) { lock (_log) _log.Add("I " + message); }
    private void Warn(string message) { lock (_log) _log.Add("W " + message); }
    private string[] Logged() { lock (_log) return [.. _log]; }

    private static byte[] Request(uint message, byte[] payload, string magic = "DSUC", ushort version = Dsu.ProtocolVersion)
    {
        var p = new byte[20 + payload.Length];
        System.Text.Encoding.ASCII.GetBytes(magic).CopyTo(p, 0);
        BitConverter.GetBytes(version).CopyTo(p, 4);
        BitConverter.GetBytes((ushort)(4 + payload.Length)).CopyTo(p, 6);
        BitConverter.GetBytes(0x12345678u).CopyTo(p, 12); // Client-ID
        BitConverter.GetBytes(message).CopyTo(p, 16);
        payload.CopyTo(p, 20);
        BitConverter.GetBytes(Dsu.Crc32(p)).CopyTo(p, 8);
        return p;
    }

    private static byte[] PortInfoRequest(params byte[] slots) =>
        Request(Dsu.MsgPortInfo, [.. BitConverter.GetBytes(slots.Length), .. slots]);

    /// <summary>Pad-Daten anfordern: Flags (0 = alle Slots, 1 = ein Slot), Slot, MAC.</summary>
    private static byte[] PadDataRequest(byte flags, byte slot = 0) => Request(Dsu.MsgPadData, [flags, slot, 0, 0, 0, 0, 0, 0]);

    private async Task SendAsync(byte[] packet) =>
        await _client.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, _server.Port));

    private async Task<byte[]> ReceiveAsync()
    {
        using var cts = new CancellationTokenSource(ReplyTimeout);
        var received = await _client.ReceiveAsync(cts.Token);
        Assert.Equal(_server.Port, received.RemoteEndPoint.Port);
        return received.Buffer;
    }

    /// <summary>Server-Paket prüfen (Kopf „DSUS“, Länge, Prüfsumme) und seinen Nachrichtentyp liefern.</summary>
    private static uint Check(byte[] p)
    {
        Assert.True(p.Length >= 20);
        Assert.Equal("DSUS"u8.ToArray(), p[..4]);
        Assert.Equal(p.Length - 16, BitConverter.ToUInt16(p, 6));
        var copy = (byte[])p.Clone();
        copy[8] = copy[9] = copy[10] = copy[11] = 0;
        Assert.Equal(BitConverter.ToUInt32(p, 8), Dsu.Crc32(copy));
        return BitConverter.ToUInt32(p, 16);
    }

    /// <summary>Version anfragen und auf die Antwort warten: danach hat der Server alles davor Gesendete verarbeitet.</summary>
    private async Task<byte[]> VersionAsync()
    {
        await SendAsync(Request(Dsu.MsgVersion, []));
        var reply = await ReceiveAsync();
        Assert.Equal(Dsu.MsgVersion, Check(reply));
        return reply;
    }

    private static (GamepadState G, PadInput P) Input(int battery = 80) =>
        (new GamepadState(XButtons.A, 0, 0, 0, 0, 0, 0, false),
         new PadInput { Motion = new Motion(0, 0, 4096, 32767, 0, 0), BatteryPercent = battery });

    [Fact]
    public async Task Version_und_Port_Info()
    {
        var version = await VersionAsync();
        Assert.Equal(Dsu.ProtocolVersion, BitConverter.ToUInt16(version, 20));
        uint serverId = BitConverter.ToUInt32(version, 12);

        // Noch kein Spieler: beide Slots frei
        await SendAsync(PortInfoRequest(0, 1));
        foreach (int slot in new[] { 0, 1 })
        {
            var info = await ReceiveAsync();
            Assert.Equal(Dsu.MsgPortInfo, Check(info));
            Assert.Equal(serverId, BitConverter.ToUInt32(info, 12));
            Assert.Equal(slot, info[20]);
            Assert.Equal(0, info[21]);                     // nicht verbunden
        }

        // Eingabe ohne angemeldeten Client: der Slot wird trotzdem gemerkt
        var (g, p) = Input();
        _server.Publish(1, Mac, g, p);
        await SendAsync(PortInfoRequest(1));
        var connected = await ReceiveAsync();
        Assert.Equal(Dsu.MsgPortInfo, Check(connected));
        Assert.Equal(1, connected[20]);
        Assert.Equal(2, connected[21]);                    // verbunden
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }, connected[24..30]);
        Assert.Equal(0x04, connected[30]);                 // Akku 80 % → „hoch“

        _server.Clear(1);
        await SendAsync(PortInfoRequest(1));
        var cleared = await ReceiveAsync();
        Assert.Equal(0, cleared[21]);
    }

    [Fact]
    public async Task Falsche_Pakete_bleiben_ohne_Antwort()
    {
        var wrongCrc = Request(Dsu.MsgVersion, []);
        wrongCrc[8] ^= 0x01;
        byte[][] bad =
        [
            [],
            [1, 2, 3],
            wrongCrc,
            Request(Dsu.MsgVersion, [], magic: "DSUS"),                         // Server-Paket (eigenes Echo)
            Request(Dsu.MsgVersion, [], version: Dsu.ProtocolVersion + 1),      // neuere Protokollversion
            Request(0x100003, []),                                              // unbekannter Nachrichtentyp
            PortInfoRequest(4, 7, 200),                                         // nur ungültige Slots
            Request(Dsu.MsgPortInfo, [0, 0, 0, 0]),                             // null Slots
        ];
        foreach (var packet in bad)
            await SendAsync(packet);

        await VersionAsync();                              // erste Antwort ist schon die auf die Versionsanfrage
        Assert.DoesNotContain(Logged(), l => l.StartsWith("W ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pad_Daten_nach_Anmeldung_fuer_alle_Slots()
    {
        var (g, p) = Input();
        _server.Publish(0, Mac, g, p);                     // noch niemand angemeldet: nichts senden
        await SendAsync(PadDataRequest(flags: 0));
        await VersionAsync();                              // Anmeldung verarbeitet, vorher kam nichts an

        _server.Publish(0, Mac, g, p);
        var first = await ReceiveAsync();
        Assert.Equal(Dsu.MsgPadData, Check(first));
        Assert.Equal(100, first.Length);
        Assert.Equal(0, first[20]);                        // Slot
        Assert.Equal(1, first[31]);                        // verbunden
        Assert.Equal(0x40, first[37] & 0x40);              // Kreuz (A)
        Assert.Equal(1f, BitConverter.ToSingle(first, 80), 3); // Beschleunigung Y = 1 g
        uint number = BitConverter.ToUInt32(first, 32);

        _server.Publish(0, Mac, g, p);
        var second = await ReceiveAsync();
        Assert.Equal(number + 1, BitConverter.ToUInt32(second, 32)); // fortlaufende Paketnummer
    }

    [Fact]
    public async Task Anmeldung_fuer_einen_Slot()
    {
        var (g, p) = Input();
        await SendAsync(PadDataRequest(flags: 1, slot: 2));
        await VersionAsync();

        _server.Publish(-1, Mac, g, p);                    // ungültige Slots: ignoriert
        _server.Publish(Dsu.MaxSlots, Mac, g, p);
        _server.Publish(0, Mac, g, p);                     // nicht angefordert
        _server.Publish(2, Mac, g, p);
        var data = await ReceiveAsync();
        Assert.Equal(Dsu.MsgPadData, Check(data));
        Assert.Equal(2, data[20]);
    }

    [Fact]
    public async Task Zweiter_Server_auf_demselben_Port_meldet_belegt()
    {
        var warnings = new List<string>();
        Assert.Null(DsuServer.Open(_server.Port, network: false, _ => { }, warnings.Add));
        Assert.Contains(warnings, w => w.Contains("belegt", StringComparison.Ordinal) && w.Contains(_server.Port.ToString(), StringComparison.Ordinal));
        await VersionAsync();                              // der erste läuft weiter
    }

    [Fact]
    public void Start_nach_belegtem_Port_erneut_und_nach_Beenden_wieder()
    {
        Assert.Null(DsuServer.Instance);
        int port;
        using (var blocker = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            port = ((IPEndPoint)blocker.Client.LocalEndPoint!).Port;
            Assert.Null(DsuServer.Start(port, network: false, Info, Warn)); // belegt, z. B. altes SKZ Connect beendet sich noch
            Assert.Null(DsuServer.Instance);
        }
        var started = DsuServer.Start(port, network: false, Info, Warn);   // später erneut: jetzt frei
        try
        {
            Assert.NotNull(started);
            Assert.Same(started, DsuServer.Instance);
            Assert.Equal(port, started.Port);
            Assert.False(started.Network);
            Assert.Same(started, DsuServer.Start(port, network: false, Info, Warn)); // läuft schon: derselbe
        }
        finally
        {
            started?.Dispose();
            started?.Dispose();                            // zweimal beenden schadet nicht
        }
        Assert.Null(DsuServer.Instance);
        Assert.Single(Logged(), l => l == "I DSU-Server beendet");

        using var again = DsuServer.Start(port, network: false, Info, Warn);
        Assert.NotNull(again);
        Assert.Same(again, DsuServer.Instance);
        again.Dispose();
        Assert.Null(DsuServer.Instance);
    }
}
