using System.Net.Http.Headers;
using System.Text.Json;
using Android.App;
using Android.Content;
using AndroidX.Core.Content;
using Switch2Pro.Protocol;

namespace NConnect.Android.Core;

/// <summary>
/// Sucht auf GitHub nach einer neueren Android-Version (Release-Tag „v1.2.3“, Datei „N-Connect-Android-…apk“) –
/// wie UpdateCheck in N-Connect für Windows, nur mit APK statt Setup-EXE. Heruntergeladen wird nur nach Zustimmung,
/// nur von GitHub, und die Datei wird geprüft (Größe, SHA-256 falls GitHub sie angibt). Android fragt vor dem
/// Installieren noch einmal nach.
/// </summary>
internal static class UpdateCheck
{
    private const string Releases = "https://api.github.com/repos/DevCatSKZ/N-Connect/releases?per_page=30";
    private const string TagPrefix = "v";
    public const string ProviderAuthority = "com.devcatskz.nconnect.fileprovider";

    /// <summary>Gefundene APK eines Releases (Name, Download-Adresse, Größe, SHA-256 als Hex – falls GitHub sie liefert).</summary>
    public sealed record Apk(string Name, string Url, long Size, string? Sha256);

    public sealed record Update(Version Version, string Url, Apk? File);

    /// <summary>Installierte Version (VersionName aus dem Paket, z. B. „0.9.0“).</summary>
    public static Version Current(Context ctx)
    {
        string? name = ctx.PackageManager!.GetPackageInfo(ctx.PackageName!, 0)!.VersionName;
        return Version.TryParse(name?.Split('-', '+')[0], out var v) ? v : new Version(0, 0, 0);
    }

    /// <summary>Neuestes Release mit Android-APK, wenn es neuer als die installierte Version ist; sonst null.</summary>
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
            Update? best = null;
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()
                    || release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
                    continue;
                var tag = release.GetProperty("tag_name").GetString();
                if (tag is null || !tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)
                    || !Version.TryParse(tag[TagPrefix.Length..], out var version))
                    continue;
                if (best is null || version > best.Version)
                    best = new Update(version, release.GetProperty("html_url").GetString() ?? "", FindApk(release));
            }
            return best is not null && Normalize(best.Version) > Normalize(Current(ctx)) ? best : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                                      or KeyNotFoundException)
        {
            Log.Info($"Update-Prüfung nicht möglich: {Log.Reason(e)}");
            return null;
        }
    }

    /// <summary>„N-Connect-Android-*.apk“ unter den Dateien eines Releases suchen.</summary>
    private static Apk? FindApk(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var asset in assets.EnumerateArray())
        {
            string? name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (name is null || url is null || !name.StartsWith("N-Connect-Android", StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) || !IsGitHub(url))
                continue;
            long size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out long v) ? v : 0;
            // GitHub liefert „digest“: "sha256:…" für neuere Uploads.
            string? digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            string? sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
            return new Apk(name, url, size, sha);
        }
        return null;
    }

    /// <summary>Nur Downloads von GitHub (Release-Dateien liegen dort bzw. werden dorthin umgeleitet).</summary>
    private static bool IsGitHub(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// APK in den App-eigenen Download-Ordner laden und prüfen. Liefert die Datei oder null (Grund im Protokoll).
    /// </summary>
    public static async Task<Java.IO.File?> DownloadAsync(Context ctx, Apk apk, IProgress<int>? progress, CancellationToken ct)
    {
        var dir = ctx.GetExternalFilesDir(global::Android.OS.Environment.DirectoryDownloads) ?? ctx.FilesDir!;
        var file = new Java.IO.File(dir, apk.Name);
        try
        {
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

    /// <summary>1.2 und 1.2.0 gelten als gleich.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
}
