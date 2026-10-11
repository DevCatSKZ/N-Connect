using System.IO.Compression;
using Android.Content;
using Android.OS;
using Android.Provider;
using NConnect.Android.Output;

namespace NConnect.Android.Kodi;

/// <summary>
/// Richtet das „DevCatSKZ Repository“ (github.com/DevCatSKZ/kodi-repo) in Kodi auf diesem Gerät ein – darüber kommen
/// unsere Kodi-Plugins samt automatischen Updates.
/// Weg 1 (immer): ZIP in den Download-Ordner laden → in Kodi „Aus ZIP-Datei installieren“.
/// Weg 2 (Shizuku/Root): direkt in Kodis Add-on-Ordner kopieren → in Kodi nur noch einschalten.
/// </summary>
public static class KodiRepository
{
    public const string KodiPackage = "org.xbmc.kodi";
    public const string AddonId = "repository.devcatskz";
    public const string ZipName = "repository.devcatskz.zip";
    private const string ZipUrl = "https://github.com/DevCatSKZ/kodi-repo/raw/main/repository.devcatskz.zip";
    private const string KodiAddons = "/sdcard/Android/data/org.xbmc.kodi/files/.kodi/addons";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static bool IsKodiInstalled(Context context)
    {
        try
        {
            context.PackageManager!.GetPackageInfo(KodiPackage, 0);
            return true;
        }
        catch (global::Android.Content.PM.PackageManager.NameNotFoundException)
        {
            return false;
        }
    }

    public static void OpenKodi(Context context)
    {
        if (context.PackageManager!.GetLaunchIntentForPackage(KodiPackage) is { } launch)
            context.StartActivity(launch);
    }

    public static void OpenKodiInStore(Context context)
    {
        try
        {
            context.StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse($"market://details?id={KodiPackage}")));
        }
        catch (ActivityNotFoundException)
        {
            context.StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse("https://kodi.tv/download/android/")));
        }
    }

    private static async Task<byte[]> DownloadZipAsync(CancellationToken ct)
    {
        byte[] data = await Http.GetByteArrayAsync(ZipUrl, ct).ConfigureAwait(false);
        // Kurz prüfen, dass es wirklich das Repository-Add-on ist (keine Fehlerseite o. Ä.).
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        if (zip.GetEntry($"{AddonId}/addon.xml") is null)
            throw new InvalidDataException("Die heruntergeladene Datei ist kein Kodi-Repository.");
        return data;
    }

    /// <summary>Weg 1: ZIP im öffentlichen Download-Ordner speichern. Gibt den Ort für die Anzeige zurück.</summary>
    public static async Task<string> SaveToDownloadsAsync(Context context, CancellationToken ct = default)
    {
        byte[] data = await DownloadZipAsync(ct).ConfigureAwait(false);
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var resolver = context.ContentResolver!;
            var collection = MediaStore.Downloads.ExternalContentUri!;
            // Eigene ältere Fassung entfernen, sonst legt Android „repository.devcatskz (1).zip“ an.
            resolver.Delete(collection, $"{MediaStore.IMediaColumns.DisplayName}=?", [ZipName]);
            var values = new ContentValues();
            values.Put(MediaStore.IMediaColumns.DisplayName, ZipName);
            values.Put(MediaStore.IMediaColumns.MimeType, "application/zip");
            values.Put(MediaStore.IMediaColumns.RelativePath, global::Android.OS.Environment.DirectoryDownloads);
            var uri = resolver.Insert(collection, values) ?? throw new IOException("Download-Ordner nicht beschreibbar.");
            await using (var output = resolver.OpenOutputStream(uri) ?? throw new IOException("Download-Ordner nicht beschreibbar."))
                await output.WriteAsync(data, ct).ConfigureAwait(false);
            return "Download/" + ZipName;
        }
        // Android 8/9: in den App-Ordner auf dem gemeinsamen Speicher (für Kodi ohne Sonderrechte lesbar).
        var dir = context.GetExternalFilesDir(null) ?? throw new IOException("Kein gemeinsamer Speicher.");
        string path = Path.Combine(dir.AbsolutePath, ZipName);
        await File.WriteAllBytesAsync(path, data, ct).ConfigureAwait(false);
        return path;
    }

    /// <summary>
    /// Weg 2: direkt in Kodis Add-on-Ordner legen (braucht Shizuku oder Root – normale Apps dürfen nicht in den Ordner
    /// einer anderen App schreiben). Danach erscheint das Repository in Kodi unter „Meine Add-ons“ und muss nur noch
    /// eingeschaltet werden.
    /// </summary>
    public static async Task InstallDirectAsync(Context context, IPrivilegedShell shell, CancellationToken ct = default)
    {
        byte[] data = await DownloadZipAsync(ct).ConfigureAwait(false);
        // In den eigenen Ordner auf dem gemeinsamen Speicher entpacken – den kann die Shell lesen.
        var dir = context.GetExternalFilesDir(null) ?? throw new IOException("Kein gemeinsamer Speicher.");
        string staging = Path.Combine(dir.AbsolutePath, "kodi");
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        using (var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read))
            zip.ExtractToDirectory(staging);
        string source = Path.Combine(staging, AddonId);
        string script = $"mkdir -p '{KodiAddons}' && rm -rf '{KodiAddons}/{AddonId}' && cp -r '{source}' '{KodiAddons}/' && echo OK";
        var process = shell.Start("sh", "-c", script);
        string output = await Task.Run(() => RootShell.ReadAll(process.InputStream!).Trim(), ct).ConfigureAwait(false);
        if (!output.EndsWith("OK", StringComparison.Ordinal))
            throw new IOException("Kopieren in den Kodi-Ordner fehlgeschlagen – wurde Kodi schon einmal gestartet?");
        Directory.Delete(staging, recursive: true);
        Log.Info("DevCatSKZ Repository direkt in Kodi abgelegt");
    }
}
