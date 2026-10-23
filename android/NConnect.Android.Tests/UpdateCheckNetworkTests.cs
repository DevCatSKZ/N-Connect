using System.Net;
using System.Security.Cryptography;
using System.Text;
using NConnect.Android.Core;

namespace NConnect.Android.Tests;

/// <summary>
/// Netzteil der Update-Prüfung mit einem nachgebauten HttpMessageHandler. Vorher meldete die App ohne Netz, bei 403
/// (Rate-Limit) und bei 5xx „SKZ Connect ist auf dem neuesten Stand“; eine System.Net.WebException (so verpackt
/// AndroidMessageHandler Java-IOExceptions nach dem Verbinden) ließ die Prüfung still abbrechen und beim Download den nicht
/// schließbaren Fortschrittsdialog stehen. Jetzt: jeder Fehler ist ein Ergebnis, nie eine Ausnahme.
/// </summary>
public sealed class UpdateCheckNetworkTests : IDisposable
{
    private static readonly Version Installed = new(0, 9, 1);
    private readonly string _dir = Directory.CreateTempSubdirectory("nconnect-update-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Antwortet mit <paramref name="respond"/> (oder wirft daraus) und zählt die Anfragen.</summary>
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Last = request;
            ct.ThrowIfCancellationRequested();
            var response = respond(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    /// <summary>Liefert erst ein paar Byte und bricht dann ab – wie eine abgerissene Verbindung mitten in der Antwort.</summary>
    private sealed class BrokenStream : Stream
    {
        private int _reads;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_reads++ > 0)
                throw new IOException("unexpected end of stream");
            int n = Math.Min(count, 100);
            Array.Fill(buffer, (byte)'x', offset, n);
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static Exception Failure(string kind) => kind switch
    {
        // Kein Netz / DNS / TLS: AndroidMessageHandler.ConnectAsync macht daraus eine HttpRequestException.
        "offline" => new HttpRequestException("Connection failure", new IOException("Unable to resolve host \"api.github.com\"")),
        // Abbruch oder Reset nach dem Verbinden, Zeitlimit beim Warten auf die Antwort: DoSendAsync wirft WebException.
        "webexception" => new WebException("Connection reset", WebExceptionStatus.ConnectionClosed),
        "ioexception" => new IOException("Software caused connection abort"),
        // Zeitlimit von HttpClient (ohne Abbruch durch den Aufrufer).
        "zeitlimit" => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
        // Alles andere (z. B. eine Java-Ausnahme) darf die Prüfung ebenfalls nicht still beenden.
        "sonstige" => new InvalidOperationException("unerwartet"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string ReleaseList(string assetName) =>
        $$"""
        [{"tag_name":"v1.0.23","draft":false,"prerelease":false,
          "html_url":"https://github.com/DevCatSKZ/SKZ-Connect/releases/tag/v1.0.23",
          "assets":[{"name":"{{assetName}}","size":1000,
                     "browser_download_url":"https://github.com/DevCatSKZ/SKZ-Connect/releases/download/v1.0.23/{{assetName}}"}]}]
        """;

    // ---------- Prüfung ----------

    [Theory]
    [InlineData("offline")]
    [InlineData("webexception")]
    [InlineData("ioexception")]
    [InlineData("zeitlimit")]
    [InlineData("sonstige")]
    public async Task Pruefung_Netzfehler_IstFehlschlag_NichtAufDemNeuestenStand(string kind)
    {
        var result = await UpdateCheck.CheckAsync(Installed, CancellationToken.None, new Handler(_ => throw Failure(kind)));
        Assert.Equal(UpdateCheck.CheckStatus.Failed, result.Status);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task Pruefung_AbbruchBeimLesenDerAntwort_IstFehlschlag()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) });
        var result = await UpdateCheck.CheckAsync(Installed, CancellationToken.None, handler);
        Assert.Equal(UpdateCheck.CheckStatus.Failed, result.Status);
    }

    [Theory]
    [InlineData(403)] // Rate-Limit ohne Anmeldung (60 Anfragen/Stunde je Adresse)
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task Pruefung_HttpFehler_IstFehlschlag(int status)
    {
        var handler = new Handler(_ => Json("{\"message\":\"API rate limit exceeded\"}", (HttpStatusCode)status));
        var result = await UpdateCheck.CheckAsync(Installed, CancellationToken.None, handler);
        Assert.Equal(UpdateCheck.CheckStatus.Failed, result.Status);
        Assert.Contains(status.ToString(), result.Detail);
    }

    [Theory]
    [InlineData("<html>Captive Portal</html>")]
    [InlineData("")]
    [InlineData("{\"message\":\"Not Found\"}")]
    public async Task Pruefung_UnlesbareAntwort_IstFehlschlag(string body)
    {
        var result = await UpdateCheck.CheckAsync(Installed, CancellationToken.None, new Handler(_ => Json(body)));
        Assert.Equal(UpdateCheck.CheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Pruefung_NeuereApk_IstVerfuegbar()
    {
        var handler = new Handler(_ => Json(ReleaseList("SKZ-Connect-Android-0.9.2.apk")));
        var result = await UpdateCheck.CheckAsync(Installed, CancellationToken.None, handler);
        Assert.Equal(UpdateCheck.CheckStatus.Available, result.Status);
        Assert.Equal(new Version(0, 9, 2), result.Update!.Version);
        // GitHub verlangt einen User-Agent.
        Assert.Contains(handler.Last!.Headers.UserAgent, p => p.Product?.Name == "SKZ-Connect" && p.Product.Version == "0.9.1");
    }

    [Theory]
    [InlineData("SKZ-Connect-Android-0.9.1.apk")] // dieselbe Version
    [InlineData("SKZ-Connect-Android-0.9.0.apk")] // ältere
    [InlineData("SKZ-Connect-Setup-1.0.23.exe")]  // keine APK an den Releases
    public async Task Pruefung_OhneNeuereApk_IstAktuell(string assetName)
    {
        var result = await UpdateCheck.CheckAsync(Installed, CancellationToken.None, new Handler(_ => Json(ReleaseList(assetName))));
        Assert.Equal(UpdateCheck.CheckStatus.UpToDate, result.Status);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task Pruefung_AbbruchDurchAufrufer_WirdWeitergeworfen()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UpdateCheck.CheckAsync(Installed, cts.Token, new Handler(_ => Json("[]"))));
    }

    // ---------- Download ----------

    private static readonly byte[] Payload = Encoding.ASCII.GetBytes(new string('x', 200_000));

    private static UpdateCheck.Apk ApkFor(byte[] content, long? size = null, string? sha = null) =>
        new("SKZ-Connect-Android-0.9.2.apk", new Version(0, 9, 2),
            "https://github.com/DevCatSKZ/SKZ-Connect/releases/download/v1.0.23/SKZ-Connect-Android-0.9.2.apk",
            size ?? content.Length, sha ?? Convert.ToHexString(SHA256.HashData(content)));

    private string Target => Path.Combine(_dir, "SKZ-Connect-Android-0.9.2.apk");

    private static Handler Serves(byte[] content) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });

    [Fact]
    public async Task Download_Erfolgreich_SchreibtGepruefteDatei()
    {
        var progress = new List<int>();
        var result = await UpdateCheck.DownloadToAsync(ApkFor(Payload), Target, Installed, new SyncProgress(progress.Add),
            CancellationToken.None, Serves(Payload));
        Assert.True(result.Ok, result.Detail);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(Target));
        Assert.Equal(100, progress[^1]);
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("webexception")]
    [InlineData("ioexception")]
    [InlineData("zeitlimit")]
    [InlineData("sonstige")]
    public async Task Download_Netzfehler_KeinWurf_TeildateiGeloescht(string kind)
    {
        await File.WriteAllBytesAsync(Target, Payload[..1000]); // Rest eines früheren Versuchs
        var result = await UpdateCheck.DownloadToAsync(ApkFor(Payload), Target, Installed, null, CancellationToken.None,
            new Handler(_ => throw Failure(kind)));
        Assert.False(result.Ok);
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task Download_AbbruchWaehrendDerUebertragung_TeildateiGeloescht()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) });
        var result = await UpdateCheck.DownloadToAsync(ApkFor(Payload), Target, Installed, null, CancellationToken.None, handler);
        Assert.False(result.Ok);
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task Download_FalscheGroesse_Verworfen()
    {
        var result = await UpdateCheck.DownloadToAsync(ApkFor(Payload, size: Payload.Length + 1), Target, Installed, null,
            CancellationToken.None, Serves(Payload));
        Assert.False(result.Ok);
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task Download_FalschePruefsumme_Verworfen()
    {
        var result = await UpdateCheck.DownloadToAsync(ApkFor(Payload, sha: new string('0', 64)), Target, Installed, null,
            CancellationToken.None, Serves(Payload));
        Assert.False(result.Ok);
        Assert.False(File.Exists(Target));
    }

    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Download_HttpFehler_Abgelehnt(int status)
    {
        var result = await UpdateCheck.DownloadToAsync(ApkFor(Payload), Target, Installed, null, CancellationToken.None,
            new Handler(_ => new HttpResponseMessage((HttpStatusCode)status)));
        Assert.False(result.Ok);
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task Download_UmleitungAufFremdenServer_Abgelehnt()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Payload),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/SKZ-Connect-Android-0.9.2.apk"),
        });
        var result = await UpdateCheck.DownloadToAsync(ApkFor(Payload), Target, Installed, null, CancellationToken.None, handler);
        Assert.False(result.Ok);
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task Download_LiegtSchonVollstaendigVor_LaedtNichtErneut()
    {
        await File.WriteAllBytesAsync(Target, Payload);
        var handler = new Handler(_ => throw Failure("offline"));
        var result = await UpdateCheck.DownloadToAsync(ApkFor(Payload), Target, Installed, null, CancellationToken.None, handler);
        Assert.True(result.Ok, result.Detail);
        Assert.Equal(0, handler.Calls);
        Assert.True(File.Exists(Target));
    }

    /// <summary>Progress&lt;T&gt; meldet über den SynchronizationContext nachträglich – für den Test sofort.</summary>
    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
