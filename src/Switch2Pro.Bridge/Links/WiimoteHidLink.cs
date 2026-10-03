using Switch2Pro.Bridge.Usb;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge.Links;

/// <summary>
/// Wii-Fernbedienung (mit Nunchuk/Classic Controller) bzw. Wii U Pro Controller, in Windows per Bluetooth
/// gekoppelt (siehe <see cref="WiiPairing"/>). Ablauf nach WiiBrew: Status abfragen → Erweiterung
/// initialisieren (0x55 → 0xA400F0, 0x00 → 0xA400FB) und erkennen → Datenformat 0x35 (bzw. 0x34 beim
/// Wii U Pro) dauernd senden lassen. Nach jedem Statusbericht muss das Format neu gesetzt werden.
/// </summary>
internal sealed class WiimoteHidLink : IControllerLink
{
    private const int InputTimeoutMs = 3000;
    private const int StatusIntervalMs = 30_000;

    private readonly HidChannel _hid;
    private readonly CancellationTokenSource _cts = new();
    private readonly RateMeter _rate = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _setupLock = new(1, 1);
    private TaskCompletionSource<byte[]>? _pendingRead;
    private bool _useSetOutputReport;
    private volatile bool _rumble;
    private int _player = -1;
    private int _battery = -1;
    private long _lastInputTicks = Environment.TickCount64;
    private int _closed, _lostRaised;
    private WiiExtension _extension = WiiExtension.None;
    private bool _extensionPlugged;

    public ControllerKind Kind { get; private set; }
    public Transport Transport => Transport.Bluetooth;
    public string Id { get; }
    public string? Address { get; }
    public DeviceCalibration Calibration { get; private set; } = Wii.DefaultCalibration(WiiExtension.None);
    public ControllerInfo Info { get; private set; } = new();
    public ControllerState? LastState { get; private set; }
    public double ReportRate => _rate.Rate;
    public bool IsLost => Volatile.Read(ref _lostRaised) == 1;
    /// <summary>Was gerade an der Fernbedienung steckt (für die Anzeige).</summary>
    public WiiExtension Extension => _extension;

    public event Action<IControllerLink, ControllerState>? StateReceived;
    public event Action<IControllerLink>? Lost;

    private WiimoteHidLink(string path, HidChannel hid, ControllerKind kind)
    {
        Id = path;
        _hid = hid;
        Kind = kind;
        Address = AddressFromPath(path);
    }

    /// <summary>Bluetooth-Adresse aus dem HID-Pfad (…_{adresse} am Ende des Bluetooth-Geräteteils), sonst null.</summary>
    private static string? AddressFromPath(string path)
    {
        var m = System.Text.RegularExpressions.Regex.Match(path, @"&([0-9a-f]{12})[_#&]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;
        var hex = m.Groups[1].Value.ToUpperInvariant();
        return string.Join(':', Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }

    public static async Task<WiimoteHidLink> ConnectAsync(string path, CancellationToken ct)
    {
        var hid = new HidChannel(path);
        var link = new WiimoteHidLink(path, hid, ControllerKind.WiiRemote);
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

    private async Task StartAsync(CancellationToken ct)
    {
        Task.Run(() => ReadLoopAsync(_cts.Token)).Forget($"{Id}: Wii-Eingaben");
        // Status abfragen: Antwort zeigt, ob eine Erweiterung steckt (und ob die Verbindung steht).
        await SendAsync(Wii.StatusRequest(false), ct);
        var deadline = Environment.TickCount64 + 2000;
        while (_battery < 0 && Environment.TickCount64 < deadline)
            await Task.Delay(50, ct);
        if (_battery < 0)
            throw new IOException($"{Id}: Wii-Fernbedienung antwortet nicht");
        await SetupExtensionAsync(ct);
        _lastInputTicks = Environment.TickCount64;
        Task.Run(() => WatchdogAsync(_cts.Token)).Forget($"{Id}: Wii-Überwachung");
        Log.Info($"{Id}: {Kind.DisplayName()} bereit (Erweiterung: {_extension}, Akku {_battery} %)");
    }

    /// <summary>Erweiterung initialisieren und erkennen, dann das passende Datenformat einstellen.</summary>
    private async Task SetupExtensionAsync(CancellationToken ct)
    {
        await _setupLock.WaitAsync(ct);
        try
        {
            var ext = WiiExtension.None;
            if (_extensionPlugged)
            {
                await SendAsync(Wii.WriteRegister(Wii.RegExtensionInit1, [0x55], _rumble), ct);
                await Task.Delay(30, ct);
                await SendAsync(Wii.WriteRegister(Wii.RegExtensionInit2, [0x00], _rumble), ct);
                await Task.Delay(30, ct);
                if (await ReadAsync(Wii.RegExtensionId, 6, ct) is { Length: >= 6 } id)
                    ext = Wii.ExtensionFromId(id);
            }
            if (ext != _extension)
                Log.Info($"{Id}: Erweiterung {_extension} → {ext}");
            _extension = ext;
            Kind = ext == WiiExtension.WiiUPro ? ControllerKind.WiiUPro : ControllerKind.WiiRemote;
            Calibration = Wii.DefaultCalibration(ext);
            byte mode = ext == WiiExtension.WiiUPro ? Wii.ModeButtonsExt19 : Wii.ModeButtonsAccelExt;
            await SendAsync(Wii.SetMode(mode, _rumble), ct);
            await SendAsync(Wii.Leds(_player, _rumble), ct);
        }
        finally
        {
            _setupLock.Release();
        }
    }

    private async Task<byte[]?> ReadAsync(uint address, ushort size, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRead = tcs;
        await SendAsync(Wii.ReadRegister(address, size, _rumble), ct);
        var done = await Task.WhenAny(tcs.Task, Task.Delay(500, ct));
        _pendingRead = null;
        return done == tcs.Task ? await tcs.Task : null;
    }

    /// <summary>Senden per WriteFile; lehnt der Treiber ab, ab dann über HidD_SetOutputReport.</summary>
    private async Task SendAsync(byte[] report, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            if (!_useSetOutputReport)
            {
                try
                {
                    await _hid.WriteAsync(report, ct);
                    return;
                }
                catch (IOException e)
                {
                    Log.Info($"{Id}: Schreiben per WriteFile abgelehnt ({e.Message}) – nutze SetOutputReport");
                    _useSetOutputReport = true;
                }
            }
            if (!_hid.SetOutputReport(report))
                throw new IOException($"{Id}: Ausgabebericht 0x{report[0]:X2} nicht gesendet");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ---------- Eingaben ----------

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[Math.Max(32, _hid.InputLength)];
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
                Handle(buffer.AsSpan(0, n).ToArray());
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException e)
        {
            Log.Info($"{Id}: Wii-Verbindung beendet ({e.Message})");
            RaiseLost();
        }
    }

    private void Handle(byte[] r)
    {
        _lastInputTicks = Environment.TickCount64;
        switch (r[0])
        {
            case Wii.InputStatus when Wii.TryParseStatus(r, out bool ext, out int battery):
            {
                bool changed = ext != _extensionPlugged;
                _extensionPlugged = ext;
                _battery = battery;
                // Nach jedem Statusbericht sendet die Fernbedienung nur noch Tasten: Format neu setzen,
                // bei geänderter Erweiterung diese neu erkennen.
                if (Volatile.Read(ref _closed) == 0 && LastState is not null)
                    SetupExtensionAsync(_cts.Token).Forget($"{Id}: Erweiterung {(changed ? "erkennen" : "Format setzen")}");
                return;
            }
            case Wii.InputRead when Wii.TryParseRead(r, out int error, out _, out var data):
                _pendingRead?.TrySetResult(error == 0 ? data : []);
                return;
            case Wii.InputAck:
                return;
        }
        if (!Wii.TryParseData(r, _extension, _battery, out var state))
            return;
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
            long lastStatus = Environment.TickCount64;
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);
                if (Environment.TickCount64 - _lastInputTicks > InputTimeoutMs)
                {
                    Log.Warn($"{Id}: seit {InputTimeoutMs} ms keine Wii-Eingaben – Verbindung verloren");
                    RaiseLost();
                    return;
                }
                if (Environment.TickCount64 - lastStatus > StatusIntervalMs)
                {
                    lastStatus = Environment.TickCount64;
                    await SendAsync(Wii.StatusRequest(_rumble), ct); // Akku aktualisieren
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            RaiseLost();
        }
    }

    public Task SetPlayerAsync(int playerIndex)
    {
        _player = playerIndex;
        return Volatile.Read(ref _closed) == 1 ? Task.CompletedTask : SendAsync(Wii.Leds(playerIndex, _rumble), _cts.Token);
    }

    /// <summary>Die Fernbedienung hat nur einen einfachen Motor: an ab ~15 % Stärke.</summary>
    public void SetRumble(byte large, byte small, float strength)
    {
        bool on = Math.Max(large, small) * strength > 40;
        if (on == _rumble || Volatile.Read(ref _closed) == 1)
            return;
        _rumble = on;
        SendAsync(Wii.Leds(_player, on), _cts.Token).Forget($"{Id}: Vibration");
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
        try
        {
            if (_rumble)
                await SendAsync(Wii.Leds(_player, false), CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(200));
        }
        catch (Exception)
        {
            // Verbindung schon weg.
        }
        _cts.Cancel();
        _hid.Dispose();
    }
}
