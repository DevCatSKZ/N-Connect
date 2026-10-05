using System.Text;
using Switch2Pro.Bridge.Usb;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge.Links;

/// <summary>
/// Sony DualShock 4 oder DualSense (auch Edge) über USB oder Bluetooth-Classic-HID.
/// Liest die Eingabeberichte (Buttons, Sticks, analoge Trigger, Gyro, Akku), schickt Vibration und
/// Lichtleiste/Spieler-LEDs zurück. Bei DualSense über Bluetooth wird mit dem ersten Ausgabebericht
/// der verbesserte Modus eingeschaltet (volle Berichte mit Gyro/Akku statt des 10-Byte-Grundberichts).
/// </summary>
internal sealed class PlayStationHidLink : IControllerLink
{
    private readonly HidChannel _hid;
    private readonly CancellationTokenSource _cts = new();
    private readonly RateMeter _rate = new();
    private readonly bool _bluetooth;
    private readonly object _effectGate = new();
    private int _closed, _lostRaised;

    private byte _rumbleSmall, _rumbleLarge;
    private (byte R, byte G, byte B) _lightbar;
    private int _playerIndex;
    private bool _firmwareHasImprovedRumble;

    public ControllerKind Kind { get; }
    public Transport Transport => _bluetooth ? Transport.Bluetooth : Transport.Usb;
    public string Id { get; }
    /// <summary>Kennung für Einstellungen je Controller: Bluetooth-Adresse bzw. USB-Instanz.</summary>
    public string? Address { get; }
    /// <summary>Spiele sehen den echten Controller sonst doppelt – per HidHide verstecken.</summary>
    public string? HidInstanceId { get; }
    /// <summary>Eigener Gerätename (DualSense Edge u. a.), sonst null = Anzeigename der Art.</summary>
    public string? ProductName { get; private set; }
    public DeviceCalibration Calibration => PlayStationPad.Calibration;
    public ControllerInfo Info { get; private set; } = new();
    public ControllerState? LastState { get; private set; }
    public double ReportRate => _rate.Rate;
    public bool IsLost => Volatile.Read(ref _lostRaised) == 1;

    /// <summary>DualSense über Bluetooth sendet volle Berichte erst nach dem ersten Ausgabebericht.</summary>
    public bool EnhancedMode { get; private set; }

    public event Action<IControllerLink, ControllerState>? StateReceived;
    public event Action<IControllerLink>? Lost;

    private PlayStationHidLink(string path, ControllerKind kind, HidChannel hid, bool bluetooth)
    {
        Id = path;
        Kind = kind;
        _hid = hid;
        _bluetooth = bluetooth;
        HidInstanceId = UsbNative.GetInstanceId(path);
        Address = bluetooth ? BluetoothAddress(path) : $"USB:{HidInstanceId ?? path}";
        _playerIndex = -1;
    }

    /// <summary>Bluetooth-Adresse des Controllers aus der Gerätehierarchie (BTH\DEV_AABBCCDDEEFF).</summary>
    private static string? BluetoothAddress(string path)
    {
        var id = UsbNative.GetInstanceId(path);
        for (int depth = 0; id is not null && depth < 5; depth++)
        {
            int dev = id.IndexOf(@"\DEV_", StringComparison.OrdinalIgnoreCase);
            if (id.StartsWith("BTH\\", StringComparison.OrdinalIgnoreCase) && dev > 0)
            {
                var mac = id[(dev + 5)..];
                return mac.Length == 12 ? string.Join(':', Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2))) : null;
            }
            id = UsbNative.GetParentInstanceId(id);
        }
        return null;
    }

    public static async Task<PlayStationHidLink> ConnectAsync(string path, ControllerKind kind, CancellationToken ct)
    {
        var hid = new HidChannel(path);
        try
        {
            bool bluetooth = PlayStationPad.IsBluetoothHidPath(path);
            var link = new PlayStationHidLink(path, kind, hid, bluetooth);
            string? product = hid.GetString(serial: false);
            link.ProductName = PlayStationPad.ProductName(path)
                ?? (product is { Length: > 0 } p && !p.StartsWith("HID#", StringComparison.Ordinal) ? p : null);

            // Seriennummer/Firmware per Feature-Bericht (USB: DS4 0x12, DS5 0x09/0x20; BT: nur DS5).
            if (!ct.IsCancellationRequested)
                link.ReadDetails(product);

            // DualSense über Bluetooth: ein erster Ausgabebericht schaltet die vollen Berichte ein
            // und setzt gleich Lichtleiste + Spieler-LEDs (SDL: LED-Reset beim Verbinden).
            if (bluetooth && kind == ControllerKind.DualSense)
            {
                var report = link.BuildEffect(0, 0);
                await hid.WriteAsync(report, ct);
            }

            link._readTask = Task.Run(() => link.ReadLoopAsync(link._cts.Token));
            Log.Info($"{link.ProductName ?? kind.DisplayName()} bereit ({(bluetooth ? "Bluetooth" : "USB")}, {path})");
            return link;
        }
        catch
        {
            hid.Dispose();
            throw;
        }
    }

    private Task? _readTask;

    /// <summary>Seriennummer (Bluetooth-Adresse) und Firmware per Feature-Bericht lesen.</summary>
    private void ReadDetails(string? product)
    {
        try
        {
            string? serial = null;
            string? firmware = null;
            if (Kind == ControllerKind.DualSense)
            {
                if (_hid.GetFeatureReport(0x09) is { } sn)
                    serial = PlayStationPad.DualSenseSerial(sn);
                if (_hid.GetFeatureReport(0x20) is { } fw && PlayStationPad.DualSenseFirmware(fw) is { } v)
                {
                    firmware = $"0x{v:X4} ({v >> 8}.{(v & 0xFF) >> 4:X1}{v & 0xF:X1})";
                    _firmwareHasImprovedRumble = v >= 0x0224;
                }
            }
            else
            {
                if (_hid.GetFeatureReport(0x12) is { } sn)
                    serial = PlayStationPad.DualShock4Serial(sn);
            }
            // Bluetooth: die Adresse steht oft schon im Pfad; serielle Kennung ggf. daraus.
            serial ??= _bluetooth ? Address : null;
            Info = new ControllerInfo { SerialNumber = serial, Firmware = firmware };
        }
        catch (Exception e)
        {
            Log.Info($"{Id}: Gerätedetails nicht lesbar ({Log.Reason(e)})");
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[Math.Max(78, _hid.InputLength)];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await _hid.ReadAsync(buffer, ct);
                if (n == 0)
                {
                    RaiseLost();
                    return;
                }
                OnReport(buffer, n);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            if (_closed == 0)
                Log.Info($"{ProductName ?? Kind.DisplayName()}: getrennt ({Log.Reason(e)})");
            RaiseLost();
        }
    }

    /// <summary>Einen empfangenen Eingabebericht zerlegen und melden (eigene Methode: ReadOnlySpan ist nicht async-fähig).</summary>
    private void OnReport(byte[] buffer, int length)
    {
        var report = buffer.AsSpan(0, length);
        bool ok = Kind == ControllerKind.DualSense
            ? PlayStationPad.TryParseDualSense(report, out var state)
            : PlayStationPad.TryParseDualShock4(report, out state);
        if (!ok)
            return;
        if (report[0] == 0x31)
            EnhancedMode = true;
        _rate.Tick();
        LastState = state;
        try
        {
            StateReceived?.Invoke(this, state);
        }
        catch (Exception e)
        {
            Log.Error($"{Id}: Eingabe verarbeiten", e);
        }
    }

    /// <summary>Ausgabebericht bauen (Vibration + Lichtleiste + Spieler-LEDs, je nach Art).</summary>
    private byte[] BuildEffect(byte small, byte large)
    {
        lock (_effectGate)
        {
            return Kind == ControllerKind.DualSense
                ? PlayStationPad.BuildDualSenseOutput(_bluetooth, small, large, _lightbar, _playerIndex, _firmwareHasImprovedRumble)
                : PlayStationPad.BuildDualShock4Output(_bluetooth, small, large, _lightbar.R, _lightbar.G, _lightbar.B);
        }
    }

    private void SendEffect()
    {
        if (_closed != 0)
            return;
        try
        {
            var report = BuildEffect(_rumbleSmall, _rumbleLarge);
            // Bluetooth-Stacks lehnen WriteFile für Ausgabeberichte teils ab → über den Steuerkanal.
            if (!_hid.WriteAsync(report, _cts.Token).Wait(500) && !_hid.SetOutputReport(report))
                Log.Warn($"{Id}: Ausgabebericht nicht gesendet");
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or AggregateException)
        {
            if (_closed == 0)
                Log.Warn($"{Id}: Ausgabebericht fehlgeschlagen ({Log.Reason(e)})");
        }
    }

    public void SetRumble(byte large, byte small, float strength)
    {
        lock (_effectGate)
        {
            _rumbleLarge = (byte)Math.Round(large * strength);
            _rumbleSmall = (byte)Math.Round(small * strength);
        }
        SendEffect();
    }

    /// <summary>Spielernummer → Lichtleiste (DualShock 4) bzw. Spieler-LEDs + Lichtleiste (DualSense).</summary>
    public Task SetPlayerAsync(int playerIndex)
    {
        lock (_effectGate)
        {
            _playerIndex = playerIndex;
            _lightbar = PlayStationPad.LightbarColor(playerIndex);
        }
        SendEffect();
        return Task.CompletedTask;
    }

    /// <summary>Lichtleiste, die ein Spiel gesetzt hat (über den virtuellen DualShock 4), an den echten weitergeben.</summary>
    public Task SetLightbarAsync(byte r, byte g, byte b)
    {
        lock (_effectGate)
            _lightbar = (r, g, b);
        SendEffect();
        return Task.CompletedTask;
    }

    private void RaiseLost()
    {
        if (Interlocked.Exchange(ref _lostRaised, 1) == 0)
            Lost?.Invoke(this);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return ValueTask.CompletedTask;
        _cts.Cancel();
        _hid.Dispose();
        return ValueTask.CompletedTask;
    }
}
