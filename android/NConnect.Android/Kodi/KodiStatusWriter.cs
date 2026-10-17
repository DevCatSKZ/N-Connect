using System.Text.Json.Nodes;
using Android.Content;
using Android.Content.PM;
using Android.Provider;
using NConnect.Android.Ble;
using NConnect.Android.Core;
using Switch2Pro.Protocol;

namespace NConnect.Android.Kodi;

/// <summary>
/// Schreibt den aktuellen Stand als status.json an einen Ort, den Kodi auf diesem Gerät lesen kann –
/// gleiches Format wie der Linux-Dienst und der Windows-KodiStatusWriter (Vertrag siehe KodiStatusWriter.Contract.cs),
/// damit das Kodi-Add-on (Menü und Home-Widget) auf Android denselben Stand anzeigt.
/// Ab Android 10 (Scoped Storage) kommt die App nicht an Kodis Datenordner – darum liegt die Datei im
/// gemeinsamen Dokumente-Ordner /sdcard/Documents/N-Connect (über MediaStore). Das Add-on liest dort mit.
/// Auf Android 8/9 genügt der App-eigene externe Ordner, den Kodi noch lesen darf.
/// </summary>
public sealed partial class KodiStatusWriter : IDisposable
{
    /// <summary>Menü/Widget lesen den Stand nur, wenn die Datei höchstens ~15 s alt ist – Herzschlag kürzer.</summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private const string DocumentsDir = "Documents/N-Connect/";
    private const string FileName = "status.json";
    /// <summary>Zwischendatei früherer Entwicklungsstände (Schreiben über Umbenennen) – wird entfernt.</summary>
    private const string OldTempName = "status.json.app.tmp";

    private readonly Context _context;
    private readonly ControllerService _service;
    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new();
    private readonly string? _version;
    private readonly bool _lowEnergy;
    private global::Android.Net.Uri? _collection;
    /// <summary>Die eigene Datei im MediaStore – einmal gesucht bzw. angelegt, danach nur noch überschrieben.</summary>
    private global::Android.Net.Uri? _file;
    private string? _filePath;
    private string _lastKey = string.Empty;
    private string? _lastError;
    private long _lastWrite;
    private volatile bool _stopped;
    private volatile string? _fallbackName;

    /// <summary>
    /// Ausweichdatei (z. B. „status (1).json“), solange „status.json“ einer früheren Installation gehört – sonst null.
    /// Hilfe › Kodi zeigt dann einen Hinweis: Das Kodi-Add-on liest womöglich nur „status.json“.
    /// </summary>
    public string? FallbackName => _fallbackName;

    public KodiStatusWriter(ControllerService service)
    {
        _service = service;
        _context = service.ApplicationContext!;
        _version = AppVersion(_context);
        _lowEnergy = _context.PackageManager?.HasSystemFeature(PackageManager.FeatureBluetoothLe) == true;
        _timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (!KodiRepository.IsKodiInstalled(_context))
            return; // kein Kodi auf diesem Gerät – nichts zu tun (bei späterer Installation nach App-Neustart aktiv)
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            // Documents-Ordner über MediaStore – ohne Speicher-Berechtigung möglich.
            _collection = MediaStore.Files.GetContentUri(MediaStore.VolumeExternalPrimary);
        }
        else
        {
            // Android 8/9: Kodi darf den externen App-Ordner anderer Apps noch lesen.
            _filePath = Path.Combine(_context.GetExternalFilesDir(null)!.AbsolutePath, FileName);
        }
        Log.Info("Kodi-status.json wird geschrieben (Add-on liest mit)");
        _timer.Change(TimeSpan.Zero, Interval);
    }

    private JsonObject BuildStatus()
    {
        var players = _service.Players;
        var devices = players.SelectMany(p => p.Devices.ToList()).ToList();
        var connected = devices.Select(d => d.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var adapter = Switch2Scanner.Adapter(_context);
        return new JsonObject
        {
            ["state"] = "running",
            ["platform"] = Platform,
            ["version"] = _version,
            ["bluetooth"] = BluetoothState(adapter is not null, _lowEnergy, adapter?.IsEnabled == true),
            ["scanning"] = _service.Scanning,
            ["connecting"] = new JsonArray([.. _service.Connecting.Select(a => (JsonNode)a)]),
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
                    ["charging"] = d.LastState?.Charging ?? false,
                    ["serial"] = d.SerialNumber,
                    ["firmware"] = d.Firmware,
                    ["rate"] = Math.Round(d.ReportRate),
                })]),
            })]),
            ["known"] = new JsonArray([.. KnownControllers.All.Select(k => (JsonNode)new JsonObject
            {
                ["address"] = k.Address,
                ["kind"] = k.Kind.ToString(),
                ["name"] = k.Kind.DisplayName(),
                ["serial"] = k.Serial,
                ["connected"] = connected.Contains(k.Address),
            })]),
            ["elsewhere"] = new JsonArray([.. _service.Elsewhere.Select(f => (JsonNode)new JsonObject
            {
                ["address"] = f.Address,
                ["name"] = f.Kind.DisplayName(),
            })]),
            ["autoPairGamepads"] = true,
            ["backgroundScan"] = true,
            ["gamepads"] = new JsonArray(),
        };
    }

    /// <summary>App-Version einmal beim Start lesen (vorher bei jedem Takt ein Binder-Aufruf).</summary>
    private static string? AppVersion(Context context)
    {
        try
        {
            return context.PackageManager!.GetPackageInfo(context.PackageName!, (PackageInfoFlags)0)?.VersionName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Tick()
    {
        lock (_gate)
        {
            if (_stopped)
                return; // Dispose hat schon „stopped“ geschrieben oder schreibt es gleich
            try
            {
                var status = BuildStatus();
                string key = ChangeKey(status);
                long now = System.Environment.TickCount64;
                if (key == _lastKey && now - _lastWrite < Heartbeat.TotalMilliseconds)
                    return;
                Write(status.ToJsonString());
                _lastKey = key;
                _lastWrite = now;
                _lastError = null;
            }
            catch (Exception e) // Timer-Thread: nie unbehandelt enden lassen
            {
                if (e.Message != _lastError) // nicht alle 3 s dieselbe Zeile
                    Log.Warn($"Kodi-status.json: {e.Message}");
                _lastError = e.Message;
            }
        }
    }

    private void Write(string json)
    {
        if (_filePath is not null)
        {
            // Direkter Dateipfad (Android 8/9): atomar über Umbenennen.
            string temp = _filePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _filePath, overwrite: true);
            return;
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            WriteMediaStore(json);
    }

    /// <summary>
    /// Die Datei wird einmal angelegt und danach nur noch überschrieben ("wt" kürzt sie) – statt bei jedem Takt
    /// einfügen, schreiben, löschen und umbenennen. Liest Kodi zufällig eine halb geschriebene Datei, verwirft das Add-on
    /// sie (ValueError) und nimmt den nächsten Takt.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private void WriteMediaStore(string json)
    {
        var resolver = _context.ContentResolver!;
        _file ??= OpenOrCreate(resolver);
        if (_file is null)
            return;
        try
        {
            using var stream = resolver.OpenOutputStream(_file, "wt")!;
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            stream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception)
        {
            _file = null; // Eintrag weg (z. B. von Hand gelöscht) – beim nächsten Takt neu suchen bzw. anlegen
            throw;
        }
    }

    /// <summary>
    /// Eigene status.json suchen, sonst anlegen. Nach einer Neuinstallation gehört eine alte status.json der früheren
    /// Installation: Die App sieht sie nicht und darf sie nicht ändern, Android legt die neue dann unter einem anderen
    /// Namen an (z. B. „status (1).json“). Dann bleibt es bei dieser Ausweichdatei (Hinweis im Protokoll und in
    /// Hilfe › Kodi); bei jedem Start wird erneut versucht, wieder „status.json“ zu bekommen – etwa nachdem die alte Datei
    /// gelöscht wurde.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private global::Android.Net.Uri? OpenOrCreate(ContentResolver resolver)
    {
        foreach (var (uri, _) in OwnFiles(resolver, OldTempName))
            resolver.Delete(uri, null, null);
        var own = OwnFiles(resolver, "status%.json");
        if (own.FirstOrDefault(f => f.Name == FileName).Uri is { } exact)
        {
            DeleteOthers(resolver, own, exact);
            _fallbackName = null;
            return exact;
        }
        var created = Insert(resolver, FileName);
        if (created is null)
            return own.Count > 0 ? UseFallback(own[0].Uri, own[0].Name) : null;
        string? name = DisplayName(resolver, created);
        if (name == FileName)
        {
            DeleteOthers(resolver, own, created);
            _fallbackName = null;
            return created;
        }
        if (own.Count > 0)
        {
            // Weiterhin belegt: bei der bisherigen Ausweichdatei bleiben, den neuen Doppelgänger wieder entfernen.
            resolver.Delete(created, null, null);
            return UseFallback(own[0].Uri, own[0].Name);
        }
        return UseFallback(created, name ?? "?");
    }

    private global::Android.Net.Uri UseFallback(global::Android.Net.Uri uri, string name)
    {
        Log.Warn($"Kodi-status.json: „{FileName}“ gehört einer früheren Installation von N-Connect – schreibe „{name}“. " +
                 "Die alte Datei im Ordner Documents/N-Connect löschen, dann nimmt N-Connect beim nächsten Start wieder „status.json“.");
        _fallbackName = name;
        return uri;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private static void DeleteOthers(ContentResolver resolver, List<(global::Android.Net.Uri Uri, string Name)> own,
        global::Android.Net.Uri keep)
    {
        foreach (var (uri, _) in own)
        {
            if (!uri.Equals(keep))
                resolver.Delete(uri, null, null);
        }
    }

    /// <summary>Eigene Dateien im Ordner Documents/N-Connect, deren Name auf <paramref name="pattern"/> passt (SQL LIKE).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private List<(global::Android.Net.Uri Uri, string Name)> OwnFiles(ContentResolver resolver, string pattern)
    {
        var found = new List<(global::Android.Net.Uri Uri, string Name)>();
        using var cursor = resolver.Query(_collection!,
            [IBaseColumns.Id, MediaStore.IMediaColumns.DisplayName],
            $"{MediaStore.IMediaColumns.RelativePath}=? AND {MediaStore.IMediaColumns.DisplayName} LIKE ?",
            [DocumentsDir, pattern], null);
        while (cursor is not null && cursor.MoveToNext())
            found.Add((ContentUris.WithAppendedId(_collection!, cursor.GetLong(0)), cursor.GetString(1) ?? ""));
        return found;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private static string? DisplayName(ContentResolver resolver, global::Android.Net.Uri uri)
    {
        using var cursor = resolver.Query(uri, [MediaStore.IMediaColumns.DisplayName], null, null, null);
        return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : null;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private global::Android.Net.Uri? Insert(ContentResolver resolver, string name)
    {
        using var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, name);
        values.Put(MediaStore.Files.IFileColumns.MimeType, "application/json");
        values.Put(MediaStore.IMediaColumns.RelativePath, DocumentsDir);
        return resolver.Insert(_collection!, values);
    }

    /// <summary>
    /// Letzter Stand „stopped“ – das Add-on zeigt dann sofort „App läuft nicht“ statt 15 s Altlast. Vorher den Timer
    /// anhalten und auf einen gerade laufenden Takt warten, sonst könnte er nach „stopped“ noch „running“ schreiben.
    /// </summary>
    public void Dispose()
    {
        _stopped = true;
        var done = new ManualResetEvent(false);
        // Das Ereignis erst freigeben, wenn der Timer es gesetzt hat (sonst signalisierte er später ein freigegebenes).
        if (!_timer.Dispose(done) || done.WaitOne(TimeSpan.FromSeconds(2)))
            done.Dispose();
        lock (_gate)
        {
            if (_filePath is null && _collection is null)
                return; // nie gestartet (kein Kodi) – nichts zu melden
            try
            {
                Write(new JsonObject { ["state"] = "stopped", ["platform"] = Platform }.ToJsonString());
            }
            catch (Exception)
            {
                // Beim Beenden ohnehin zu spät für eine Warnung.
            }
        }
    }
}
