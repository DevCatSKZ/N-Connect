using System.Text;
using Android.Bluetooth;
using Android.Content;
using Switch2Pro.Protocol;
using Java.Util;

namespace SkzConnect.Android.Ble;

/// <summary>
/// Verbindung zu einem Switch-2-Controller (Pro Controller 2, Joy-Con 2, GameCube) über Bluetooth LE – Portierung
/// von <c>Switch2BleLink</c> (Windows) auf die Android-GATT-API. Ablauf: verbinden → MTU → Dienst → Antworten
/// abonnieren → Funktionen einschalten → Eingaben abonnieren → Kalibrierung und Gerätedaten → ggf. mit diesem
/// Gerät koppeln (Befehl 0x15, danach Wiederverbinden ohne SYNC).
/// Android erlaubt nur eine GATT-Operation gleichzeitig; alle Schritte laufen deshalb nacheinander über <see cref="Op"/>.
/// </summary>
public sealed class Switch2Device : IDisposable
{
    private const int CommandTimeoutMs = 600;
    private const int CommandAttempts = 3;
    private const int InputTimeoutMs = 2500;
    private const int RumbleIntervalMs = 12;
    private static readonly UUID CccdUuid = UUID.FromString("00002902-0000-1000-8000-00805f9b34fb")!;

    private readonly Context _context;
    private readonly BluetoothDevice _device;
    private readonly GattEvents _events;
    private readonly SemaphoreSlim _opLock = new(1, 1);
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private BluetoothGatt? _gatt;
    private BluetoothGattCharacteristic? _command, _response, _input, _rumble;
    private TaskCompletionSource<bool>? _op;
    private TaskCompletionSource<bool>? _connected;
    private volatile Pending? _pending;
    private long _lastInputTicks;
    private volatile int _rumbleLarge, _rumbleSmall;
    private float _rumbleStrength = 1f;
    private readonly SemaphoreSlim _rumbleSignal = new(0, int.MaxValue);
    private int _closed, _lostRaised, _reports, _parseFailures;
    private long _totalReports;
    private long _rateWindowStart = Environment.TickCount64;

    private sealed record Pending(byte Command, byte Subcommand, uint? Address, TaskCompletionSource<byte[]> Reply);

    public ControllerKind Kind { get; }
    public string Address => _device.Address ?? "?";
    public DeviceCalibration Calibration { get; private set; } = DeviceCalibration.Default;
    public string? SerialNumber { get; private set; }
    public string? Firmware { get; private set; }
    public ControllerState? LastState { get; private set; }
    public double ReportRate { get; private set; }
    public bool IsLost => Volatile.Read(ref _lostRaised) == 1;

    public event Action<Switch2Device, ControllerState>? StateReceived;
    public event Action<Switch2Device>? Lost;

    private Switch2Device(Context context, BluetoothDevice device, ControllerKind kind)
    {
        _context = context;
        _device = device;
        Kind = kind;
        _events = new GattEvents(this);
    }

    /// <summary>
    /// Verbindet und richtet den Controller ein. <paramref name="pairHost"/> ≠ 0: mit dieser Bluetooth-Adresse koppeln,
    /// damit der Controller sich künftig ohne SYNC verbindet.
    /// </summary>
    public static async Task<Switch2Device> ConnectAsync(Context context, BluetoothDevice device, ControllerKind kind,
        ulong pairHost, CancellationToken ct)
    {
        var dev = new Switch2Device(context, device, kind);
        try
        {
            await dev.StartAsync(pairHost, ct).ConfigureAwait(false);
            return dev;
        }
        catch
        {
            dev.Dispose();
            throw;
        }
    }

    private async Task StartAsync(ulong pairHost, CancellationToken ct)
    {
        // Android meldet beim ersten Versuch gern Status 133 – bis zu drei Anläufe.
        for (int attempt = 1; ; attempt++)
        {
            _connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _gatt = _device.ConnectGatt(_context, false, _events, BluetoothTransports.Le);
            var done = await Task.WhenAny(_connected.Task, Task.Delay(10000, ct)).ConfigureAwait(false);
            if (done == _connected.Task && _connected.Task.Result)
                break;
            _gatt?.Close();
            _gatt = null;
            if (attempt >= 3)
                throw new InvalidOperationException("Controller hat die Verbindung nicht angenommen.");
            await Task.Delay(400 * attempt, ct).ConfigureAwait(false);
        }
        var gatt = _gatt!;
        Log.Info($"{Address}: verbunden, richte ein …");
        gatt.RequestConnectionPriority(GattConnectionPriority.High); // kurzes Intervall = flüssige Eingaben
        await Task.Delay(250, ct).ConfigureAwait(false);

        // Eingabebericht 0x05 ist ~64 Byte; mit der Standard-MTU (23) würde er abgeschnitten.
        await Op(() => gatt.RequestMtu(247), "MTU", ct).ConfigureAwait(false);
        await Op(() => gatt.DiscoverServices(), "Dienste", ct).ConfigureAwait(false);
        Log.Info($"{Address}: Dienste gefunden");

        var service = gatt.GetService(Uuid(Gatt.HidService))
                      ?? throw new InvalidOperationException("Controller-Dienst nicht gefunden.");
        _command = service.GetCharacteristic(Uuid(Gatt.CommandOutput)) ?? throw new InvalidOperationException("Befehlskanal fehlt.");
        _response = service.GetCharacteristic(Uuid(Gatt.CommandResponse)) ?? throw new InvalidOperationException("Antwortkanal fehlt.");
        _input = service.GetCharacteristic(Uuid(Gatt.InputReportCommon)) ?? throw new InvalidOperationException("Eingabekanal fehlt.");
        _rumble = service.GetCharacteristic(Uuid(Gatt.RumbleOutput(Kind)));

        await EnableNotifyAsync(_response, ct).ConfigureAwait(false);
        byte mask = Commands.FeatureMask(Kind);
        await SendRequiredAsync(Commands.SetFeatures(mask), ct).ConfigureAwait(false);
        await SendRequiredAsync(Commands.EnableFeatures(mask), ct).ConfigureAwait(false);
        Log.Info($"{Address}: Funktionen eingeschaltet (Maske {mask:X2})");
        await EnableNotifyAsync(_input, ct).ConfigureAwait(false);
        Log.Info($"{Address}: Eingaben abonniert");

        await ReadCalibrationAsync(ct).ConfigureAwait(false);
        await ReadDeviceInfoAsync(ct).ConfigureAwait(false);
        if (pairHost != 0)
            await PairWithHostAsync(pairHost, ct).ConfigureAwait(false);
        else
            Log.Info($"{Address}: keine Kopplung (Controller nicht im SYNC-Modus oder eigene Adresse unbekannt)");

        // Wächter erst nach der Einrichtung: vorher bestätigen die Befehlsantworten, dass der Controller da ist.
        _lastInputTicks = Environment.TickCount64;
        _ = Task.Run(() => WatchdogAsync(_cts.Token));
        _ = Task.Run(() => RumbleLoopAsync(_cts.Token));
        Log.Info($"{Address}: {Kind.DisplayName()} bereit (Seriennr. {SerialNumber ?? "?"}, Firmware {Firmware ?? "?"})");
    }

    // ---------- GATT-Operationen (nacheinander) ----------

    /// <summary>Startet eine GATT-Operation und wartet auf ihren Rückruf.</summary>
    private async Task Op(Func<bool> start, string what, CancellationToken ct, int timeoutMs = 5000)
    {
        await _opLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var op = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _op = op;
            if (!start())
                throw new InvalidOperationException($"{what}: Android hat die Anfrage abgelehnt.");
            var done = await Task.WhenAny(op.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
            if (done != op.Task)
                throw new TimeoutException($"{what}: keine Antwort vom Controller.");
            if (!op.Task.Result)
                throw new InvalidOperationException($"{what}: fehlgeschlagen.");
        }
        finally
        {
            _op = null;
            _opLock.Release();
        }
    }

    private Task EnableNotifyAsync(BluetoothGattCharacteristic ch, CancellationToken ct)
    {
        var gatt = _gatt!;
        if (!gatt.SetCharacteristicNotification(ch, true))
            throw new InvalidOperationException("Benachrichtigungen nicht einschaltbar.");
        var cccd = ch.GetDescriptor(CccdUuid) ?? throw new InvalidOperationException("Benachrichtigungs-Deskriptor fehlt.");
        byte[] enable = [0x01, 0x00];
        return Op(() =>
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                return gatt.WriteDescriptor(cccd, enable) == (int)CurrentBluetoothStatusCodes.Success;
#pragma warning disable CA1422 // ältere Android-Versionen
            cccd.SetValue(enable);
            return gatt.WriteDescriptor(cccd);
#pragma warning restore CA1422
        }, "Benachrichtigung", ct);
    }

    /// <summary>Schreiben ohne Antwort; wartet auf die Flusskontrolle (OnCharacteristicWrite).</summary>
    private async Task<bool> WriteAsync(BluetoothGattCharacteristic? ch, byte[] data, CancellationToken ct)
    {
        var gatt = _gatt;
        if (ch is null || gatt is null || Volatile.Read(ref _closed) != 0)
            return false;
        try
        {
            await Op(() =>
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                    return gatt.WriteCharacteristic(ch, data, (int)GattWriteType.NoResponse) == (int)CurrentBluetoothStatusCodes.Success;
#pragma warning disable CA1422
                ch.WriteType = GattWriteType.NoResponse;
                ch.SetValue(data);
                return gatt.WriteCharacteristic(ch);
#pragma warning restore CA1422
            }, "Schreiben", ct, 1000).ConfigureAwait(false);
            return true;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    // ---------- Befehle ----------

    private async Task<byte[]?> SendCommandAsync(byte[] command, CancellationToken ct, uint? memoryAddress = null,
        int attempts = CommandAttempts)
    {
        await _commandLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                var reply = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending = new Pending(command[0], command[3], memoryAddress, reply);
                if (await WriteAsync(_command, command, ct).ConfigureAwait(false))
                {
                    var done = await Task.WhenAny(reply.Task, Task.Delay(CommandTimeoutMs, ct)).ConfigureAwait(false);
                    if (done == reply.Task)
                        return await reply.Task.ConfigureAwait(false);
                }
                await Task.Delay(120 * attempt, ct).ConfigureAwait(false);
            }
            if (attempts > 1)
                Log.Warn($"{Address}: keine Antwort auf Befehl {command[0]:X2}/{command[3]:X2}");
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
        if (await SendCommandAsync(command, ct).ConfigureAwait(false) is null)
            throw new InvalidOperationException($"Controller antwortet nicht (Befehl {command[0]:X2}/{command[3]:X2}).");
    }

    private async Task<byte[]?> ReadMemoryAsync(uint address, byte length, CancellationToken ct)
    {
        var response = await SendCommandAsync(Commands.ReadMemory(address, length), ct, address).ConfigureAwait(false);
        return response is not null && Commands.TryParseMemoryRead(response, address, out var data) ? data : null;
    }

    private async Task ReadCalibrationAsync(CancellationToken ct)
    {
        var cal = new DeviceCalibration();
        if (await ReadStickAsync(Commands.AddrUserLeftStickCalibration, Commands.AddrLeftStickCalibration, ct) is { } l)
            cal = cal with { Left = l };
        if (await ReadStickAsync(Commands.AddrUserRightStickCalibration, Commands.AddrRightStickCalibration, ct) is { } r)
            cal = cal with { Right = r };
        else if (Kind == ControllerKind.JoyCon2Right)
            cal = cal with { Right = cal.Left }; // einziger Stick: Kalibrierung im ersten Block
        if (await ReadMemoryAsync(Commands.AddrGyroCalibration, 0x10, ct) is { } g && GyroBias.TryParse(g, out var bias))
            cal = cal with { Gyro = bias };
        if (Kind == ControllerKind.GameCube2 && await ReadMemoryAsync(Commands.AddrTriggerCalibration, 2, ct) is { Length: 2 } t
            && t[0] < 128 && t[1] < 128)
            cal = cal with { TriggerZeroLeft = t[0], TriggerZeroRight = t[1] };
        Calibration = cal;
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

    /// <summary>Seriennummer (Block 0x13000, ASCII ab Byte 2) und Firmware-Version.</summary>
    private async Task ReadDeviceInfoAsync(CancellationToken ct)
    {
        if (await SendCommandAsync(Commands.FirmwareVersion(), ct).ConfigureAwait(false) is { Length: >= 12 } fw)
            Firmware = $"{fw[9]}.{fw[10]}.{fw[11]}";
        if (await ReadMemoryAsync(Commands.AddrDeviceInfo, 0x40, ct).ConfigureAwait(false) is { Length: >= 18 } d)
        {
            string serial = Encoding.ASCII.GetString(d, 2, 16).TrimEnd('\0', ' ', '\xFF');
            if (serial.Length > 0 && serial.All(c => c is >= ' ' and <= '~'))
                SerialNumber = serial;
        }
    }

    /// <summary>
    /// Koppelt den Controller mit diesem Gerät (Nintendo-Verfahren, Befehl 0x15). Danach wirbt er nach einem Tastendruck
    /// für dieses Gerät und verbindet sich ohne SYNC. Er merkt sich nur EINEN Host: für Switch 2 oder PC dort erneut SYNC.
    /// </summary>
    private async Task<bool> PairWithHostAsync(ulong hostAddress, CancellationToken ct)
    {
        if (await ReadMemoryAsync(Commands.AddrPairing, 0x40, ct).ConfigureAwait(false) is { } block
            && PairingRecord.TryParse(block, out var existing) && existing.HostAddresses.Contains(hostAddress))
            return true;
        var pairing = new Pairing();
        if (await SendCommandAsync(Pairing.AddressesRequest(hostAddress), ct) is null
            || await SendCommandAsync(pairing.KeysRequest(), ct) is not { } keys
            || !pairing.TryDeriveKey(keys, out var ltk)
            || await SendCommandAsync(pairing.ConfirmRequest(), ct) is not { } confirm
            || !pairing.VerifyConfirmation(confirm, ltk))
        {
            Log.Warn($"{Address}: Kopplung mit diesem Gerät fehlgeschlagen – Wiederverbinden nur per SYNC");
            return false;
        }
        bool ok = await SendCommandAsync(Pairing.FinishRequest(), ct).ConfigureAwait(false) is not null;
        Log.Info($"{Address}: mit diesem Gerät gekoppelt: {ok}");
        return ok;
    }

    public Task SetPlayerAsync(int playerIndex) =>
        Volatile.Read(ref _closed) == 1 ? Task.CompletedTask
            : SendCommandAsync(Commands.SetPlayerLeds(Commands.PlayerLedMask(playerIndex)), _cts.Token);

    /// <summary>Kurzes „Verbunden“-Klicken (eingebautes Muster des Controllers).</summary>
    public Task PlayConnectFeedbackAsync() => SendCommandAsync(Commands.PlayConnectSample(), _cts.Token);

    // ---------- Rückrufe ----------

    /// <summary>
    /// Benachrichtigung zuordnen – über das Merkmal-Objekt selbst, nicht über <c>Uuid</c>: Jeder Uuid-Abruf erzeugt ein
    /// neues Java-Objekt; bei ~90 Eingaben/s häufen sich sonst Java-Verweise, die Laufzeit erzwingt ständig
    /// Speicherbereinigungen und die App friert ein.
    /// </summary>
    private void OnNotify(BluetoothGattCharacteristic? characteristic, byte[]? data)
    {
        if (data is null || characteristic is null)
            return;
        if (_input is not null && (ReferenceEquals(characteristic, _input) || characteristic.Equals(_input)))
            OnInput(data);
        else if (_response is not null && (ReferenceEquals(characteristic, _response) || characteristic.Equals(_response)))
            OnResponse(data);
    }

    private void OnResponse(byte[] data)
    {
        if (_pending is not { } p || !Commands.IsResponseTo(data, p.Command, p.Subcommand))
            return;
        if (p.Address is { } address && !Commands.TryParseMemoryRead(data, address, out _))
            return; // verspätete Antwort auf einen früheren Lesebefehl
        p.Reply.TrySetResult(data);
    }

    private void OnInput(byte[] data)
    {
        if (!InputReports.TryParseReport05(data, out var state, Kind))
        {
            if (_parseFailures++ % 200 == 0)
                Log.Warn($"{Address}: Eingabebericht nicht lesbar ({data.Length} Byte: {Convert.ToHexString(data.AsSpan(0, Math.Min(16, data.Length)))}…) – MTU zu klein?");
            return;
        }
        _lastInputTicks = Environment.TickCount64;
        _totalReports++;
        LastState = state;
        _reports++;
        long now = Environment.TickCount64;
        if (now - _rateWindowStart >= 1000)
        {
            ReportRate = _reports * 1000.0 / (now - _rateWindowStart);
            _reports = 0;
            _rateWindowStart = now;
        }
        try
        {
            StateReceived?.Invoke(this, state);
        }
        catch (Exception e)
        {
            Log.Error($"{Address}: Eingabe verarbeiten", e);
        }
    }

    private void OnConnectionChanged(GattStatus status, ProfileState state)
    {
        if (state == ProfileState.Connected && status == GattStatus.Success)
        {
            _connected?.TrySetResult(true);
            return;
        }
        if (state == ProfileState.Disconnected)
        {
            _connected?.TrySetResult(false);
            _op?.TrySetResult(false);
            if (Volatile.Read(ref _closed) == 0)
            {
                Log.Info($"{Address}: Bluetooth getrennt (Status {(int)status})");
                RaiseLost();
            }
        }
    }

    private async Task WatchdogAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
                if (Environment.TickCount64 - _lastInputTicks > InputTimeoutMs)
                {
                    Log.Warn($"{Address}: seit {InputTimeoutMs} ms keine Eingaben – Verbindung verloren " +
                             $"(bisher {_totalReports} Berichte, {_parseFailures} unlesbar)");
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
            return;
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
                    await WriteAsync(_rumble, Rumble.BuildPacket(Kind, large, small, counter++, _rumbleStrength), ct).ConfigureAwait(false);
                    await Task.Delay(RumbleIntervalMs, ct).ConfigureAwait(false);
                }
                else
                {
                    if (wasActive)
                        await WriteAsync(_rumble, Rumble.BuildPacket(Kind, 0, 0, counter++, 0), ct).ConfigureAwait(false);
                    while (_rumbleSignal.CurrentCount > 0)
                        _rumbleSignal.Wait(0);
                    await _rumbleSignal.WaitAsync(1000, ct).ConfigureAwait(false);
                }
                wasActive = active;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RaiseLost()
    {
        if (Interlocked.Exchange(ref _lostRaised, 1) == 0)
            Lost?.Invoke(this);
    }

    private static UUID Uuid(Guid g) => UUID.FromString(g.ToString())!;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return;
        _cts.Cancel();
        try
        {
            _gatt?.Disconnect();
            _gatt?.Close();
        }
        catch (Exception)
        {
            // Bluetooth evtl. schon aus
        }
        _gatt = null;
    }

    /// <summary>Android-Rückrufe → Gerät.</summary>
    private sealed class GattEvents(Switch2Device owner) : BluetoothGattCallback
    {
        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState) =>
            owner.OnConnectionChanged(status, newState);

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status) =>
            owner._op?.TrySetResult(status == GattStatus.Success);

        public override void OnMtuChanged(BluetoothGatt? gatt, int mtu, GattStatus status)
        {
            Log.Info($"{owner.Address}: MTU {mtu}");
            owner._op?.TrySetResult(true); // auch bei kleinerer MTU weiter; der Controller handelt meist 247 aus
        }

        public override void OnDescriptorWrite(BluetoothGatt? gatt, BluetoothGattDescriptor? descriptor, GattStatus status) =>
            owner._op?.TrySetResult(status == GattStatus.Success);

        public override void OnCharacteristicWrite(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status) =>
            owner._op?.TrySetResult(status == GattStatus.Success);

        // Ab Android 13 kommt der Wert direkt mit; der alte Rückruf wird dann nicht zusätzlich ausgewertet.
        public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value) =>
            owner.OnNotify(characteristic, value);

#pragma warning disable CA1422, CS0672
        public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic)
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(33))
                owner.OnNotify(characteristic, characteristic?.GetValue());
        }
#pragma warning restore CA1422, CS0672
    }
}
