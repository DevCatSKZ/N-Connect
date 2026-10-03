namespace Switch2Pro.Bridge;

internal static class Paths
{
    public static string SettingsDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Switch2ProBridge");
    public static string SettingsFile { get; } = Path.Combine(SettingsDir, "settings.json");
    public static string LogDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Switch2ProBridge");
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
