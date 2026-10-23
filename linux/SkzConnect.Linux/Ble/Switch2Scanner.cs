using System.Collections.Concurrent;
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using Switch2Pro.Protocol;
using Tmds.DBus;
using Advertisement = Switch2Pro.Protocol.Advertisement;

namespace SkzConnect.Linux.Ble;

/// <summary>
/// Ein werbender Switch-2-Controller. <paramref name="Host"/> = Gerät, für das er wirbt (0 = SYNC-Modus);
/// <paramref name="Paired"/> = mit diesem Adapter gekoppelt (BlueZ-Bond), unabhängig davon, was die Werbung meldet.
/// </summary>
public sealed record FoundController(IDevice1 Device, string Address, byte AddressType, ControllerKind Kind, ulong Host,
    short Rssi, bool Paired)
{
    public bool SyncMode => Host == 0;
}

/// <summary>
/// Sucht über BlueZ nach Switch-2-Controllern (Nintendo-Herstellerdaten 0x0553). BlueZ meldet neue Geräte als
/// „gefunden“ und bekannte über Eigenschaftsänderungen (RSSI/Herstellerdaten) – beides wird ausgewertet, damit auch
/// ein schon einmal gesehener Controller nach einem Tastendruck sofort erkannt wird.
/// Je Gerät gibt es genau einen Eigenschaften-Beobachter; entfernt BlueZ das Gerät (z. B. Telefone mit wechselnder
/// Zufallsadresse), wird er wieder abgemeldet – so wächst nichts über eine lange Laufzeit an.
/// </summary>
public sealed class Switch2Scanner : IAsyncDisposable
{
    private readonly Adapter _adapter;
    private readonly ConcurrentDictionary<string, IDisposable> _watched = new();
    private IDisposable? _removedWatch;
    private bool _disposed;

    public bool Scanning { get; private set; }
    public event Action<FoundController>? Found;
    /// <summary>Jedes Gerät, das BlueZ meldet (für den Gamepad-Manager: Xbox, PlayStation, Switch 1 …).</summary>
    public event Action<IDevice1, Device1Properties>? Seen;

    /// <summary>true: auch klassisches Bluetooth (BR/EDR) durchsuchen – nötig für Xbox-/PlayStation-/Switch-1-Controller.</summary>
    public bool IncludeClassic { get; set; }

    public Switch2Scanner(Adapter adapter)
    {
        _adapter = adapter;
        _ = WatchRemovedAsync();
        _adapter.DeviceFound += OnDeviceFound;
    }

    public async Task StartAsync()
    {
        if (Scanning)
            return;
        try
        {
            await _adapter.SetDiscoveryFilterAsync(new Dictionary<string, object>
            {
                ["Transport"] = IncludeClassic ? "auto" : "le",
                ["DuplicateData"] = true,
            }).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Warn($"Suchfilter nicht gesetzt: {Log.Reason(e)}");
        }
        foreach (var path in await BlueZ.DevicePathsAsync(_adapter).ConfigureAwait(false))
            await WatchAsync(path).ConfigureAwait(false);
        try
        {
            await _adapter.StartDiscoveryAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (e.Message.Contains("InProgress", StringComparison.OrdinalIgnoreCase))
        {
            // Suche läuft schon (z. B. von Kodis Bluetooth-Einstellungen)
        }
        Scanning = true;
        Log.Info(IncludeClassic ? "Suche nach Controllern gestartet (LE + klassisch)" : "Suche nach Switch-2-Controllern gestartet");
    }

    public async Task StopAsync()
    {
        if (!Scanning)
            return;
        Scanning = false;
        try
        {
            await _adapter.StopDiscoveryAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Adapter weg oder Suche schon beendet
        }
        Log.Info("Suche beendet");
    }

    /// <summary>
    /// Suche neu anstoßen. Nötig nach jedem eigenen Verbindungsaufbau: Der Kernel pausiert dafür die Suche und nimmt
    /// sie danach nicht wieder auf, während BlueZ weiter „Discovering = true“ meldet – neue Controller blieben unsichtbar.
    /// Läuft auch, wenn die Suche vorher absichtlich angehalten wurde (Verbindungsaufbau).
    /// </summary>
    public async Task RestartAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);
        await StartAsync().ConfigureAwait(false);
    }

    /// <summary>Die Suche wurde von außen beendet (anderes Programm) – beim nächsten Start neu anmelden.</summary>
    public void MarkStopped() => Scanning = false;

    private async Task OnDeviceFound(Adapter sender, DeviceFoundEventArgs e)
    {
        // Linux.Bluetooth legt je Meldung ein eigenes Device-Objekt samt Beobachter an – nur den Pfad übernehmen und
        // das Objekt gleich wieder freigeben (der eigene Beobachter in WatchAsync genügt).
        var path = e.Device.ObjectPath;
        e.Device.Dispose();
        await WatchAsync(path).ConfigureAwait(false);
    }

    private async Task WatchAsync(ObjectPath path)
    {
        var device = BlueZ.Device(path);
        string key = path.ToString();
        if (_watched.ContainsKey(key))
        {
            await EvaluateAsync(device).ConfigureAwait(false); // bekanntes Gerät: beim Suchstart gleich prüfen
            return;
        }
        try
        {
            var watch = await device.WatchPropertiesAsync(changes =>
            {
                foreach (var (name, _) in changes.Changed)
                    if (name is "RSSI" or "ManufacturerData" or "Name" or "Class" or "Appearance")
                    {
                        _ = EvaluateAsync(device);
                        return;
                    }
            }).ConfigureAwait(false);
            bool added;
            lock (_watched)
                added = !_disposed && _watched.TryAdd(key, watch);
            if (!added)
                watch.Dispose(); // schon beobachtet (gleichzeitig gemeldet) oder Suche beendet
            await EvaluateAsync(device).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Gerät inzwischen wieder verschwunden
        }
    }

    /// <summary>Entfernte Geräte abmelden (InterfacesRemoved), damit <see cref="_watched"/> nicht unbegrenzt wächst.</summary>
    private async Task WatchRemovedAsync()
    {
        try
        {
            var manager = Connection.System.CreateProxy<IObjectManager>(BlueZ.Service, ObjectPath.Root);
            var watch = await manager.WatchInterfacesRemovedAsync(removed =>
            {
                var (path, interfaces) = removed;
                if (interfaces.Contains(BlueZ.DeviceInterface) && _watched.TryRemove(path.ToString(), out var gone))
                    gone.Dispose();
            }).ConfigureAwait(false);
            lock (_watched)
            {
                if (_disposed)
                    watch.Dispose();
                else
                    _removedWatch = watch;
            }
        }
        catch (Exception e)
        {
            Log.Warn($"Entfernte Geräte werden nicht verfolgt: {Log.Reason(e)}");
        }
    }

    private async Task EvaluateAsync(IDevice1 device)
    {
        if (!Scanning)
            return;
        try
        {
            var all = await device.GetAllAsync().ConfigureAwait(false);
            Seen?.Invoke(device, all);
            if (all.Connected || all.ManufacturerData is null
                || !all.ManufacturerData.TryGetValue(Gatt.NintendoCompanyId, out var raw) || raw is not byte[] data)
                return;
            var kind = Advertisement.Kind(Gatt.NintendoCompanyId, data);
            if (kind == ControllerKind.Unknown)
                return;
            ulong host = Advertisement.IsSyncMode(data) ? 0 : Advertisement.HostAddress(data);
            byte type = all.AddressType == "random" ? AttClient.AddressRandom : AttClient.AddressPublic;
            Found?.Invoke(new FoundController(device, all.Address, type, kind, host, all.RSSI, all.Paired));
        }
        catch (Exception)
        {
            // Eigenschaften nicht lesbar (Gerät entfernt)
        }
    }

    public async ValueTask DisposeAsync()
    {
        _adapter.DeviceFound -= OnDeviceFound;
        await StopAsync().ConfigureAwait(false);
        lock (_watched)
        {
            _disposed = true;
            _removedWatch?.Dispose();
            _removedWatch = null;
            foreach (var w in _watched.Values)
                w.Dispose();
            _watched.Clear();
        }
    }
}
