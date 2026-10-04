using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Switch2Pro.Bridge;

/// <summary>
/// Sucht auf GitHub nach einer neueren Version (Releases mit Tag „v1.2.3“, wie sie der Build-Workflow anlegt).
/// Lädt nichts herunter – meldet nur, damit der Benutzer selbst entscheidet. Fehler (offline, privates Repo) werden
/// still ignoriert.
/// </summary>
internal static class UpdateCheck
{
    private const string Releases = "https://api.github.com/repos/DevCatSKZ/N-Connect/releases?per_page=30";
    private const string TagPrefix = "v";

    public sealed record Update(Version Version, string Url);

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
                    best = new Update(version, release.GetProperty("html_url").GetString() ?? "");
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
