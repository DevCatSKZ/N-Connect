using System.Text;
using Switch2Pro.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;

namespace Switch2Pro.Bridge.Links;

/// <summary>
/// Bluetooth-LE-Verbindung zu einem Switch-2-Controller (Pro Controller 2, Joy-Con 2, GameCube).
/// Ablauf: verbinden → GATT-Merkmale → Gerätedaten und Kalibrierung lesen → Funktionen einschalten →
/// Eingaben abonnieren. Mit echter Hardware geprüft (Pro Controller 2, Windows 11).
/// </summary>
internal sealed class Switch2BleLink : IControllerLink
{
    private const int CommandTimeoutMs = 500;
    private const int CommandAttempts = 3;
    /// <summary>Der Controller sendet auch in Ruhe ~30 Berichte/s; bleiben sie so lange aus, ist er weg.</summary>
    private const int InputTimeoutMs = 2500;
    private const int RumbleIntervalMs = 12;
    private int _gripPoll;

    private readonly BluetoothLEDevice _device;
    private readonly GattSession _session;
    private readonly IDisposable? _connectionParameters;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly RateMeter _rate = new();

    private GattDeviceService? _service;
    private GattCharacteristic _command = null!;
    private GattCharacteristic _response = null!;
    private GattCharacteristic _input = null!;
    private GattCharacteristic? _rumble;

    private sealed record PendingReply(byte Command, byte Subcommand, uint? Address, TaskCompletionSource<byte[]> Reply);
    private volatile PendingReply? _pending;

    private long _lastInputTicks = Environment.TickCount64;
    private volatile int _rumbleLarge, _rumbleSmall;
    private float _rumbleStrength = 1f;
    private readonly SemaphoreSlim _rumbleSignal = new(0, int.MaxValue);
    private int _closed, _lostRaised;

    public ControllerKind Kind { get; }
    public Transport Transport => Transport.BluetoothLE;
    public string Id { get; }
    public string? Address => Id;
    public DeviceCalibration Calibration { get; private set; } = DeviceCalibration.Default;
    public ControllerInfo Info { get; private set; } = new();
    public ControllerState? LastState { get; private set; }
    public double ReportRate => _rate.Rate;
    public bool IsLost => Volatile.Read(ref _lostRaised) == 1;
    public bool InGrip { get; private set; }
    private bool IsJoyCon2 => Kind is ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right;

    /// <summary>Charging Grip erkennen und dessen GL/GR-Tasten einschalten (nur Joy-Con 2).</summary>
    private async Task CheckGripAsync(CancellationToken ct)
    {
        var reply = await SendCommandAsync(Commands.GripInfo(), ct, attempts: 1);
        bool inGrip = reply is not null && Commands.IsInGrip(reply);
        if (inGrip && !InGrip)
        {
            await SendCommandAsync(Commands.EnableGripButtons(true), ct, attempts: 2);
            Log.Info($"{Id}: steckt im Charging Grip – GL/GR eingeschaltet");
        }
        else if (!inGrip && InGrip)
        {
            Log.Info($"{Id}: aus dem Charging Grip genommen");
        }
        InGrip = inGrip;
    }

    public event Action<IControllerLink, ControllerState>? StateReceived;
    public event Action<IControllerLink>? Lost;

    private Switch2BleLink(ControllerKind kind, string id, BluetoothLEDevice device, GattSession session, IDisposable? parameters)
    {
        Kind = kind;
        Id = id;
        _device = device;
        _session = session;
        _connectionParameters = parameters;
    }

    public static async Task<Switch2BleLink> ConnectAsync(ulong address, BluetoothAddressType addressType, ControllerKind kind,
        CancellationToken ct)
    {
        string id = FormatAddress(address);
        // Kein Pairing nötig: Der Controller nimmt die Verbindung direkt an.
        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address, addressType)
                     ?? throw new InvalidOperationException("Windows konnte das Bluetooth-Gerät nicht öffnen.");
        GattSession? session = null;
        IDisposable? parameters = null;
        try
        {
            session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);
            session.MaintainConnection = true;
            parameters = RequestLowLatency(device, id);
        }
        catch
        {
            parameters?.Dispose();
            session?.Dispose();
            device.Dispose();
            throw;
        }

        var link = new Switch2BleLink(kind, id, device, session, parameters);
        try
        {
            await link.StartAsync(ct);
            return link;
        }
        catch
        {
            await link.DisposeAsync();
            throw;
        }
    }

    /// <summary>Kürzeres Verbindungsintervall (Windows 11 22H2+) = mehr Berichte pro Sekunde, weniger Verzögerung.</summary>
    private static IDisposable? RequestLowLatency(BluetoothLEDevice device, string id)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            return null;
        try
        {
            var request = device.RequestPreferredConnectionParameters(BluetoothLEPreferredConnectionParameters.ThroughputOptimized);
            Log.Info($"{id}: schnelle Verbindung angefragt: {request.Status}");
            return request;
        }
        catch (Exception e)
        {
            Log.Warn($"{id}: Verbindungsparameter nicht gesetzt: {Log.Reason(e)}");
            return null;
        }
    }

    private IDisposable? _lateParameters;
    private int _lateRequests;
    private bool _wasFast;

    /// <summary>
    /// Der Controller fordert nach dem Verbinden selbst 30 ms an (gemessen) – das ergibt nur ~33 Berichte/s.
    /// Dann noch einmal die schnelle Verbindung anfragen (Anfrage bleibt bis zum Trennen bestehen).
    /// </summary>
    private void OnConnectionParametersChanged(BluetoothLEDevice sender, object args)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            return;
        var p = sender.GetConnectionParameters();
        Log.Info($"{Id}: Verbindungsintervall {p.ConnectionInterval * 1.25:F2} ms, Latenz {p.ConnectionLatency}");
        // Erst nachfragen, wenn die Verbindung schon einmal schnell war (der Controller schaltet danach
        // selbst auf 30 ms zurück) – höchstens dreimal, damit kein Hin und Her entsteht.
        if (p.ConnectionInterval * 1.25 < 10)
            _wasFast = true;
        if (p.ConnectionInterval * 1.25 > 15 && _wasFast && _lateRequests < 3 && Volatile.Read(ref _closed) == 0)
        {
            try
            {
                _lateRequests++;
                _lateParameters?.Dispose();
                // Windows verhandelt nur neu, wenn sich die Vorgabe ändert: kurz „ausgeglichen“, dann wieder „schnell“.
                using (sender.RequestPreferredConnectionParameters(BluetoothLEPreferredConnectionParameters.Balanced)) { }
                _lateParameters = sender.RequestPreferredConnectionParameters(BluetoothLEPreferredConnectionParameters.ThroughputOptimized);
                if (Volatile.Read(ref _closed) != 0) // inzwischen getrennt: Anfrage nicht verwaisen lassen
                {
                    _lateParameters.Dispose();
                    _lateParameters = null;
                    return;
                }
                Log.Info($"{Id}: schnelle Verbindung erneut angefragt ({_lateRequests}/3)");
            }
            catch (Exception e)
            {
                Log.Warn($"{Id}: erneute Anfrage nicht möglich: {Log.Reason(e)}");
            }
        }
    }

    private async Task StartAsync(CancellationToken ct)
    {
        _device.ConnectionStatusChanged += OnConnectionStatusChanged;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            _device.ConnectionParametersChanged += OnConnectionParametersChanged;

        _service = await FindServiceAsync(ct);
        var chars = await _service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
        if (chars.Status != GattCommunicationStatus.Success)
            throw new InvalidOperationException($"GATT-Merkmale nicht lesbar: {chars.Status}");
        GattCharacteristic? Find(Guid uuid) => chars.Characteristics.FirstOrDefault(c => c.Uuid == uuid);

        _command = Find(Gatt.CommandOutput) ?? throw new InvalidOperationException("Befehlskanal fehlt.");
        _response = Find(Gatt.CommandResponse) ?? throw new InvalidOperationException("Antwortkanal fehlt.");
        _input = Find(Gatt.InputReportCommon) ?? throw new InvalidOperationException("Eingabekanal fehlt.");
        _rumble = Find(Gatt.RumbleOutput(Kind));

        _response.ValueChanged += OnResponse;
        await EnableNotifyAsync(_response);

        // Reihenfolge auf schnelle Rückmeldung ausgelegt: erst Funktionen und Eingaben (der Controller
        // ist damit sofort spielbar), Spieler-LED setzt danach der Spieler; Kalibrierung und Gerätedaten
        // werden direkt im Anschluss gelesen (bis dahin gelten Standardwerte, ca. 0,5 s).
        // Feature-Maske 0x2F wie die Konsole (Tasten, Sticks, Bewegungssensor …). 0xFF schaltet bei Joy-Con
        // zusätzliche Felder ein, die ZL/ZR-Phantomtasten erzeugen (Switch2Connect).
        // Hier werden keine Kopplungsdaten geschrieben. Nur nach SYNC koppelt der Manager den Controller
        // mit diesem PC (Befehl 0x15, siehe PairWithHostAsync) – danach muss er an der Switch 2 neu gekoppelt werden.
        await SendRequiredAsync(Commands.SetFeatures(Commands.FeatureMask(Kind)), ct);
        await SendRequiredAsync(Commands.EnableFeatures(Commands.FeatureMask(Kind)), ct);

        _input.ValueChanged += OnInput;
        await EnableNotifyAsync(_input);
        _lastInputTicks = Environment.TickCount64;

        _ = Task.Run(() => WatchdogAsync(_cts.Token));
        _ = Task.Run(() => RumbleLoopAsync(_cts.Token));

        await ReadCalibrationAsync(ct);
        await ReadDeviceInfoAsync(ct);
        if (IsJoyCon2)
            await CheckGripAsync(ct);
        Log.Info($"{Id}: {Kind.DisplayName()} bereit (Seriennr. {Info.SerialNumber ?? "?"}, Firmware {Info.Firmware ?? "?"})");
    }

    /// <summary>
    /// Koppelt den Controller mit diesem PC (Nintendo-Verfahren, Befehl 0x15). Danach wirbt er nach
    /// einem Tastendruck für den PC und verbindet sich ohne SYNC. Der Controller merkt sich nur einen
    /// Host: Für die Switch 2 dort erneut SYNC drücken. Mit echter Hardware geprüft (Pro Controller 2).
    /// </summary>
    public async Task<bool> PairWithHostAsync(ulong hostAddress)
    {
        var ct = _cts.Token;
        if (await ReadMemoryAsync(Commands.AddrPairing, 0x40, ct) is { } block
            && PairingRecord.TryParse(block, out var existing) && existing.HostAddresses.Contains(hostAddress))
            return true; // schon mit diesem PC gekoppelt – Flash nicht unnötig neu schreiben

        var pairing = new Pairing();
        if (await SendCommandAsync(Pairing.AddressesRequest(hostAddress), ct) is null
            || await SendCommandAsync(pairing.KeysRequest(), ct) is not { } keys
            || !pairing.TryDeriveKey(keys, out var ltk)
            || await SendCommandAsync(pairing.ConfirmRequest(), ct) is not { } confirm
            || !pairing.VerifyConfirmation(confirm, ltk))
        {
            Log.Warn($"{Id}: Kopplung mit dem PC fehlgeschlagen – Wiederverbinden nur per SYNC");
            return false;
        }
        bool ok = await SendCommandAsync(Pairing.FinishRequest(), ct) is not null;
        Log.Info($"{Id}: mit diesem PC gekoppelt: {ok}");
        return ok;
    }

    private async Task<GattDeviceService> FindServiceAsync(CancellationToken ct)
    {
        // Der Verbindungsaufbau braucht teils mehrere Anläufe (gemessen: bis ~10 s).
        for (int attempt = 1; ; attempt++)
        {
            var result = await _device.GetGattServicesForUuidAsync(Gatt.HidService, BluetoothCacheMode.Uncached);
            if (result.Status == GattCommunicationStatus.Success && result.Services.Count > 0)
            {
                foreach (var extra in result.Services.Skip(1))
                    extra.Dispose();
                return result.Services[0];
            }
            if (attempt >= 8)
                throw new InvalidOperationException($"Controller-Dienst nicht erreichbar ({result.Status}).");
            await Task.Delay(Math.Min(400 * attempt, 2000), ct);
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

    // ---------- Befehle ----------

    /// <summary>Sendet einen Befehl (bis zu 3 Versuche) und liefert die Antwort oder null.</summary>
    private async Task<byte[]?> SendCommandAsync(byte[] command, CancellationToken ct, uint? memoryAddress = null,
        int attempts = CommandAttempts)
    {
        await _commandLock.WaitAsync(ct);
        try
        {
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                var reply = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending = new PendingReply(command[0], command[3], memoryAddress, reply);
                if (await WriteAsync(_command, command))
                {
                    var finished = await Task.WhenAny(reply.Task, Task.Delay(CommandTimeoutMs, ct));
                    if (finished == reply.Task)
                        return await reply.Task;
                }
                await Task.Delay(120 * attempt, ct);
            }
            if (attempts > 1)
                Log.Warn($"{Id}: keine Antwort auf Befehl {command[0]:X2}/{command[3]:X2}");
            return null;
        }
        finally
        {
            _pending = null;
            _commandLock.Release();
        }
    }

    private async Task SendRequiredAsync(byte[] command, CancellationToken ct)
    {
        if (await SendCommandAsync(command, ct) is null)
            throw new InvalidOperationException($"Controller antwortet nicht (Befehl {command[0]:X2}/{command[3]:X2}).");
    }

    private void OnResponse(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] data);
        if (data is null || _pending is not { } p || !Commands.IsResponseTo(data, p.Command, p.Subcommand))
            return;
        // Eine verspätete Antwort auf einen früheren Lesebefehl darf den aktuellen nicht erfüllen.
        if (p.Address is { } address && !Commands.TryParseMemoryRead(data, address, out _))
            return;
        p.Reply.TrySetResult(data);
    }

    private async Task<byte[]?> ReadMemoryAsync(uint address, byte length, CancellationToken ct)
    {
        var response = await SendCommandAsync(Commands.ReadMemory(address, length), ct, address);
        return response is not null && Commands.TryParseMemoryRead(response, address, out var data) ? data : null;
    }

    private async Task ReadDeviceInfoAsync(CancellationToken ct)
    {
        string? firmware = null;
        if (await SendCommandAsync(Commands.FirmwareVersion(), ct) is { Length: >= 12 } fw)
            firmware = $"{fw[9]}.{fw[10]}.{fw[11]}";
        var info = await ReadMemoryAsync(Commands.AddrDeviceInfo, 0x40, ct);
        Info = DeviceInfo.Parse(info) with { Firmware = firmware };
    }

    private async Task ReadCalibrationAsync(CancellationToken ct)
    {
        var cal = new DeviceCalibration();
        // Benutzerkalibrierung (Systemeinstellungen der Switch 2) hat Vorrang vor der Werkskalibrierung.
        if (await ReadStickAsync(Commands.AddrUserLeftStickCalibration, Commands.AddrLeftStickCalibration, ct) is { } l)
            cal = cal with { Left = l };
        else if (Kind != ControllerKind.JoyCon2Right)
            Log.Warn($"{Id}: Kalibrierung linker Stick nicht lesbar – Standardwerte");
        if (await ReadStickAsync(Commands.AddrUserRightStickCalibration, Commands.AddrRightStickCalibration, ct) is { } r)
            cal = cal with { Right = r };
        else if (Kind == ControllerKind.JoyCon2Right)
            cal = cal with { Right = cal.Left }; // einziger Stick: Kalibrierung steht im ersten Stick-Block (gemessen)
        else if (Kind is ControllerKind.Pro2 or ControllerKind.GameCube2)
            Log.Warn($"{Id}: Kalibrierung rechter Stick nicht lesbar – Standardwerte");
        if (await ReadMemoryAsync(Commands.AddrGyroCalibration, 0x10, ct) is { } g && GyroBias.TryParse(g, out var bias))
            cal = cal with { Gyro = bias };
        // Ruhewert der Trigger nur übernehmen, wenn er plausibel ist (sonst bliebe ein Trigger dauerhaft gedrückt).
        if (Kind == ControllerKind.GameCube2 && await ReadMemoryAsync(Commands.AddrTriggerCalibration, 2, ct) is { Length: 2 } t
            && t[0] < 128 && t[1] < 128)
            cal = cal with { TriggerZeroLeft = t[0], TriggerZeroRight = t[1] };
        Calibration = cal;
        Log.Info($"{Id}: Kalibrierung L={cal.Left} R={cal.Right} Gyro={cal.Gyro}");
    }

    private async Task<StickCalibration?> ReadStickAsync(uint userAddress, uint factoryAddress, CancellationToken ct)
    {
        if (await ReadMemoryAsync(userAddress, 11, ct) is { Length: 11 } user && user[0] == 0xB2 && user[1] == 0xA1
            && StickCalibration.TryParse(user.AsSpan(2), out var u))
            return u;
        if (await ReadMemoryAsync(factoryAddress, 9, ct) is { } factory && StickCalibration.TryParse(factory, out var f))
            return f;
        return null;
    }

    public Task SetPlayerAsync(int playerIndex) =>
        Volatile.Read(ref _closed) == 1
            ? Task.CompletedTask
            : SendCommandAsync(Commands.SetPlayerLeds(Commands.PlayerLedMask(playerIndex)), _cts.Token);

    /// <summary>Kurzes „Verbunden“-Klicken (eingebautes Muster).</summary>
    public Task PlayConnectFeedbackAsync() => SendCommandAsync(Commands.PlayConnectSample(), _cts.Token);

    // ---------- Eingaben ----------

    private void OnInput(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] data);
        if (data is null || !InputReports.TryParseReport05(data, out var state, Kind))
            return;
        state = BatteryTracker.Apply(Info.SerialNumber, state, data);
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

    private async Task WatchdogAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);
                if (IsJoyCon2 && ++_gripPoll % 10 == 0)
                    CheckGripAsync(ct).Forget($"{Id}: Griff prüfen");
                if (Environment.TickCount64 - _lastInputTicks > InputTimeoutMs)
                {
                    Log.Warn($"{Id}: seit {InputTimeoutMs} ms keine Eingaben – Verbindung verloren");
                    RaiseLost();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

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
        if (_rumble is null)
        {
            Log.Info($"{Id}: kein Vibrationskanal für {Kind}");
            return;
        }
        int counter = 0;
        bool wasActive = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                byte large = (byte)_rumbleLarge, small = (byte)_rumbleSmall;
                bool active = (large | small) != 0;
                if (active)
                {
                    await WriteAsync(_rumble, Rumble.BuildPacket(Kind, large, small, counter++, _rumbleStrength));
                    await Task.Delay(RumbleIntervalMs, ct);
                }
                else
                {
                    if (wasActive)
                        await WriteAsync(_rumble, Rumble.BuildPacket(Kind, 0, 0, counter++, 0));
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
            Log.Warn($"{Id}: Vibration abgeschaltet: {Log.Reason(e)}");
        }
    }

    // ---------- Verbindungsende ----------

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        // Nur protokollieren: Beim Verbindungsaufbau meldet Windows oft kurz „getrennt“ (gemessen), und
        // MaintainConnection verbindet selbst neu. Als verloren gilt die Verbindung erst, wenn keine Eingaben
        // mehr kommen (Watchdog, 2,5 s).
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            Log.Info($"{Id}: Bluetooth meldet getrennt");
    }

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
                await WriteAsync(_rumble, Rumble.BuildPacket(Kind, 0, 0, 0, 0)).WaitAsync(TimeSpan.FromMilliseconds(300));
        }
        catch (Exception)
        {
            // Beim Trennen ist ein fehlgeschlagenes Stopp-Paket egal.
        }
        if (_input is not null) _input.ValueChanged -= OnInput;
        if (_response is not null) _response.ValueChanged -= OnResponse;

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            _device.ConnectionParametersChanged -= OnConnectionParametersChanged;
        _service?.Dispose();
        _lateParameters?.Dispose();
        _connectionParameters?.Dispose();
        _session.MaintainConnection = false;
        _session.Dispose();
        _device.Dispose();
        // _cts bewusst nicht entsorgen: Hintergrund-Tasks können das Token noch kurz abfragen.
    }

    public static string FormatAddress(ulong address) =>
        string.Join(':', Enumerable.Range(0, 6).Reverse().Select(i => ((address >> (i * 8)) & 0xFF).ToString("X2")));
}

/// <summary>Gerätedaten aus dem Speicherblock 0x13000 (Seriennummer, Farben).</summary>
internal static class DeviceInfo
{
    /// <summary>
    /// Aufbau (aufgezeichnet): 01 00 | Seriennummer (ASCII, 14 Zeichen) 00 00 | VID PID | 01 06 01 |
    /// Farben je 3 Byte: Gehäuse, Tasten, Griff links/rechts.
    /// </summary>
    public static ControllerInfo Parse(byte[]? d)
    {
        if (d is null || d.Length < 0x1F)
            return new ControllerInfo();
        string serial = Encoding.ASCII.GetString(d, 2, 16).TrimEnd('\0', ' ', '\xFF');
        int Color(int o) => d[o] << 16 | d[o + 1] << 8 | d[o + 2];
        return new ControllerInfo
        {
            SerialNumber = serial.All(c => c is >= ' ' and <= '~') && serial.Length > 0 ? serial : null,
            BodyColor = Color(0x19),
            ButtonColor = Color(0x1C),
            GripColor = d.Length >= 0x22 ? Color(0x1F) : null,
        };
    }
}
