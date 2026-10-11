using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Switch2Pro.Protocol;

namespace NConnect.Linux.Output;

/// <summary>
/// Cemuhook-/DSU-Server (Port 26760) wie unter Windows: Emulatoren (Dolphin, Cemu, Citra/Lime3DS, Ryujinx-Nachfolger …)
/// holen sich Gyro, Beschleunigung und Tasten der Spieler 1–4. Standard nur lokal; mit <c>network</c> auch für andere
/// Geräte im Heimnetz (z. B. Cemu auf dem PC mit dem Controller am Pi).
/// </summary>
public sealed class DsuServer : IDisposable
{
    public static DsuServer? Instance { get; private set; }

    private readonly UdpClient _udp;
    private readonly uint _serverId = (uint)Random.Shared.Next();
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<IPEndPoint, Client> _clients = new();
    private readonly Slot?[] _slots = new Slot?[Dsu.MaxSlots];
    private readonly long _start = Stopwatch.GetTimestamp();

    public bool Network { get; }

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

    private DsuServer(UdpClient udp, bool network)
    {
        _udp = udp;
        Network = network;
        _ = Task.Run(ReceiveLoopAsync);
    }

    /// <summary>Server starten; null, wenn der Port schon belegt ist.</summary>
    public static DsuServer? Start(bool network)
    {
        try
        {
            var address = network ? IPAddress.Any : IPAddress.Loopback;
            var udp = new UdpClient(new IPEndPoint(address, Dsu.Port));
            Instance = new DsuServer(udp, network);
            Log.Info($"DSU-Server (Cemuhook) läuft auf {address}:{Dsu.Port}");
            return Instance;
        }
        catch (SocketException e)
        {
            Log.Warn($"DSU-Server nicht gestartet – Port {Dsu.Port} belegt? ({Log.Reason(e)})");
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
                Log.Warn($"DSU: Anfrage nicht verarbeitet: {Log.Reason(e)}");
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
                var client = _clients.GetOrAdd(from, _ => new Client());
                client.LastSeen = Environment.TickCount64;
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
        var s = _slots[slot] = Merge(_slots[slot], mac, p);
        if (_clients.IsEmpty)
            return;
        uint number = ++s.PacketNumber;
        ulong micros = (ulong)Stopwatch.GetElapsedTime(_start).TotalMicroseconds;
        byte[]? packet = null;
        long now = Environment.TickCount64;
        foreach (var (endpoint, client) in _clients)
        {
            if (now - client.LastSeen > 5000)
            {
                _clients.TryRemove(endpoint, out _); // Clients melden sich regelmäßig neu an
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

    public void Dispose()
    {
        _cts.Cancel();
        _udp.Dispose();
        if (ReferenceEquals(Instance, this))
            Instance = null;
        Log.Info("DSU-Server beendet");
    }
}
