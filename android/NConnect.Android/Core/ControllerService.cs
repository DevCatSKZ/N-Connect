using System.Collections.Concurrent;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using AndroidX.Core.App;
using NConnect.Android.Ble;
using NConnect.Android.Output;
using NConnect.Android.Touch;
using NConnect.Android.UI;
using Switch2Pro.Protocol;

namespace NConnect.Android.Core;

/// <summary>
/// Vordergrunddienst: sucht Controller, hält ihre Verbindungen und die virtuellen Controller, solange gespielt wird
/// (Android beendet Apps im Hintergrund sonst). Zeigt eine dauerhafte Benachrichtigung mit Status und Schnellaktionen.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class ControllerService : Service
{
    public const string ActionStop = "com.devcatskz.nconnect.STOP";
    public const string ActionEditTouch = "com.devcatskz.nconnect.EDIT_TOUCH";
    private const string Channel = "controller";
    private const string ChannelAlerts = "hinweise";
    private const int NotificationId = 1;

    public static ControllerService? Instance { get; private set; }
    public static event Action? Changed;

    private readonly Switch2Scanner _scanner = new();
    private readonly List<Player> _players = [];
    private readonly ConcurrentDictionary<string, byte> _connecting = new();
    private readonly ConcurrentDictionary<string, FoundController> _elsewhere = new();
    /// <summary>Von Hand getrennte Controller kurz ignorieren, sonst verbänden sie sich sofort wieder.</summary>
    private readonly ConcurrentDictionary<string, long> _quietUntil = new();
    /// <summary>Nach Fehlschlägen zunehmend lange Pause vor dem nächsten Versuch (Fehlversuche, Pause bis).</summary>
    private readonly ConcurrentDictionary<string, (int Failures, long Until)> _retry = new();
    private readonly CancellationTokenSource _cts = new();
    private BluetoothStateReceiver? _btReceiver;
    private Kodi.KodiStatusWriter? _kodiStatus;

    public bool BluetoothOn => Switch2Scanner.Adapter(this)?.IsEnabled == true;
    public bool Scanning => _scanner.Scanning;

    public IReadOnlyList<Player> Players
    {
        get
        {
            lock (_players)
                return [.. _players];
        }
    }

    /// <summary>Gefundene Controller, die mit einem anderen Gerät gekoppelt sind (Hinweis „SYNC drücken“).</summary>
    public IReadOnlyCollection<FoundController> Elsewhere => [.. _elsewhere.Values];

    /// <summary>Adressen, deren Verbindungsaufbau gerade läuft (für den Kodi-Status).</summary>
    public IReadOnlyCollection<string> Connecting => [.. _connecting.Keys];

    /// <summary>Ausweichname der Kodi-status.json nach einer Neuinstallation (sonst null) – Hinweis in Hilfe › Kodi.</summary>
    public string? KodiStatusFallback => _kodiStatus?.FallbackName;

    /// <summary>Dienst starten (aus einer Activity heraus).</summary>
    public static void Start(Context context)
    {
        var intent = new Intent(context, typeof(ControllerService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        HangWatchdog.Start();
        Instance = this;
        CreateChannel(this);
        _scanner.Found += OnFound;
        _btReceiver = new BluetoothStateReceiver(this);
        RegisterReceiver(_btReceiver, new IntentFilter(BluetoothAdapter.ActionStateChanged));
        OutputRouter.Changed += UpdateNotification;
        TouchService.StateChanged += OnTouchStateChanged;
        _kodiStatus = new Kodi.KodiStatusWriter(this);
        _kodiStatus.Start();
        NConnectTile.RequestUpdate(this);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        switch (intent?.Action)
        {
            case ActionStop:
                StopEverything();
                return StartCommandResult.NotSticky;
            case ActionEditTouch:
                if (TouchService.Instance is { } touch)
                {
                    CloseNotificationShade();
                    touch.ShowEditor();
                }
                else
                {
                    TouchService.OpenSystemSettings(this);
                }
                return StartCommandResult.Sticky;
        }
        if (!TryStart())
            return StartCommandResult.NotSticky;
        if (!_outputWatch)
        {
            _outputWatch = true;
            _ = Task.Run(() => WatchOutputAsync(_cts.Token));
            _ = Task.Run(() => MaintenanceLoopAsync(_cts.Token));
        }
        if (AppPrefs.DsuEnabled)
            DsuServer.Start(network: false, Log.Info, Log.Warn);
        return StartCommandResult.Sticky;
    }

    private bool _outputWatch;

    /// <summary>
    /// Als Vordergrunddienst starten und suchen. Fehlt „Geräte in der Nähe“ oder lehnt Android den Start ab, beendet sich
    /// der Dienst mit einem Protokolleintrag – ab Android 12 würfe die Suche, ab Android 14 schon StartForeground eine
    /// SecurityException. Die Berechtigung wird bewusst erst nach StartAsForeground geprüft: Endet ein per
    /// StartForegroundService gestarteter Dienst ohne StartForeground, beendet Android die App („did not then call
    /// startForeground“). Bis Android 13 klappt StartForeground auch ohne die Berechtigung; ob Android ab 14 den
    /// abgelehnten Aufruf gelten lässt, ist nicht auf einem Gerät geprüft – Kachel, Autostart und App prüfen deshalb vorher.
    /// </summary>
    private bool TryStart()
    {
        try
        {
            StartAsForeground();
            if (UI.Permissions.HasBluetooth(this))
            {
                StartScan();
                return true;
            }
            Log.Warn("Controller-Dienst beendet: Berechtigung „Geräte in der Nähe“ fehlt");
        }
        catch (Exception e)
        {
            Log.Error("Controller-Dienst konnte nicht starten", e);
        }
        _scanner.Stop();
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
        return false;
    }

    /// <summary>
    /// Hält den Ausgabeweg aktuell: ist (noch) keiner aktiv – etwa weil Shizuku erst nach SKZ Connect gestartet wurde –,
    /// wird alle 2 s nachgeprüft und sofort übernommen; fällt Shizuku weg (Neustart), merkt die App das ebenso.
    /// Bei gesundem Weg genügt eine Prüfung alle 15 s (Akku).
    /// </summary>
    private async Task WatchOutputAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var before = OutputRouter.Shell;
                bool lost = before is not null && !before.HasPermission();
                if (before is null || lost)
                {
                    var now = await OutputRouter.ResolveAsync(interactive: false);
                    if (now is not null && now != before)
                    {
                        Log.Info($"Ausgabeweg bereit: {now.Name}");
                        await ReapplyOutputAsync();
                    }
                    else if (lost && now is null)
                    {
                        Log.Warn($"{before!.Name} nicht mehr verfügbar – virtuelle Controller pausiert");
                        RaiseChanged();
                    }
                }
                await Task.Delay(before is null || lost ? 2000 : 15000, ct);
            }
        }
        catch (System.OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Langsame Nebenaufgaben: Akku-Warnung und Profil des gerade laufenden Spiels. Jeder Durchlauf ist einzeln
    /// abgesichert – eine Ausnahme (z. B. aus UsageStats/AppOps) darf die Schleife nicht still beenden, sonst gäbe es bis
    /// zum Neustart des Dienstes weder Akku-Warnungen noch Profilwechsel.
    /// </summary>
    private async Task MaintenanceLoopAsync(CancellationToken ct)
    {
        string? lastError = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(5000, ct);
                try
                {
                    if (_scanner is { Scanning: true, LowPower: false } && System.Environment.TickCount64 >= _fastScanUntil)
                        UpdateScanning(); // schnelles Suchfenster abgelaufen → stromsparend weitersuchen
                    foreach (var player in Players)
                    {
                        if (player.CheckBattery() is { } warning)
                            NotifyBattery(player, warning);
                    }
                    Profiles.GameProfileWatcher.Poll(this, RaiseChanged);
                    lastError = null;
                }
                catch (Exception e) when (e is not System.OperationCanceledException)
                {
                    if (e.Message != lastError) // nicht alle 5 s dieselbe Zeile
                        Log.Error("Wartung (Akku/Profil)", e);
                    lastError = e.Message;
                }
            }
        }
        catch (System.OperationCanceledException)
        {
        }
    }

    private void StartAsForeground()
    {
        var n = BuildNotification();
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, n, ForegroundService.TypeConnectedDevice);
        else
            StartForeground(NotificationId, n);
    }

    /// <summary>Bis dahin (TickCount64) wird schnell gesucht – danach stromsparend, auch ganz ohne Controller.</summary>
    private long _fastScanUntil;
    private const long FastScanMs = 120_000;

    public void StartScan()
    {
        _fastScanUntil = System.Environment.TickCount64 + FastScanMs;
        UpdateScanning();
        if (!_scanner.Scanning && !BluetoothOn)
            Log.Warn("Bluetooth ist aus – Suche startet, sobald es eingeschaltet wird");
        RaiseChanged();
    }

    /// <summary>
    /// Suche dem Zustand anpassen: nur in den ersten zwei Minuten nach App-Start/Bluetooth-an/Trennen und ohne
    /// Controller schnell suchen (Dauer-LowLatency belastet Akku und das ganze Telefon), sonst stromsparend – und gar
    /// nicht mehr, wenn alle acht Spielerplätze belegt sind und auch kein Joy-Con mehr auf seinen Partner wartet.
    /// </summary>
    private void UpdateScanning()
    {
        lock (_players)
        {
            bool pairPossible = _players.Any(p => p.Devices.Count == 1 && p.Devices[0].Kind.IsJoyCon());
            if (_players.Count >= 8 && !pairPossible)
            {
                _scanner.Stop();
                return;
            }
            bool fast = _players.Count == 0 && System.Environment.TickCount64 < _fastScanUntil;
            _scanner.Start(this, lowPower: !fast);
        }
    }

    // ---------- Controller finden und verbinden ----------

    private void OnFound(FoundController f)
    {
        ulong me = AppPrefs.ParseAddress(AppPrefs.HostAddress);
        // Mit diesem Gerät gekoppelt (Android-Bond)? Dann ist die Einordnung „anderes Gerät“ sicher falsch –
        // z. B. wenn die Host-Adresse in der Werbung älter oder anders angeordnet ist. Die Prüfung kostet einen
        // Binder-Aufruf plus Java-Objekte, daher nur wenn nötig und zwischengespeichert (siehe IsBonded).
        bool forUs = f.SyncMode || (me != 0 && f.Host == me) || IsBonded(f.Address);
        if (!forUs)
        {
            if (_elsewhere.TryAdd(f.Address, f))
            {
                Log.Info($"{f.Address}: {Texts.ShortName(f.Kind)} wirbt für {BtAddress.Format(f.Host)} – mit einem anderen Gerät gekoppelt");
                RaiseChanged();
            }
            return;
        }
        _elsewhere.TryRemove(f.Address, out _);
        if (_quietUntil.TryGetValue(f.Address, out long until) && System.Environment.TickCount64 < until)
            return;
        // Nach einem Fehlschlag nicht sofort wieder – sonst kreiselt die Verbindung im Sekundentakt.
        if (_retry.TryGetValue(f.Address, out var retry) && System.Environment.TickCount64 < retry.Until)
            return;
        if (IsConnected(f.Address) || !_connecting.TryAdd(f.Address, 0))
            return;
        // Nur im SYNC-Modus koppeln – und nur, wenn die eigene Adresse bekannt ist (sonst wäre die Kopplung wertlos).
        ulong pairHost = f.SyncMode ? me : 0;
        // Suchergebnisse kommen auf dem Haupt-Thread an – Verbindung, Einrichtung und virtueller Controller (Shizuku/Root-
        // Prozess) laufen im Hintergrund, sonst friert die Oberfläche ein („App reagiert nicht“).
        _ = Task.Run(() => ConnectAsync(f, pairHost));
    }

    private readonly ConcurrentDictionary<string, (bool Bonded, long Ticks)> _bondCache = new();

    /// <summary>
    /// Bond-Status eines Controllers, höchstens alle 15 s neu abgefragt. Werbende Controller melden sich mehrmals pro
    /// Sekunde – jede Abfrage erzeugt Java-Objekte und einen Binder-Aufruf auf dem Haupt-Thread, ohne Zwischenspeicher
    /// führt das zu Speicherbereinigungs-Stürmen und hängender Oberfläche.
    /// </summary>
    private bool IsBonded(string address)
    {
        long now = System.Environment.TickCount64;
        if (_bondCache.TryGetValue(address, out var cached) && now - cached.Ticks < 15000)
            return cached.Bonded;
        bool bonded = false;
        try
        {
            using var device = Switch2Scanner.Adapter(this)?.GetRemoteDevice(address);
            bonded = device?.BondState == Bond.Bonded;
        }
        catch (Exception)
        {
            // Bluetooth gerade aus oder Adresse ungültig – als „nicht gekoppelt“ werten
        }
        _bondCache[address] = (bonded, now);
        return bonded;
    }

    private bool IsConnected(string address)
    {
        lock (_players)
            return _players.Any(p => p.Devices.Any(d => d.Address == address));
    }

    private async Task ConnectAsync(FoundController f, ulong pairHost)
    {
        Log.Info($"{f.Address}: {Texts.ShortName(f.Kind)} gefunden{(f.SyncMode ? " (SYNC)" : "")}, verbinde …");
        RaiseChanged();
        try
        {
            var adapter = Switch2Scanner.Adapter(this) ?? throw new InvalidOperationException("Bluetooth ist nicht verfügbar.");
            var remote = adapter.GetRemoteDevice(f.Address) ?? throw new InvalidOperationException("Controller nicht gefunden.");
            var device = await Switch2Device.ConnectAsync(this, remote, f.Kind, pairHost, _cts.Token).ConfigureAwait(false);
            if (device.IsLost)
            {
                Log.Warn($"{f.Address}: während der Einrichtung getrennt – nicht übernommen");
                device.Dispose();
                NoteFailure(f.Address);
                return;
            }
            _retry.TryRemove(f.Address, out _); // Fehlerzähler bei Erfolg zurücksetzen
            _bondCache.TryRemove(f.Address, out _); // evtl. gerade neu gekoppelt
            AddToPlayer(device);
        }
        catch (Exception e) when (!_cts.IsCancellationRequested)
        {
            Log.Error($"{f.Address}: Verbindung fehlgeschlagen", e);
            NoteFailure(f.Address);
        }
        finally
        {
            _connecting.TryRemove(f.Address, out _);
            RaiseChanged();
        }
    }

    /// <summary>Fehlversuch merken und die nächste Pause verdoppeln (5 s → höchstens 60 s).</summary>
    private void NoteFailure(string address)
    {
        var entry = _retry.AddOrUpdate(address, (0, 0L), (_, old) => old);
        int failures = entry.Failures + 1;
        long delay = Math.Min(60000, 5000L << Math.Min(failures - 1, 4));
        _retry[address] = (failures, System.Environment.TickCount64 + delay);
    }

    private void AddToPlayer(Switch2Device device)
    {
        var settings = AppPrefs.Settings;
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
                int index = Enumerable.Range(0, 8).First(i => _players.All(p => p.Index != i));
                player = new Player(index);
                _players.Add(player);
                _players.Sort((a, b) => a.Index.CompareTo(b.Index));
            }
            player.Add(device);
        }
        device.StateReceived += player.OnState;
        device.Lost += OnLost;
        KnownControllers.Remember(device.Address, device.Kind, device.SerialNumber);
        if (!pairing)
            player.AttachOutput(OutputRouter.CreatePad(player.Index, L.F("SKZ Connect Spieler {0}", player.Index + 1)));
        _ = device.SetPlayerAsync(player.Index);
        _ = device.PlayConnectFeedbackAsync();
        Log.Info(pairing ? $"Joy-Con zusammengefasst (Spieler {player.Index + 1})"
            : $"{Texts.ShortName(device.Kind)} verbunden (Spieler {player.Index + 1})");
        UpdateScanning();
        RaiseChanged();
    }

    private void OnLost(Switch2Device device)
    {
        Player? emptied = null;
        lock (_players)
        {
            var player = _players.FirstOrDefault(p => p.Devices.Contains(device));
            if (player is null)
                return;
            player.Remove(device);
            if (player.Devices.Count == 0)
            {
                _players.Remove(player);
                emptied = player;
            }
        }
        device.Dispose();
        emptied?.Dispose();
        DsuServer.Instance?.Clear(emptied?.Index ?? -1);
        Log.Info($"{Texts.ShortName(device.Kind)} getrennt");
        if (emptied is not null)
            _fastScanUntil = System.Environment.TickCount64 + FastScanMs;
        UpdateScanning();
        RaiseChanged();
    }

    /// <summary>Einen Spieler trennen (der Controller bleibt gekoppelt und verbindet sich per Tastendruck neu).</summary>
    public void DisconnectPlayer(Player player)
    {
        bool none;
        lock (_players)
        {
            if (!_players.Remove(player))
                return;
            none = _players.Count == 0;
        }
        Quiet(player);
        _ = Task.Run(player.Dispose); // Prozess-/Bluetooth-Abbau nicht auf dem Hauptthread
        Log.Info($"Spieler {player.Index + 1} getrennt");
        if (none)
            StartScan();
        else
            RaiseChanged();
    }

    /// <summary>Alle Controller trennen (bleiben gekoppelt und verbinden sich per Tastendruck neu).</summary>
    public void DisconnectAll()
    {
        List<Player> all;
        lock (_players)
        {
            all = [.. _players];
            _players.Clear();
        }
        foreach (var p in all)
        {
            Quiet(p);
            DsuServer.Instance?.Clear(p.Index);
        }
        _ = Task.Run(() => { foreach (var p in all) p.Dispose(); });
        StartScan();
    }

    private void Quiet(Player player)
    {
        long until = System.Environment.TickCount64 + 15000;
        foreach (var d in player.Devices)
            _quietUntil[d.Address] = until;
    }

    /// <summary>Ausgabeweg wurde geändert: virtuelle Controller neu anlegen.</summary>
    public async Task ReapplyOutputAsync()
    {
        await OutputRouter.ResolveAsync(interactive: false);
        var players = Players;
        await Task.Run(() =>
        {
            foreach (var p in players)
                p.AttachOutput(OutputRouter.CreatePad(p.Index, L.F("SKZ Connect Spieler {0}", p.Index + 1)));
        });
        RaiseChanged();
    }

    private void OnTouchStateChanged()
    {
        if (AppPrefs.TouchEnabled || AppPrefs.Mode == OutputPath.Touch)
            _ = ReapplyOutputAsync();
        else
            UpdateNotification();
    }

    private void StopEverything()
    {
        _scanner.Stop();
        DsuServer.Instance?.Dispose();
        DisconnectAll();
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    public override void OnDestroy()
    {
        _cts.Cancel();
        _scanner.Stop();
        lock (_players)
        {
            foreach (var p in _players)
                p.Dispose();
            _players.Clear();
        }
        if (_btReceiver is not null)
            UnregisterReceiver(_btReceiver);
        _kodiStatus?.Dispose();
        _kodiStatus = null;
        OutputRouter.Changed -= UpdateNotification;
        TouchService.StateChanged -= OnTouchStateChanged;
        Instance = null;
        Changed?.Invoke();
        NConnectTile.RequestUpdate(this);
        base.OnDestroy();
    }

    private void RaiseChanged()
    {
        UpdateNotification();
        Changed?.Invoke();
    }

    // ---------- Benachrichtigung ----------

    /// <summary>Benachrichtigungs-Kanäle anlegen – auch aus dem BootReceiver heraus nutzbar.</summary>
    internal static void CreateChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;
        var manager = context.GetSystemService(NotificationService)!.JavaCast<NotificationManager>()!;
        var channel = new NotificationChannel(Channel, L.T("Controller-Verbindung"), NotificationImportance.Low)
        {
            Description = L.T("Zeigt, welche Controller verbunden sind, solange SKZ Connect im Hintergrund läuft."),
        };
        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);
        var alerts = new NotificationChannel(ChannelAlerts, L.T("Hinweise"), NotificationImportance.Default)
        {
            Description = L.T("Akku-Warnungen und Hinweise auf neue Versionen."),
        };
        manager.CreateNotificationChannel(alerts);
    }

    private Notification BuildNotification()
    {
        var players = Players;
        string title = players.Count switch
        {
            0 => BluetoothOn ? "Warte auf Controller" : "Bluetooth ist aus",
            1 => L.F("{0} verbunden", players[0].Name),
            _ => L.F("{0} Controller verbunden", players.Count),
        };
        string text = players.Count == 0
            ? (BluetoothOn ? "SYNC-Taste drücken oder eine Taste an einem gekoppelten Controller." : "Schalte Bluetooth ein, damit SKZ Connect Controller findet.")
            : OutputRouter.Describe();

        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var builder = new NotificationCompat.Builder(this, Channel)
            .SetSmallIcon(Resource.Drawable.ic_stat_controller)!
            .SetContentTitle(L.T(title))!
            .SetContentText(L.T(text))!
            .SetOngoing(true)!
            .SetOnlyAlertOnce(true)!
            .SetContentIntent(open)!
            .SetCategory(NotificationCompat.CategoryService)!
            .SetColor(UI.Look.C(UI.Look.P(this).Accent).ToArgb())!
            .SetPriority(NotificationCompat.PriorityLow)!;
        if (AppPrefs.TouchEnabled || AppPrefs.Mode == OutputPath.Touch)
            builder.AddAction(0, L.T("Touch-Layout bearbeiten"), ServiceIntent(ActionEditTouch, 1));
        builder.AddAction(0, L.T("Beenden"), ServiceIntent(ActionStop, 2));
        return builder.Build()!;
    }

    private PendingIntent ServiceIntent(string action, int requestCode) =>
        PendingIntent.GetService(this, requestCode, new Intent(this, typeof(ControllerService)).SetAction(action),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

    /// <summary>Akku-Warnung als eigene Benachrichtigung (je Spieler einmal, solange nicht geladen wird).</summary>
    private void NotifyBattery(Player player, string text)
    {
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var n = new NotificationCompat.Builder(this, ChannelAlerts)
            .SetSmallIcon(Resource.Drawable.ic_battery)!
            .SetContentTitle(L.T("Akku fast leer"))!
            .SetContentText(text)!
            .SetContentIntent(open)!
            .SetAutoCancel(true)!
            .SetColor(UI.Look.C(UI.Look.P(this).Accent).ToArgb())!
            .Build()!;
        try
        {
            NotificationManagerCompat.From(this)!.Notify(NotificationId + 10 + player.Index, n);
        }
        catch (Java.Lang.SecurityException)
        {
        }
    }

    private void UpdateNotification()
    {
        if (Instance is null)
            return;
        try
        {
            NotificationManagerCompat.From(this)!.Notify(NotificationId, BuildNotification());
        }
        catch (Java.Lang.SecurityException)
        {
            // Benachrichtigungen nicht erlaubt – der Dienst läuft trotzdem
        }
    }

    private void CloseNotificationShade()
    {
        // Ab Android 12 schließt das System die Leiste beim Antippen einer Aktion selbst.
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
#pragma warning disable CA1422
            SendBroadcast(new Intent(Intent.ActionCloseSystemDialogs));
#pragma warning restore CA1422
    }

    /// <summary>Bluetooth an/aus: Suche starten bzw. Controller als getrennt behandeln.</summary>
    private sealed class BluetoothStateReceiver(ControllerService service) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            var state = (State)intent!.GetIntExtra(BluetoothAdapter.ExtraState, -1);
            if (state == State.On)
                service.StartScan();
            else if (state == State.Off)
            {
                service.DisconnectAll();
                service._scanner.Stop();
                service.RaiseChanged();
            }
        }
    }
}
