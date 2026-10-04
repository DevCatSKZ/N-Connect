using System.Diagnostics;
using Microsoft.Win32;

namespace Switch2Pro.Bridge;

/// <summary>
/// Optionale Anbindung an HidHide (Nefarius, frei): versteckt die HID-Schnittstelle eines per USB angeschlossenen
/// Controllers vor anderen Programmen, damit Spiele ihn nicht doppelt sehen (Original + virtueller Xbox-Controller).
/// Diese App wird dabei freigegeben und liest den Controller weiter. Das Einrichten braucht einmal Adminrechte
/// (Windows fragt nach) – nur auf Klick des Benutzers.
/// </summary>
internal static class HidHide
{
    public const string Download = "https://github.com/nefarius/HidHide/releases/latest";

    /// <summary>Pfad zu HidHideCLI.exe, falls HidHide installiert ist.</summary>
    public static string? CliPath
    {
        get
        {
            var candidates = new List<string>();
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Nefarius Software Solutions e.U.\HidHide");
                if (key?.GetValue("Path") is string dir)
                    candidates.Add(Path.Combine(dir, "x64", "HidHideCLI.exe"));
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            candidates.Add(Path.Combine(programFiles, "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe"));
            return candidates.FirstOrDefault(File.Exists);
        }
    }

    public static bool IsInstalled => CliPath is not null;

    /// <summary>
    /// Versteckt die Geräte (HID-Instanz-IDs) und gibt diese App frei. Fragt per UAC nach Adminrechten.
    /// true, wenn HidHideCLI ohne Fehler durchlief.
    /// </summary>
    public static async Task<bool> HideAsync(IEnumerable<string> instanceIds)
    {
        if (CliPath is not { } cli || Environment.ProcessPath is not { } self)
            return false;
        var args = new List<string> { "--app-reg", Quote(self) };
        foreach (var id in instanceIds)
        {
            args.Add("--dev-hide");
            args.Add(Quote(id));
        }
        args.Add("--cloak-on");
        try
        {
            using var process = Process.Start(new ProcessStartInfo(cli, string.Join(' ', args))
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
                return false;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Log.Info($"HidHide: {string.Join(' ', args)} → Ergebnis {process.ExitCode}");
            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or TimeoutException or InvalidOperationException)
        {
            // Abbruch der UAC-Abfrage: Win32Exception 1223.
            Log.Warn($"HidHide: nicht eingerichtet ({Log.Reason(e)})");
            return false;
        }
    }

    private static string Quote(string s) => $"\"{s.Replace("\"", "")}\"";
}
