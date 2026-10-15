using System.Text.Json;
using System.Text.Json.Nodes;
using Android.Content;
using Android.OS;
using Android.Provider;
using NConnect.Android.Core;
using Switch2Pro.Protocol;

namespace NConnect.Android.Kodi;

/// <summary>
/// Schreibt den aktuellen Stand als status.json an einen Ort, den Kodi auf diesem Gerät lesen kann –
/// gleiches Format wie der Linux-Dienst und der Windows-KodiStatusWriter, damit das Kodi-Add-on
/// (Menü und Home-Widget) auf Android denselben Stand anzeigt.
/// Ab Android 10 (Scoped Storage) kommt die App nicht an Kodis Datenordner – darum liegt die Datei im
/// gemeinsamen Dokumente-Ordner /sdcard/Documents/N-Connect (über MediaStore). Das Add-on liest dort mit.
/// Auf Android 8/9 genügt der App-eigene externe Ordner, den Kodi noch lesen darf.
/// </summary>
public sealed class KodiStatusWriter : IDisposable
{
    /// <summary>Menü/Widget lesen den Stand nur, wenn die Datei höchstens ~15 s alt ist – Herzschlag kürzer.</summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private const string DocumentsDir = "Documents/N-Connect/";
    private const string FileName = "status.json";
    private const string TempName = "status.json.app.tmp";

    private readonly Context _context;
    private readonly ControllerService _service;
    private readonly System.Threading.Timer _timer;
    private global::Android.Net.Uri? _collection;
    private string? _filePath;
    private string _lastJson = string.Empty;
    private long _lastWrite;

    public KodiStatusWriter(ControllerService service)
    {
        _service = service;
        _context = service.ApplicationContext!;
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
        return new JsonObject
        {
            ["state"] = "running",
            ["version"] = AppVersion(),
            ["bluetooth"] = _service.BluetoothOn ? "on" : "off",
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

    private string? AppVersion()
    {
        try
        {
            return _context.PackageManager!
                .GetPackageInfo(_context.PackageName!, (global::Android.Content.PM.PackageInfoFlags)0)?.VersionName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Tick()
    {
        try
        {
            string json = BuildStatus().ToJsonString();
            if (json == _lastJson && System.Environment.TickCount64 - _lastWrite < Heartbeat.TotalMilliseconds)
                return;
            Write(json);
            _lastJson = json;
            _lastWrite = System.Environment.TickCount64;
        }
        catch (Exception e) // Timer-Thread: nie unbehandelt enden lassen
        {
            Log.Warn($"Kodi-status.json: {e.Message}");
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
    /// MediaStore hat kein atomares Ersetzen – darum immer in eine TMP-Datei schreiben und dann per
    /// DisplayName umbenennen (echtes Datei-Rename). Kodi liest den Pfad direkt und sieht nie eine
    /// halb geschriebene Datei.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private void WriteMediaStore(string json)
    {
        var resolver = _context.ContentResolver!;
        global::Android.Net.Uri? tempUri = FindFile(resolver, TempName) ?? InsertFile(resolver, TempName);
        using (var stream = resolver.OpenOutputStream(tempUri!, "w")!)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            stream.Write(bytes, 0, bytes.Length);
        }
        if (FindFile(resolver, FileName) is { } old)
            resolver.Delete(old, null, null);
        var rename = new ContentValues();
        rename.Put(MediaStore.IMediaColumns.DisplayName, FileName);
        resolver.Update(tempUri!, rename, null, null);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private global::Android.Net.Uri? FindFile(ContentResolver resolver, string name)
    {
        using var cursor = resolver.Query(_collection!,
            [IBaseColumns.Id],
            $"{MediaStore.IMediaColumns.RelativePath}=? AND {MediaStore.IMediaColumns.DisplayName}=?",
            [DocumentsDir, name], null);
        return cursor is not null && cursor.MoveToFirst()
            ? ContentUris.WithAppendedId(_collection!, cursor.GetLong(0))
            : null;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private global::Android.Net.Uri? InsertFile(ContentResolver resolver, string name)
    {
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, name);
        values.Put(MediaStore.Files.IFileColumns.MimeType, "application/json");
        values.Put(MediaStore.IMediaColumns.RelativePath, DocumentsDir);
        return resolver.Insert(_collection!, values);
    }

    /// <summary>Letzter Stand „stopped“ – das Add-on zeigt dann sofort „App läuft nicht“ statt 15 s Altlast.</summary>
    public void Dispose()
    {
        _timer.Dispose();
        try
        {
            Write(new JsonObject { ["state"] = "stopped" }.ToJsonString());
        }
        catch (Exception)
        {
            // Beim Beenden ohnehin zu spät für eine Warnung.
        }
    }
}
