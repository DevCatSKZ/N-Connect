using System.Net.Http.Headers;
using System.Text.Json;
using Android.Content;
using AndroidX.Core.Content;

namespace NConnect.Android.Core;

/// <summary>
/// Sucht auf GitHub nach einer neueren Android-Version (Datei „N-Connect-Android-…apk“ an einem Release, siehe
/// UpdateCheck.Releases.cs) – wie UpdateCheck in N-Connect für Windows, nur mit APK statt Setup-EXE. Heruntergeladen
/// wird nur nach Zustimmung, nur von GitHub, und die Datei wird geprüft (Größe, SHA-256 falls GitHub sie angibt).
/// Android fragt vor dem Installieren noch einmal nach.
/// </summary>
internal static partial class UpdateCheck
{
    private const string Releases = "https://api.github.com/repos/DevCatSKZ/N-Connect/releases?per_page=30";
    public const string ProviderAuthority = "com.devcatskz.nconnect.fileprovider";

    /// <summary>Installierte Version (VersionName aus dem Paket, z. B. „0.9.1“).</summary>
    public static Version Current(Context ctx)
    {
        string? name = ctx.PackageManager!.GetPackageInfo(ctx.PackageName!, 0)!.VersionName;
        return Version.TryParse(name?.Split('-', '+')[0], out var v) ? v : new Version(0, 0, 0);
    }

    /// <summary>
    /// Neueste Android-APK unter den Releases (nach der Version im Dateinamen, nicht nach dem Tag), wenn sie neuer als
    /// die installierte Version ist; sonst null.
    /// </summary>
    public static async Task<Update?> FindAsync(Context ctx, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("N-Connect", Current(ctx).ToString()));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.GetAsync(Releases, ct);
            if (!response.IsSuccessStatusCode)
                return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var best = Newest(doc.RootElement);
            Log.Info(best is null ? "Update-Prüfung: an keinem Release liegt eine Android-APK"
                : $"Update-Prüfung: neueste APK {best.File.Name}, installiert {Current(ctx)}");
            return best is not null && IsNewer(best.Version, Current(ctx)) ? best : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                                      or KeyNotFoundException)
        {
            Log.Info($"Update-Prüfung nicht möglich: {Log.Reason(e)}");
            return null;
        }
    }

    private static Java.IO.File DownloadDir(Context ctx) =>
        ctx.GetExternalFilesDir(global::Android.OS.Environment.DirectoryDownloads) ?? ctx.FilesDir!;

    /// <summary>
    /// APK in den App-eigenen Download-Ordner laden und prüfen. Liegt sie dort schon vollständig (z. B. weil vorher noch
    /// „Unbekannte Apps installieren“ erlaubt werden musste), wird sie nicht erneut geladen. Andere APKs im Ordner
    /// werden gelöscht. Liefert die Datei oder null (Grund im Protokoll).
    /// </summary>
    public static async Task<Java.IO.File?> DownloadAsync(Context ctx, Apk apk, IProgress<int>? progress, CancellationToken ct)
    {
        var file = new Java.IO.File(DownloadDir(ctx), apk.Name);
        CleanUp(ctx, keep: apk.Name);
        try
        {
            if (await IsCompleteAsync(file, apk, ct))
            {
                Log.Info($"Update: {apk.Name} liegt schon vollständig vor");
                progress?.Report(100);
                return file;
            }
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("N-Connect", Current(ctx).ToString()));
            using var response = await http.GetAsync(apk.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri is not { } final || !IsGitHub(final.ToString()))
            {
                Log.Warn($"Update: Download abgelehnt ({(int)response.StatusCode}, {response.RequestMessage?.RequestUri?.Host})");
                return null;
            }
            long total = response.Content.Headers.ContentLength ?? apk.Size;
            using var sha = System.Security.Cryptography.SHA256.Create();
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(file.AbsolutePath!, FileMode.Create))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    done += read;
                    if (total > 0)
                        progress?.Report((int)(done * 100 / total));
                }
                sha.TransformFinalBlock([], 0, 0);
                if (apk.Size > 0 && done != apk.Size)
                {
                    Log.Warn($"Update: Größe stimmt nicht ({done} statt {apk.Size} Byte)");
                    file.Delete();
                    return null;
                }
            }
            string hash = Convert.ToHexString(sha.Hash!);
            if (apk.Sha256 is { } expected && !hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn("Update: Prüfsumme stimmt nicht – Datei verworfen");
                file.Delete();
                return null;
            }
            Log.Info($"Update: {apk.Name} heruntergeladen (SHA-256 {hash}{(apk.Sha256 is null ? ", ohne Vergleichswert" : ", geprüft")})");
            return file;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
                                      or Java.Lang.SecurityException)
        {
            Log.Warn($"Update: Download fehlgeschlagen: {Log.Reason(e)}");
            try { file.Delete(); } catch (Exception) { /* bleibt liegen */ }
            return null;
        }
    }

    /// <summary>Schon geladene Datei mit passender Größe und Prüfsumme? (Ohne Vergleichswert wird lieber frisch geladen.)</summary>
    private static async Task<bool> IsCompleteAsync(Java.IO.File file, Apk apk, CancellationToken ct)
    {
        if (apk.Sha256 is null || apk.Size <= 0 || !file.Exists() || file.Length() != apk.Size)
            return false;
        await using var stream = new FileStream(file.AbsolutePath!, FileMode.Open, FileAccess.Read);
        byte[] hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).Equals(apk.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Alte Update-APKs im Download-Ordner löschen: beim App-Start alle, die nicht neuer als die installierte Version sind
    /// (also schon eingespielt oder überholt), vor einem Download alle außer der neuen (<paramref name="keep"/>).
    /// </summary>
    public static void CleanUp(Context ctx, string? keep = null)
    {
        try
        {
            var current = Current(ctx);
            foreach (var file in DownloadDir(ctx).ListFiles() ?? [])
            {
                using (file)
                {
                    string name = file.Name ?? "";
                    if (!name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) || name == keep)
                        continue;
                    if (keep is null && VersionOf(name) is { } version && IsNewer(version, current))
                        continue; // noch nicht installiert – bleibt für den nächsten Versuch
                    if (file.Delete())
                        Log.Info($"Update: alte Datei {name} gelöscht");
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"Update: Aufräumen nicht möglich: {Log.Reason(e)}");
        }
    }

    /// <summary>System-Installationsdialog für eine geladene APK öffnen (über den FileProvider).</summary>
    public static void Install(Context ctx, Java.IO.File apk)
    {
        var uri = FileProvider.GetUriForFile(ctx, ProviderAuthority, apk)!;
        var intent = new Intent(Intent.ActionView)
            .SetDataAndType(uri, "application/vnd.android.package-archive")!
            .AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission)!;
        ctx.StartActivity(intent);
    }

    /// <summary>Darf die App Pakete installieren? (Ab Android 8 einmalig pro Quelle erlauben.)</summary>
    public static bool CanInstall(Context ctx) =>
        !OperatingSystem.IsAndroidVersionAtLeast(26) || ctx.PackageManager!.CanRequestPackageInstalls();

    /// <summary>Systemseite „Unbekannte Apps installieren“ für N-Connect öffnen.</summary>
    public static void OpenInstallPermission(Context ctx) =>
        ctx.StartActivity(new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources,
                global::Android.Net.Uri.Parse($"package:{ctx.PackageName}"))!
            .AddFlags(ActivityFlags.NewTask));
}
