using System.Text.Json;
using System.Text.RegularExpressions;

namespace SkzConnect.Android.Core;

/// <summary>
/// Auswertung der GitHub-Release-Liste (ohne Android-Abhängigkeiten, damit sie sich ohne Gerät testen lässt –
/// android/SkzConnect.Android.Tests). Die APKs hängen an den gemeinsamen Releases (Tag = Windows-Version „v1.0.x“),
/// deshalb zählt nicht der Tag, sondern die Version im Dateinamen „SKZ-Connect-Android-0.9.1.apk“; Releases ohne APK
/// werden übersprungen.
/// </summary>
internal static partial class UpdateCheck
{
    /// <summary>„SKZ-Connect-Android-0.9.1.apk“ → 0.9.1 (Groß-/Kleinschreibung egal).</summary>
    [GeneratedRegex(@"^SKZ-Connect-Android-(\d+(?:\.\d+){1,3})\.apk$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApkName();

    /// <summary>Gefundene APK eines Releases (Name, Version, Download-Adresse, Größe, SHA-256 als Hex – falls GitHub sie liefert).</summary>
    public sealed record Apk(string Name, Version Version, string Url, long Size, string? Sha256);

    public sealed record Update(Version Version, string Url, Apk File);

    /// <summary>Release mit der höchsten APK-Version (ohne Entwürfe und Vorabversionen); null, wenn keines eine APK hat.</summary>
    internal static Update? Newest(JsonElement releases)
    {
        Update? best = null;
        if (releases.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object
                || release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True
                || release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
                continue;
            if (FindApk(release) is not { } apk)
                continue;
            string url = release.TryGetProperty("html_url", out var page) && page.ValueKind == JsonValueKind.String ? page.GetString()! : "";
            if (best is null || Normalize(apk.Version) > Normalize(best.Version))
                best = new Update(apk.Version, url, apk);
        }
        return best;
    }

    /// <summary>Gibt es ein Update? Nur eine echt höhere Version zählt (1.2 und 1.2.0 sind gleich).</summary>
    internal static bool IsNewer(Version available, Version installed) => Normalize(available) > Normalize(installed);

    /// <summary>„SKZ-Connect-Android-&lt;Version&gt;.apk“ unter den Dateien eines Releases suchen (höchste Version, falls mehrere).</summary>
    private static Apk? FindApk(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;
        Apk? best = null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object)
                continue;
            string? name = asset.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            string? url = asset.TryGetProperty("browser_download_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            if (VersionOf(name) is not { } version || url is null || !IsGitHub(url))
                continue;
            long size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out long v) ? v : 0;
            // GitHub liefert „digest“: "sha256:…" für neuere Uploads.
            string? digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            string? sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
            if (best is null || Normalize(version) > Normalize(best.Version))
                best = new Apk(name!, version, url, size, sha);
        }
        return best;
    }

    /// <summary>Version aus dem Dateinamen einer Android-APK; null bei anderen Dateien.</summary>
    internal static Version? VersionOf(string? fileName) =>
        fileName is not null && ApkName().Match(fileName) is { Success: true } m && Version.TryParse(m.Groups[1].Value, out var v) ? v : null;

    /// <summary>Nur Downloads von GitHub (Release-Dateien liegen dort bzw. werden dorthin umgeleitet).</summary>
    internal static bool IsGitHub(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>1.2 und 1.2.0 gelten als gleich.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
}
