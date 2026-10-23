using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.Runtime;
using Switch2Pro.Protocol;

namespace SkzConnect.Android.Ble;

/// <summary>Ein werbender Switch-2-Controller (nur Daten – kein Java-Objekt, siehe <see cref="Switch2Scanner"/>).</summary>
/// <param name="Host">Gerät, für das er wirbt (0 = SYNC-Modus, für alle).</param>
public sealed record FoundController(string Address, ControllerKind Kind, ulong Host, int Rssi)
{
    public bool SyncMode => Host == 0;
}

/// <summary>
/// Sucht per Bluetooth LE nach Switch-2-Controllern (Nintendo-Herstellerdaten 0x0553, Kopf 01 00 03 7E). Der Filter
/// sorgt dafür, dass Android auch bei ausgeschaltetem Bildschirm weitersucht und nicht jedes Gerät meldet.
/// </summary>
public sealed class Switch2Scanner
{
    private readonly Callback _callback;
    private BluetoothLeScanner? _scanner;

    public bool Scanning { get; private set; }
    public event Action<FoundController>? Found;
    public event Action<string>? Failed;

    public Switch2Scanner() => _callback = new Callback(this);

    public static BluetoothAdapter? Adapter(Context context) =>
        context.GetSystemService(Context.BluetoothService)?.JavaCast<BluetoothManager>()?.Adapter;

    public bool LowPower { get; private set; }

    /// <summary>
    /// Startet die Suche. <paramref name="lowPower"/>: stromsparend (sobald ein Controller verbunden ist – schnelles
    /// Dauersuchen kostet Akku und kann die Latenz der bestehenden Verbindung verschlechtern).
    /// false = Bluetooth aus oder nicht vorhanden.
    /// </summary>
    public bool Start(Context context, bool lowPower = false)
    {
        if (Scanning && LowPower == lowPower)
            return true;
        Stop();
        LowPower = lowPower;
        var adapter = Adapter(context);
        if (adapter is null || !adapter.IsEnabled)
            return false;
        _scanner = adapter.BluetoothLeScanner;
        if (_scanner is null)
            return false;
        var filter = new ScanFilter.Builder()
            .SetManufacturerData(Gatt.NintendoCompanyId, [0x01, 0x00, 0x03, 0x7E], [0xFF, 0xFF, 0xFF, 0xFF])!
            .Build()!;
        var settings = new ScanSettings.Builder()
            .SetScanMode(lowPower ? global::Android.Bluetooth.LE.ScanMode.Balanced : global::Android.Bluetooth.LE.ScanMode.LowLatency)!
            .Build()!;
        try
        {
            _scanner.StartScan([filter], settings, _callback);
        }
        catch (Exception e) when (e is Java.Lang.SecurityException or Java.Lang.IllegalStateException)
        {
            // „Geräte in der Nähe“ entzogen oder Bluetooth gerade im Umschalten – nicht abstürzen, später erneut.
            Log.Warn($"Bluetooth-Suche nicht gestartet: {Log.Reason(e)}");
            return false;
        }
        Scanning = true;
        Log.Info($"Suche nach Switch-2-Controllern gestartet{(lowPower ? " (stromsparend)" : "")}");
        return true;
    }

    public void Stop()
    {
        if (!Scanning)
            return;
        try
        {
            _scanner?.StopScan(_callback);
        }
        catch (Exception)
        {
            // Bluetooth inzwischen aus
        }
        Scanning = false;
        Log.Info("Suche beendet");
    }

    private readonly Dictionary<string, (long Ticks, ulong Host)> _lastSeen = [];

    /// <summary>
    /// Ein Suchergebnis auswerten. Wichtig: Jedes Ergebnis erzeugt Java-Objekte (ScanResult, BluetoothDevice, ScanRecord),
    /// die hier sofort wieder freigegeben werden – sonst sammeln sich bei werbenden Controllern (zig Meldungen pro
    /// Sekunde) Tausende Java-Verweise an, die Laufzeit erzwingt laufend Speicherbereinigungen und die Oberfläche
    /// friert ein („App reagiert nicht“). Wiederholte Meldungen desselben Controllers höchstens einmal pro Sekunde.
    /// </summary>
    private void OnResult(ScanResult result)
    {
        try
        {
            string? address;
            using (var device = result.Device)
                address = device?.Address;
            byte[]? data;
            using (var record = result.ScanRecord)
                data = record?.GetManufacturerSpecificData(Gatt.NintendoCompanyId);
            if (address is null || data is null)
                return;
            var kind = Advertisement.Kind(Gatt.NintendoCompanyId, data);
            if (kind == ControllerKind.Unknown)
                return;
            ulong host = Advertisement.IsSyncMode(data) ? 0 : Advertisement.HostAddress(data);
            long now = System.Environment.TickCount64;
            lock (_lastSeen)
            {
                if (_lastSeen.TryGetValue(address, out var last) && last.Host == host && now - last.Ticks < 1000)
                    return;
                _lastSeen[address] = (now, host);
                // Damit die Liste nicht über Stunden wächst: alte Einträge gelegentlich ausmisten.
                if (_lastSeen.Count > 32)
                {
                    var stale = _lastSeen.Where(kv => now - kv.Value.Ticks > 60000).Select(kv => kv.Key).ToList();
                    foreach (var key in stale)
                        _lastSeen.Remove(key);
                }
            }
            Found?.Invoke(new FoundController(address, kind, host, result.Rssi));
        }
        finally
        {
            result.Dispose();
        }
    }

    private sealed class Callback(Switch2Scanner owner) : ScanCallback
    {
        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
        {
            if (result is not null)
                owner.OnResult(result);
        }

        public override void OnBatchScanResults(IList<ScanResult>? results)
        {
            foreach (var r in results ?? [])
                owner.OnResult(r);
        }

        public override void OnScanFailed(ScanFailure errorCode)
        {
            owner.Scanning = false;
            Log.Warn($"Bluetooth-Suche fehlgeschlagen: {errorCode}");
            owner.Failed?.Invoke(errorCode.ToString());
        }
    }
}
