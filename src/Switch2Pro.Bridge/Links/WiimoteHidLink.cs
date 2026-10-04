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
    /// <summary>Laufende Leseanfrage: untere 16 Bit der Adresse (so meldet die Antwort sie) und Ergebnis.</summary>
    private sealed record PendingRead(ushort AddressLow, TaskCompletionSource<byte[]> Reply);
    private volatile PendingRead? _pendingRead;
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

    private readonly Func<Settings> _settings;
    private readonly WiiParser _parser = new();

    private WiimoteHidLink(string path, HidChannel hid, ControllerKind kind, Func<Settings> settings)
    {
        Id = path;
        _hid = hid;
        Kind = kind;
        _settings = settings;
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

    public static async Task<WiimoteHidLink> ConnectAsync(string path, Func<Settings> settings, CancellationToken ct)
    {
        var hid = new HidChannel(path);
        var link = new WiimoteHidLink(path, hid, ControllerKind.WiiRemote, settings);
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

    private int _setupRetries;

    /// <summary>Erkennung in 2 s noch einmal (die Fernbedienung hat nicht geantwortet).</summary>
    private void RetrySetupLater() =>
        Task.Run(async () =>
        {
            await Task.Delay(2000, _cts.Token);
            if (Volatile.Read(ref _closed) == 0 && _extensionPlugged)
                await SetupExtensionAsync(_cts.Token);
        }).Forget($"{Id}: Erweiterung erneut erkennen");

    /// <summary>MotionPlus: null = noch nicht geprüft, sonst vorhanden ja/nein (wird nur einmal gesucht).</summary>
    private bool? _motionPlus;
    private bool _irOn;
    private bool _pointerWanted;

    /// <summary>
    /// Erweiterung initialisieren und erkennen (auch MotionPlus, ggf. mit Nunchuk/Classic dahinter), IR-Kamera für den
    /// Zeiger ein-/ausschalten und das passende Datenformat einstellen.
    /// </summary>
    private async Task SetupExtensionAsync(CancellationToken ct)
    {
        await _setupLock.WaitAsync(ct);
        try
        {
            var ext = WiiExtension.None;
            bool unanswered = false; // steckt eine Erweiterung, aber die Kennung kam nicht an (Funk überlastet)
            // 1) Ist schon ein MotionPlus aktiv? Dann steht seine Kennung im Erweiterungsregister – nichts neu einschalten.
            var current = _extensionPlugged ? await ReadAsync(Wii.RegExtensionId, 6, ct) : null;
            if (current is { Length: >= 6 } && Wii.ExtensionFromId(current).HasMotionPlus())
            {
                ext = Wii.ExtensionFromId(current);
            }
            else
            {
                // 2) Gewöhnliche Erweiterung: unverschlüsselt initialisieren (0x55 → F0, 0x00 → FB) und erkennen.
                if (_extensionPlugged)
                {
                    await SendAsync(Wii.WriteRegister(Wii.RegExtensionInit1, [0x55], _rumble), ct);
                    await Task.Delay(30, ct);
                    await SendAsync(Wii.WriteRegister(Wii.RegExtensionInit2, [0x00], _rumble), ct);
                    await Task.Delay(30, ct);
                    if (await ReadAsync(Wii.RegExtensionId, 6, ct) is { Length: >= 6 } id)
                        ext = Wii.ExtensionFromId(id);
                    else
                        unanswered = true;
                }
                if (unanswered)
                {
                    // Keine Antwort heißt nicht „keine Erweiterung“: bisherige behalten und gleich noch einmal versuchen.
                    if (Interlocked.Increment(ref _setupRetries) <= 5)
                    {
                        Log.Info($"{Id}: Erweiterung antwortet nicht – neuer Versuch");
                        RetrySetupLater();
                    }
                    ext = _extension;
                }
                else
                {
                    Interlocked.Exchange(ref _setupRetries, 0);
                }
                // 3) MotionPlus (Aufsatz oder „MotionPlus Inside“) suchen und einschalten – Nunchuk/Classic wird durchgereicht.
                if (!unanswered && _motionPlus != false && ext is WiiExtension.None or WiiExtension.Nunchuk or WiiExtension.Classic)
                    ext = await TryActivateMotionPlusAsync(ext, ct);
            }
            if (ext != _extension)
                Log.Info($"{Id}: Erweiterung {_extension} → {ext}");
            _extension = ext;
            Kind = ext == WiiExtension.WiiUPro ? ControllerKind.WiiUPro : ControllerKind.WiiRemote;
            Calibration = Wii.DefaultCalibration(ext);

            // IR-Kamera nur für den Zeiger (kostet Akku); der Wii U Pro hat keine.
            _pointerWanted = _settings().WiiPointerMouse && ext != WiiExtension.WiiUPro;
            if (_pointerWanted && !_irOn)
            {
                await SendAsync(Wii.IrPixelClock(_rumble), ct);
                await Task.Delay(50, ct);
                await SendAsync(Wii.IrLogic(_rumble), ct);
                await Task.Delay(50, ct);
                foreach (var step in Wii.IrSetup(_rumble))
                {
                    await SendAsync(step, ct);
                    await Task.Delay(50, ct);
                }
                _irOn = true;
                Log.Info($"{Id}: IR-Kamera für den Zeiger eingeschaltet");
            }
            else if (!_pointerWanted && _irOn)
            {
                await SendAsync([Wii.ReportIrPixelClock, (byte)(_rumble ? 1 : 0)], ct);
                await SendAsync([Wii.ReportIrLogic, (byte)(_rumble ? 1 : 0)], ct);
                _irOn = false;
            }

            byte mode = ext == WiiExtension.WiiUPro ? Wii.ModeButtonsExt19 : _irOn ? Wii.ModeButtonsAccelIrExt : Wii.ModeButtonsAccelExt;
            await SendAsync(Wii.SetMode(mode, _rumble), ct);
            await SendAsync(Wii.Leds(_player, _rumble), ct);
        }
        finally
        {
            _setupLock.Release();
        }
    }

    /// <summary>
    /// MotionPlus suchen (0x55 → 0xA600F0, Kennung bei 0xA600FA) und im passenden Modus einschalten (0xA600FE).
    /// Liefert die neue Erweiterungsart (MotionPlus…) oder unverändert <paramref name="behind"/>.
    /// </summary>
    private async Task<WiiExtension> TryActivateMotionPlusAsync(WiiExtension behind, CancellationToken ct)
    {
        await SendAsync(Wii.WriteRegister(Wii.RegMotionPlusInit, [0x55], _rumble), ct);
        await Task.Delay(30, ct);
        var id = await ReadAsync(Wii.RegMotionPlusId, 6, ct);
        if (id is null)
            return behind; // keine Antwort (Funk überlastet): beim nächsten Mal wieder suchen
        if (id.Length < 6 || !Wii.IsInactiveMotionPlus(id))
        {
            _motionPlus = false;
            return behind;
        }
        _motionPlus = true;
        await SendAsync(Wii.WriteRegister(Wii.RegMotionPlusActivate, [Wii.MotionPlusMode(behind)], _rumble), ct);
        await Task.Delay(150, ct);
        if (await ReadAsync(Wii.RegExtensionId, 6, ct) is { Length: >= 6 } active && Wii.ExtensionFromId(active).HasMotionPlus())
        {
            _extensionPlugged = true;
            return Wii.ExtensionFromId(active);
        }
        Log.Info($"{Id}: MotionPlus gefunden, ließ sich aber nicht einschalten");
        return behind;
    }

    /// <summary>
    /// Speicher der Fernbedienung lesen; null, wenn keine Antwort kam. Bei vielen Bluetooth-Controllern kommen Antworten
    /// deutlich später (gemessen: ~11 statt 100 Berichte/s) – daher großzügig warten, mehrfach fragen und Antworten
    /// über die Adresse zuordnen (eine verspätete Antwort darf nicht als Antwort auf die nächste Anfrage gelten).
    /// </summary>
    private async Task<byte[]?> ReadAsync(uint address, ushort size, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var pending = new PendingRead((ushort)address, new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously));
            _pendingRead = pending;
            await SendAsync(Wii.ReadRegister(address, size, _rumble), ct);
            var done = await Task.WhenAny(pending.Reply.Task, Task.Delay(1500, ct));
            _pendingRead = null;
            if (done == pending.Reply.Task)
                return await pending.Reply.Task;
        }
        Log.Info($"{Id}: keine Antwort beim Lesen von 0x{address:X6}");
        return null;
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
                    Log.Info($"{Id}: Schreiben per WriteFile abgelehnt ({Log.Reason(e)}) – nutze SetOutputReport");
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
            Log.Info($"{Id}: Wii-Verbindung beendet ({Log.Reason(e)})");
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
                {
                    if (changed)
                        SetupExtensionAsync(_cts.Token).Forget($"{Id}: Erweiterung erkennen");
                    else
                        SendAsync(Wii.SetMode(_extension == WiiExtension.WiiUPro ? Wii.ModeButtonsExt19 : _irOn ? Wii.ModeButtonsAccelIrExt : Wii.ModeButtonsAccelExt, _rumble), _cts.Token)
                            .Forget($"{Id}: Datenformat setzen");
                }
                return;
            }
            case Wii.InputRead when Wii.TryParseRead(r, out int error, out ushort addressLow, out var data):
                if (_pendingRead is { } pending && pending.AddressLow == addressLow)
                    pending.Reply.TrySetResult(error == 0 ? data : []);
                return;
            case Wii.InputAck:
                return;
        }
        if (!_parser.TryParse(r, _extension, _battery, out var state))
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
                // Zeiger in den Einstellungen ein-/ausgeschaltet: IR-Kamera und Datenformat anpassen.
                if (_extension != WiiExtension.WiiUPro && _settings().WiiPointerMouse != _pointerWanted)
                    await SetupExtensionAsync(ct);
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
