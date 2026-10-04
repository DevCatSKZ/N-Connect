using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Switch2Pro.Bridge;

/// <summary>
/// Sucht auf GitHub nach einer neueren Version (Releases mit Tag „v1.2.3“, wie sie der Build-Workflow anlegt) und lädt
/// auf Wunsch deren Installer herunter (<see cref="DownloadAsync"/>). Heruntergeladen wird nur, wenn der Benutzer
/// zustimmt, nur von GitHub, und die Datei wird geprüft (Größe, SHA-256, falls GitHub sie angibt). Fehler (offline,
/// privates Repo) werden still ignoriert.
/// </summary>
internal static class UpdateCheck
{
    private const string Releases = "https://api.github.com/repos/DevCatSKZ/N-Connect/releases?per_page=30";
    private const string TagPrefix = "v";

    /// <summary>Gefundene Version: Seite des Releases und – falls vorhanden – der Installer als Datei.</summary>
    public sealed record Update(Version Version, string Url, Installer? Setup = null);

    /// <summary>Installer-Datei eines Releases (Name, Download-Adresse, Größe, SHA-256 als Hex – falls GitHub sie liefert).</summary>
    public sealed record Installer(string Name, string Url, long Size, string? Sha256);

    /// <summary>Installer unter den Dateien eines Releases: „N-Connect-Setup-….exe“.</summary>
    private static Installer? FindInstaller(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var asset in assets.EnumerateArray())
        {
            string? name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (name is null || url is null || !name.StartsWith("N-Connect-Setup", StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !IsGitHub(url))
                continue;
            long size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out long v) ? v : 0;
            // GitHub liefert „digest“: "sha256:…" für neuere Uploads.
            string? digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            string? sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
            return new Installer(name, url, size, sha);
        }
        return null;
    }

    /// <summary>Nur Downloads von GitHub (Release-Dateien liegen dort bzw. werden dorthin umgeleitet).</summary>
    private static bool IsGitHub(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Installer herunterladen (in den Temp-Ordner) und prüfen. Liefert den Pfad oder null (Fehler – Grund im Protokoll).
    /// <paramref name="progress"/>: Prozent 0–100.
    /// </summary>
    public static async Task<string?> DownloadAsync(Installer setup, IProgress<int>? progress, CancellationToken ct)
    {
        string folder = Path.Combine(Path.GetTempPath(), "N-Connect-Update");
        string path = Path.Combine(folder, Path.GetFileName(setup.Name));
        try
        {
            Directory.CreateDirectory(folder);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("N-Connect", Current.ToString()));
            using var response = await http.GetAsync(setup.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri is not { } final || !IsGitHub(final.ToString()))
            {
                Log.Warn($"Update: Download abgelehnt ({(int)response.StatusCode}, {response.RequestMessage?.RequestUri?.Host})");
                return null;
            }
            long total = response.Content.Headers.ContentLength ?? setup.Size;
            using var sha = System.Security.Cryptography.SHA256.Create();
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = File.Create(path))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    done += read;
                    if (total > 0)
                        progress?.Report((int)(done * 100 / total));
                }
                sha.TransformFinalBlock([], 0, 0);
                if (setup.Size > 0 && done != setup.Size)
                {
                    Log.Warn($"Update: Größe stimmt nicht ({done} statt {setup.Size} Byte)");
                    file.Close();
                    File.Delete(path);
                    return null;
                }
            }
            string hash = Convert.ToHexString(sha.Hash!);
            if (setup.Sha256 is { } expected && !hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn($"Update: Prüfsumme stimmt nicht – Datei verworfen");
                File.Delete(path);
                return null;
            }
            Log.Info($"Update: {setup.Name} heruntergeladen (SHA-256 {hash}{(setup.Sha256 is null ? ", ohne Vergleichswert" : ", geprüft")})");
            return path;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Update: Download fehlgeschlagen: {Log.Reason(e)}");
            try { File.Delete(path); } catch (Exception) { /* bleibt im Temp-Ordner */ }
            return null;
        }
    }

    /// <summary>Eigene Version (aus dem Build, z. B. 1.0.42).</summary>
    public static Version Current
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (info is not null && Version.TryParse(info.Split('+', '-')[0], out var v))
                return v;
            return asm.GetName().Version ?? new Version(1, 0, 0);
        }
    }

    public static async Task<Update?> FindAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("N-Connect", Current.ToString()));
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
                    best = new Update(version, release.GetProperty("html_url").GetString() ?? "", FindInstaller(release));
            }
            return best is not null && Normalize(best.Version) > Normalize(Current) ? best : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                                      or KeyNotFoundException)
        {
            Log.Info($"Update-Prüfung nicht möglich: {Log.Reason(e)}");
            return null;
        }
    }

    /// <summary>1.2 und 1.2.0 gelten als gleich.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
}
