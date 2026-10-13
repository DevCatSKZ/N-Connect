using System.IO.Compression;
using System.Net.Http;

namespace Switch2Pro.Bridge;

/// <summary>
/// Kodi auf diesem PC: das „DevCatSKZ Repository“ (github.com/DevCatSKZ/kodi-repo) direkt in Kodis Add-on-Ordner legen
/// oder Repository/Plugin herunterladen. Unter Windows darf N-Connect in Kodis Benutzerordner schreiben – keine
/// Adminrechte nötig. Kodi aus dem Microsoft Store hat seinen Ordner unter %LOCALAPPDATA%\Packages.
/// </summary>
internal static class KodiIntegration
{
    public const string AddonId = "repository.devcatskz";
    /// <summary>Add-on, dessen status.json das Kodi-Menü und das Home-Widget anzeigen.</summary>
    public const string ServiceAddonId = "service.nconnect";
    public const string RepositoryZipName = "repository.devcatskz.zip";
    public const string PluginZipName = "N-Connect-Kodi-LibreELEC-alle-Geraete.zip";
    private const string RepositoryUrl = "https://github.com/DevCatSKZ/kodi-repo/raw/main/repository.devcatskz.zip";
    private const string PluginUrl = "https://github.com/DevCatSKZ/kodi-repo/raw/main/downloads/N-Connect-Kodi-LibreELEC-alle-Geraete.zip";
    public const string RepositoryPage = "https://github.com/DevCatSKZ/kodi-repo";
    public const string KodiDownloadPage = "https://kodi.tv/download/windows/";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Add-on-Ordner aller gefundenen Kodi-Installationen (normal und Microsoft Store).</summary>
    public static List<string> FindAddonFolders()
    {
        var result = new List<string>();
        string roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kodi");
        if (Directory.Exists(roaming))
            result.Add(Path.Combine(roaming, "addons"));
        try
        {
            string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (Directory.Exists(packages))
                foreach (string pkg in Directory.GetDirectories(packages, "XBMCFoundation.Kodi_*"))
                {
                    string store = Path.Combine(pkg, "LocalCache", "Roaming", "Kodi");
                    if (Directory.Exists(store))
                        result.Add(Path.Combine(store, "addons"));
                }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Store-Ordner nicht lesbar – nur die normale Installation nutzen
        }
        return result;
    }

    public static bool KodiFound => FindAddonFolders().Count > 0;

    /// <summary>Datenordner des Add-ons (userdata/addon_data/service.nconnect) je gefundener Kodi-Installation.
    /// Dorthin schreibt <see cref="KodiStatusWriter"/> die status.json – daraus lesen Menü und Widget.</summary>
    public static List<string> FindDataDirs()
    {
        var result = new List<string>();
        foreach (string addons in FindAddonFolders())
            if (Directory.GetParent(addons) is { } kodi)
                result.Add(Path.Combine(kodi.FullName, "userdata", "addon_data", ServiceAddonId));
        return result;
    }

    private static async Task<byte[]> DownloadAsync(string url, bool checkRepository)
    {
        byte[] data = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
        if (checkRepository)
        {
            using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            if (zip.GetEntry($"{AddonId}/addon.xml") is null)
                throw new InvalidDataException("Die heruntergeladene Datei ist kein Kodi-Repository.");
        }
        return data;
    }

    /// <summary>Repository in alle gefundenen Kodi-Installationen entpacken. Gibt die Anzahl zurück.</summary>
    public static async Task<int> InstallRepositoryAsync()
    {
        byte[] data = await DownloadAsync(RepositoryUrl, checkRepository: true).ConfigureAwait(false);
        int done = 0;
        foreach (string addons in FindAddonFolders())
        {
            Directory.CreateDirectory(addons);
            string target = Path.Combine(addons, AddonId);
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            zip.ExtractToDirectory(addons, overwriteFiles: true);
            Log.Info($"DevCatSKZ Repository in Kodi abgelegt: {target}");
            done++;
        }
        return done;
    }

    /// <summary>Repository- oder Plugin-ZIP in den Download-Ordner laden; gibt den Pfad zurück.</summary>
    public static async Task<string> SaveToDownloadsAsync(bool plugin)
    {
        byte[] data = await DownloadAsync(plugin ? PluginUrl : RepositoryUrl, checkRepository: !plugin).ConfigureAwait(false);
        string folder = DownloadsFolder();
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, plugin ? PluginZipName : RepositoryZipName);
        await File.WriteAllBytesAsync(path, data).ConfigureAwait(false);
        Log.Info($"Kodi-Download gespeichert: {path}");
        return path;
    }

    private static string DownloadsFolder()
    {
        // Bekannter Ordner „Downloads“ (FOLDERID_Downloads); Fallback: Benutzerordner\Downloads.
        try
        {
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out string path) == 0)
                return path;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] Guid id,
        uint flags, IntPtr token, out string path);

    public static void Open(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Öffnen fehlgeschlagen: {e.Message}");
        }
    }

    public static void ShowInExplorer(string file)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Log.Warn($"Explorer: {e.Message}");
        }
    }
}
