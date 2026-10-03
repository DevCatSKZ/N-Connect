using Switch2Pro.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;

namespace Switch2Pro.Bridge;

/// <summary>
/// Eine Bluetooth-LE-Verbindung zu einem Pro Controller 2 samt virtuellem Controller.
/// Ablauf: verbinden → GATT-Merkmale holen → Start-Befehle → Kalibrierung lesen →
/// Eingaben abonnieren → Eingaben an den virtuellen Controller, Vibration zurück.
/// </summary>
internal sealed class ControllerSession : IAsyncDisposable
{
    private const int CommandTimeoutMs = 400;
    /// <summary>Ohne Eingaben UND ohne Bluetooth-Verbindung gilt der Controller als getrennt.</summary>
    private const int InputTimeoutMs = 3000;
    private const int RumbleIntervalMs = 15;

    private readonly Func<Settings> _settings;
    private readonly PadFactory _factory;
    private readonly BluetoothLEDevice _device;
    private readonly GattSession _session;
    private readonly IDisposable? _connectionParameters;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _padGate = new();

    private GattCharacteristic _command = null!;
    private GattCharacteristic _response = null!;
    private GattCharacteristic _input05 = null!;
    private GattCharacteristic? _input09;
    private GattCharacteristic? _rumble;

    /// <summary>Erwartete Antwort (Befehl, Unterbefehl, Ziel) – als ein Objekt, damit es atomar wechselt.</summary>
    private sealed record PendingReply(byte Command, byte Subcommand, TaskCompletionSource<byte[]> Reply);
    private volatile PendingReply? _pending;

    private IVirtualPad? _pad;
    private StickCalibration _left = StickCalibration.Default;
    private StickCalibration _right = StickCalibration.Default;
    private GyroBias _gyroBias;

    private long _lastInputTicks = Environment.TickCount64;
    private bool _got05;
    private volatile int _rumbleLarge, _rumbleSmall;
    private readonly SemaphoreSlim _rumbleSignal = new(0, int.MaxValue);
    private int _closed;

    public ulong Address { get; }
    public string AddressText { get; }
    public int PlayerIndex { get; private set; }
    public ControllerState? LastState { get; private set; }
    public (StickCalibration Left, StickCalibration Right) Calibration => (_left, _right);

    /// <summary>Wird genau einmal ausgelöst, wenn die Verbindung abbricht.</summary>
    public event Action<ControllerSession>? Lost;
    public event Action<ControllerSession>? StatusChanged;

    private ControllerSession(ulong address, int playerIndex, Func<Settings> settings, PadFactory factory,
        BluetoothLEDevice device, GattSession session, IDisposable? connectionParameters)
    {
        Address = address;
        AddressText = FormatAddress(address);
        PlayerIndex = playerIndex;
        _settings = settings;
        _factory = factory;
        _device = device;
        _session = session;
        _connectionParameters = connectionParameters;
    }

    public static async Task<ControllerSession> ConnectAsync(ulong address, BluetoothAddressType addressType,
        int playerIndex, Func<Settings> settings, PadFactory factory,
        Action<ControllerSession> onLost, Action<ControllerSession> onStatus, CancellationToken ct)
    {
        // Kein Pairing über Windows! Der Controller kennt kein Standard-Pairing (SMP) und
        // würde die Verbindung sonst trennen. Wir verbinden direkt über die Adresse.
        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address, addressType)
                     ?? throw new InvalidOperationException("Windows konnte das Bluetooth-Gerät nicht öffnen.");
        GattSession? session = null;
        IDisposable? parameters = null;
        try
        {
            session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);
            session.MaintainConnection = true;
            parameters = RequestLowLatency(device);
        }
        catch
        {
            parameters?.Dispose();
            session?.Dispose();
            device.Dispose();
            throw;
        }

        var s = new ControllerSession(address, playerIndex, settings, factory, device, session, parameters);
        s.Lost += onLost;
        s.StatusChanged += onStatus;
        try
        {
            await s.StartAsync(ct);
            if (s.IsLost)
                throw new InvalidOperationException("Verbindung während des Starts verloren.");
            return s;
        }
        catch
        {
            s.Lost -= onLost;
            await s.DisposeAsync();
            throw;
        }
    }

    /// <summary>Windows 11 22H2+: kürzeres Verbindungsintervall = weniger Eingabeverzögerung.</summary>
    private static IDisposable? RequestLowLatency(BluetoothLEDevice device)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            return null;
        try
        {
            var request = device.RequestPreferredConnectionParameters(
                BluetoothLEPreferredConnectionParameters.ThroughputOptimized);
            Log.Info($"Verbindungsparameter angefragt: {request.Status}");
            return request;
        }
        catch (Exception e)
        {
            Log.Warn($"Verbindungsparameter nicht gesetzt: {e.Message}");
            return null;
        }
    }

    private async Task StartAsync(CancellationToken ct)
    {
        _device.ConnectionStatusChanged += OnConnectionStatusChanged;

        // Kurz warten, bis die Verbindung steht – sonst schlägt die erste Dienstsuche oft fehl.
        await Task.Delay(500, ct);
        var service = await FindServiceAsync(ct);
        var chars = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
        if (chars.Status != GattCommunicationStatus.Success)
            throw new InvalidOperationException($"GATT-Merkmale nicht lesbar: {chars.Status}");
        GattCharacteristic? Find(Guid id) => chars.Characteristics.FirstOrDefault(c => c.Uuid == id);

        _command = Find(Gatt.CommandOutput) ?? throw new InvalidOperationException("Befehlskanal fehlt – ist das ein Pro Controller 2?");
        _response = Find(Gatt.CommandResponse) ?? throw new InvalidOperationException("Antwortkanal fehlt.");
        _input05 = Find(Gatt.InputReportCommon) ?? throw new InvalidOperationException("Eingabekanal fehlt.");
        _input09 = Find(Gatt.InputReportPro);
        _rumble = Find(Gatt.ProRumbleOutput);

        _response.ValueChanged += OnResponse;
        await EnableNotifyAsync(_response);

        foreach (var command in Commands.InitSequence)
        {
            ct.ThrowIfCancellationRequested();
            await SendCommandAsync(command, ct);
        }

        await ReadCalibrationAsync(ct);
        await SetPlayerLedsAsync(PlayerIndex, ct);
        if (_settings().ConnectFeedback)
            await SendCommandAsync(Commands.PlayConnectSample(), ct);

        CreatePad(_settings().OutputMode);

        _input05.ValueChanged += OnInput05;
        await EnableNotifyAsync(_input05);
        _lastInputTicks = Environment.TickCount64;

        _ = Task.Run(() => WatchdogAsync(_cts.Token));
        _ = Task.Run(() => RumbleLoopAsync(_cts.Token));
        Log.Info($"{AddressText}: verbunden als Spieler {PlayerIndex + 1}");
    }

    private async Task<GattDeviceService> FindServiceAsync(CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            var result = await _device.GetGattServicesForUuidAsync(Gatt.HidService, BluetoothCacheMode.Uncached);
            if (result.Status == GattCommunicationStatus.Success && result.Services.Count > 0)
                return result.Services[0];
            if (attempt >= 4)
                throw new InvalidOperationException($"Controller-Dienst nicht gefunden ({result.Status}).");
            await Task.Delay(400 * attempt, ct);
        }
    }

    private static async Task EnableNotifyAsync(GattCharacteristic characteristic)
    {
        var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify);
        if (result.Status != GattCommunicationStatus.Success)
            throw new InvalidOperationException($"Benachrichtigungen nicht aktivierbar ({characteristic.Uuid}): {result.Status}");
    }

    private static async Task<bool> WriteAsync(GattCharacteristic characteristic, byte[] data)
    {
        var result = await characteristic.WriteValueWithResultAsync(
            CryptographicBuffer.CreateFromByteArray(data), GattWriteOption.WriteWithoutResponse);
        return result.Status == GattCommunicationStatus.Success;
    }

    /// <summary>Sendet einen Befehl und wartet kurz auf die Antwort (null = keine Antwort).</summary>
    private async Task<byte[]?> SendCommandAsync(byte[] command, CancellationToken ct)
    {
        await _commandLock.WaitAsync(ct);
        try
        {
            var reply = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = new PendingReply(command[0], command[3], reply);
            if (!await WriteAsync(_command, command))
            {
                Log.Warn($"{AddressText}: Befehl {command[0]:X2}/{command[3]:X2} nicht gesendet");
                return null;
            }
            var finished = await Task.WhenAny(reply.Task, Task.Delay(CommandTimeoutMs, ct));
            if (finished != reply.Task)
            {
                Log.Warn($"{AddressText}: keine Antwort auf {command[0]:X2}/{command[3]:X2}");
                return null;
            }
            return await reply.Task;
        }
        finally
        {
            _pending = null;
            _commandLock.Release();
        }
    }

    private void OnResponse(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] data);
        if (data is null)
            return;
        if (_pending is { } p && Commands.IsResponseTo(data, p.Command, p.Subcommand))
            p.Reply.TrySetResult(data);
    }

    private async Task<byte[]?> ReadMemoryAsync(uint address, byte length, CancellationToken ct)
    {
        var response = await SendCommandAsync(Commands.ReadMemory(address, length), ct);
        return response is not null && Commands.TryParseMemoryRead(response, address, out var data) ? data : null;
    }

    private async Task ReadCalibrationAsync(CancellationToken ct)
    {
        if (await ReadMemoryAsync(Commands.AddrLeftStickCalibration, 9, ct) is { } l && StickCalibration.TryParse(l, out var left))
            _left = left;
        else
            Log.Warn($"{AddressText}: Kalibrierung linker Stick nicht lesbar – Standardwerte");
        if (await ReadMemoryAsync(Commands.AddrRightStickCalibration, 9, ct) is { } r && StickCalibration.TryParse(r, out var right))
            _right = right;
        else
            Log.Warn($"{AddressText}: Kalibrierung rechter Stick nicht lesbar – Standardwerte");
        if (await ReadMemoryAsync(Commands.AddrGyroCalibration, 0x10, ct) is { } g && GyroBias.TryParse(g, out var bias))
            _gyroBias = bias;
        Log.Info($"{AddressText}: Kalibrierung L={_left} R={_right} Gyro={_gyroBias}");
    }

    private Task SetPlayerLedsAsync(int playerIndex, CancellationToken ct) =>
        SendCommandAsync(Commands.SetPlayerLeds(Commands.PlayerLedMask(playerIndex)), ct);

    // ---------- virtueller Controller ----------

    private void CreatePad(OutputMode mode)
    {
        var pad = _factory.Create(mode);
        if (pad is Ds4Pad ds4)
            ds4.Bias = _gyroBias;
        pad.Rumble += OnGameRumble;
        pad.PlayerIndexAssigned += OnPlayerIndexAssigned;
        IVirtualPad? old;
        lock (_padGate)
        {
            old = _pad;
            _pad = pad;
        }
        if (old is not null)
        {
            old.Rumble -= OnGameRumble;
            old.PlayerIndexAssigned -= OnPlayerIndexAssigned;
            old.Dispose();
        }
        // Wurde die Sitzung inzwischen geschlossen, darf kein virtueller Controller übrig bleiben.
        if (Volatile.Read(ref _closed) == 1)
        {
            lock (_padGate)
            {
                if (ReferenceEquals(_pad, pad))
                    _pad = null;
            }
            pad.Rumble -= OnGameRumble;
            pad.PlayerIndexAssigned -= OnPlayerIndexAssigned;
            pad.Dispose();
        }
    }

    /// <summary>Ausgabeart wechseln (z. B. Xbox 360 → DualShock 4) ohne neu zu verbinden.</summary>
    public void SwitchOutput(OutputMode mode)
    {
        if (Volatile.Read(ref _closed) == 0)
            CreatePad(mode);
    }

    private void OnPlayerIndexAssigned(int index)
    {
        if (index == PlayerIndex || index is < 0 or > 7)
            return;
        PlayerIndex = index;
        _ = SetPlayerLedsAsync(index, _cts.Token).ContinueWith(_ => { }, TaskScheduler.Default);
        StatusChanged?.Invoke(this);
    }

    private void OnInput05(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] data);
        if (data is null || !InputReports.TryParseReport05(data, out var state))
            return;
        _got05 = true;
        Deliver(state);
    }

    private void OnInput09(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        if (_got05)
            return;
        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] data);
        if (data is not null && InputReports.TryParseReport09(data, out var state))
            Deliver(state);
    }

    private void Deliver(ControllerState state)
    {
        _lastInputTicks = Environment.TickCount64;
        var previous = LastState;
        LastState = state;
        var gamepad = Mapping.ToGamepad(state, _settings(), _left, _right);
        IVirtualPad? pad;
        lock (_padGate)
            pad = _pad;
        try
        {
            pad?.Update(gamepad, state);
        }
        catch (Exception e)
        {
            Log.Error($"{AddressText}: virtueller Controller", e);
        }
        if (previous is null || previous.BatteryPercent / 5 != state.BatteryPercent / 5 || previous.Charging != state.Charging)
            StatusChanged?.Invoke(this);
    }

    private async Task WatchdogAsync(CancellationToken ct)
    {
        try
        {
            // Falls Bericht 0x05 ausbleibt, auf den Pro-Bericht 0x09 ausweichen.
            await Task.Delay(1500, ct);
            if (!_got05 && _input09 is not null)
            {
                Log.Warn($"{AddressText}: kein Bericht 0x05 – nutze Bericht 0x09 (ohne Gyro)");
                _input09.ValueChanged += OnInput09;
                await EnableNotifyAsync(_input09);
            }
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);
                // Nur abbrechen, wenn auch Windows die Verbindung nicht mehr meldet – ein ruhender
                // Controller darf nicht getrennt werden, falls er im Leerlauf weniger sendet.
                if (Environment.TickCount64 - _lastInputTicks > InputTimeoutMs
                    && _device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                {
                    Log.Warn($"{AddressText}: keine Eingaben und keine Verbindung mehr");
                    RaiseLost();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.Error($"{AddressText}: Überwachung", e);
            RaiseLost();
        }
    }

    // ---------- Vibration ----------

    private void OnGameRumble(byte large, byte small)
    {
        _rumbleLarge = large;
        _rumbleSmall = small;
        _rumbleSignal.Release();
    }

    private async Task RumbleLoopAsync(CancellationToken ct)
    {
        if (_rumble is null)
        {
            Log.Warn($"{AddressText}: kein Vibrationskanal – Vibration aus");
            return;
        }
        int counter = 0;
        bool wasActive = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var settings = _settings();
                byte large = (byte)_rumbleLarge, small = (byte)_rumbleSmall;
                bool active = settings.RumbleEnabled && (large | small) != 0;
                if (active)
                {
                    await WriteAsync(_rumble, Rumble.BuildPacket(large, small, counter++, settings.RumbleStrength));
                    await Task.Delay(RumbleIntervalMs, ct);
                }
                else
                {
                    if (wasActive)
                        await WriteAsync(_rumble, Rumble.StopPacket(counter++));
                    while (_rumbleSignal.CurrentCount > 0)
                        _rumbleSignal.Wait(0);
                    await _rumbleSignal.WaitAsync(1000, ct);
                }
                wasActive = active;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.Warn($"{AddressText}: Vibration abgeschaltet: {e.Message}");
        }
    }

    // ---------- Verbindungsende ----------

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            Log.Info($"{AddressText}: Bluetooth getrennt");
            RaiseLost();
        }
    }

    private int _lostRaised;

    public bool IsLost => Volatile.Read(ref _lostRaised) == 1;

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
        _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        try
        {
            if (_rumble is not null && _device.ConnectionStatus == BluetoothConnectionStatus.Connected)
                await WriteAsync(_rumble, Rumble.StopPacket(0)).WaitAsync(TimeSpan.FromMilliseconds(300));
        }
        catch (Exception)
        {
            // Beim Trennen ist ein fehlgeschlagenes Stopp-Paket egal.
        }
        if (_input05 is not null) _input05.ValueChanged -= OnInput05;
        if (_input09 is not null) _input09.ValueChanged -= OnInput09;
        if (_response is not null) _response.ValueChanged -= OnResponse;

        IVirtualPad? pad;
        lock (_padGate)
        {
            pad = _pad;
            _pad = null;
        }
        if (pad is not null)
        {
            pad.Rumble -= OnGameRumble;
            pad.PlayerIndexAssigned -= OnPlayerIndexAssigned;
            pad.Dispose();
        }

        _connectionParameters?.Dispose();
        _session.MaintainConnection = false;
        _session.Dispose();
        _device.Dispose();
        // _cts bewusst nicht entsorgen: Hintergrund-Tasks können das Token noch kurz abfragen.
    }

    public static string FormatAddress(ulong address) =>
        string.Join(':', Enumerable.Range(0, 6).Reverse().Select(i => ((address >> (i * 8)) & 0xFF).ToString("X2")));
}
