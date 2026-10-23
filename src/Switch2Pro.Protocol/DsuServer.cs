using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Switch2Pro.Protocol;

/// <summary>
/// Cemuhook-/DSU-Server (UDP, Port 26760) für Windows, Android und Linux: Emulatoren (Cemu, Dolphin, Citra/Lime3DS,
/// Yuzu-/Ryujinx-Nachfolger …) holen sich hier Gyro, Beschleunigung und Tasten der Spieler 1–4. Standard nur lokal
/// (127.0.0.1); mit <c>network</c> auch für andere Geräte im Heimnetz (z. B. Cemu auf dem PC mit dem Controller am Pi).
/// Die Pakete baut <see cref="Dsu"/>; protokolliert wird über die Rückrufe der App.
/// </summary>
public sealed class DsuServer : IDisposable
{
    /// <summary>Der Server der App (über <see cref="Start(bool, Action{string}, Action{string})"/>), sonst null.</summary>
    public static DsuServer? Instance { get; private set; }

    private static readonly object Gate = new();
    /// <summary>Clients melden sich regelmäßig neu an; wer so lange schweigt, ist weg.</summary>
    private const long ClientTimeoutMs = 5000;

    private readonly UdpClient _udp;
    private readonly Action<string> _info;
    private readonly Action<string> _warn;
    private readonly uint _serverId = (uint)Random.Shared.Next();
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<IPEndPoint, Client> _clients = new();
    private readonly Slot?[] _slots = new Slot?[Dsu.MaxSlots];
    private readonly long _start = Stopwatch.GetTimestamp();
    private int _disposed;

    /// <summary>true: lauscht im Heimnetz (alle Adressen), false: nur auf 127.0.0.1.</summary>
    public bool Network { get; }

    /// <summary>Tatsächlich belegter Port (sonst <see cref="Dsu.Port"/>, in Tests ein freier).</summary>
    public int Port { get; }

    private sealed class Client
    {
        public long LastSeen;
        public bool AllSlots;
        public readonly HashSet<int> Slots = [];
    }

    private sealed record Slot(ulong Mac, int Battery, bool Charging)
    {
        public uint PacketNumber;
    }

    private DsuServer(UdpClient udp, bool network, Action<string> info, Action<string> warn)
    {
        _udp = udp;
        _info = info;
        _warn = warn;
        Network = network;
        Port = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        _ = Task.Run(ReceiveLoopAsync);
    }

    /// <summary>
    /// Server der App starten und als <see cref="Instance"/> merken; läuft er schon mit derselben Einstellung, bleibt
    /// es bei ihm. null, wenn der Port belegt ist (z. B. DS4Windows oder ein SKZ Connect, das sich gerade beendet) –
    /// ein späterer Aufruf versucht es erneut.
    /// </summary>
    public static DsuServer? Start(bool network, Action<string> info, Action<string> warn) =>
        Start(Dsu.Port, network, info, warn);

    internal static DsuServer? Start(int port, bool network, Action<string> info, Action<string> warn)
    {
        lock (Gate)
        {
            if (Instance is { } running)
            {
                if (running.Network == network)
                    return running;
                running.Dispose();
            }
            return Instance = Open(port, network, info, warn);
        }
    }

    /// <summary>Server öffnen, ohne ihn als <see cref="Instance"/> zu merken (Tests); null, wenn der Port belegt ist.</summary>
    internal static DsuServer? Open(int port, bool network, Action<string> info, Action<string> warn)
    {
        var address = network ? IPAddress.Any : IPAddress.Loopback;
        UdpClient? udp = null;
        try
        {
            udp = new UdpClient(new IPEndPoint(address, port));
            if (OperatingSystem.IsWindows())
            {
                // ICMP „Port nicht erreichbar“ von beendeten Clients nicht als Empfangsfehler melden.
                const int SIO_UDP_CONNRESET = -1744830452;
                udp.Client.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null);
            }
            var server = new DsuServer(udp, network, info, warn);
            info($"DSU-Server (Cemuhook) läuft auf {address}:{server.Port}");
            return server;
        }
        catch (SocketException e)
        {
            udp?.Dispose();
            warn($"DSU-Server nicht gestartet – Port {port} belegt? ({Reason(e)}, {e.SocketErrorCode})");
            return null;
        }
    }

    private async Task ReceiveLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await _udp.ReceiveAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                if (_cts.IsCancellationRequested)
                    return;
                continue;
            }
            try
            {
                Handle(received.Buffer, received.RemoteEndPoint);
            }
            catch (Exception e)
            {
                _warn($"DSU: Anfrage nicht verarbeitet: {Reason(e)}");
            }
        }
    }

    private void Handle(byte[] packet, IPEndPoint from)
    {
        var type = Dsu.ParseRequest(packet, out var body);
        switch (type)
        {
            case Dsu.MsgVersion:
                Send(Dsu.VersionResponse(_serverId), from);
                break;
            case Dsu.MsgPortInfo:
                foreach (int slot in Dsu.RequestedSlots(body))
                {
                    if (slot is < 0 or >= Dsu.MaxSlots)
                        continue;
                    var s = _slots[slot];
                    Send(Dsu.PortInfo(_serverId, slot, s is not null, s?.Mac ?? 0, s?.Battery ?? -1, s?.Charging ?? false), from);
                }
                break;
            case Dsu.MsgPadData:
                long now = Environment.TickCount64;
                if (!_clients.ContainsKey(from))
                    RemoveStale(now); // im Heimnetz nicht unbegrenzt Absender sammeln, auch ohne Controller
                var client = _clients.GetOrAdd(from, _ => new Client());
                client.LastSeen = now;
                // Anmeldung: 0 = alle Slots, 1 = ein bestimmter Slot, 2 = per MAC (wir liefern dann alle)
                byte flags = body.Length > 0 ? body[0] : (byte)0;
                if (flags == 1 && body.Length > 1)
                    lock (client.Slots) client.Slots.Add(body[1]);
                else
                    client.AllSlots = true;
                break;
        }
    }

    /// <summary>Neue Eingabe eines Spielers verteilen (nur Spieler 1–4).</summary>
    public void Publish(int slot, ulong mac, GamepadState g, PadInput p)
    {
        if (slot is < 0 or >= Dsu.MaxSlots)
            return;
        // Auch ohne Clients merken: ein Emulator, der später fragt, sieht den Slot sofort als belegt.
        var s = _slots[slot] = Merge(_slots[slot], mac, p);
        if (_clients.IsEmpty)
            return;
        uint number = ++s.PacketNumber;
        ulong micros = (ulong)Stopwatch.GetElapsedTime(_start).TotalMicroseconds;
        byte[]? packet = null;
        long now = Environment.TickCount64;
        foreach (var (endpoint, client) in _clients)
        {
            if (now - client.LastSeen > ClientTimeoutMs)
            {
                _clients.TryRemove(endpoint, out _);
                continue;
            }
            bool wanted;
            lock (client.Slots) wanted = client.AllSlots || client.Slots.Contains(slot);
            if (!wanted)
                continue;
            packet ??= Dsu.PadData(_serverId, slot, mac, number, g, p, micros);
            Send(packet, endpoint);
        }
    }

    private static Slot Merge(Slot? previous, ulong mac, PadInput p) =>
        previous is not null && previous.Mac == mac && previous.Battery == p.BatteryPercent && previous.Charging == p.Charging
            ? previous
            : new Slot(mac, p.BatteryPercent, p.Charging) { PacketNumber = previous?.PacketNumber ?? 0 };

    /// <summary>Spieler getrennt: Slot als frei melden.</summary>
    public void Clear(int slot)
    {
        if (slot is >= 0 and < Dsu.MaxSlots)
            _slots[slot] = null;
    }

    private void RemoveStale(long now)
    {
        foreach (var (endpoint, client) in _clients)
            if (now - client.LastSeen > ClientTimeoutMs)
                _clients.TryRemove(endpoint, out _);
    }

    private void Send(byte[] packet, IPEndPoint to)
    {
        try
        {
            _udp.Send(packet, packet.Length, to);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            // Client weg – wird nach 5 s ohne Anmeldung entfernt.
        }
    }

    private static string Reason(Exception e) =>
        string.IsNullOrWhiteSpace(e.Message) ? $"{e.GetType().Name} 0x{e.HResult:X8}" : e.Message.Trim();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _cts.Cancel();
        _udp.Dispose();
        lock (Gate)
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }
        _info("DSU-Server beendet");
    }
}
