using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Switch2Pro.Bridge;

/// <summary>
/// Sucht auf GitHub (nefarius/HidHide) nach einer neueren HidHide-Version als der installierten. Installiert wird nur
/// nach Rückfrage: Installer von GitHub laden (<see cref="UpdateCheck.DownloadAsync"/>), Authenticode-Signatur von
/// Nefarius prüfen (<see cref="IsSignedByNefarius"/>), dann das offizielle Setup mit Oberfläche starten – es entfernt
/// die alte Version und verlangt dabei einen Neustart von Windows, das soll der Benutzer sehen.
/// </summary>
internal static class HidHideUpdate
{
    private const string Latest = "https://api.github.com/repos/nefarius/HidHide/releases/latest";
    public const string ReleasesPage = "https://github.com/nefarius/HidHide/releases/latest";
    private const string Signer = "Nefarius Software Solutions e.U.";

    public sealed record Update(Version Version, UpdateCheck.Installer Setup);

    /// <summary>Installierte Version (aus HidHideCLI.exe), null = HidHide fehlt.</summary>
    public static Version? Installed =>
        HidHide.CliPath is { } cli && Version.TryParse(FileVersionInfo.GetVersionInfo(cli).FileVersion, out var v) ? v : null;

    /// <summary>Neuere Version mit x64-Installer, sonst null (auch offline, Fehler im Protokoll).</summary>
    public static async Task<Update?> FindAsync(Version installed, CancellationToken ct)
    {
        // HidHide gibt es nur für x64.
        if (RuntimeInformation.OSArchitecture != Architecture.X64)
            return null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("N-Connect", UpdateCheck.Current.ToString()));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.GetAsync(Latest, ct);
            if (!response.IsSuccessStatusCode)
                return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var release = doc.RootElement;
            string? tag = release.GetProperty("tag_name").GetString();
            if (tag is null || !Version.TryParse(tag.TrimStart('v', 'V'), out var version) || Normalize(version) <= Normalize(installed))
                return null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                string? name = asset.GetProperty("name").GetString();
                string? url = asset.GetProperty("browser_download_url").GetString();
                if (name is null || url is null || !name.StartsWith("HidHide", StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith("_x64.exe", StringComparison.OrdinalIgnoreCase)
                    || !url.StartsWith("https://github.com/nefarius/HidHide/", StringComparison.OrdinalIgnoreCase))
                    continue;
                long size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out long l) ? l : 0;
                string? digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                string? sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
                return new Update(version, new UpdateCheck.Installer(name, url, size, sha));
            }
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                                      or KeyNotFoundException)
        {
            Log.Info($"HidHide-Update-Prüfung nicht möglich: {Log.Reason(e)}");
            return null;
        }
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    /// <summary>Gültige Authenticode-Signatur (Windows prüft die Kette) und Unterzeichner ist Nefarius.</summary>
    public static bool IsSignedByNefarius(string path)
    {
        var file = new WintrustFileInfo
        {
            Size = (uint)Marshal.SizeOf<WintrustFileInfo>(),
            FilePath = Marshal.StringToCoTaskMemUni(path),
        };
        IntPtr filePtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePtr, false);
            var data = new WintrustData
            {
                Size = (uint)Marshal.SizeOf<WintrustData>(),
                UiChoice = 2,     // WTD_UI_NONE
                UnionChoice = 1,  // WTD_CHOICE_FILE
                File = filePtr,
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            int result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            if (result != 0)
            {
                Log.Warn($"HidHide-Update: Signatur ungültig (0x{result:X8})");
                return false;
            }
#pragma warning disable SYSLIB0057 // Zertifikat des Unterzeichners aus der (eben geprüften) Signatur lesen
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            string name = cert.GetNameInfo(X509NameType.SimpleName, false);
            if (name == Signer)
                return true;
            Log.Warn($"HidHide-Update: unerwarteter Unterzeichner „{name}“");
            return false;
        }
        catch (System.Security.Cryptography.CryptographicException e)
        {
            Log.Warn($"HidHide-Update: Signatur nicht lesbar ({Log.Reason(e)})");
            return false;
        }
        finally
        {
            Marshal.FreeCoTaskMem(file.FilePath);
            Marshal.FreeCoTaskMem(filePtr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustFileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WintrustData data);
}
