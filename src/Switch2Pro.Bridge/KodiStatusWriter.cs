using System.Text.Json;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Schreibt den aktuellen Stand als status.json in Kodis Add-on-Datenordner (userdata/addon_data/service.nconnect)
/// – gleiches Format wie der Linux-Dienst, damit das Kodi-Add-on (Menü und Home-Widget) auf jeder Plattform
/// dieselbe Anzeige hat. Auf Windows verbindet die App die Controller selbst; das Add-on zeigt nur an.
/// </summary>
internal sealed class KodiStatusWriter : IDisposable
{
    /// <summary>Menü/Widget lesen den Stand nur, wenn die Datei höchstens ~15 s alt ist – Herzschlag kürzer.</summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ControllerManager _manager;
    private readonly Func<Settings> _settings;
    private readonly System.Threading.Timer _timer;
    private string _lastJson = string.Empty;
    private DateTime _lastWrite = DateTime.MinValue;

    public KodiStatusWriter(ControllerManager manager, Func<Settings> settings)
    {
        _manager = manager;
        _settings = settings;
        _timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (KodiIntegration.FindDataDirs().Count == 0)
            return; // kein Kodi installiert – nichts zu tun (bei späterer Installation nach App-Neustart aktiv)
        Log.Info("Kodi gefunden – status.json wird für das Kodi-Add-on geschrieben");
        _timer.Change(TimeSpan.Zero, Interval);
    }

    private object BuildStatus()
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
                rate = (int)Math.Round(l.ReportRate),
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
        string? problem = _manager.AdapterProblem;
        return new
        {
            state = "running",
            version = Application.ProductVersion,
            bluetooth = problem is null ? "on" : problem.Contains("kein", StringComparison.OrdinalIgnoreCase) ? "missing" : "off",
            scanning = problem is null,
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
            string json = JsonSerializer.Serialize(BuildStatus(), JsonOptions);
            if (json != _lastJson || DateTime.UtcNow - _lastWrite >= Heartbeat)
            {
                var dirs = KodiIntegration.FindDataDirs();
                foreach (string dir in dirs)
                    WriteAtomic(dir, json);
                if (dirs.Count > 0)
                {
                    _lastJson = json;
                    _lastWrite = DateTime.UtcNow;
                }
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

    public void Dispose() => _timer.Dispose();
}
