using System.Text.Json;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Schreibt den aktuellen Stand als status.json in Kodis Add-on-Datenordner (userdata/addon_data/service.skzconnect)
/// – gleiches Format wie der Linux-Dienst, damit das Kodi-Add-on (Menü und Home-Widget) auf jeder Plattform
/// dieselbe Anzeige hat. Auf Windows verbindet die App die Controller selbst; das Add-on zeigt nur an.
/// Geschrieben wird nur in Kodi-Installationen, in denen das Add-on service.skzconnect installiert ist; das wird alle
/// 30 s neu geprüft, damit ein später installiertes Kodi bzw. Add-on ohne Neustart von SKZ Connect erkannt wird.
/// </summary>
internal sealed class KodiStatusWriter : IDisposable
{
    /// <summary>Menü/Widget lesen den Stand nur, wenn die Datei höchstens ~15 s alt ist – Herzschlag kürzer.</summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    /// <summary>So oft nachsehen, ob Kodi bzw. das Add-on (noch) da ist.</summary>
    private static readonly TimeSpan DirsInterval = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ControllerManager _manager;
    private readonly Func<Settings> _settings;
    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new();
    private List<string> _dirs = [];
    private DateTime _dirsChecked = DateTime.MinValue;
    /// <summary>Zuletzt geschriebener Stand ohne die ständig schwankende Berichtsrate (für den Änderungsvergleich).</summary>
    private string _lastCompare = string.Empty;
    private DateTime _lastWrite = DateTime.MinValue;
    private bool _disposed;

    public KodiStatusWriter(ControllerManager manager, Func<Settings> settings)
    {
        _manager = manager;
        _settings = settings;
        _timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(TimeSpan.Zero, Interval);

    /// <summary>Wert für "bluetooth" in der status.json (Vertrag mit dem Kodi-Add-on: on | off | missing | no_le).</summary>
    internal static string BluetoothValue(BluetoothAdapterState state) => state switch
    {
        BluetoothAdapterState.Off => "off",
        BluetoothAdapterState.Missing => "missing",
        BluetoothAdapterState.NoLe => "no_le",
        _ => "on",
    };

    /// <param name="withRate">Berichtsrate aufnehmen – nicht für den Änderungsvergleich, sonst wäre jeder Takt eine „Änderung“.</param>
    private object BuildStatus(bool withRate)
    {
        var settings = _settings();
        var players = _manager.Players.Select(p =>
        {
            var devices = p.Links.Select(l => (object)new
            {
                address = l.Address ?? l.Id,
                name = settings.NameFor(l.Address) ?? l.ProductName ?? l.Kind.DisplayName(),
                kind = l.Kind.ToString(),
                serial = l.Info.SerialNumber,
                firmware = l.Info.Firmware,
                battery = l.LastState?.BatteryPercent ?? -1,
                charging = l.LastState?.Charging ?? false,
                rate = withRate ? (int)Math.Round(l.ReportRate) : 0,
            }).ToList();
            return (object)new
            {
                player = p.Index + 1,
                name = p.DisplayName(settings),
                battery = p.Links.Select(l => l.LastState?.BatteryPercent ?? -1).Where(b => b >= 0).DefaultIfEmpty(-1).Min(),
                charging = p.Links.Any(l => l.LastState?.Charging == true),
                devices,
            };
        }).ToList();
        var connectedAddresses = _manager.Players
            .SelectMany(p => p.Links.Select(l => l.Address ?? l.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Bekannte Controller: Adressen stehen in KnownControllers, die gemerkte Art in ControllerKinds –
        // damit zeigt das Widget getrennte Controller ausgegraut mit dem richtigen Symbol.
        var known = settings.KnownControllers
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Select(address =>
            {
                settings.ControllerKinds.TryGetValue(address, out var kind);
                return (object)new
                {
                    address,
                    kind = kind == ControllerKind.Unknown ? null : kind.ToString(),
                    name = settings.NameFor(address) ?? (kind == ControllerKind.Unknown ? null : kind.DisplayName()),
                    connected = connectedAddresses.Contains(address),
                };
            }).ToList();
        var bluetooth = _manager.Bluetooth;
        return new
        {
            state = "running",
            platform = "windows",
            version = Application.ProductVersion,
            bluetooth = BluetoothValue(bluetooth),
            scanning = bluetooth == BluetoothAdapterState.On,
            connecting = Array.Empty<string>(),
            players,
            elsewhere = Array.Empty<object>(),
            gamepads = Array.Empty<object>(),
            known,
            autoPairGamepads = true,
            backgroundScan = true,
        };
    }

    private void Tick()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                if (DateTime.UtcNow - _dirsChecked >= DirsInterval)
                {
                    _dirsChecked = DateTime.UtcNow;
                    var dirs = KodiIntegration.FindDataDirs();
                    if (dirs.Count > 0 && _dirs.Count == 0)
                        Log.Info("Kodi-Add-on gefunden – status.json wird für das Kodi-Add-on geschrieben");
                    _dirs = dirs;
                }
                if (_dirs.Count == 0)
                    return; // kein Kodi mit SKZ-Connect-Add-on – nichts zu tun
                string compare = JsonSerializer.Serialize(BuildStatus(withRate: false), JsonOptions);
                if (compare == _lastCompare && DateTime.UtcNow - _lastWrite < Heartbeat)
                    return;
                string json = JsonSerializer.Serialize(BuildStatus(withRate: true), JsonOptions);
                foreach (string dir in _dirs)
                    WriteAtomic(dir, json);
                _lastCompare = compare;
                _lastWrite = DateTime.UtcNow;
            }
        }
        catch (Exception e) // Timer-Thread: nie unbehandelt enden lassen
        {
            Log.Warn($"Kodi-status.json: {e.Message}");
        }
    }

    private static void WriteAtomic(string dir, string json)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, "status.json");
            string temp = target + ".app.tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, target, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"status.json nach {dir}: {e.Message}");
        }
    }

    /// <summary>Beim Beenden einmal „stopped“ schreiben – sonst zeigte Kodi bis zu 15 s weiter verbundene Controller.</summary>
    public void Dispose()
    {
        _timer.Dispose();
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_lastWrite == DateTime.MinValue)
                return; // nie geschrieben (kein Add-on) – nichts zurückzusetzen
            string json = JsonSerializer.Serialize(new { state = "stopped", platform = "windows" }, JsonOptions);
            foreach (string dir in _dirs)
                WriteAtomic(dir, json);
        }
    }
}
