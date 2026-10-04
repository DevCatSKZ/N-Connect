namespace Switch2Pro.Bridge;

internal static class Paths
{
    public static string SettingsDir { get; } = Folder(Environment.SpecialFolder.ApplicationData);
    public static string SettingsFile { get; } = Path.Combine(SettingsDir, "settings.json");
    public static string LogDir { get; } = Folder(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Ordner „N-Connect“; der Ordner der Vorversion („Switch2ProBridge“) wird einmalig übernommen.</summary>
    private static string Folder(Environment.SpecialFolder root)
    {
        string baseDir = Environment.GetFolderPath(root);
        string dir = Path.Combine(baseDir, "N-Connect");
        string legacy = Path.Combine(baseDir, "Switch2ProBridge");
        try
        {
            if (!Directory.Exists(dir) && Directory.Exists(legacy))
                Directory.Move(legacy, dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Alter Ordner gesperrt: dann eben mit Standardwerten im neuen Ordner beginnen.
        }
        return dir;
    }
    public static string LogFile { get; } = Path.Combine(LogDir, "bridge.log");
}

/// <summary>Kleines Datei-Protokoll (wird ab 1 MB einmal rotiert).</summary>
internal static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? e = null) =>
        Write("ERROR", e is null ? message : $"{message}: {e}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Paths.LogDir);
                var info = new FileInfo(Paths.LogFile);
                if (info.Exists && info.Length > 1_000_000)
                    File.Move(Paths.LogFile, Paths.LogFile + ".1", overwrite: true);
                File.AppendAllText(Paths.LogFile, line);
            }
            catch (IOException)
            {
                // Protokoll ist nur Hilfe bei der Fehlersuche; niemals daran scheitern.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
