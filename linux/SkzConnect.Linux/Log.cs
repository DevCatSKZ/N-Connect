using System.Text;

namespace SkzConnect.Linux;

/// <summary>Protokoll auf stderr (systemd/Kodi-Protokoll) und in eine Datei (wird ab 512 KB neu begonnen).</summary>
public static class Log
{
    private const long MaxFileBytes = 512 * 1024;
    private static readonly object Lock = new();
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static string? _file;

    public static void UseFile(string path)
    {
        _file = path;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }
        catch (Exception)
        {
            _file = null;
        }
    }

    public static void Info(string message) => Write("I", message);
    public static void Warn(string message) => Write("W", message);
    public static void Error(string message, Exception? e = null) =>
        Write("E", e is null ? message : $"{message}: {e.GetType().Name}: {e.Message}");

    public static string Reason(Exception e) => e.Message;

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {level} {message}";
        lock (Lock)
        {
            Console.Error.WriteLine(line);
            if (_file is null)
                return;
            try
            {
                var info = new FileInfo(_file);
                if (info.Exists && info.Length > MaxFileBytes)
                    File.Move(_file, _file + ".1", overwrite: true);
                File.AppendAllText(_file, line + "\n", Utf8NoBom);
            }
            catch (Exception)
            {
                // Protokoll darf den Dienst nie stören
            }
        }
    }
}
