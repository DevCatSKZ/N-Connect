using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace NConnect.Android.Core;

/// <summary>
/// Netzteil der Update-Prüfung – ohne Android-Abhängigkeiten, damit er sich mit einem eigenen HttpMessageHandler testen
/// lässt (android/NConnect.Android.Tests). Beide Methoden werfen bei Fehlern nicht, sondern liefern sie als Ergebnis: Unter
/// Android kommen Netzfehler je nach Stelle als HttpRequestException (Verbindungsaufbau), System.Net.WebException
/// (AndroidMessageHandler verpackt so Java-IOExceptions nach dem Verbinden, z. B. Abbruch oder Zeitlimit beim Warten auf
/// die Antwort), IOException (beim Lesen) oder TaskCanceledException (Zeitlimit von HttpClient) an. Eine Prüfung, die
/// daran scheitert, darf weder „auf dem neuesten Stand“ melden noch still abbrechen.
/// </summary>
internal static partial class UpdateCheck
{
    private const string Releases = "https://api.github.com/repos/DevCatSKZ/SKZ-Connect/releases?per_page=30";

    public enum CheckStatus { UpToDate, Available, Failed }

    /// <summary>Ergebnis einer Prüfung; <see cref="Update"/> nur bei <see cref="CheckStatus.Available"/>, Detail fürs Protokoll.</summary>
    public sealed record CheckResult(CheckStatus Status, Update? Update, string Detail);

    /// <summary>Ergebnis eines Downloads; Detail fürs Protokoll (bei einem Fehler der Grund).</summary>
    public sealed record DownloadResult(bool Ok, string Detail);

    /// <summary>
    /// Release-Liste von GitHub holen und mit der installierten Version vergleichen. Fehlgeschlagen ist die Prüfung bei
    /// jedem Netzfehler, bei einer Antwort außer 2xx (z. B. 403 beim Rate-Limit, 5xx) und bei einer unlesbaren Antwort.
    /// Nur ein Abbruch über <paramref name="ct"/> wird weitergeworfen.
    /// </summary>
    internal static async Task<CheckResult> CheckAsync(Version installed, CancellationToken ct, HttpMessageHandler? handler = null)
    {
        try
        {
            using var http = Client(installed, TimeSpan.FromSeconds(15), handler);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.GetAsync(Releases, ct);
            if (!response.IsSuccessStatusCode)
                return new(CheckStatus.Failed, null, $"GitHub antwortet mit {(int)response.StatusCode} {response.ReasonPhrase}");
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new(CheckStatus.Failed, null, "Antwort von GitHub ist keine Release-Liste");
            var best = Newest(doc.RootElement);
            if (best is null)
                return new(CheckStatus.UpToDate, null, "an keinem Release liegt eine Android-APK");
            string detail = $"neueste APK {best.File.Name}, installiert {installed}";
            return IsNewer(best.Version, installed)
                ? new(CheckStatus.Available, best, detail)
                : new(CheckStatus.UpToDate, null, detail);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new(CheckStatus.Failed, null, $"{e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// APK nach <paramref name="path"/> laden und prüfen (Ziel muss auf GitHub liegen, Größe, SHA-256 falls bekannt). Liegt
    /// dort schon eine vollständige Datei mit passender Prüfsumme, wird nicht erneut geladen. Bei jedem Fehler wird die
    /// (Teil-)Datei gelöscht.
    /// </summary>
    internal static async Task<DownloadResult> DownloadToAsync(Apk apk, string path, Version installed, IProgress<int>? progress,
        CancellationToken ct, HttpMessageHandler? handler = null)
    {
        bool ok = false;
        try
        {
            if (await IsCompleteAsync(path, apk, ct))
            {
                progress?.Report(100);
                ok = true;
                return new(true, $"{apk.Name} liegt schon vollständig vor");
            }
            using var http = Client(installed, TimeSpan.FromMinutes(10), handler);
            using var response = await http.GetAsync(apk.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri is not { } final || !IsGitHub(final.ToString()))
                return new(false, $"Download abgelehnt ({(int)response.StatusCode}, {response.RequestMessage?.RequestUri?.Host})");
            long total = response.Content.Headers.ContentLength ?? apk.Size;
            using var sha = SHA256.Create();
            long done = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(path, FileMode.Create))
            {
                var buffer = new byte[81920];
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
            }
            if (apk.Size > 0 && done != apk.Size)
                return new(false, $"Größe stimmt nicht ({done} statt {apk.Size} Byte)");
            string hash = Convert.ToHexString(sha.Hash!);
            if (apk.Sha256 is { } expected && !hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
                return new(false, "Prüfsumme stimmt nicht – Datei verworfen");
            ok = true;
            return new(true, $"{apk.Name} heruntergeladen (SHA-256 {hash}{(apk.Sha256 is null ? ", ohne Vergleichswert" : ", geprüft")})");
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new(false, $"Download fehlgeschlagen: {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            if (!ok)
            {
                try { File.Delete(path); } catch (Exception) { /* bleibt liegen, CleanUp räumt später auf */ }
            }
        }
    }

    /// <summary>Schon geladene Datei mit passender Größe und Prüfsumme? (Ohne Vergleichswert wird lieber frisch geladen.)</summary>
    private static async Task<bool> IsCompleteAsync(string path, Apk apk, CancellationToken ct)
    {
        if (apk.Sha256 is null || apk.Size <= 0)
            return false;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != apk.Size)
            return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).Equals(apk.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>HttpClient mit Zeitlimit und User-Agent (GitHub verlangt einen); ohne <paramref name="handler"/> der Standard der Plattform.</summary>
    private static HttpClient Client(Version installed, TimeSpan timeout, HttpMessageHandler? handler)
    {
        var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = timeout;
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SKZ-Connect", installed.ToString()));
        return http;
    }
}
