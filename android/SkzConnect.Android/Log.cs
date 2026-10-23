using System.Text;

namespace SkzConnect.Android;

/// <summary>
/// Protokoll: Android-Logcat (Tag „SKZ Connect“), die letzten Zeilen im Speicher (Anzeige/Teilen unter „Diagnose“) und
/// eine Datei im App-Speicher (<c>files/skzconnect.log</c>, wird ab 512 KB neu begonnen) – Logcat ist auf vielen Geräten so
/// voll, dass ältere Zeilen nach Minuten verschwinden.
/// </summary>
public static class Log
{
    private const string Tag = "SKZ Connect";
    private const int Keep = 600;
    private const long MaxFileBytes = 512 * 1024;
    private static readonly LinkedList<string> Lines = new();
    private static string? _file;

    public static event Action? Changed;

    public static void Info(string message) => Write("I", message, () => global::Android.Util.Log.Info(Tag, message));
    public static void Warn(string message) => Write("W", message, () => global::Android.Util.Log.Warn(Tag, message));

    public static void Error(string message, Exception? e = null)
    {
        string text = e is null ? message : $"{message}: {e.GetType().Name}: {e.Message}";
        Write("E", text, () => global::Android.Util.Log.Error(Tag, text));
    }

    public static string Reason(Exception e) => e.Message;

    public static IReadOnlyList<string> Recent()
    {
        lock (Lines)
            return [.. Lines];
    }

    /// <summary>Pfad der Protokolldatei (zum Teilen).</summary>
    public static string FilePath => _file ??= Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "skzconnect.log");

    private static void Write(string level, string message, Action logcat)
    {
        logcat();
        string line = $"{DateTime.Now:HH:mm:ss.fff} {level} {message}";
        lock (Lines)
        {
            Lines.AddLast(line);
            while (Lines.Count > Keep)
                Lines.RemoveFirst();
            try
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxFileBytes)
                    File.Move(FilePath, FilePath + ".1", overwrite: true);
                File.AppendAllText(FilePath, line + "\n", Encoding.UTF8);
            }
            catch (Exception)
            {
                // Protokoll darf die App nie stören
            }
        }
        Changed?.Invoke();
    }
}
