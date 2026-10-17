using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Linux.Bluetooth;
using NConnect.Linux.Ble;
using NConnect.Linux.Core;
using NConnect.Linux.Output;
using Switch2Pro.Protocol;
using Tmds.DBus;

namespace NConnect.Linux;

/// <summary>
/// Der Dienst: wartet auf einen Bluetooth-Adapter, sucht Switch-2-Controller, verbindet sie und legt je Spieler einen
/// virtuellen Xbox-360-Controller an. Schreibt den Zustand nach status.json, meldet Ereignisse als JSON-Zeilen auf
/// stdout und nimmt Befehle zeilenweise über stdin an (für das Kodi-Add-on):
///   reload | disconnect &lt;Spieler 1…8&gt; | disconnect-all | status | scan
///   | gamepad-disconnect &lt;Adresse&gt; | gamepad-forget &lt;Adresse&gt;
/// Aus Kodi gestartet (<c>--kodi</c>) endet er mit Kodi: bei geschlossenem stdin oder wenn der Elternprozess weg ist.
/// </summary>
public sealed class Daemon : IAsyncDisposable
{
    private const int QuietMs = 15000;
    private static readonly JsonSerializerOptions StatusJson = new() { WriteIndented = true };

    [DllImport("libc")] private static extern int getppid();

    private readonly string _dataDir;
    private readonly string? _adapterName;
    private readonly bool _withKodi;
    private readonly string _settingsPath;
    private readonly string _statusPath;
    private readonly List<Player> _players = [];
    private readonly ConcurrentDictionary<string, byte> _connecting = new();
    private readonly ConcurrentDictionary<string, FoundController> _elsewhere = new();
    private readonly ConcurrentDictionary<string, long> _quietUntil = new();
    private readonly SemaphoreSlim _statusSignal = new(0, int.MaxValue);
    private readonly object _stdoutLock = new();
    private volatile Settings _settings;
    private FileSystemWatcher? _settingsWatcher;
    private CancellationTokenSource? _run;
    private CancellationTokenSource? _session;
    private Adapter? _adapter;
    private Switch2Scanner? _scanner;
    /// <summary>„on“ | „off“ | „missing“ | „no_le“ (Vertrag der status.json); null = noch nicht geprüft.</summary>
    private volatile string? _adapterState;
    private string? _adapterAddress;
    private int _adapterIndex;
    private ulong _hostAddress;

    /// <param name="withKodi">Aus Kodi gestartet: mit Kodi enden (stdin zu oder Elternprozess weg).</param>
    public Daemon(string dataDir, string? adapterName, bool withKodi = false)
    {
        _dataDir = dataDir;
        _adapterName = adapterName;
        _withKodi = withKodi;
        _settingsPath = Path.Combine(dataDir, "settings.json");
        _statusPath = Path.Combine(dataDir, "status.json");
        _settings = Settings.Load(_settingsPath);
        if (_settings.LoadError is { } error)
            Log.Warn($"settings.json nicht lesbar ({error}) – Standardwerte");
    }

    /// <summary>Legt den virtuellen Controller eines Spielers an (Tests ersetzen das – dort gibt es kein /dev/uinput).</summary>
    internal Func<string, UInputPad?> CreatePad { get; init; } = phys => UInputPad.Create(phys);

    private Settings CurrentSettings() => _settings;

    public async Task RunAsync(CancellationToken stop)
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(stop);
        _run = run;
        stop = run.Token;
        WatchSettings();
        ApplyDsu();
        _ = Task.Run(() => RetryDsuAsync(stop), stop);
        _ = Task.Run(() => StatusWriterAsync(stop), stop);
        _ = Task.Run(() => ReadCommandsAsync(stop), stop);
        _ = Task.Run(() => InactivityAsync(stop), stop);
        if (_withKodi)
            _ = Task.Run(() => WatchParentAsync(stop), stop);
        RequestStatus();

        // Eine „Sitzung“ je verfügbarem Adapter. Verschwindet er (Bluetooth aus, bluetoothd neu gestartet, Dongle
        // gezogen), werden alle Controller getrennt und es wird erneut gewartet.
        while (!stop.IsCancellationRequested)
        {
            Adapter? adapter = null;
            try
            {
                // Abbrechbar: Läuft bluetoothd nicht, hängt die Anfrage bis zu 25 s in der D-Bus-Aktivierung von org.bluez –
                // Beenden (Kodi zu, Neustart über das Menü) soll darauf nicht warten.
                adapter = await FindAdapterAsync().WaitAsync(stop).ConfigureAwait(false);
                _adapter = adapter; // wird in EndSessionAsync freigegeben
                if (adapter is null)
                    OnNoAdapter();
                else
                    await RunSessionAsync(adapter, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                await OnRoundFailedAsync(e, adapterReached: adapter is not null).ConfigureAwait(false);
            }
            await EndSessionAsync().ConfigureAwait(false);
            await Task.Delay(5000, stop).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// bluetoothd läuft nicht bzw. lässt sich nicht starten – das ist „off“, nicht „missing“. Ohne Aktivierungsdatei
    /// meldet der D-Bus ServiceUnknown/NameHasNoOwner. Mit ihr (LibreELEC: org.bluez.service → systemd) versucht er,
    /// bluetoothd zu starten: Ist Bluetooth in den Einstellungen aus, startet die Unit wegen ihrer Bedingung nicht, und
    /// nach 25 s kommt TimedOut („Failed to activate service 'org.bluez'“); scheitert der Start selbst, ein Spawn- bzw.
    /// systemd-Fehler. Andere Fehlernamen klärt <see cref="OnRoundFailedAsync"/> über eine Nachfrage beim D-Bus.
    /// </summary>
    internal static bool IsBluetoothServiceDown(Exception e) =>
        e is DBusException { ErrorName: { } name }
        && (name is "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner"
                or "org.freedesktop.DBus.Error.TimedOut"
            || name.StartsWith("org.freedesktop.DBus.Error.Spawn.", StringComparison.Ordinal)
            || name.StartsWith("org.freedesktop.systemd1.", StringComparison.Ordinal));

    /// <summary>
    /// Hat der D-Bus-Name org.bluez einen Besitzer (läuft bluetoothd)? Fragt nur den D-Bus selbst und startet nichts.
    /// Tests ersetzen das – dort gibt es keinen System-D-Bus.
    /// </summary>
    internal Func<Task<bool>> BluetoothServiceRunningAsync { get; init; } =
        () => Connection.System.IsServiceActiveAsync(BlueZ.Service);

    /// <summary>
    /// Eine Runde endete mit einem Fehler: den Bluetooth-Zustand nachführen, damit nach dem Ende der Sitzung kein
    /// „on“ stehen bleibt, während bluetoothd weg ist (sonst erschiene der Hinweis „Bluetooth ist aus“ nie).
    /// Läuft bluetoothd nicht – am Fehlernamen erkannt oder, bei jedem anderen Fehler, beim D-Bus nachgefragt –, ist
    /// Bluetooth „off“. Sonst: Hat BlueZ in dieser Runde gar keinen Adapter geliefert, ist ein „on“/„no_le“ veraltet
    /// → „missing“. Scheiterte erst das Einrichten bzw. Überwachen des gefundenen Adapters, bleibt der Zustand stehen:
    /// Die nächste Runde stellt ihn neu fest (weg → „missing“, aus → „off“), und ein Fehler, der bei jedem Einrichten
    /// wiederkehrt, ließe ihn sonst alle paar Sekunden springen – mit einer Kodi-Meldung bei jedem Wechsel.
    /// </summary>
    /// <param name="adapterReached">BlueZ hat in dieser Runde einen Adapter geliefert.</param>
    internal async Task OnRoundFailedAsync(Exception e, bool adapterReached)
    {
        if (IsBluetoothServiceDown(e) || await IsBluetoothServiceRunningAsync().ConfigureAwait(false) == false)
        {
            // Unter LibreELEC heißt das: Bluetooth ist in den Einstellungen ausgeschaltet.
            if (_adapterState != "off")
                Log.Warn($"Bluetooth-Dienst (bluetoothd) läuft nicht – Bluetooth ist aus ({Log.Reason(e)})");
            SetAdapterState("off");
            return;
        }
        Log.Warn($"Bluetooth nicht verfügbar: {Log.Reason(e)}");
        if (_adapterState is null || (!adapterReached && _adapterState is ("on" or "no_le")))
            SetAdapterState("missing"); // kein (erreichbarer) Adapter – auch nicht ewig „startet …“ anzeigen
    }

    /// <summary>true/false laut D-Bus; null = unbekannt (D-Bus selbst nicht erreichbar oder keine Antwort).</summary>
    private async Task<bool?> IsBluetoothServiceRunningAsync()
    {
        try
        {
            return await BluetoothServiceRunningAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool _waitedForAdapter;

    /// <summary>
    /// BlueZ antwortet, meldet aber keinen Adapter. Kommt bluetoothd gerade erst (wieder) hoch – Bluetooth eben
    /// eingeschaltet, Systemstart –, übernimmt er den D-Bus-Namen, bevor er seine Adapter anmeldet; eine Anfrage in
    /// diesem Moment sieht noch keinen. Kommt man von „off“ oder ist noch nichts geprüft, daher eine Runde (5 s) abwarten,
    /// statt kurz „Kein Bluetooth-Adapter“ zu melden; fehlt er dann noch, ist es „missing“.
    /// </summary>
    internal void OnNoAdapter()
    {
        if (_adapterState is (null or "off") && !_waitedForAdapter)
        {
            _waitedForAdapter = true;
            return;
        }
        SetAdapterState("missing");
    }

    /// <summary>Den Dienst von innen beenden (Kodi weg) – wie SIGTERM: Controller trennen, Pads entfernen.</summary>
    private void StopRunning(string reason)
    {
        Log.Info($"{reason} – N-Connect beendet sich");
        try
        {
            _run?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // läuft schon nicht mehr
        }
    }

    /// <summary>
    /// Aus Kodi gestartet: Endet Kodi (auch durch einen Absturz), wird der Daemon an init weitergereicht – dann mit
    /// beenden. Sonst liefe er verwaist weiter, hielte Controller und DSU-Port, und der nächste Kodi-Start legte ein
    /// zweites Programm daneben. (Geschlossenes stdin fängt dasselbe meist schon früher ab.)
    /// </summary>
    private async Task WatchParentAsync(CancellationToken stop)
    {
        int parent = getppid();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(2000, stop).ConfigureAwait(false);
                if (getppid() != parent)
                {
                    StopRunning("Kodi läuft nicht mehr");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunSessionAsync(Adapter adapter, CancellationToken stop)
    {
        _waitedForAdapter = false;
        if (!await adapter.GetPoweredAsync().ConfigureAwait(false))
        {
            SetAdapterState("off");
            return;
        }
        _adapterAddress = await adapter.GetAddressAsync().ConfigureAwait(false);
        _hostAddress = ParseAddress(_adapterAddress);
        _adapterIndex = int.TryParse(AdapterId(adapter).Replace("hci", ""), out int index) ? index : 0;
        Log.Info($"Bluetooth-Adapter {AdapterId(adapter)} ({_adapterAddress})");
        bool le = await BlueZ.SupportsLeAsync(adapter).ConfigureAwait(false) != false;
        if (!le)
            Log.Warn($"{AdapterId(adapter)} kann kein Bluetooth LE – Switch-2-Controller lassen sich damit nicht verbinden, " +
                     "andere Controller (Xbox, PlayStation, Switch 1 …) schon");
        SetAdapterState(le ? "on" : "no_le");

        _session = CancellationTokenSource.CreateLinkedTokenSource(stop);
        _gamepads = new GamepadManager(adapter) { Enabled = AutoPairGamepads() };
        _gamepads.Changed += OnGamepadChanged;
        if (_agent is null)
        {
            try
            {
                var agent = new PairingAgent();
                await agent.RegisterAsync().ConfigureAwait(false);
                _agent = agent;
            }
            catch (Exception e)
            {
                Log.Warn($"Kopplungs-Agent nicht angemeldet ({Log.Reason(e)}) – Gamepads mit Bestätigung koppeln evtl. nicht");
            }
        }
        _gamepads.Agent = _agent;
        _scanner = new Switch2Scanner(adapter) { IncludeClassic = _gamepads.Enabled };
        _scanner.Found += OnFound;
        _scanner.Seen += _gamepads.OnSeen;
        _backgroundScan = LinuxSetting("BackgroundScan", true);
        if (ShouldScan)
            await _scanner.StartAsync().ConfigureAwait(false);
        else
            Log.Info("Hintergrundsuche ist aus – gesucht wird nur auf Anforderung");
        await RefreshGamepadsAsync().ConfigureAwait(false);
        RequestStatus();

        // Adapter überwachen: aus- oder weggefallen → Sitzung beenden.
        int ticks = 0;
        while (!_session.IsCancellationRequested)
        {
            await Task.Delay(3000, _session.Token).ConfigureAwait(false);
            if (!await adapter.GetPoweredAsync().ConfigureAwait(false))
            {
                Log.Info("Bluetooth wurde ausgeschaltet");
                SetAdapterState("off");
                return;
            }
            if (ShouldScan)
            {
                // Manche Programme (Kodis Bluetooth-Seite) beenden die Suche – dann neu starten.
                if (!_scanner.Scanning || !await adapter.GetDiscoveringAsync().ConfigureAwait(false))
                {
                    _scanner.MarkStopped();
                    await _scanner.StartAsync().ConfigureAwait(false);
                    RequestStatus();
                }
                // Sicherheitsnetz: „Discovering“ kann true melden, obwohl der Chip nicht mehr sucht – jede Minute auffrischen.
                if (++ticks % 20 == 0)
                    KickScan();
            }
            else if (_scanner.Scanning)
            {
                await _scanner.StopAsync().ConfigureAwait(false); // Suche auf Anforderung abgelaufen
                RequestStatus();
            }
            await RefreshGamepadsAsync().ConfigureAwait(false);
            CheckBatteries();
        }
    }

    // ---------- Spielerzuordnung (slots.json: Adresse → Spieler 0…7) ----------

    private Dictionary<string, int>? _slots;

    private string SlotsPath => Path.Combine(_dataDir, "slots.json");

    private int? RememberedSlot(string address)
    {
        lock (_stdoutLock)
        {
            _slots ??= LoadSlots();
            return _slots.TryGetValue(address, out int slot) ? slot : null;
        }
    }

    private void RememberSlot(string address, int slot)
    {
        lock (_stdoutLock)
        {
            _slots ??= LoadSlots();
            if (_slots.TryGetValue(address, out int old) && old == slot)
                return;
            _slots[address] = slot;
            try
            {
                string temp = SlotsPath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_slots, StatusJson));
                File.Move(temp, SlotsPath, overwrite: true);
            }
            catch (Exception e)
            {
                Log.Warn($"slots.json: {Log.Reason(e)}");
            }
        }
    }

    private Dictionary<string, int> LoadSlots()
    {
        try
        {
            if (File.Exists(SlotsPath) && JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(SlotsPath)) is { } d)
                return new Dictionary<string, int>(d.Where(kv => kv.Value is >= 0 and < 8), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // kaputte Datei: neu beginnen
        }
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    // ---------- Bekannte Controller (known.json: Adresse → Art, Seriennummer) ----------

    private sealed record KnownController(string Kind, string? Serial);

    private Dictionary<string, KnownController>? _known;

    private string KnownPath => Path.Combine(_dataDir, "known.json");

    /// <summary>Verbundenen Controller merken – damit das Widget ihn auch zeigt, wenn er gerade nicht verbunden ist.</summary>
    private void RememberKnown(Switch2Device device)
    {
        var entry = new KnownController(device.Kind.ToString(), device.SerialNumber);
        lock (_stdoutLock)
        {
            _known ??= LoadKnown();
            if (_known.TryGetValue(device.Address, out var old) && old == entry)
                return;
            _known[device.Address] = entry;
            try
            {
                string temp = KnownPath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_known, StatusJson));
                File.Move(temp, KnownPath, overwrite: true);
            }
            catch (Exception e)
            {
                Log.Warn($"known.json: {Log.Reason(e)}");
            }
        }
    }

    private List<KeyValuePair<string, KnownController>> KnownSnapshot()
    {
        lock (_stdoutLock)
        {
            _known ??= LoadKnown();
            return [.. _known];
        }
    }

    private Dictionary<string, KnownController> LoadKnown()
    {
        try
        {
            if (File.Exists(KnownPath) && JsonSerializer.Deserialize<Dictionary<string, KnownController>>(File.ReadAllText(KnownPath)) is { } d)
                return new Dictionary<string, KnownController>(d, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // kaputte Datei: neu beginnen
        }
        return new Dictionary<string, KnownController>(StringComparer.OrdinalIgnoreCase);
    }

    // ---------- DSU-Server (Gyro für Emulatoren) ----------

    private readonly object _dsuLock = new();
    private static readonly int[] DsuRetrySeconds = [2, 5, 10, 20, 30];

    private void ApplyDsu()
    {
        lock (_dsuLock)
        {
            bool want = _settings.DsuServer;
            bool network = LinuxSetting("DsuNetwork", false);
            var current = DsuServer.Instance;
            if (current is not null && (!want || current.Network != network))
                current.Dispose();
            if (want && DsuServer.Instance is null)
                DsuServer.Start(network, Log.Info, Log.Warn);
        }
    }

    /// <summary>
    /// Port beim Start belegt – meist von einem N-Connect, das sich gerade noch beendet (Neustart über das Menü oder
    /// nach Adapterwechsel). Einige Male nachfassen; danach erst wieder bei einer Einstellungsänderung.
    /// </summary>
    private async Task RetryDsuAsync(CancellationToken stop)
    {
        try
        {
            foreach (int seconds in DsuRetrySeconds)
            {
                if (!_settings.DsuServer || DsuServer.Instance is not null)
                    return;
                await Task.Delay(seconds * 1000, stop).ConfigureAwait(false);
                ApplyDsu();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ---------- Akku-Warnung ----------

    private static readonly int[] BatteryThresholds = [20, 10];
    private readonly Dictionary<string, int> _batteryWarned = [];

    /// <summary>
    /// Meldet einmal bei 20 % und einmal bei 10 % (Ereignis „battery-low“) – für Switch-2-Controller und andere
    /// Gamepads. Beim Laden oder wenn der Akku wieder über der Schwelle liegt, wird die Warnung zurückgesetzt.
    /// </summary>
    private void CheckBatteries()
    {
        if (!LinuxSetting("BatteryWarning", true))
            return;
        List<Player> players;
        lock (_players)
            players = [.. _players];
        var seen = new HashSet<string>();
        foreach (var p in players)
            if (p.Devices.FirstOrDefault()?.Address is { } key)
            {
                seen.Add(key);
                Check(key, p.Name, p.BatteryPercent, p.Charging, p.Index + 1);
            }
        foreach (var g in _gamepads?.Known ?? [])
            if (g.Connected)
            {
                seen.Add(g.Address);
                Check(g.Address, g.Name, g.Battery, charging: false, player: 0);
            }
        foreach (var gone in _batteryWarned.Keys.Where(k => !seen.Contains(k)).ToList())
            _batteryWarned.Remove(gone);

        void Check(string key, string name, int battery, bool charging, int player)
        {
            if (battery < 0)
                return;
            int warned = _batteryWarned.GetValueOrDefault(key, 101);
            if (charging || battery > warned + 5)
            {
                _batteryWarned.Remove(key); // geladen: später wieder warnen
                return;
            }
            int threshold = BatteryThresholds.Where(t => battery <= t).DefaultIfEmpty(101).Min();
            if (threshold < warned)
            {
                _batteryWarned[key] = threshold;
                Log.Info($"{name}: Akku schwach ({battery} %)");
                Emit("battery-low", new JsonObject { ["name"] = name, ["battery"] = battery, ["player"] = player });
            }
        }
    }

    // ---------- Suche: ständig oder auf Anforderung ----------

    private const int OnDemandScanMs = 120_000;
    private volatile bool _backgroundScan = true;
    private long _scanUntil;

    private bool ShouldScan => _backgroundScan || Environment.TickCount64 < Interlocked.Read(ref _scanUntil);

    /// <summary>Befehl „scan“: bei abgeschalteter Hintergrundsuche 2 Minuten lang suchen.</summary>
    private async Task ScanNowAsync()
    {
        Interlocked.Exchange(ref _scanUntil, Environment.TickCount64 + OnDemandScanMs);
        if (_scanner is { Scanning: false } scanner)
            await scanner.StartAsync().ConfigureAwait(false);
        Log.Info("Suche auf Anforderung (2 Minuten)");
        RequestStatus();
    }

    // ---------- Andere Gamepads (Xbox, PlayStation, Switch 1 …) ----------

    private GamepadManager? _gamepads;
    private PairingAgent? _agent;

    private bool AutoPairGamepads() => LinuxSetting("AutoPairGamepads", true);

    /// <summary>Linux-eigene Einstellung in settings.json (nicht Teil der gemeinsamen Settings-Klasse).</summary>
    private bool LinuxSetting(string key, bool fallback)
    {
        try
        {
            if (File.Exists(_settingsPath) && JsonNode.Parse(File.ReadAllText(_settingsPath),
                    documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                is JsonObject o && o[key] is JsonValue v && v.TryGetValue(out bool b))
                return b;
        }
        catch (Exception)
        {
            // Standard
        }
        return fallback;
    }

    private async Task RefreshGamepadsAsync()
    {
        if (_gamepads is null)
            return;
        var before = _gamepads.Known;
        try
        {
            await _gamepads.RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Warn($"Gamepads: {Log.Reason(e)}");
        }
        if (!before.SequenceEqual(_gamepads.Known))
            RequestStatus();
    }

    private void OnGamepadChanged(string what, string address, string name)
    {
        Emit("gamepad-" + what, new JsonObject { ["address"] = address, ["name"] = name });
        if (what is "paired" or "pair-failed")
            KickScan();
        RequestStatus();
    }

    private async Task<Adapter?> FindAdapterAsync()
    {
        var adapters = await BlueZManager.GetAdaptersAsync().ConfigureAwait(false);
        var chosen = _adapterName is null ? adapters.FirstOrDefault()
            : adapters.FirstOrDefault(a => string.Equals(AdapterId(a), _adapterName, StringComparison.OrdinalIgnoreCase));
        // Jedes Adapter-Objekt beobachtet D-Bus-Signale, bis es freigegeben wird – die übrigen sofort freigeben
        // (bei ausgeschaltetem Bluetooth wird alle 5 s neu gesucht).
        foreach (var a in adapters)
            if (!ReferenceEquals(a, chosen))
                a.Dispose();
        return chosen;
    }

    /// <summary>„hci0“ – Linux.Bluetooth liefert als Name den D-Bus-Pfad („/org/bluez/hci0“).</summary>
    private static string AdapterId(Adapter adapter) => adapter.Name.Split('/').Last();

    private async Task EndSessionAsync()
    {
        _session?.Cancel();
        if (_scanner is not null)
        {
            _scanner.Found -= OnFound;
            if (_gamepads is not null)
                _scanner.Seen -= _gamepads.OnSeen;
            try
            {
                await _scanner.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // D-Bus weg
            }
            _scanner = null;
        }
        await DisconnectAllAsync(quiet: false).ConfigureAwait(false);
        if (_gamepads is not null)
            _gamepads.Changed -= OnGamepadChanged;
        _gamepads = null;
        if (_agent is not null)
        {
            await _agent.UnregisterAsync().ConfigureAwait(false);
            _agent = null;
        }
        _elsewhere.Clear();
        _session?.Dispose();
        _session = null;
        _adapter?.Dispose();
        _adapter = null;
    }

    // ---------- Controller finden und verbinden ----------

    private void OnFound(FoundController f)
    {
        // „Für uns“: SYNC-Werbung, Werbung mit der Adresse dieses Adapters – oder mit ihm gekoppelt.
        // Letzteres ist der verlässlichere Nachweis: Steht in der Werbung eine ältere/stimmt die Reihenfolge
        // nicht überein, liegt der Controller sonst dauerhaft fälschlich in „anderes Gerät“.
        bool forUs = f.SyncMode || f.Paired || (_hostAddress != 0 && f.Host == _hostAddress);
        if (!forUs)
        {
            if (_elsewhere.TryAdd(f.Address, f))
            {
                Log.Info($"{f.Address}: {f.Kind.DisplayName()} wirbt für {BtAddress.Format(f.Host)} – mit einem anderen Gerät gekoppelt (SYNC drücken)");
                Emit("elsewhere", new JsonObject { ["address"] = f.Address, ["kind"] = f.Kind.ToString(), ["name"] = f.Kind.DisplayName() });
                RequestStatus();
            }
            return;
        }
        if (_elsewhere.TryRemove(f.Address, out _))
            RequestStatus();
        if (_quietUntil.TryGetValue(f.Address, out long until) && Environment.TickCount64 < until)
            return;
        if (_session is not { } session || IsConnected(f.Address) || !_connecting.TryAdd(f.Address, 0))
            return;
        ulong pairHost = f.SyncMode && _settings.AutoPair ? _hostAddress : 0;
        _ = ConnectAsync(f, pairHost, session.Token);
    }

    private bool IsConnected(string address)
    {
        lock (_players)
            return _players.Any(p => p.Devices.Any(d => d.Address == address));
    }

    private async Task ConnectAsync(FoundController f, ulong pairHost, CancellationToken ct)
    {
        Log.Info($"{f.Address}: {f.Kind.DisplayName()} gefunden{(f.SyncMode ? " (SYNC)" : "")}, verbinde …");
        Emit("connecting", new JsonObject { ["address"] = f.Address, ["name"] = f.Kind.DisplayName() });
        RequestStatus();
        // Suche anhalten, bevor verbunden wird: Bei Chips mit geteilter WLAN/BT-Antenne (Raspberry Pi)
        // frisst die aktive Suche samt BR/EDR-Inquiry das gesamte Sendefenster – der frische Link bekommt
        // keine Pakete vom Controller und stirbt sofort wieder (HCI-Status 0x3e → errno 38).
        if (_scanner is { Scanning: true } scanner)
            await scanner.StopAsync().ConfigureAwait(false);
        try
        {
            var device = await Switch2Device.ConnectAsync(_adapterAddress!, _adapterIndex, f.Address, f.AddressType, f.Kind,
                pairHost, ct).ConfigureAwait(false);
            if (device.IsLost)
            {
                Log.Warn($"{f.Address}: während der Einrichtung getrennt – nicht übernommen");
                await device.DisposeAsync().ConfigureAwait(false);
                return;
            }
            AddToPlayer(device);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            Log.Error($"{f.Address}: Verbindung fehlgeschlagen", e);
            Emit("failed", new JsonObject { ["address"] = f.Address, ["name"] = f.Kind.DisplayName(), ["reason"] = e.Message });
            if (e.Message.Contains("errno 38"))
                WarnWifiCoexistence();
        }
        catch (Exception)
        {
            // Dienst wird beendet
        }
        finally
        {
            _connecting.TryRemove(f.Address, out _);
            KickScan();
            RequestStatus();
        }
    }

    private int _kickPending;
    private bool _wifiHintShown;

    /// <summary>
    /// errno 38 = ENOSYS = nicht zugeordneter HCI-Status, bei uns in der Praxis 0x3e „Connection Failed to be
    /// Established“: Der Chip meldet die Verbindung, bekommt dann aber kein Paket vom Controller – klassisch, wenn
    /// auf Chips mit geteilter WLAN/BT-Antenne (Raspberry Pi, Mini-PCs) ein aktives WLAN-Radio die Funkzeit frisst.
    /// Einmal pro Sitzung warnen, wenn gerade ein WLAN-Interface oben ist.
    /// </summary>
    private void WarnWifiCoexistence()
    {
        if (_wifiHintShown)
            return;
        try
        {
            foreach (var iface in Directory.EnumerateDirectories("/sys/class/net"))
            {
                string name = Path.GetFileName(iface);
                if (!File.Exists(Path.Combine(iface, "phy80211")) && !Directory.Exists(Path.Combine(iface, "wireless")))
                    continue;
                if (File.ReadAllText(Path.Combine(iface, "operstate")).Trim() is not ("up" or "unknown"))
                    continue;
                _wifiHintShown = true;
                Log.Warn($"WLAN ({name}) ist aktiv und teilt sich die Antenne mit Bluetooth – falls Verbindungen " +
                         "weiter abgelehnt werden: WLAN ausschalten, wenn es nicht gebraucht wird (LibreELEC: Einstellungen > Verbindungen)");
                Emit("wifi-hint", new JsonObject { ["interface"] = name });
                return;
            }
        }
        catch (Exception)
        {
            // /sys nicht lesbar – Hinweis entfällt
        }
    }

    /// <summary>Suche nach einem Verbindungsaufbau/-abbau neu starten (siehe <see cref="Switch2Scanner.RestartAsync"/>).</summary>
    private void KickScan()
    {
        if (!ShouldScan || Interlocked.Exchange(ref _kickPending, 1) == 1)
            return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(500).ConfigureAwait(false); // mehrere Ereignisse kurz hintereinander bündeln
            Interlocked.Exchange(ref _kickPending, 0);
            try
            {
                if (_scanner is { } scanner)
                    await scanner.RestartAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Warn($"Suche neu starten: {Log.Reason(e)}");
            }
        });
    }

    internal void AddToPlayer(Switch2Device device)
    {
        var settings = _settings;
        Player player;
        bool pairing = false;
        lock (_players)
        {
            var partner = settings.CombineJoyCons && !settings.IsSingleJoyCon(device.Address)
                ? _players.FirstOrDefault(p => p.CanPairWith(device))
                : null;
            if (partner is not null)
            {
                player = partner;
                pairing = true;
            }
            else
            {
                // Spielerzuordnung merken: derselbe Controller bekommt wieder seinen Platz, wenn er frei ist.
                int index = RememberedSlot(device.Address) is { } slot && _players.All(p => p.Index != slot)
                    ? slot
                    : Enumerable.Range(0, 8).First(i => _players.All(p => p.Index != i));
                player = new Player(index, CurrentSettings);
                player.KodiButtonPressed += (p, b) =>
                    Emit("button", new JsonObject { ["player"] = p.Index + 1, ["button"] = b.ToString() });
                _players.Add(player);
                _players.Sort((a, b) => a.Index.CompareTo(b.Index));
            }
            player.Add(device);
        }
        RememberSlot(device.Address, player.Index);
        RememberKnown(device);
        device.StateReceived += player.OnState;
        if (!pairing)
        {
            try
            {
                player.AttachOutput(CreatePad($"nconnect/player{player.Index + 1}"));
            }
            catch (Exception e)
            {
                Log.Error($"Spieler {player.Index + 1}: virtueller Controller nicht angelegt", e);
                Emit("error", new JsonObject { ["reason"] = e.Message });
            }
        }
        if (settings.GameLeds)
            _ = device.SetPlayerAsync(player.Index);
        if (settings.ConnectFeedback)
            _ = device.PlayConnectFeedbackAsync();
        Log.Info(pairing ? $"Joy-Con zusammengefasst (Spieler {player.Index + 1})"
            : $"{device.Kind.DisplayName()} verbunden (Spieler {player.Index + 1})");
        Emit(pairing ? "paired" : "connected", new JsonObject
        {
            ["player"] = player.Index + 1,
            ["name"] = player.Name,
            ["address"] = device.Address,
            ["kind"] = device.Kind.ToString(),
        });
        RequestStatus();
        // Erst jetzt auf den Abbruch hören – und danach noch einmal nachsehen: Lost meldet sich nur einmal. Riss die
        // Verbindung schon während der Einrichtung oben ab, ginge die Meldung sonst verloren, und der Controller bliebe
        // als Geist verbunden (neue Verbindungsversuche würden als „schon verbunden“ verworfen). OnLost verträgt den
        // doppelten Aufruf; erst nach dem Anlegen des Pads, damit dieses mit entfernt wird.
        device.Lost += OnLost;
        if (device.IsLost)
            OnLost(device);
    }

    private void OnLost(Switch2Device device) => _ = Task.Run(async () =>
    {
        Player? emptied = null;
        int index;
        lock (_players)
        {
            var player = _players.FirstOrDefault(p => p.Devices.Contains(device));
            if (player is null)
                return;
            index = player.Index;
            player.Remove(device);
            if (player.Devices.Count == 0)
            {
                _players.Remove(player);
                emptied = player;
            }
        }
        await device.DisposeAsync().ConfigureAwait(false);
        if (emptied is not null)
            await emptied.DisposeAsync().ConfigureAwait(false);
        Log.Info($"{device.Kind.DisplayName()} getrennt (Spieler {index + 1})");
        Emit("disconnected", new JsonObject { ["player"] = index + 1, ["name"] = device.Kind.DisplayName(), ["address"] = device.Address });
        KickScan();
        RequestStatus();
    });

    private async Task DisconnectPlayerAsync(int index, string reason)
    {
        Player? player;
        lock (_players)
        {
            player = _players.FirstOrDefault(p => p.Index == index);
            if (player is null)
                return;
            _players.Remove(player);
        }
        Quiet(player);
        string name = player.Name;
        await player.DisposeAsync().ConfigureAwait(false);
        Log.Info($"Spieler {index + 1} getrennt ({reason})");
        Emit("disconnected", new JsonObject { ["player"] = index + 1, ["name"] = name, ["reason"] = reason });
        KickScan();
        RequestStatus();
    }

    private async Task DisconnectAllAsync(bool quiet)
    {
        List<Player> all;
        lock (_players)
        {
            all = [.. _players];
            _players.Clear();
        }
        foreach (var p in all)
        {
            if (quiet)
                Quiet(p);
            await p.DisposeAsync().ConfigureAwait(false);
        }
        if (all.Count > 0)
            RequestStatus();
    }

    private void Quiet(Player player)
    {
        long until = Environment.TickCount64 + QuietMs;
        foreach (var d in player.Devices.ToList())
            _quietUntil[d.Address] = until;
    }

    /// <summary>Nach <see cref="Settings.InactivityMinutes"/> ohne Eingabe trennen (spart Akku, wie unter Windows).</summary>
    private async Task InactivityAsync(CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(30000, stop).ConfigureAwait(false);
                int minutes = _settings.InactivityMinutes;
                if (minutes <= 0)
                    continue;
                List<Player> idle;
                lock (_players)
                    idle = _players.Where(p => Environment.TickCount64 - p.LastActivityTicks > minutes * 60000L).ToList();
                foreach (var p in idle)
                    await DisconnectPlayerAsync(p.Index, $"{minutes} min ohne Eingabe").ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ---------- Einstellungen ----------

    private void WatchSettings()
    {
        try
        {
            _settingsWatcher = new FileSystemWatcher(_dataDir, "settings.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            _settingsWatcher.Changed += (_, _) => ReloadSettingsSoon();
            _settingsWatcher.Created += (_, _) => ReloadSettingsSoon();
            _settingsWatcher.Renamed += (_, _) => ReloadSettingsSoon();
            _settingsWatcher.EnableRaisingEvents = true;
        }
        catch (Exception e)
        {
            Log.Warn($"Einstellungen werden nicht überwacht: {Log.Reason(e)}");
        }
    }

    private int _reloadPending;

    private void ReloadSettingsSoon()
    {
        if (Interlocked.Exchange(ref _reloadPending, 1) == 1)
            return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(300).ConfigureAwait(false); // Schreibvorgang abschließen lassen
            Interlocked.Exchange(ref _reloadPending, 0);
            ReloadSettings();
        });
    }

    private void ReloadSettings()
    {
        var fresh = Settings.Load(_settingsPath);
        if (fresh.LoadError is { } error)
        {
            Log.Warn($"settings.json nicht lesbar ({error}) – bisherige Einstellungen bleiben");
            return;
        }
        _settings = fresh;
        Log.Info("Einstellungen neu geladen");
        ApplyDsu();
        bool background = LinuxSetting("BackgroundScan", true);
        if (background != _backgroundScan)
        {
            _backgroundScan = background;
            Log.Info($"Hintergrundsuche: {(background ? "an" : "aus")}"); // die Überwachungsschleife startet/stoppt die Suche
        }
        if (_gamepads is { } gamepads && _scanner is { } scanner)
        {
            bool auto = AutoPairGamepads();
            if (auto != gamepads.Enabled)
            {
                gamepads.Enabled = auto;
                scanner.IncludeClassic = auto;
                Log.Info($"Andere Controller automatisch koppeln: {(auto ? "an" : "aus")}");
                KickScan();
            }
        }
        RequestStatus();
    }

    // ---------- Befehle (stdin) ----------

    private async Task ReadCommandsAsync(CancellationToken stop)
    {
        try
        {
            using var reader = new StreamReader(Console.OpenStandardInput());
            // Aus Kodi gestartet: stdin zu = Kodi beendet oder abgestürzt → mit beenden. Sonst (systemd, von Hand):
            // kein stdin – nur Statusdatei und Signale.
            if (await ReadCommandsAsync(reader, HandleCommandAsync, stop).ConfigureAwait(false) && _withKodi)
                StopRunning("Kodi hat die Verbindung geschlossen");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.Warn($"Befehle: {Log.Reason(e)}");
        }
    }

    /// <summary>
    /// Liest Befehle zeilenweise und führt sie aus; true = Eingabe zu Ende (stdin geschlossen). Jeder Befehl ist
    /// einzeln abgesichert: Ein Fehler (z. B. D-Bus-Fehler beim Trennen eines Gamepads, das sich gerade selbst getrennt
    /// hat) darf den Befehlskanal nicht beenden – sonst gingen alle weiteren Menübefehle bis zum Neustart ins Leere.
    /// </summary>
    internal static async Task<bool> ReadCommandsAsync(TextReader reader, Func<string, Task> handle, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(stop).ConfigureAwait(false);
            if (line is null)
                return true;
            try
            {
                await handle(line.Trim()).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException || !stop.IsCancellationRequested)
            {
                Log.Warn($"Befehl „{line.Trim()}“ fehlgeschlagen: {Log.Reason(e)}");
            }
        }
        return false;
    }

    internal async Task HandleCommandAsync(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts.FirstOrDefault())
        {
            case "reload":
                ReloadSettings();
                break;
            case "disconnect" when parts.Length > 1 && int.TryParse(parts[1], out int player):
                await DisconnectPlayerAsync(player - 1, "von Hand").ConfigureAwait(false);
                break;
            case "disconnect-all":
                await DisconnectAllAsync(quiet: true).ConfigureAwait(false);
                Emit("disconnected-all", new JsonObject());
                break;
            case "status":
                RequestStatus();
                break;
            case "scan":
                await ScanNowAsync().ConfigureAwait(false);
                break;
            case "gamepad-disconnect" when parts.Length > 1 && _gamepads is { } g1:
                await g1.DisconnectAsync(parts[1]).ConfigureAwait(false);
                await RefreshGamepadsAsync().ConfigureAwait(false);
                break;
            case "gamepad-forget" when parts.Length > 1 && _gamepads is { } g2:
                if (await g2.ForgetAsync(parts[1]).ConfigureAwait(false))
                    Log.Info($"{parts[1]}: Kopplung gelöscht");
                await RefreshGamepadsAsync().ConfigureAwait(false);
                break;
            case null:
                break;
            default:
                Log.Warn($"Unbekannter Befehl: {line}");
                break;
        }
    }

    // ---------- Status und Ereignisse ----------

    internal void SetAdapterState(string state)
    {
        if (_adapterState == state)
            return;
        _adapterState = state;
        _waitedForAdapter = false; // neuer Zustand: beim nächsten „kein Adapter“ wieder eine Runde abwarten (OnNoAdapter)
        Emit("bluetooth", new JsonObject { ["state"] = state });
        RequestStatus();
    }

    private void Emit(string name, JsonObject data)
    {
        data["event"] = name;
        string line = data.ToJsonString();
        lock (_stdoutLock)
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
    }

    private void RequestStatus() => _statusSignal.Release();

    /// <summary>Schreibt status.json bei Änderungen (gebündelt) und alle 5 s (Akku, Berichtsrate).</summary>
    private async Task StatusWriterAsync(CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await _statusSignal.WaitAsync(5000, stop).ConfigureAwait(false);
                await Task.Delay(100, stop).ConfigureAwait(false);
                while (_statusSignal.CurrentCount > 0)
                    _statusSignal.Wait(0);
                WriteStatus("running");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    internal void WriteStatus(string state)
    {
        List<Player> players;
        lock (_players)
            players = [.. _players];
        string? bluetooth = _adapterState;
        var status = new JsonObject
        {
            // Bluetooth noch nicht geprüft: „starting“ – das Add-on zeigt dann „startet …“ statt eines falschen Hinweises.
            ["state"] = state == "running" && bluetooth is null ? "starting" : state,
            ["platform"] = "linux",
            ["version"] = typeof(Daemon).Assembly.GetName().Version?.ToString(3),
            ["adapter"] = _adapterAddress,
            ["scanning"] = _scanner?.Scanning == true,
            ["connecting"] = new JsonArray([.. _connecting.Keys.Select(a => (JsonNode)a)]),
            ["players"] = new JsonArray([.. players.Select(p => (JsonNode)new JsonObject
            {
                ["player"] = p.Index + 1,
                ["name"] = p.Name,
                ["battery"] = p.BatteryPercent,
                ["charging"] = p.Charging,
                ["devices"] = new JsonArray([.. p.Devices.ToList().Select(d => (JsonNode)new JsonObject
                {
                    ["address"] = d.Address,
                    ["kind"] = d.Kind.ToString(),
                    ["name"] = d.Kind.DisplayName(),
                    ["battery"] = d.LastState?.BatteryPercent ?? -1,
                    ["serial"] = d.SerialNumber,
                    ["firmware"] = d.Firmware,
                    ["rate"] = Math.Round(d.ReportRate),
                })]),
            })]),
            ["known"] = new JsonArray([.. KnownSnapshot().Select(k => (JsonNode)new JsonObject
            {
                ["address"] = k.Key,
                ["kind"] = k.Value.Kind,
                ["name"] = Enum.TryParse<ControllerKind>(k.Value.Kind, out var kind) ? kind.DisplayName() : "Nintendo Controller",
                ["serial"] = k.Value.Serial,
                ["connected"] = players.Any(p => p.Devices.ToList().Any(d => string.Equals(d.Address, k.Key, StringComparison.OrdinalIgnoreCase))),
            })]),
            ["elsewhere"] = new JsonArray([.. _elsewhere.Values.Select(f => (JsonNode)new JsonObject
            {
                ["address"] = f.Address,
                ["name"] = f.Kind.DisplayName(),
            })]),
            ["autoPairGamepads"] = _gamepads?.Enabled ?? false,
            ["backgroundScan"] = _backgroundScan,
            ["gamepads"] = new JsonArray([.. (_gamepads?.Known ?? []).Select(g => (JsonNode)new JsonObject
            {
                ["address"] = g.Address,
                ["name"] = g.Name,
                ["connected"] = g.Connected,
                ["battery"] = g.Battery,
            })]),
        };
        if (bluetooth is not null)
            status["bluetooth"] = bluetooth; // genau einer von on | off | missing | no_le
        try
        {
            string temp = _statusPath + ".tmp";
            File.WriteAllText(temp, status.ToJsonString(StatusJson));
            File.Move(temp, _statusPath, overwrite: true);
        }
        catch (Exception e)
        {
            Log.Warn($"status.json: {Log.Reason(e)}");
        }
    }

    private static ulong ParseAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        string hex = text.Replace(":", "").Trim();
        return hex.Length == 12 && ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out ulong v) ? v : 0;
    }

    public async ValueTask DisposeAsync()
    {
        _settingsWatcher?.Dispose();
        await EndSessionAsync().ConfigureAwait(false);
        DsuServer.Instance?.Dispose();
        WriteStatus("stopped");
    }
}
