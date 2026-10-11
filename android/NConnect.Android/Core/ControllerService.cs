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
    private const int NotificationId = 1;

    public static ControllerService? Instance { get; private set; }
    public static event Action? Changed;

    private readonly Switch2Scanner _scanner = new();
    private readonly List<Player> _players = [];
    private readonly ConcurrentDictionary<string, byte> _connecting = new();
    private readonly ConcurrentDictionary<string, FoundController> _elsewhere = new();
    /// <summary>Von Hand getrennte Controller kurz ignorieren, sonst verbänden sie sich sofort wieder.</summary>
    private readonly ConcurrentDictionary<string, long> _quietUntil = new();
    private readonly CancellationTokenSource _cts = new();
    private BluetoothStateReceiver? _btReceiver;

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
        CreateChannel();
        _scanner.Found += OnFound;
        _btReceiver = new BluetoothStateReceiver(this);
        RegisterReceiver(_btReceiver, new IntentFilter(BluetoothAdapter.ActionStateChanged));
        OutputRouter.Changed += UpdateNotification;
        TouchService.StateChanged += OnTouchStateChanged;
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
        StartAsForeground();
        StartScan();
        if (!_outputWatch)
        {
            _outputWatch = true;
            _ = Task.Run(() => WatchOutputAsync(_cts.Token));
        }
        return StartCommandResult.Sticky;
    }

    private bool _outputWatch;

    /// <summary>
    /// Hält den Ausgabeweg aktuell: ist (noch) keiner aktiv – etwa weil Shizuku erst nach N-Connect gestartet wurde –,
    /// wird alle 2 s nachgeprüft und sofort übernommen; fällt Shizuku weg (Neustart), merkt die App das ebenso.
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
                await Task.Delay(2000, ct);
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

    public void StartScan()
    {
        bool anyConnected;
        lock (_players)
            anyConnected = _players.Count > 0;
        if (!_scanner.Start(this, lowPower: anyConnected))
            Log.Warn("Bluetooth ist aus – Suche startet, sobald es eingeschaltet wird");
        RaiseChanged();
    }

    // ---------- Controller finden und verbinden ----------

    private void OnFound(FoundController f)
    {
        ulong me = AppPrefs.ParseAddress(AppPrefs.HostAddress);
        bool forUs = f.SyncMode || (me != 0 && f.Host == me);
        if (!forUs)
        {
            if (_elsewhere.TryAdd(f.Address, f))
            {
                Log.Info($"{f.Address}: {Texts.ShortName(f.Kind)} ist mit einem anderen Gerät gekoppelt");
                RaiseChanged();
            }
            return;
        }
        _elsewhere.TryRemove(f.Address, out _);
        if (_quietUntil.TryGetValue(f.Address, out long until) && System.Environment.TickCount64 < until)
            return;
        if (IsConnected(f.Address) || !_connecting.TryAdd(f.Address, 0))
            return;
        // Nur im SYNC-Modus koppeln – und nur, wenn die eigene Adresse bekannt ist (sonst wäre die Kopplung wertlos).
        ulong pairHost = f.SyncMode ? me : 0;
        // Suchergebnisse kommen auf dem Haupt-Thread an – Verbindung, Einrichtung und virtueller Controller (Shizuku/Root-
        // Prozess) laufen im Hintergrund, sonst friert die Oberfläche ein („App reagiert nicht“).
        _ = Task.Run(() => ConnectAsync(f, pairHost));
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
            var device = await Switch2Device.ConnectAsync(this, f.Device, f.Kind, pairHost, _cts.Token).ConfigureAwait(false);
            if (device.IsLost)
            {
                Log.Warn($"{f.Address}: während der Einrichtung getrennt – nicht übernommen");
                device.Dispose();
                return;
            }
            AddToPlayer(device);
        }
        catch (Exception e) when (!_cts.IsCancellationRequested)
        {
            Log.Error($"{f.Address}: Verbindung fehlgeschlagen", e);
        }
        finally
        {
            _connecting.TryRemove(f.Address, out _);
            RaiseChanged();
        }
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
            player.AttachOutput(OutputRouter.CreatePad(player.Index, $"N-Connect Spieler {player.Index + 1}"));
        _ = device.SetPlayerAsync(player.Index);
        _ = device.PlayConnectFeedbackAsync();
        Log.Info(pairing ? $"Joy-Con zusammengefasst (Spieler {player.Index + 1})"
            : $"{Texts.ShortName(device.Kind)} verbunden (Spieler {player.Index + 1})");
        _scanner.Start(this, lowPower: true);
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
        Log.Info($"{Texts.ShortName(device.Kind)} getrennt");
        bool none;
        lock (_players)
            none = _players.Count == 0;
        if (none)
            _scanner.Start(this, lowPower: false);
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
            _scanner.Start(this, lowPower: false);
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
            Quiet(p);
        _ = Task.Run(() => { foreach (var p in all) p.Dispose(); });
        _scanner.Start(this, lowPower: false);
        RaiseChanged();
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
                p.AttachOutput(OutputRouter.CreatePad(p.Index, $"N-Connect Spieler {p.Index + 1}"));
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
        OutputRouter.Changed -= UpdateNotification;
        TouchService.StateChanged -= OnTouchStateChanged;
        Instance = null;
        Changed?.Invoke();
        base.OnDestroy();
    }

    private void RaiseChanged()
    {
        UpdateNotification();
        Changed?.Invoke();
    }

    // ---------- Benachrichtigung ----------

    private void CreateChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;
        var channel = new NotificationChannel(Channel, "Controller-Verbindung", NotificationImportance.Low)
        {
            Description = "Zeigt, welche Controller verbunden sind, solange N-Connect im Hintergrund läuft.",
        };
        channel.SetShowBadge(false);
        GetSystemService(NotificationService)!.JavaCast<NotificationManager>()!.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification()
    {
        var players = Players;
        string title = players.Count switch
        {
            0 => BluetoothOn ? "Warte auf Controller" : "Bluetooth ist aus",
            1 => $"{players[0].Name} verbunden",
            _ => $"{players.Count} Controller verbunden",
        };
        string text = players.Count == 0
            ? (BluetoothOn ? "SYNC-Taste drücken oder eine Taste an einem gekoppelten Controller." : "Schalte Bluetooth ein, damit N-Connect Controller findet.")
            : OutputRouter.Describe();

        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var builder = new NotificationCompat.Builder(this, Channel)
            .SetSmallIcon(Resource.Drawable.ic_stat_controller)!
            .SetContentTitle(title)!
            .SetContentText(text)!
            .SetOngoing(true)!
            .SetOnlyAlertOnce(true)!
            .SetContentIntent(open)!
            .SetCategory(NotificationCompat.CategoryService)!
            .SetColor(UI.Look.C(UI.Look.P(this).Accent).ToArgb())!
            .SetPriority(NotificationCompat.PriorityLow)!;
        if (AppPrefs.TouchEnabled || AppPrefs.Mode == OutputPath.Touch)
            builder.AddAction(0, "Touch-Layout bearbeiten", ServiceIntent(ActionEditTouch, 1));
        builder.AddAction(0, "Beenden", ServiceIntent(ActionStop, 2));
        return builder.Build()!;
    }

    private PendingIntent ServiceIntent(string action, int requestCode) =>
        PendingIntent.GetService(this, requestCode, new Intent(this, typeof(ControllerService)).SetAction(action),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

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
