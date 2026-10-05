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

    /// <summary>Geplante Aufgabe (vom Installer angelegt, läuft als SYSTEM), die ohne UAC-Abfrage versteckt.</summary>
    public const string TaskName = "N-Connect HidHide";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// Versteckt die Geräte (HID-Instanz-IDs) und gibt diese App frei. Ohne Rückfrage über die geplante Aufgabe des
    /// Installers, sonst per UAC-Abfrage. true, wenn HidHideCLI ohne Fehler durchlief.
    /// </summary>
    public static async Task<bool> HideAsync(IEnumerable<string> instanceIds)
    {
        var ids = instanceIds.ToList();
        await Gate.WaitAsync();
        try
        {
            return await HideViaTaskAsync(ids) ?? await HideElevatedAsync(ids);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Startet die geplante Aufgabe mit den IDs als Argument und wartet auf ihr Ergebnis. null, wenn die Aufgabe fehlt
    /// oder für eine andere N-Connect.exe eingerichtet ist (z. B. Entwicklerversion) – dann der UAC-Weg.
    /// </summary>
    private static async Task<bool?> HideViaTaskAsync(List<string> ids)
    {
        dynamic task;
        DateTime before;
        try
        {
            if (Type.GetTypeFromProgID("Schedule.Service") is not { } type || Environment.ProcessPath is not { } self)
                return null;
            dynamic service = Activator.CreateInstance(type)!;
            service.Connect();
            task = service.GetFolder(@"\").GetTask(TaskName);
            string target = task.Definition.Actions.Item(1).Path;
            if (!string.Equals(Path.GetFullPath(target.Trim('"')), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase))
            {
                Log.Info($"HidHide: Aufgabe gehört zu {target} – frage per UAC");
                return null;
            }
            before = task.LastRunTime;
            task.Run(string.Join('|', ids));
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException
                                      or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException)
        {
            Log.Info($"HidHide: Aufgabe nicht verfügbar ({Log.Reason(e)}) – frage per UAC");
            return null;
        }
        // Zustand: 2 = in Warteschlange, 4 = läuft; danach steht das Ergebnis (Rückgabewert des Helfers) bereit.
        bool started = false;
        for (var until = DateTime.UtcNow.AddSeconds(30); DateTime.UtcNow < until; )
        {
            await Task.Delay(250);
            int state = task.State;
            if (state is 2 or 4)
                started = true;
            else if (started || (DateTime)task.LastRunTime > before)
            {
                int result = task.LastTaskResult;
                Log.Info($"HidHide (Aufgabe): {string.Join(' ', ids)} → Ergebnis {result}");
                return result == 0;
            }
        }
        Log.Warn("HidHide (Aufgabe): keine Rückmeldung binnen 30 s");
        return false;
    }

    private static async Task<bool> HideElevatedAsync(List<string> instanceIds)
    {
        if (CliPath is not { } cli || Environment.ProcessPath is not { } self)
            return false;
        var args = CliArguments(self, instanceIds);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(cli, args)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
                return false;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Log.Info($"HidHide: {args} → Ergebnis {process.ExitCode}");
            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or TimeoutException or InvalidOperationException)
        {
            // Abbruch der UAC-Abfrage: Win32Exception 1223.
            Log.Warn($"HidHide: nicht eingerichtet ({Log.Reason(e)})");
            return false;
        }
    }

    private static string CliArguments(string self, IEnumerable<string> instanceIds) =>
        string.Join(' ', new[] { "--app-reg", Quote(self) }
            .Concat(instanceIds.SelectMany(id => new[] { "--dev-hide", Quote(id) }))
            .Append("--cloak-on"));

    /// <summary>
    /// Helfer der geplanten Aufgabe (läuft als SYSTEM, Aufruf <c>--hidhide-helper "id1|id2"</c>). Die IDs kommen vom
    /// angemeldeten Benutzer und werden deshalb streng geprüft: versteckt wird nur, was gerade als HID-Gerät angeschlossen
    /// und ein Nintendo-/Sony-Controller bzw. bekanntes Kabel-Pad ist. Freigegeben wird nur diese EXE selbst. Schreibt keine
    /// Dateien. Rückgabe: 0 = versteckt, 2 = nichts Gültiges angefragt, 3 = HidHide fehlt, sonst Code von HidHideCLI.
    /// </summary>
    public static int RunHelper(string[] args)
    {
        int at = Array.IndexOf(args, "--hidhide-helper");
        if (at < 0 || at + 1 >= args.Length || CliPath is not { } cli || Environment.ProcessPath is not { } self)
            return CliPath is null ? 3 : 2;
        var requested = args[at + 1].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(16).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = new List<string>();
        foreach (var path in Usb.UsbNative.GetInterfacePaths(Usb.UsbNative.HidInterface))
        {
            if (!IsKnownController(path) || Usb.UsbNative.GetInstanceId(path) is not { } id || !requested.Contains(id))
                continue;
            ids.Add(id);
        }
        if (ids.Count == 0)
            return 2;
        using var process = Process.Start(new ProcessStartInfo(cli, CliArguments(self, ids))
        {
            UseShellExecute = false, CreateNoWindow = true,
        });
        if (process is null || !process.WaitForExit(20_000))
            return 1;
        return process.ExitCode;
    }

    private static bool IsKnownController(string hidPath) =>
        hidPath.Contains("vid&0002057e_pid&", StringComparison.OrdinalIgnoreCase)
        || hidPath.Contains("vid_057e&pid_", StringComparison.OrdinalIgnoreCase)
        || Protocol.PlayStationPad.KindFromHidPath(hidPath) is not null
        || Protocol.WiredSwitchPad.NameFromHidPath(hidPath) is not null;

    private static string Quote(string s) => $"\"{s.Replace("\"", "")}\"";
}
