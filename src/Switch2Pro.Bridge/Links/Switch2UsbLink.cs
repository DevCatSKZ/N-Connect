using Switch2Pro.Bridge.Usb;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge.Links;

/// <summary>
/// Pro Controller 2 bzw. GameCube-Controller am USB-Kabel: Befehle über die Bulk-Endpunkte (WinUSB, Interface 1),
/// Eingaben und Vibration über HID (Interface 0). Der Eingabebericht ist derselbe wie per Bluetooth (0x05),
/// nur mit vorangestellter Report-ID – bis zu ~500 Berichte/s statt ~33 per Bluetooth. Ablauf wie in SDL
/// (SDL_hidapi_switch2.c, zlib-Lizenz) beschrieben: erst Speicher lesen, dann Start-Sequenz.
/// </summary>
internal sealed class Switch2UsbLink : IControllerLink
{
    /// <summary>Beim Anstecken (Wechsel von Bluetooth auf USB) pausiert der Controller kurz – erst danach als getrennt werten.</summary>
    private const int InputTimeoutMs = 3000;
    private const int RumbleIntervalMs = 12;

    private readonly UsbControllerInfo _device;
    private readonly WinUsbChannel _usb;
    private readonly HidChannel _hid;
    private readonly CancellationTokenSource _cts = new();
    private readonly RateMeter _rate = new();
    private readonly SemaphoreSlim _rumbleSignal = new(0, int.MaxValue);
    private volatile int _rumbleLarge, _rumbleSmall;
    private float _rumbleStrength = 1f;
    private long _lastInputTicks;
    private int _closed, _lostRaised;

    public ControllerKind Kind { get; }
    public Transport Transport => Transport.Usb;
    public string Id => _device.DeviceId;
    /// <summary>Per USB ist keine Bluetooth-Adresse bekannt; Einstellungen je Controller laufen über die Seriennummer.</summary>
    public string? Address => Info.SerialNumber is { } serial ? $"USB:{serial}" : null;
    public string? HidInstanceId => _device.HidInstanceId;
    public DeviceCalibration Calibration { get; private set; } = DeviceCalibration.Default;
    public ControllerInfo Info { get; private set; } = new();
    public ControllerState? LastState { get; private set; }
    public double ReportRate => _rate.Rate;
    public bool IsLost => Volatile.Read(ref _lostRaised) == 1;

    public event Action<IControllerLink, ControllerState>? StateReceived;
    public event Action<IControllerLink>? Lost;

    private Switch2UsbLink(UsbControllerInfo device, WinUsbChannel usb, HidChannel hid)
    {
        _device = device;
        _usb = usb;
        _hid = hid;
        Kind = device.Kind;
    }

    /// <summary>Öffnet den Controller, liest Kalibrierung und Gerätedaten und startet die Eingaben.</summary>
    public static async Task<Switch2UsbLink> OpenAsync(UsbControllerInfo device, CancellationToken ct)
    {
        var usb = new WinUsbChannel(device.WinUsbPath);
        HidChannel hid;
        try
        {
            hid = new HidChannel(device.HidPath);
        }
        catch
        {
            usb.Dispose();
            throw;
        }
        var link = new Switch2UsbLink(device, usb, hid);
        try
        {
            await Task.Run(link.Initialize, ct);
            link.Start();
            return link;
        }
        catch
        {
            await link.DisposeAsync();
            throw;
        }
    }

    /// <summary>Synchron (WinUSB): Speicher lesen, dann Start-Sequenz. Läuft auf dem Threadpool.</summary>
    private void Initialize()
    {
        Info = DeviceInfo.Parse(ReadMemory(Commands.AddrDeviceInfo));

        var cal = new DeviceCalibration();
        if (ReadStick(Commands.AddrUserLeftStickCalibration, Commands.AddrLeftStickCalibration) is { } l)
            cal = cal with { Left = l };
        if (ReadStick(Commands.AddrUserRightStickCalibration, Commands.AddrRightStickCalibration) is { } r)
            cal = cal with { Right = r };
        if (ReadMemory(Commands.AddrGyroCalibration) is { } g && GyroBias.TryParse(g, out var bias))
            cal = cal with { Gyro = bias };
        if (Kind == ControllerKind.GameCube2 && ReadMemory(Commands.AddrTriggerCalibration) is { Length: >= 2 } t
            && t[0] < 128 && t[1] < 128)
            cal = cal with { TriggerZeroLeft = t[0], TriggerZeroRight = t[1] };
        Calibration = cal;

        foreach (var command in Commands.UsbStartSequence(Commands.FeatureMask(Kind)))
            _usb.Transact(command); // Antworten werden nicht gebraucht (wie SDL)
        Log.Info($"{Id}: {Kind.DisplayName()} per USB bereit (Seriennr. {Info.SerialNumber ?? "?"}), Kalibrierung L={cal.Left} R={cal.Right}");
    }

    private byte[]? ReadMemory(uint address)
    {
        // Ein Block hat 0x40 Byte; Werte am Blockanfang lesen wir aus dem Block, in dem sie liegen.
        uint block = address & ~0x3Fu;
        var reply = _usb.Transact(Commands.ReadMemoryUsb(block));
        if (reply is null || !Commands.TryParseMemoryRead(reply, block, out var data, Commands.SubMemoryReadUsb))
            return null;
        int offset = (int)(address - block);
        return offset < data.Length ? data[offset..] : null;
    }

    private StickCalibration? ReadStick(uint userAddress, uint factoryAddress)
    {
        if (ReadMemory(userAddress) is { Length: >= 11 } user && user[0] == 0xB2 && user[1] == 0xA1
            && StickCalibration.TryParse(user.AsSpan(2), out var u))
            return u;
        if (ReadMemory(factoryAddress) is { } factory && StickCalibration.TryParse(factory, out var f))
            return f;
        return null;
    }

    private void Start()
    {
        _lastInputTicks = Environment.TickCount64;
        Task.Run(() => ReadLoopAsync(_cts.Token)).Forget($"{Id}: USB-Eingaben");
        Task.Run(() => WatchdogAsync(_cts.Token)).Forget($"{Id}: USB-Überwachung");
        Task.Run(() => RumbleLoopAsync(_cts.Token)).Forget($"{Id}: USB-Vibration");
    }

    // ---------- Eingaben ----------

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
                // Byte 0 = Report-ID, danach wie der Bluetooth-Bericht 0x05.
                if (n < 2 || !InputReports.TryParseReport05(buffer.AsSpan(1, n - 1), out var state, Kind))
                    continue;
                // Am Kabel lädt der Controller immer; die Ladespannung rechnet die Akkuschätzung heraus.
                state = BatteryTracker.Apply(Info.SerialNumber ?? Id, state with { Charging = true }, buffer.AsSpan(1, n - 1));
                _lastInputTicks = Environment.TickCount64;
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
            Log.Info($"{Id}: USB getrennt ({Log.Reason(e)})");
            RaiseLost();
        }
    }

    private async Task WatchdogAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);
                if (Environment.TickCount64 - Volatile.Read(ref _lastInputTicks) > InputTimeoutMs)
                {
                    Log.Warn($"{Id}: seit {InputTimeoutMs} ms keine USB-Eingaben – getrennt");
                    RaiseLost();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public Task SetPlayerAsync(int playerIndex) =>
        Volatile.Read(ref _closed) == 1
            ? Task.CompletedTask
            : Task.Run(() => _usb.Transact(Commands.SetPlayerLeds(Commands.PlayerLedMask(playerIndex), Commands.TransportUsb)));

    public Task PlayConnectFeedbackAsync() =>
        Volatile.Read(ref _closed) == 1
            ? Task.CompletedTask
            : Task.Run(() => _usb.Transact(Commands.PlayConnectSample(Commands.TransportUsb)));

    // ---------- Vibration ----------

    public void SetRumble(byte large, byte small, float strength)
    {
        _rumbleStrength = strength;
        if (_rumbleLarge == large && _rumbleSmall == small)
            return;
        _rumbleLarge = large;
        _rumbleSmall = small;
        _rumbleSignal.Release();
    }

    private async Task RumbleLoopAsync(CancellationToken ct)
    {
        int counter = 0;
        bool wasActive = false;
        var erm = new Rumble.GameCubeMotor();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                byte large = (byte)_rumbleLarge, small = (byte)_rumbleSmall;
                bool active = (large | small) != 0;
                if (active || wasActive)
                {
                    var report = Kind == ControllerKind.GameCube2
                        ? erm.NextReport(active ? Math.Max(large, small) * _rumbleStrength / 255f : 0f, counter++)
                        : Rumble.BuildUsbReport(active ? large : (byte)0, active ? small : (byte)0, counter++, active ? _rumbleStrength : 0f);
                    await _hid.WriteAsync(report, ct);
                }
                wasActive = active;
                if (active)
                {
                    await Task.Delay(RumbleIntervalMs, ct);
                }
                else
                {
                    while (_rumbleSignal.CurrentCount > 0)
                        _rumbleSignal.Wait(0);
                    await _rumbleSignal.WaitAsync(1000, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.Warn($"{Id}: Vibration abgeschaltet: {Log.Reason(e)}");
        }
    }

    // ---------- Ende ----------

    private void RaiseLost()
    {
        if (Interlocked.Exchange(ref _lostRaised, 1) == 0)
            Lost?.Invoke(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return;
        _cts.Cancel();
        try
        {
            var stop = Kind == ControllerKind.GameCube2 ? new Rumble.GameCubeMotor().NextReport(0f, 0) : Rumble.BuildUsbReport(0, 0, 0, 0f);
            await _hid.WriteAsync(stop, CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(200));
        }
        catch (Exception)
        {
            // Kabel schon gezogen: egal.
        }
        _hid.Dispose();
        _usb.Dispose();
    }
}
