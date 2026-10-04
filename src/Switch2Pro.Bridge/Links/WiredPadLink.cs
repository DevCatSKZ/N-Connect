using Switch2Pro.Bridge.Usb;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge.Links;

/// <summary>
/// Lizenziertes Kabel-Gamepad für die Switch (HORI, PowerA, PDP) über USB-HID: nur Eingaben (kein Gyro, keine
/// Vibration, keine Spieler-LEDs). Erscheint wie ein Switch Pro Controller (gleiche Tasten, Grafik und Belegung),
/// aber unter seinem eigenen Namen. Format siehe <see cref="WiredSwitchPad"/>.
/// </summary>
internal sealed class WiredPadLink : IControllerLink
{
    private readonly HidChannel _hid;
    private readonly CancellationTokenSource _cts = new();
    private readonly RateMeter _rate = new();
    private int _closed, _lostRaised;

    public ControllerKind Kind => ControllerKind.Pro1;
    public Transport Transport => Transport.Usb;
    public string Id { get; }
    /// <summary>Kennung für Einstellungen je Controller (Name, Spielerplatz, Stick-Kalibrierung): USB-Instanz-ID bzw. Pfad.</summary>
    public string? Address { get; }
    public string? ProductName { get; }
    /// <summary>Spiele sehen das Gamepad zusätzlich direkt – per HidHide verstecken (Extras „Doppelt angezeigt?“).</summary>
    public string? HidInstanceId { get; }
    public DeviceCalibration Calibration => WiredSwitchPad.Calibration;
    public ControllerInfo Info { get; } = new();
    public ControllerState? LastState { get; private set; }
    public double ReportRate => _rate.Rate;
    public bool IsLost => Volatile.Read(ref _lostRaised) == 1;

    public event Action<IControllerLink, ControllerState>? StateReceived;
    public event Action<IControllerLink>? Lost;

    private WiredPadLink(string path, string name, HidChannel hid)
    {
        Id = path;
        HidInstanceId = UsbNative.GetInstanceId(path);
        Address = $"USB:{HidInstanceId ?? path}";
        ProductName = name;
        _hid = hid;
    }

    public static Task<WiredPadLink> OpenAsync(string path, string name)
    {
        var link = new WiredPadLink(path, name, new HidChannel(path));
        Task.Run(() => link.ReadLoopAsync(link._cts.Token)).Forget($"{name}: Eingaben");
        Log.Info($"{name} per USB bereit ({path})");
        return Task.FromResult(link);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[Math.Max(64, _hid.InputLength)];
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
                // Windows stellt die Report-ID voran (0 = Gerät ohne Report-IDs).
                if (n < 8 || !WiredSwitchPad.TryParse(buffer.AsSpan(1, n - 1), out var state))
                    continue;
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
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException e)
        {
            Log.Info($"{ProductName}: USB getrennt ({Log.Reason(e)})");
            RaiseLost();
        }
    }

    public void SetRumble(byte large, byte small, float strength)
    {
        // Diese Gamepads haben keine Vibration.
    }

    public Task SetPlayerAsync(int playerIndex) => Task.CompletedTask;

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
