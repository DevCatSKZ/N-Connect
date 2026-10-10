using System.IO.Ports;
using Switch2Pro.Bridge.Links;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Verbindung zu einem Funkadapter (ESP32/nRF52840) über USB-CDC (siehe docs/ADAPTER-PROTOKOLL.md). Öffnet den
/// COM-Port, erkennt den Adapter am <see cref="AdapterMessage.Hello"/>, macht aus jeder <c>Connected</c>-Meldung einen
/// <see cref="AdapterLink"/> (den der <see cref="ControllerManager"/> wie einen normalen Controller einhängt) und
/// reicht jeden <c>Input</c>-Bericht an den passenden Link weiter. Vibration und Spieler-LED gehen als Rahmen zurück.
/// Die Rahmenauswertung (<see cref="Dispatch"/>) ist von der seriellen Ein-/Ausgabe getrennt und wird eigens getestet.
/// </summary>
internal sealed class AdapterConnection : IDisposable
{
    private readonly object _writeGate = new();
    private readonly Dictionary<byte, AdapterLink> _slots = [];
    private readonly AdapterFrameReader _reader = new(maxFramePayload: 128);
    private SerialPort? _port;
    private CancellationTokenSource? _cts;
    private volatile bool _helloSeen;

    /// <summary>Ein Controller am Adapter ist verbunden – der Manager hängt ihn wie gewohnt ein.</summary>
    public event Action<AdapterLink>? ControllerArrived;

    /// <summary>Name des gefundenen Adapters (aus <see cref="AdapterMessage.Hello"/>), falls verbunden.</summary>
    public string? AdapterName { get; private set; }

    public bool IsConnected => _port is { IsOpen: true } && _helloSeen;

    /// <summary>
    /// Adapter suchen und öffnen. <paramref name="preferredPort"/> (z. B. "COM15") zuerst, sonst alle Ports der Reihe
    /// nach: Ping senden und auf Hello warten. Gibt den gefundenen Portnamen zurück oder null.
    /// </summary>
    public async Task<string?> StartAsync(string? preferredPort, CancellationToken ct)
    {
        var ports = SerialPort.GetPortNames().Distinct().ToList();
        if (!string.IsNullOrWhiteSpace(preferredPort))
            ports = ports.OrderByDescending(p => string.Equals(p, preferredPort, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var name in ports)
        {
            if (ct.IsCancellationRequested)
                return null;
            if (await TryOpenAsync(name, ct))
                return name;
        }
        return null;
    }

    private async Task<bool> TryOpenAsync(string name, CancellationToken ct)
    {
        SerialPort? port = null;
        try
        {
            port = new SerialPort(name, 115200) { ReadTimeout = 200, WriteTimeout = 500, DtrEnable = true, RtsEnable = true };
            port.Open();
            _port = port;
            _helloSeen = false;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _cts.Token;
            _ = Task.Run(() => ReadLoop(port, token), token);
            // Adapter zur Begrüßung auffordern und kurz auf Hello warten.
            for (int i = 0; i < 10 && !_helloSeen && !token.IsCancellationRequested; i++)
            {
                Send(AdapterProtocol.Encode(AdapterMessage.Ping, 0));
                await Task.Delay(100, token);
            }
            if (_helloSeen)
            {
                Log.Info($"Funkadapter an {name}: {AdapterName ?? "?"}");
                return true;
            }
            Stop();
            return false;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            try { port?.Dispose(); } catch { /* egal */ }
            if (ReferenceEquals(_port, port))
                _port = null;
            return false;
        }
    }

    private void ReadLoop(SerialPort port, CancellationToken ct)
    {
        var buffer = new byte[256];
        try
        {
            while (!ct.IsCancellationRequested && port.IsOpen)
            {
                int read;
                try { read = port.Read(buffer, 0, buffer.Length); }
                catch (TimeoutException) { continue; }
                if (read <= 0)
                    continue;
                foreach (var frame in _reader.Push(buffer.AsSpan(0, read)))
                    Dispatch(frame);
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException or UnauthorizedAccessException)
        {
            if (!ct.IsCancellationRequested)
                Log.Warn($"Funkadapter getrennt: {Log.Reason(e)}");
        }
        // Port weg: alle Controller als getrennt melden.
        lock (_slots)
        {
            foreach (var link in _slots.Values)
                link.MarkLost();
            _slots.Clear();
        }
        _helloSeen = false;
    }

    /// <summary>Einen empfangenen Rahmen auswerten (ohne serielle E/A – eigens getestet).</summary>
    internal void Dispatch(AdapterFrame frame)
    {
        switch (frame.Type)
        {
            case AdapterMessage.Hello:
                AdapterName = frame.Payload.Length > 1
                    ? System.Text.Encoding.UTF8.GetString(frame.Payload, 1, frame.Payload.Length - 1)
                    : "Funkadapter";
                _helloSeen = true;
                break;
            case AdapterMessage.Connected when AdapterProtocol.TryReadConnected(frame.Payload, out var kind, out var address) && kind != ControllerKind.Unknown:
                AdapterLink link;
                lock (_slots)
                {
                    if (_slots.Remove(frame.Slot, out var old))
                        old.MarkLost();
                    link = new AdapterLink(frame.Slot, kind, address, Send);
                    _slots[frame.Slot] = link;
                }
                ControllerArrived?.Invoke(link);
                break;
            case AdapterMessage.Input:
                AdapterLink? target;
                lock (_slots)
                    _slots.TryGetValue(frame.Slot, out target);
                target?.Feed(frame.Payload);
                break;
            case AdapterMessage.Disconnected:
                lock (_slots)
                    if (_slots.Remove(frame.Slot, out var gone))
                        gone.MarkLost();
                break;
            case AdapterMessage.Log:
                Log.Info($"Adapter: {System.Text.Encoding.UTF8.GetString(frame.Payload)}");
                break;
        }
    }

    private void Send(byte[] frame)
    {
        lock (_writeGate)
        {
            if (_port is { IsOpen: true } port)
                port.Write(frame, 0, frame.Length);
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* egal */ }
        lock (_writeGate)
        {
            try { _port?.Close(); } catch { /* egal */ }
            _port?.Dispose();
            _port = null;
        }
        lock (_slots)
        {
            foreach (var link in _slots.Values)
                link.MarkLost();
            _slots.Clear();
        }
        _helloSeen = false;
    }

    public void Dispose() => Stop();
}
