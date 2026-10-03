using System.Collections.Concurrent;
using Switch2Pro.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Radios;
using Windows.Security.Cryptography;

namespace Switch2Pro.Bridge;

/// <summary>
/// Sucht per BLE-Werbung nach Pro Controllern 2 und hält für jeden eine Sitzung.
/// Mehrere Controller gleichzeitig werden unterstützt (Spieler 1–8).
/// </summary>
internal sealed class ControllerManager : IAsyncDisposable
{
    private readonly Func<Settings> _settings;
    private readonly PadFactory _factory;
    private readonly BluetoothLEAdvertisementWatcher _watcher = new() { ScanningMode = BluetoothLEScanningMode.Active };
    private readonly ConcurrentDictionary<ulong, ControllerSession> _sessions = new();
    private readonly ConcurrentDictionary<ulong, byte> _connecting = new();
    private readonly ConcurrentDictionary<ulong, long> _retryAfter = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly System.Threading.Timer _restartTimer;
    private volatile bool _disposed;

    public event Action? Changed;
    public event Action<string>? Notify;
    /// <summary>Ein Controller wurde erfolgreich verbunden (Adresse) – zum Merken für spätere Verbindungen.</summary>
    public event Action<string>? ControllerConnected;

    public string? AdapterProblem { get; private set; }

    public ControllerManager(Func<Settings> settings, PadFactory factory)
    {
        _settings = settings;
        _factory = factory;
        _watcher.Received += OnAdvertisement;
        _watcher.Stopped += OnWatcherStopped;
        _restartTimer = new System.Threading.Timer(_ => _ = StartAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public IReadOnlyList<ControllerSession> Sessions =>
        _sessions.Values.OrderBy(s => s.PlayerIndex).ToList();

    public bool IsConnecting => !_connecting.IsEmpty;

    public async Task StartAsync()
    {
        if (_disposed)
            return;
        AdapterProblem = await CheckAdapterAsync();
        Changed?.Invoke();
        if (AdapterProblem is not null)
        {
            Log.Warn(AdapterProblem);
            _restartTimer.Change(5000, Timeout.Infinite);
            return;
        }
        try
        {
            if (_watcher.Status != BluetoothLEAdvertisementWatcherStatus.Started)
            {
                _watcher.Start();
                Log.Info("Suche nach Controllern gestartet");
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

    private static async Task<string?> CheckAdapterAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter is null)
                return "Kein Bluetooth-Adapter gefunden.";
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
        // z. B. Bluetooth ausgeschaltet: regelmäßig neu versuchen.
        Log.Warn($"Bluetooth-Suche beendet ({args.Error}) – neuer Versuch in 5 s");
        _restartTimer.Change(5000, Timeout.Infinite);
    }

    private void OnAdvertisement(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        ulong address = args.BluetoothAddress;
        if (_disposed || _sessions.ContainsKey(address) || _connecting.ContainsKey(address))
            return;
        if (_retryAfter.TryGetValue(address, out long until) && Environment.TickCount64 < until)
            return;

        byte[]? match = null;
        foreach (var md in args.Advertisement.ManufacturerData)
        {
            if (md.CompanyId != Gatt.NintendoCompanyId)
                continue;
            CryptographicBuffer.CopyToByteArray(md.Data, out byte[] data);
            if (data is not null && Advertisement.IsProController2(md.CompanyId, data))
            {
                match = data;
                break;
            }
        }
        if (match is null)
            return;

        string text = ControllerSession.FormatAddress(address);
        var settings = _settings();
        if (!settings.IsAllowed(text))
            return;
        // Sucht der Controller nach seiner Switch 2 (Tastendruck statt SYNC), nur dann übernehmen,
        // wenn er schon einmal mit diesem PC verbunden war und das gewünscht ist – sonst würden wir
        // ihn der Konsole wegschnappen.
        if (!Advertisement.IsSyncMode(match) && !(settings.AutoReconnect && settings.IsKnown(text)))
            return;
        if (!_connecting.TryAdd(address, 0))
            return;
        _ = ConnectAsync(address, args.BluetoothAddressType, text);
    }

    private async Task ConnectAsync(ulong address, BluetoothAddressType type, string text)
    {
        Changed?.Invoke();
        int player = FreePlayerIndex();
        Log.Info($"{text}: Pro Controller 2 gefunden ({type}), verbinde als Spieler {player + 1} …");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var session = await ControllerSession.ConnectAsync(address, type, player, _settings, _factory,
                OnSessionLost, _ => Changed?.Invoke(), timeout.Token);
            if (_disposed)
            {
                await session.DisposeAsync();
                return;
            }
            _sessions[address] = session;
            if (session.IsLost)
            {
                OnSessionLost(session); // Abbruch zwischen Start und Eintragen
                return;
            }
            ControllerConnected?.Invoke(text);
            Notify?.Invoke($"Pro Controller verbunden (Spieler {session.PlayerIndex + 1})");
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        catch (Exception e)
        {
            Log.Error($"{text}: Verbindung fehlgeschlagen", e);
            _retryAfter[address] = Environment.TickCount64 + 3000;
        }
        finally
        {
            ReleasePlayerIndex(player);
            _connecting.TryRemove(address, out _);
            Changed?.Invoke();
        }
    }

    private readonly HashSet<int> _reservedPlayers = [];

    /// <summary>Kleinste freie Spielernummer; bleibt bis zum Verbindungsende reserviert.</summary>
    private int FreePlayerIndex()
    {
        lock (_reservedPlayers)
        {
            var used = _sessions.Values.Select(s => s.PlayerIndex).Concat(_reservedPlayers).ToHashSet();
            int index = Enumerable.Range(0, 8).FirstOrDefault(i => !used.Contains(i));
            _reservedPlayers.Add(index);
            return index;
        }
    }

    private void ReleasePlayerIndex(int index)
    {
        lock (_reservedPlayers)
            _reservedPlayers.Remove(index);
    }

    private void OnSessionLost(ControllerSession session)
    {
        _ = Task.Run(async () =>
        {
            if (_sessions.TryRemove(new KeyValuePair<ulong, ControllerSession>(session.Address, session)))
            {
                Log.Info($"{session.AddressText}: getrennt");
                Notify?.Invoke($"Pro Controller getrennt (Spieler {session.PlayerIndex + 1})");
            }
            await session.DisposeAsync();
            _retryAfter[session.Address] = Environment.TickCount64 + 1500;
            Changed?.Invoke();
        });
    }

    /// <summary>Neue Ausgabeart auf alle verbundenen Controller anwenden.</summary>
    public void ApplyOutputMode(OutputMode mode)
    {
        foreach (var s in _sessions.Values)
        {
            try
            {
                s.SwitchOutput(mode);
            }
            catch (Exception e)
            {
                Log.Error($"{s.AddressText}: Ausgabeart wechseln", e);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _restartTimer.Dispose();
        _cts.Cancel();
        _watcher.Received -= OnAdvertisement;
        _watcher.Stopped -= OnWatcherStopped;
        try { _watcher.Stop(); } catch (Exception) { /* bereits gestoppt */ }
        foreach (var s in _sessions.Values)
            await s.DisposeAsync();
        _sessions.Clear();
        _cts.Dispose();
    }
}
