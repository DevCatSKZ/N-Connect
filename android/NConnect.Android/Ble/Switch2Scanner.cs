using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.Runtime;
using Switch2Pro.Protocol;

namespace NConnect.Android.Ble;

/// <summary>Ein werbender Switch-2-Controller.</summary>
/// <param name="Host">Gerät, für das er wirbt (0 = SYNC-Modus, für alle).</param>
public sealed record FoundController(BluetoothDevice Device, ControllerKind Kind, ulong Host, int Rssi)
{
    public bool SyncMode => Host == 0;
    public string Address => Device.Address ?? "?";
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
        _scanner.StartScan([filter], settings, _callback);
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

    private void OnResult(ScanResult result)
    {
        var device = result.Device;
        var data = result.ScanRecord?.GetManufacturerSpecificData(Gatt.NintendoCompanyId);
        if (device is null || data is null)
            return;
        var kind = Advertisement.Kind(Gatt.NintendoCompanyId, data);
        if (kind == ControllerKind.Unknown)
            return;
        ulong host = Advertisement.IsSyncMode(data) ? 0 : Advertisement.HostAddress(data);
        Found?.Invoke(new FoundController(device, kind, host, result.Rssi));
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
