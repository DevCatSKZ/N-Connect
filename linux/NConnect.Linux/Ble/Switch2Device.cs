using System.Text;
using Switch2Pro.Protocol;

namespace NConnect.Linux.Ble;

/// <summary>
/// Verbindung zu einem Switch-2-Controller (Pro Controller 2, Joy-Con 2, GameCube) – Portierung der Android-Fassung.
/// Transport: eigener ATT-Client (<see cref="AttClient"/>), nicht BlueZs GATT-Dienst – dessen Diensteabfrage hängt bei
/// diesen Controllern (keine Antwort auf „Secondary Services“). Ablauf: verbinden → MTU → kurzes Verbindungsintervall
/// → Dienst und Merkmale → Antworten abonnieren → Funktionen einschalten → Eingaben abonnieren → Kalibrierung und
/// Gerätedaten → ggf. mit diesem Gerät koppeln (Befehl 0x15, danach ohne SYNC).
/// </summary>
public sealed class Switch2Device : IAsyncDisposable
{
    private const int CommandTimeoutMs = 600;
    private const int CommandAttempts = 3;
    private const int InputTimeoutMs = 2500;
    private const int RumbleIntervalMs = 12;

    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private AttClient? _att;
    private ushort? _command, _rumble;
    private ushort _responseHandle, _inputHandle;
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
    public string Address { get; }
    public DeviceCalibration Calibration { get; private set; } = DeviceCalibration.Default;
    public string? SerialNumber { get; private set; }
    public string? Firmware { get; private set; }
    public ControllerState? LastState { get; private set; }
    public double ReportRate { get; private set; }
    public bool IsLost => Volatile.Read(ref _lostRaised) == 1;

    public event Action<Switch2Device, ControllerState>? StateReceived;
    public event Action<Switch2Device>? Lost;

    /// <summary>Nur das Objekt, noch ohne Verbindung (verbunden wird in <see cref="ConnectAsync"/>; Tests nutzen es direkt).</summary>
    internal Switch2Device(string address, ControllerKind kind)
    {
        Address = address;
        Kind = kind;
    }

    /// <summary>
    /// Verbindet und richtet ein. <paramref name="adapterAddress"/>/<paramref name="adapterIndex"/>: eigener Adapter
    /// (hci0 = 0). <paramref name="pairHost"/> ≠ 0: mit dieser Adresse koppeln.
    /// </summary>
    public static async Task<Switch2Device> ConnectAsync(string adapterAddress, int adapterIndex, string address,
        byte addressType, ControllerKind kind, ulong pairHost, CancellationToken ct)
    {
        var dev = new Switch2Device(address, kind);
        try
        {
            await dev.StartAsync(adapterAddress, adapterIndex, addressType, pairHost, ct).ConfigureAwait(false);
            return dev;
        }
        catch
        {
            await dev.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task StartAsync(string adapterAddress, int adapterIndex, byte addressType, ulong pairHost, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                _att = await AttClient.ConnectAsync(adapterAddress, Address, addressType, TimeSpan.FromSeconds(10), ct)
                    .ConfigureAwait(false);
                break;
            }
            catch (Exception e) when (attempt < 3 && !ct.IsCancellationRequested)
            {
                Log.Warn($"{Address}: Verbindungsversuch {attempt} fehlgeschlagen: {Log.Reason(e)}");
                await Task.Delay(400 * attempt, ct).ConfigureAwait(false);
            }
        }
        var att = _att!;
        att.Notified += OnNotify;
        att.Lost += () =>
        {
            if (Volatile.Read(ref _closed) == 0)
            {
                Log.Info($"{Address}: Bluetooth getrennt");
                RaiseLost();
            }
        };
        Log.Info($"{Address}: verbunden, richte ein …");

        // Eingabebericht 0x05 ist ~64 Byte; mit der Standard-MTU (23) würde er abgeschnitten.
        await att.ExchangeMtuAsync(512, ct).ConfigureAwait(false);
        bool fast = att.RequestConnectionInterval(adapterIndex);
        Log.Info($"{Address}: MTU {att.Mtu}, kurzes Verbindungsintervall {(fast ? "angefordert" : "nicht möglich")}");

        var range = await att.FindServiceAsync(Gatt.HidService, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Controller-Dienst nicht gefunden.");
        var chars = await att.DiscoverCharacteristicsAsync(range.Start, range.End, ct).ConfigureAwait(false);
        AttClient.Characteristic Need(Guid uuid, string what) =>
            chars.FirstOrDefault(c => c.Uuid == uuid) ?? throw new InvalidOperationException($"{what} fehlt.");
        _command = Need(Gatt.CommandOutput, "Befehlskanal").Value;
        var response = Need(Gatt.CommandResponse, "Antwortkanal");
        var input = Need(Gatt.InputReportCommon, "Eingabekanal");
        _rumble = chars.FirstOrDefault(c => c.Uuid == Gatt.RumbleOutput(Kind))?.Value;
        _responseHandle = response.Value;
        _inputHandle = input.Value;
        Log.Info($"{Address}: Dienst gefunden ({chars.Count} Merkmale)");

        await SubscribeAsync(response, chars, range.End, ct).ConfigureAwait(false);
        byte mask = Commands.FeatureMask(Kind);
        await SendRequiredAsync(Commands.SetFeatures(mask), ct).ConfigureAwait(false);
        await SendRequiredAsync(Commands.EnableFeatures(mask), ct).ConfigureAwait(false);
        Log.Info($"{Address}: Funktionen eingeschaltet (Maske {mask:X2})");
        await SubscribeAsync(input, chars, range.End, ct).ConfigureAwait(false);
        Log.Info($"{Address}: Eingaben abonniert");

        await ReadCalibrationAsync(ct).ConfigureAwait(false);
        await ReadDeviceInfoAsync(ct).ConfigureAwait(false);
        if (pairHost != 0)
            await PairWithHostAsync(pairHost, ct).ConfigureAwait(false);

        _lastInputTicks = Environment.TickCount64;
        _ = Task.Run(() => WatchdogAsync(_cts.Token));
        _ = Task.Run(() => RumbleLoopAsync(_cts.Token));
        Log.Info($"{Address}: {Kind.DisplayName()} bereit (Seriennr. {SerialNumber ?? "?"}, Firmware {Firmware ?? "?"})");
    }

    /// <summary>Benachrichtigungen einschalten: CCCD (0x2902) des Merkmals suchen und 01 00 schreiben.</summary>
    private async Task SubscribeAsync(AttClient.Characteristic ch, List<AttClient.Characteristic> all, ushort serviceEnd,
        CancellationToken ct)
    {
        var next = all.Where(c => c.Declaration > ch.Declaration).Select(c => c.Declaration).DefaultIfEmpty((ushort)(serviceEnd + 1)).Min();
        ushort cccd = await _att!.FindCccdAsync((ushort)(ch.Value + 1), (ushort)(next - 1), ct).ConfigureAwait(false)
                      ?? (ushort)(ch.Value + 1);
        await _att.WriteRequestAsync(cccd, [0x01, 0x00], ct).ConfigureAwait(false);
    }

    private void OnNotify(ushort handle, byte[] value)
    {
        if (handle == _inputHandle)
            OnInput(value);
        else if (handle == _responseHandle)
            OnResponse(value);
    }

    private Task<bool> WriteAsync(ushort? handle, byte[] data) =>
        Task.FromResult(handle is { } h && Volatile.Read(ref _closed) == 0 && _att?.WriteCommand(h, data) == true);


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
                if (await WriteAsync(_command, command).ConfigureAwait(false))
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
            cal = cal with { Right = cal.Left };
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

    /// <summary>Koppelt den Controller mit diesem Gerät (Befehl 0x15); danach verbindet er sich per Tastendruck.</summary>
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

    public Task PlayConnectFeedbackAsync() => SendCommandAsync(Commands.PlayConnectSample(), _cts.Token);

    // ---------- Rückrufe ----------

    private void OnResponse(byte[] data)
    {
        if (_pending is not { } p || !Commands.IsResponseTo(data, p.Command, p.Subcommand))
            return;
        if (p.Address is { } address && !Commands.TryParseMemoryRead(data, address, out _))
            return;
        p.Reply.TrySetResult(data);
    }

    private void OnInput(byte[] data)
    {
        if (!InputReports.TryParseReport05(data, out var state, Kind))
        {
            if (_parseFailures++ % 200 == 0)
                Log.Warn($"{Address}: Eingabebericht nicht lesbar ({data.Length} Byte)");
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
                    await WriteAsync(_rumble, Rumble.BuildPacket(Kind, large, small, counter++, _rumbleStrength)).ConfigureAwait(false);
                    await Task.Delay(RumbleIntervalMs, ct).ConfigureAwait(false);
                }
                else
                {
                    if (wasActive)
                        await WriteAsync(_rumble, Rumble.BuildPacket(Kind, 0, 0, counter++, 0)).ConfigureAwait(false);
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

    internal void RaiseLost()
    {
        if (Interlocked.Exchange(ref _lostRaised, 1) == 0)
            Lost?.Invoke(this);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return ValueTask.CompletedTask;
        _cts.Cancel();
        _att?.Dispose(); // Socket zu = Verbindung getrennt (der Kernel baut die LE-Verbindung ab)
        return ValueTask.CompletedTask;
    }
}
