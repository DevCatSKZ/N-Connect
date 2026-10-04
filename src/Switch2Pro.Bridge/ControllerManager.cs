using System.Collections.Concurrent;
using Switch2Pro.Bridge.Links;
using Switch2Pro.Bridge.Usb;
using Switch2Pro.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Radios;
using Windows.Security.Cryptography;

namespace Switch2Pro.Bridge;

/// <summary>
/// Findet alle unterstützten Controller und ordnet sie Spielern zu:
/// Switch-2-Controller über ihre Bluetooth-LE-Werbung, Switch-1-Controller über die HID-Geräte,
/// die Windows nach der normalen Bluetooth-Kopplung bereitstellt. Zwei Joy-Con werden automatisch
/// zu einem Controller zusammengefasst.
/// </summary>
internal sealed class ControllerManager : IAsyncDisposable
{
    private const int MaxPlayers = 8;

    private readonly Func<Settings> _settings;
    private readonly PadFactory _factory;
    private readonly BluetoothLEAdvertisementWatcher _watcher = new() { ScanningMode = BluetoothLEScanningMode.Active };
    private readonly ConcurrentDictionary<string, IControllerLink> _links = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _connecting = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _retryAfter = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Von Hand (oder wegen Inaktivität) getrennte Controller → Zeitpunkt der letzten Werbung. Solange der Controller
    /// ohne Pause weiter wirbt, wird er nicht wieder verbunden; erst ein neuer Tastendruck (Werbung nach einer Pause) verbindet.
    /// </summary>
    private readonly ConcurrentDictionary<string, long> _suppressed = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Timer _inactivityTimer;
    /// <summary>Geräte, deren Verbindungsfehler schon gemeldet wurde (nicht bei jedem neuen Versuch wieder).</summary>
    private readonly ConcurrentDictionary<string, byte> _unreachable = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Von Hand getrennte USB-Controller (bis das Kabel abgezogen wird).</summary>
    private readonly ConcurrentDictionary<string, byte> _usbSuppressed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Player> _players = [];
    private readonly object _playerGate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly System.Threading.Timer _restartTimer;
    private readonly List<Task> _tasks = [];
    private volatile bool _disposed;

    public event Action? Changed;
    public event Action<string>? Notify;
    /// <summary>Ein Joy-Con wurde getrennt (true) bzw. wieder zum Paar gefügt (false) – Adresse zum Merken.</summary>
    public event Action<string, bool>? JoyConModeChanged;
    /// <summary>Ein Switch-2-Controller wurde verbunden (Adresse, Art) – zum Merken für das Wiederverbinden per Tastendruck.</summary>
    public event Action<string, ControllerKind>? ControllerConnected;

    public string? AdapterProblem { get; private set; }

    /// <summary>Bluetooth-Adresse dieses PCs (für Kopplung und Wiederverbinden per Tastendruck).</summary>
    private ulong _hostAddress;

    public ControllerManager(Func<Settings> settings, PadFactory factory)
    {
        _settings = settings;
        _factory = factory;
        _watcher.Received += OnAdvertisement;
        _watcher.Stopped += OnWatcherStopped;
        _restartTimer = new System.Threading.Timer(_ => StartBluetoothAsync().Forget("Bluetooth-Suche starten"), null, Timeout.Infinite, Timeout.Infinite);
        _inactivityTimer = new System.Threading.Timer(_ => CheckInactivity(), null, 10_000, 10_000);
    }

    /// <summary>Einstellungen wurden hier geändert (z. B. Gyro kalibriert) und sollen gespeichert werden.</summary>
    public event Action? SaveRequested;

    public void RequestSave() => SaveRequested?.Invoke();

    /// <summary>Alle Controller eines Spielers trennen. Sie verbinden sich erst nach dem nächsten Tastendruck wieder.</summary>
    public void Disconnect(Player player, string reason = "getrennt")
    {
        foreach (var link in player.Links)
        {
            _suppressed[link.Id] = Environment.TickCount64;
            if (link.Transport == Transport.Usb)
                _usbSuppressed[link.Id] = 0; // am Kabel: erst nach Abziehen und Einstecken wieder verwenden
            Log.Info($"{link.Id}: {reason}");
            Track(Task.Run(async () =>
            {
                // Switch 1: schlafen legen (sonst hält Windows die Verbindung). Antwort kommt evtl. nicht mehr.
                var sleep = link.SleepAsync();
                sleep.Forget($"{link.Id}: schlafen legen"); // Fehler auch nach Ablauf der Wartezeit beobachten
                await Task.WhenAny(sleep, Task.Delay(1000));
                OnLinkLost(link);
            }));
        }
    }

    /// <summary>Verbindungen, für die schon vor schwachem Bluetooth gewarnt wurde (einmal je Verbindung).</summary>
    private readonly ConcurrentDictionary<IControllerLink, byte> _weakWarned = new();
    private readonly ConcurrentDictionary<IControllerLink, long> _connectedSince = new();

    /// <summary>
    /// Schwache Bluetooth-Verbindung erkennen: Liefert ein Controller nach dem Verbinden dauerhaft weniger als 20 Berichte/s
    /// (normal: 33–60), stört meist etwas (USB-3-Geräte, Funkkopfhörer, schwacher Adapter) – einmal mit Tipp melden.
    /// </summary>
    private void CheckWeakConnections()
    {
        long now = Environment.TickCount64;
        foreach (var link in _links.Values)
        {
            // Wii sendet nur bei Änderung – wenige Berichte im Ruhezustand sind dort kein Zeichen schwachen Funks.
            if (link.Transport == Transport.Usb || link is DemoLink or WiimoteHidLink)
                continue;
            long since = _connectedSince.GetOrAdd(link, now);
            if (now - since < 20_000 || link.ReportRate >= 20 || link.ReportRate <= 0 || !_weakWarned.TryAdd(link, 0))
                continue;
            Log.Warn($"{link.Id}: schwache Verbindung ({link.ReportRate:F0} Berichte/s)");
            Notify?.Invoke($"{link.Kind.DisplayName()}: schwache Bluetooth-Verbindung ({link.ReportRate:F0} statt 33–60 Berichte/s). " +
                           "Tipp: Bluetooth-Stick per Verlängerung näher an den Controller, weg von USB-3-Anschlüssen und Funkkopfhörern.");
        }
        foreach (var gone in _connectedSince.Keys.Where(l => !_links.Values.Contains(l)).ToList())
        {
            _connectedSince.TryRemove(gone, out _);
            _weakWarned.TryRemove(gone, out _);
        }
    }

    private void CheckInactivity()
    {
        CheckWeakConnections();
        int minutes = _settings().InactivityMinutes;
        if (minutes <= 0 || _disposed)
            return;
        foreach (var player in Players)
        {
            // Am USB-Kabel lädt der Controller ohnehin – dort nicht trennen.
            if (Environment.TickCount64 - player.LastActivity < minutes * 60_000L || player.Links.Any(l => l.Transport == Transport.Usb))
                continue;
            Notify?.Invoke($"Spieler {player.Index + 1}: nach {minutes} min ohne Eingabe getrennt");
            Disconnect(player, $"nach {minutes} min ohne Eingabe getrennt");
        }
    }

    public IReadOnlyList<Player> Players
    {
        get { lock (_playerGate) return _players.OrderBy(p => p.Index).ToList(); }
    }

    public bool IsConnecting => !_connecting.IsEmpty;

    public async Task StartAsync()
    {
        await StartBluetoothAsync();
        Track(Task.Run(() => HidScanLoopAsync(_cts.Token)));
    }

    private void Track(Task task)
    {
        lock (_tasks)
        {
            _tasks.RemoveAll(t => t.IsCompleted);
            _tasks.Add(task);
        }
    }

    // ---------- Bluetooth LE (Switch 2) ----------

    private async Task StartBluetoothAsync()
    {
        if (_disposed)
            return;
        var problem = await CheckAdapterAsync();
        if (_disposed)
            return; // während der Prüfung beendet
        // Nur bei Änderung melden und protokollieren (die Prüfung läuft alle 5 s, solange ein Problem besteht).
        if (problem != AdapterProblem)
        {
            if (problem is not null)
            {
                Log.Warn(problem);
                Notify?.Invoke(problem + " " + AdapterHint(problem));
            }
            else if (AdapterProblem is not null)
            {
                Log.Info("Bluetooth-Adapter bereit");
                Notify?.Invoke("Bluetooth ist bereit – Controller können verbunden werden.");
            }
        }
        AdapterProblem = problem;
        Changed?.Invoke();
        if (AdapterProblem is not null)
        {
            _restartTimer.Change(5000, Timeout.Infinite);
            return;
        }
        try
        {
            if (_watcher.Status != BluetoothLEAdvertisementWatcherStatus.Started)
            {
                _watcher.Start();
                Log.Info("Suche nach Switch-2-Controllern gestartet");
            }
        }
        catch (Exception e)
        {
            Log.Error("Bluetooth-Suche konnte nicht starten", e);
            AdapterProblem = "Bluetooth-Suche konnte nicht starten.";
            Changed?.Invoke();
            _restartTimer.Change(5000, Timeout.Infinite);
        }
    }

    /// <summary>Was der Benutzer bei einem Bluetooth-Problem tun kann (für Einblendung und Hinweisleiste).</summary>
    public static string AdapterHint(string problem) => problem switch
    {
        "Bluetooth ist ausgeschaltet." => "Bluetooth in den Windows-Einstellungen einschalten. Controller per USB funktionieren weiterhin.",
        "Dieser Bluetooth-Adapter unterstützt kein Bluetooth LE." =>
            "Für Switch-2-Controller wird ein Adapter mit Bluetooth 4.0 oder neuer gebraucht. Andere Controller und USB funktionieren weiterhin.",
        _ => "Bluetooth-Adapter (z. B. USB-Stick) einstecken bzw. Bluetooth in Windows aktivieren. Controller per USB funktionieren weiterhin.",
    };

    private async Task<string?> CheckAdapterAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter is null)
                return "Kein Bluetooth-Adapter gefunden.";
            _hostAddress = adapter.BluetoothAddress;
            if (!adapter.IsLowEnergySupported || !adapter.IsCentralRoleSupported)
                return "Dieser Bluetooth-Adapter unterstützt kein Bluetooth LE.";
            var radio = await adapter.GetRadioAsync();
            if (radio is not null && radio.State != RadioState.On)
                return "Bluetooth ist ausgeschaltet.";
            return null;
        }
        catch (Exception e)
        {
            return $"Bluetooth nicht verfügbar: {e.Message}";
        }
    }

    private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        if (_disposed)
            return;
        Log.Warn($"Bluetooth-Suche beendet ({args.Error}) – neuer Versuch in 5 s");
        _restartTimer.Change(5000, Timeout.Infinite);
    }

    private void OnAdvertisement(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        if (_disposed)
            return;
        string id = Switch2BleLink.FormatAddress(args.BluetoothAddress);
        if (_links.ContainsKey(id) || _connecting.ContainsKey(id))
            return;
        if (_retryAfter.TryGetValue(id, out long until) && Environment.TickCount64 < until)
            return;

        foreach (var md in args.Advertisement.ManufacturerData)
        {
            if (md.CompanyId != Gatt.NintendoCompanyId)
                continue;
            CryptographicBuffer.CopyToByteArray(md.Data, out byte[] data);
            if (data is null)
                continue;
            var kind = Advertisement.Kind(md.CompanyId, data);
            if (kind == ControllerKind.Unknown)
                continue;

            var settings = _settings();
            if (!settings.IsAllowed(id))
                return;
            // SYNC (Host-Adresse leer) → immer verbinden und mit dem PC koppeln.
            // Tastendruck → der Controller wirbt nur für den Host, mit dem er gekoppelt ist. Ist das dieser PC,
            // verbinden; ist es eine Konsole, ignorieren – er würde die Verbindung ohnehin ablehnen (gemessen).
            ulong host = Advertisement.HostAddress(data);
            bool sync = host == 0;
            if (!sync && (host != _hostAddress || !settings.AutoReconnect))
                return;
            // Eben getrennt: weiter werbende Controller nicht gleich wieder verbinden – erst nach einer Pause
            // (Controller schläft) und neuem Tastendruck. SYNC verbindet immer.
            if (!sync && _suppressed.TryGetValue(id, out long lastSeen))
            {
                long now = Environment.TickCount64;
                if (now - lastSeen < 3000)
                {
                    _suppressed[id] = now;
                    return;
                }
                _suppressed.TryRemove(id, out _);
            }
            else if (sync)
            {
                _suppressed.TryRemove(id, out _);
            }
            if (!_connecting.TryAdd(id, 0))
                return;
            Track(ConnectBleAsync(args.BluetoothAddress, args.BluetoothAddressType, kind, id, sync));
            return;
        }
    }

    private async Task ConnectBleAsync(ulong address, BluetoothAddressType type, ControllerKind kind, string id, bool sync)
    {
        Changed?.Invoke();
        Log.Info($"{id}: {kind.DisplayName()} gefunden, verbinde …");
        try
        {
            await RemoveStaleWindowsPairingAsync(address, type, id);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var link = await Switch2BleLink.ConnectAsync(address, type, kind, timeout.Token);
            if (!Attach(link))
            {
                _retryAfter[id] = Environment.TickCount64 + 10000; // z. B. alle Spielerplätze belegt
                return;
            }
            ControllerConnected?.Invoke(id, kind);
            if (_settings().ConnectFeedback)
                link.PlayConnectFeedbackAsync().Forget($"{id}: Verbindungs-Vibration");
            // Nach SYNC mit diesem PC koppeln, damit später ein Tastendruck zum Verbinden reicht.
            if (sync && _hostAddress != 0 && _settings().AutoReconnect)
            {
                try
                {
                    bool paired = await link.PairWithHostAsync(_hostAddress);
                    // Einmalig erklären: Der Controller merkt sich nur einen Host – an der Switch 2 danach einmal neu koppeln.
                    var settings = _settings();
                    if (paired && !settings.ConsoleHintShown)
                    {
                        settings.ConsoleHintShown = true;
                        RequestSave();
                        Notify?.Invoke("Controller mit dem PC gekoppelt – ab jetzt reicht ein Tastendruck. Hinweis: Um ihn wieder " +
                                       "an der Switch 2 zu nutzen, dort einmal kurz SYNC drücken.");
                    }
                }
                catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
                {
                    Log.Info($"{id}: Kopplung abgebrochen (Verbindung beendet)");
                }
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        catch (Exception e)
        {
            Log.Error($"{id}: Verbindung fehlgeschlagen", e);
            _retryAfter[id] = Environment.TickCount64 + 3000;
        }
        finally
        {
            _connecting.TryRemove(id, out _);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Switch-2-Controller brauchen keine Windows-Kopplung. Ein Eintrag aus „Gerät hinzufügen“ schadet
    /// sogar: Windows verschlüsselt dann mit einem Schlüssel, den der Controller nach jedem SYNC mit
    /// der Konsole nicht mehr kennt, und die Verbindung bricht sofort wieder ab (gemessen). Daher
    /// wird so ein Eintrag automatisch entfernt.
    /// </summary>
    private static async Task RemoveStaleWindowsPairingAsync(ulong address, BluetoothAddressType type, string id)
    {
        try
        {
            using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address, type);
            if (device?.DeviceInformation.Pairing.IsPaired != true)
                return;
            var result = await device.DeviceInformation.Pairing.UnpairAsync();
            Log.Info($"{id}: alte Windows-Kopplung entfernt ({result.Status})");
            await Task.Delay(500);
        }
        catch (Exception e)
        {
            Log.Warn($"{id}: Windows-Kopplung nicht prüfbar: {Log.Reason(e)}");
        }
    }

    // ---------- HID (Switch 1 per Bluetooth/USB, Switch 2 per USB) ----------

    private async Task HidScanLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    ScanHid();
                }
                catch (Exception e)
                {
                    Log.Warn($"HID-Suche: {Log.Reason(e)}");
                }
                try
                {
                    ScanUsb();
                }
                catch (Exception e)
                {
                    Log.Warn($"USB-Suche: {Log.Reason(e)}");
                }
                await Task.Delay(2000, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ScanHid()
    {
        foreach (var path in UsbNative.GetInterfacePaths(UsbNative.HidInterface))
        {
            var kind = Switch1Devices.KindFromHidPath(path);
            // Lizenzierte Kabel-Gamepads (HORI, PowerA, PDP): eigenes, einfaches Format.
            string? wiredPad = kind == ControllerKind.Unknown ? WiredSwitchPad.NameFromHidPath(path) : null;
            if (kind == ControllerKind.Unknown && wiredPad is null || _links.ContainsKey(path) || _connecting.ContainsKey(path))
                continue;
            if (_retryAfter.TryGetValue(path, out long until) && Environment.TickCount64 < until)
                continue;
            // Von Hand getrennt: Windows meldet das HID-Gerät noch kurz weiter, bis der Controller schläft.
            if (_suppressed.TryGetValue(path, out long since) && Environment.TickCount64 - since < 10_000)
                continue;
            if (!_connecting.TryAdd(path, 0))
                continue;
            Track(wiredPad is not null ? ConnectWiredPadAsync(path, wiredPad) : ConnectHidAsync(path, kind));
        }
    }

    private async Task ConnectWiredPadAsync(string path, string name)
    {
        Changed?.Invoke();
        try
        {
            var link = await WiredPadLink.OpenAsync(path, name);
            _unreachable.TryRemove(path, out _);
            Attach(link);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Z. B. von einem anderen Programm exklusiv geöffnet (Steam): nur einmal melden, still weiter versuchen.
            if (_unreachable.TryAdd(path, 0))
                Log.Warn($"{name}: nicht verwendbar ({Log.Reason(e)}) – wird still weiter versucht");
            _retryAfter[path] = Environment.TickCount64 + 5000;
        }
        finally
        {
            _connecting.TryRemove(path, out _);
            Changed?.Invoke();
        }
    }

    private async Task ConnectHidAsync(string path, ControllerKind kind)
    {
        Changed?.Invoke();
        Log.Info($"{kind.DisplayName()} gefunden ({path})");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            IControllerLink link = kind == ControllerKind.WiiRemote
                ? await WiimoteHidLink.ConnectAsync(path, _settings, timeout.Token)
                : await Switch1HidLink.ConnectAsync(path, kind, timeout.Token);
            _unreachable.TryRemove(path, out _);
            Attach(link);
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        catch (Exception e)
        {
            // Z. B. gekoppelt, aber ausgeschaltet: Windows zeigt das HID-Gerät trotzdem an. Nur einmal protokollieren,
            // sonst entstünde alle 5 s ein Eintrag, solange der Controller aus ist.
            if (_unreachable.TryAdd(path, 0))
                Log.Warn($"{kind.DisplayName()}: nicht erreichbar ({Log.Reason(e)}) – wird still weiter versucht");
            _retryAfter[path] = Environment.TickCount64 + 5000;
        }
        finally
        {
            _connecting.TryRemove(path, out _);
            Changed?.Invoke();
        }
    }

    // ---------- USB (Pro Controller 2, GameCube) ----------

    private void ScanUsb()
    {
        var devices = UsbEnumerator.Find();
        // Von Hand getrennte USB-Controller erst wieder verwenden, nachdem das Kabel einmal abgezogen war.
        foreach (var id in _usbSuppressed.Keys)
            if (devices.All(d => d.DeviceId != id))
                _usbSuppressed.TryRemove(id, out _);
        foreach (var device in devices)
        {
            string id = device.DeviceId;
            if (_links.ContainsKey(id) || _connecting.ContainsKey(id) || _usbSuppressed.ContainsKey(id))
                continue;
            if (_retryAfter.TryGetValue(id, out long until) && Environment.TickCount64 < until)
                continue;
            if (!_connecting.TryAdd(id, 0))
                continue;
            Track(ConnectUsbAsync(device));
        }
    }

    private async Task ConnectUsbAsync(UsbControllerInfo device)
    {
        Changed?.Invoke();
        Log.Info($"{device.Kind.DisplayName()} per USB gefunden ({device.DeviceId})");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var link = await Switch2UsbLink.OpenAsync(device, timeout.Token);
            _unreachable.TryRemove(device.DeviceId, out _);
            // Derselbe Controller noch per Bluetooth verbunden? Dann übernimmt das Kabel (schneller, lädt).
            if (link.Info.SerialNumber is { } serial)
            {
                foreach (var ble in _links.Values.Where(l => l.Transport == Transport.BluetoothLE && l.Info.SerialNumber == serial).ToList())
                {
                    _suppressed[ble.Id] = Environment.TickCount64;
                    Log.Info($"{ble.Id}: per USB angeschlossen – Bluetooth-Verbindung wird beendet");
                    OnLinkLost(ble);
                }
            }
            if (Attach(link))
            {
                link.PlayConnectFeedbackAsync().Forget($"{device.DeviceId}: Verbindungs-Vibration");
            }
            else
            {
                _retryAfter[device.DeviceId] = Environment.TickCount64 + 10000;
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        catch (Exception e)
        {
            // Z. B. von einem anderen Programm (Steam) belegt – einmal melden, dann still weiter versuchen.
            if (_unreachable.TryAdd(device.DeviceId, 0))
            {
                Log.Warn($"{device.Kind.DisplayName()} per USB: nicht nutzbar ({Log.Reason(e)})");
                Notify?.Invoke($"{device.Kind.DisplayName()} per USB ist von einem anderen Programm belegt (z. B. Steam). " +
                               "Das Programm schließen oder in Steam die Nintendo-Unterstützung abschalten.");
            }
            _retryAfter[device.DeviceId] = Environment.TickCount64 + 10000;
        }
        finally
        {
            _connecting.TryRemove(device.DeviceId, out _);
            Changed?.Invoke();
        }
    }

    // ---------- Spieler ----------

    /// <summary>Verbindung einem Spieler zuordnen. false, wenn die App gerade beendet wird.</summary>
    private bool Attach(IControllerLink link)
    {
        // Steckt derselbe Controller schon am USB-Kabel, nicht zusätzlich per Bluetooth verwenden.
        if (link.Transport == Transport.BluetoothLE && link.Info.SerialNumber is { } serial
            && _links.Values.Any(l => l.Transport == Transport.Usb && l.Info.SerialNumber == serial))
        {
            Log.Info($"{link.Id}: steckt am USB-Kabel – Bluetooth-Verbindung nicht verwendet");
            _suppressed[link.Id] = Environment.TickCount64;
            link.DisposeAsync().AsTask().Forget($"{link.Id}: Trennen");
            return false;
        }
        if (_disposed || !_links.TryAdd(link.Id, link))
        {
            link.DisposeAsync().AsTask().Forget($"{link.Id}: Trennen");
            return false;
        }
        link.Lost += OnLinkLost;

        string message;
        lock (_playerGate)
        {
            // Grundsätzlich als Paar – außer der Joy-Con (oder sein Partner) wurde zuletzt einzeln verwendet.
            var settings = _settings();
            var partner = settings.CombineJoyCons && !settings.IsSingleJoyCon(link.Address)
                ? _players.FirstOrDefault(p => p.CanPairWith(link) && !settings.IsSingleJoyCon(p.Links.FirstOrDefault()?.Address))
                : null;
            if (partner is not null)
            {
                partner.Add(link);
                message = $"Joy-Con zusammengefasst (Spieler {partner.Index + 1})";
            }
            else
            {
                int index = FreeIndex(link.Address);
                if (index < 0)
                {
                    Log.Warn($"{link.Id}: schon {MaxPlayers} Spieler – Controller wird nicht verwendet");
                    Notify?.Invoke($"Schon {MaxPlayers} Controller verbunden – {link.Kind.DisplayName()} wird nicht verwendet. Erst einen anderen trennen.");
                    _links.TryRemove(link.Id, out _);
                    link.DisposeAsync().AsTask().Forget($"{link.Id}: Trennen");
                    return false;
                }
                try
                {
                    _players.Add(NewPlayer(index, link));
                }
                catch (Exception e)
                {
                    // Virtueller Controller nicht anlegbar (ViGEm): Verbindung nicht verwaist zurücklassen.
                    Log.Error($"{link.Id}: virtueller Controller nicht anlegbar", e);
                    link.Lost -= OnLinkLost;
                    _links.TryRemove(link.Id, out _);
                    link.DisposeAsync().AsTask().Forget($"{link.Id}: Trennen");
                    return false;
                }
                message = $"{link.Kind.DisplayName()} verbunden (Spieler {index + 1})";
            }
        }
        if (link.IsLost)
            OnLinkLost(link);
        Log.Info(message);
        Notify?.Invoke(message);
        ApplyOutputMode(); // Paar mit eigener Ausgabeart des neuen Joy-Con
        QueueHide(link);
        Changed?.Invoke();
        return true;
    }

    // ---------- Originale vor Spielen und Steam verstecken (HidHide) ----------

    private readonly HashSet<string> _hidePending = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>In dieser Sitzung abgelehnt (UAC abgebrochen) oder fehlgeschlagen – nicht erneut fragen.</summary>
    private readonly HashSet<string> _hideDeclined = new(StringComparer.OrdinalIgnoreCase);
    private bool _hideRunning, _hidHideMissingNoted;

    /// <summary>
    /// Steam und viele Spiele (SDL) unterstützen Switch-1-, NSO- und USB-Controller selbst und sähen sie doppelt
    /// (Original + virtueller Controller) – dann kommen Eingaben doppelt oder vermischt an. Deshalb jeden solchen
    /// Controller automatisch per HidHide verstecken (N-Connect bleibt freigegeben). Mehrere Controller (Joy-Con-Paar)
    /// werden gesammelt und mit einer einzigen Windows-Abfrage (Adminrechte) versteckt.
    /// </summary>
    private void QueueHide(IControllerLink link)
    {
        var settings = _settings();
        if (!settings.HideFromGames || link.HidInstanceId is not { } id || settings.IsHidden(id) || _disposed)
            return;
        if (!HidHide.IsInstalled)
        {
            if (!_hidHideMissingNoted)
            {
                _hidHideMissingNoted = true;
                Log.Warn("HidHide fehlt – Steam/Spiele können Switch-1-, NSO- und USB-Controller doppelt sehen");
                Notify?.Invoke("Steam und manche Spiele sehen diesen Controller sonst doppelt. Abhilfe: HidHide installieren " +
                               "(Seite „Allgemein“ → „Original-Controller verstecken“).");
            }
            return;
        }
        lock (_hidePending)
        {
            if (_hideDeclined.Contains(id) || !_hidePending.Add(id) || _hideRunning)
                return;
            _hideRunning = true;
        }
        Track(HideSoonAsync());
    }

    /// <summary>Alle verbundenen Controller verstecken (Einstellung eben eingeschaltet); abgelehnte erneut versuchen.</summary>
    public void HideConnected()
    {
        lock (_hidePending)
            _hideDeclined.Clear();
        foreach (var link in _links.Values)
            QueueHide(link);
    }

    private async Task HideSoonAsync()
    {
        try
        {
            await Task.Delay(3000, _cts.Token); // zweiten Joy-Con des Paars abwarten – eine Abfrage für beide
            List<string> ids;
            lock (_hidePending)
            {
                ids = [.. _hidePending];
                _hidePending.Clear();
            }
            if (ids.Count == 0)
                return;
            bool ok = await HidHide.HideAsync(ids);
            if (ok)
            {
                var settings = _settings();
                settings.HiddenDevices = [.. settings.HiddenDevices, .. ids.Where(id => !settings.IsHidden(id))];
                RequestSave();
                bool steam = System.Diagnostics.Process.GetProcessesByName("steam").Length > 0;
                Notify?.Invoke(steam
                    ? "Original-Controller vor Spielen versteckt. Steam bitte einmal neu starten – danach sieht es nur noch den virtuellen Controller."
                    : "Original-Controller vor Spielen versteckt – Steam und Spiele sehen nur noch den virtuellen Controller.");
            }
            else
            {
                lock (_hidePending)
                    _hideDeclined.UnionWith(ids);
                Notify?.Invoke("Original-Controller nicht versteckt (Abfrage abgebrochen). Nachholen: Controller → Einstellungen → Extras → „Doppelt angezeigt?“.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            bool again;
            lock (_hidePending)
            {
                _hideRunning = false;
                again = _hidePending.Count > 0;
                if (again)
                    _hideRunning = true;
            }
            if (again && !_disposed)
                Track(HideSoonAsync());
        }
    }

    private void OnLinkLost(IControllerLink link)
    {
        Track(Task.Run(async () =>
        {
            if (!_links.TryRemove(new KeyValuePair<string, IControllerLink>(link.Id, link)))
                return;
            link.Lost -= OnLinkLost;
            Player? emptied = null;
            lock (_playerGate)
            {
                var player = _players.FirstOrDefault(p => p.Links.Contains(link));
                if (player is not null)
                {
                    player.Remove(link);
                    if (player.IsEmpty)
                    {
                        _players.Remove(player);
                        emptied = player;
                    }
                }
            }
            emptied?.Dispose();
            await link.DisposeAsync();
            _retryAfter[link.Id] = Environment.TickCount64 + 1500;
            ControllerPairing.NoteDisconnected(link.Address); // eben getrennt ≠ Kopplungsmodus
            Log.Info($"{link.Kind.DisplayName()} getrennt");
            if (!_disposed)
            {
                Notify?.Invoke($"{link.Kind.DisplayName()} getrennt");
                if (emptied is not null)
                    CompactPlayers();
            }
            Changed?.Invoke();
        }));
    }

    /// <summary>
    /// Lücken schließen: Fällt ein Spieler weg (getrennt, Joy-Con zum Paar zusammengefasst), rücken die übrigen auf –
    /// wer Spieler 2 war, wird Spieler 1 usw. Die virtuellen Controller werden in der neuen Reihenfolge angelegt, damit
    /// Windows, Steam und Spiele dieselbe Reihenfolge sehen. Gemerkte Plätze bleiben unverändert; ein zurückkehrender
    /// Controller bekommt seinen Platz nur, wenn er frei ist, sonst reiht er sich hinten ein.
    /// </summary>
    private void CompactPlayers()
    {
        List<Player> ordered;
        var moved = new List<string>();
        lock (_playerGate)
        {
            ordered = [.. _players.OrderBy(p => p.Index)];
            if (ordered.Select((p, i) => p.Index == i).All(same => same))
                return;
            var settings = _settings();
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Index == i)
                    continue;
                ordered[i].MoveTo(i);
                moved.Add($"{ordered[i].DisplayName(settings)} ist jetzt Spieler {i + 1}");
            }
        }
        foreach (var p in ordered)
            p.ReleasePad();
        foreach (var p in ordered)
        {
            try
            {
                p.RestorePad();
            }
            catch (Exception e)
            {
                Log.Error($"Spieler {p.Index + 1}: virtuellen Controller neu anlegen", e);
            }
        }
        foreach (var message in moved)
        {
            Log.Info(message);
            Notify?.Invoke(message);
        }
        Changed?.Invoke();
    }

    private Player NewPlayer(int index, IControllerLink link)
    {
        var player = new Player(index, link, _settings, _factory);
        player.Changed += () => Changed?.Invoke();
        player.SplitRequested += (p, l) => Split(p, l);
        player.PairRequested += OnPairRequested;
        player.Warning += message => Notify?.Invoke(message);
        return player;
    }

    private int FreeIndex(string? address = null)
    {
        var used = _players.Select(p => p.Index).ToHashSet();
        // Gemerkter Platz des Controllers, wenn frei und ohne Lücke davor – so landet er nach dem Verbinden wieder
        // dort, aber es entsteht kein „Spieler 3“ ohne Spieler 1 und 2.
        if (_settings().SlotFor(address) is { } slot && !used.Contains(slot) && slot <= _players.Count)
            return slot;
        return Enumerable.Range(0, MaxPlayers).FirstOrDefault(i => !used.Contains(i), -1);
    }

    /// <summary>
    /// Spieler auf einen anderen Platz setzen; ein dort sitzender Spieler tauscht. LEDs folgen, die virtuellen
    /// Controller werden in der neuen Reihenfolge neu angelegt (damit auch Spiele sie in dieser Reihenfolge sehen),
    /// und der Platz wird je Controller gemerkt.
    /// </summary>
    public void MovePlayer(Player player, int target)
    {
        if (target is < 0 or >= MaxPlayers)
            return;
        List<Player> ordered;
        Player? other;
        lock (_playerGate)
        {
            target = Math.Min(target, _players.Count - 1); // keine Lücken: höchstens der letzte belegte Platz
            if (!_players.Contains(player) || player.Index == target)
                return;
            other = _players.FirstOrDefault(p => p.Index == target);
            int from = player.Index;
            other?.MoveTo(from);
            player.MoveTo(target);
            ordered = [.. _players.OrderBy(p => p.Index)];
        }
        // Erst alle virtuellen Controller abbauen, dann der Reihe nach anlegen: Windows vergibt die Xbox-Plätze
        // in Anlegereihenfolge.
        foreach (var p in ordered)
            p.ReleasePad();
        foreach (var p in ordered)
        {
            try
            {
                p.RestorePad();
            }
            catch (Exception e)
            {
                Log.Error($"Spieler {p.Index + 1}: virtuellen Controller neu anlegen", e);
            }
        }
        var settings = _settings();
        foreach (var p in other is null ? [player] : new[] { player, other })
            foreach (var link in p.Links)
                if (link.Address is { } address)
                    settings.SetSlot(address, p.Index);
        RequestSave();
        string message = other is null
            ? $"{player.Kind.DisplayName()} ist jetzt Spieler {target + 1}"
            : $"Spieler {player.Index + 1} und {other.Index + 1} getauscht";
        Log.Info(message);
        Notify?.Invoke(message);
        Changed?.Invoke();
    }

    /// <summary>Einen Joy-Con aus seinem Paar lösen: er wird ein eigener Spieler (quer gehalten).</summary>
    public void Split(Player player, IControllerLink link)
    {
        string? message = null;
        lock (_playerGate)
        {
            if (!_players.Contains(player) || !player.IsPair || !player.Links.Contains(link))
                return;
            int index = FreeIndex(link.Address);
            if (index < 0)
                return;
            player.Remove(link);
            try
            {
                _players.Add(NewPlayer(index, link));
            }
            catch (Exception e)
            {
                Log.Error("Joy-Con trennen: virtueller Controller nicht anlegbar", e);
                player.Add(link); // zurück ins Paar
                return;
            }
            message = $"Joy-Con getrennt – {link.Kind.DisplayName()} ist jetzt Spieler {index + 1}";
        }
        // Beide Hälften bleiben einzeln, auch beim nächsten Verbinden.
        foreach (var l in player.Links.Append(link))
            if (l.Address is { } address)
                JoyConModeChanged?.Invoke(address, true);
        Log.Info(message);
        Notify?.Invoke(message);
        Changed?.Invoke();
    }

    /// <summary>Ein Paar in zwei einzelne Joy-Con trennen (Knopf im Fenster).</summary>
    public void SplitPair(Player player)
    {
        var right = player.Links.FirstOrDefault(l => l.Kind.IsJoyCon() && !l.Kind.IsLeftJoyCon());
        if (player.IsPair && right is not null)
            Split(player, right);
    }

    /// <summary>Einzelnen Joy-Con mit einem passenden einzelnen Partner verbinden (Knopf im Fenster).</summary>
    public bool PairWithAnySingle(Player player)
    {
        Player? partner;
        lock (_playerGate)
            partner = _players.FirstOrDefault(p => p != player && !p.IsPair && p.Links.Count == 1 && player.CanPairWith(p.Links[0]));
        if (partner is null)
            return false;
        Merge(player, partner);
        return true;
    }

    /// <summary>Gibt es zu diesem einzelnen Joy-Con einen passenden Partner?</summary>
    public bool HasPartner(Player player)
    {
        lock (_playerGate)
            return _players.Any(p => p != player && !p.IsPair && p.Links.Count == 1 && player.CanPairWith(p.Links[0]));
    }

    /// <summary>Zwei einzelne Joy-Con (links + rechts) zu einem Spieler zusammenfügen.</summary>
    public void Merge(Player a, Player b)
    {
        Player? emptied = null;
        string? message = null;
        lock (_playerGate)
        {
            if (a == b || !_players.Contains(a) || !_players.Contains(b) || a.IsPair || b.IsPair)
                return;
            var link = b.Links.FirstOrDefault();
            if (link is null || !a.CanPairWith(link))
                return;
            // Die kleinere Spielernummer bleibt.
            if (b.Index < a.Index)
                (a, b) = (b, a);
            link = b.Links[0];
            b.Remove(link);
            _players.Remove(b);
            emptied = b;
            a.Add(link);
            message = $"Joy-Con zusammengefasst (Spieler {a.Index + 1})";
        }
        foreach (var l in a.Links)
            if (l.Address is { } address)
                JoyConModeChanged?.Invoke(address, false);
        emptied?.Dispose();
        Log.Info(message);
        Notify?.Invoke(message);
        if (emptied is not null)
            CompactPlayers();
        ApplyOutputMode(); // das Paar übernimmt ggf. die eigene Ausgabeart eines Joy-Con
        Changed?.Invoke();
    }

    /// <summary>Geste wie an der Switch: L am linken und R am rechten Joy-Con innerhalb einer Sekunde.</summary>
    private void OnPairRequested(Player requester)
    {
        Player? partner;
        lock (_playerGate)
        {
            long now = Environment.TickCount64;
            partner = _players.FirstOrDefault(p => p != requester && !p.IsPair && p.PairRequestTicks >= 0
                && now - p.PairRequestTicks < 1000 && p.Links.Count == 1 && requester.CanPairWith(p.Links[0]));
        }
        if (partner is not null)
            Merge(requester, partner);
    }

    /// <summary>Prüfmodus (--demo): simulierte Controller – ein Pro Controller 2 und ein Joy-Con-2-Paar.</summary>
    public void StartDemo()
    {
        Log.Info("Demo-Modus: simulierte Controller werden hinzugefügt");
        if (Environment.GetCommandLineArgs().Contains("--demo-retro"))
        {
            int n = 10;
            foreach (var kind in new[]
                     {
                         ControllerKind.SnesController, ControllerKind.NesController, ControllerKind.N64Controller,
                         ControllerKind.MegaDrive, ControllerKind.WiiRemote, ControllerKind.WiiUPro,
                     })
                Attach(new DemoLink(kind, n++));
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--demo-all"))
        {
            // Je Art ein Controller (höchstens 8 Spieler): prüft die Karten aller Controller auf einmal.
            int n = 20;
            foreach (var kind in new[]
                     {
                         ControllerKind.GameCube2, ControllerKind.Pro1, ControllerKind.JoyCon1Left, ControllerKind.JoyCon1Right,
                         ControllerKind.N64Controller, ControllerKind.WiiRemote, ControllerKind.WiiUPro, ControllerKind.MegaDrive,
                     })
                Attach(new DemoLink(kind, n++));
            return;
        }
        Attach(new DemoLink(ControllerKind.Pro2, 1));
        Attach(new DemoLink(ControllerKind.JoyCon2Left, 2));
        Attach(new DemoLink(ControllerKind.JoyCon2Right, 3));
        Attach(new DemoLink(ControllerKind.JoyCon1Right, 4));
    }

    /// <summary>
    /// Ausgabeart (allgemein oder je Controller) geändert: Spieler, deren virtueller Controller nicht mehr passt,
    /// bekommen einen neuen – dabei werden alle in Spielerreihenfolge neu angelegt, damit Windows und Steam die
    /// Xbox-Plätze weiter in dieser Reihenfolge vergeben.
    /// </summary>
    public void ApplyOutputMode()
    {
        var settings = _settings();
        var ordered = Players;
        if (ordered.All(p => p.Output == p.DesiredOutput(settings)))
            return;
        foreach (var p in ordered)
            p.ReleasePad();
        foreach (var p in ordered)
        {
            try
            {
                p.RestorePad();
            }
            catch (Exception e)
            {
                Log.Error($"Spieler {p.Index + 1}: Ausgabeart wechseln", e);
            }
        }
        Changed?.Invoke();
    }

    /// <summary>Ausgabeart für die Controller eines Spielers festlegen (null = wie allgemein eingestellt).</summary>
    public void SetPlayerOutput(Player player, OutputMode? mode)
    {
        var settings = _settings();
        foreach (var link in player.Links)
            if (link.Address is { } address)
                settings.SetOutput(address, mode);
        RequestSave();
        Log.Info($"Spieler {player.Index + 1}: Ausgabe {(mode?.ToString() ?? "wie allgemein")}");
        ApplyOutputMode();
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _restartTimer.Dispose();
        _inactivityTimer.Dispose();
        _cts.Cancel();
        _watcher.Received -= OnAdvertisement;
        _watcher.Stopped -= OnWatcherStopped;
        try { _watcher.Stop(); } catch (Exception) { /* bereits gestoppt */ }

        // Laufende Verbindungsversuche und Aufräumarbeiten abwarten, damit danach kein virtueller
        // Controller mehr entsteht und der ViGEm-Client sicher freigegeben werden kann.
        Task[] pending;
        lock (_tasks)
            pending = [.. _tasks];
        try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { /* Zeitlimit */ }

        foreach (var p in Players)
            p.Dispose();
        lock (_playerGate)
            _players.Clear();
        foreach (var link in _links.Values)
        {
            link.Lost -= OnLinkLost;
            await link.DisposeAsync();
        }
        _links.Clear();
        _cts.Dispose();
    }
}

/// <summary>Erkennung von Switch-1-Controllern an ihrem HID-Gerätepfad.</summary>
internal static class Switch1Devices
{
    /// <summary>
    /// Bluetooth: ...vid&amp;0002057e_pid&amp;2009..., USB: ...vid_057e&amp;pid_2009...
    /// </summary>
    public static ControllerKind KindFromHidPath(string path)
    {
        var p = path.ToLowerInvariant();
        if (!p.Contains("vid&0002057e_pid&") && !p.Contains("vid_057e&pid_"))
            return ControllerKind.Unknown;
        foreach (var (pid, kind) in new[]
                 {
                     ("2009", ControllerKind.Pro1),
                     ("2006", ControllerKind.JoyCon1Left),
                     ("2007", ControllerKind.JoyCon1Right),   // auch NES-Controller (Typ aus der Geräteinfo)
                     ("200e", ControllerKind.JoyCon1Left),    // Ladegriff per USB: je Joy-Con eine Schnittstelle, Seite aus der Geräteinfo
                     ("2017", ControllerKind.SnesController),
                     ("2019", ControllerKind.N64Controller),
                     ("201e", ControllerKind.MegaDrive),
                     ("0306", ControllerKind.WiiRemote),
                     ("0330", ControllerKind.WiiRemote),       // Fernbedienung Plus oder Wii U Pro (Erweiterungskennung)
                 })
        {
            if (p.Contains($"pid&{pid}") || p.Contains($"pid_{pid}"))
                return kind;
        }
        return ControllerKind.Unknown;
    }
}
