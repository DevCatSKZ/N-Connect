using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Switch2Pro.Bridge;

/// <summary>
/// Optionale Anbindung an HidHide (Nefarius, frei): versteckt die HID-Schnittstelle eines per USB angeschlossenen
/// Controllers vor anderen Programmen, damit Spiele ihn nicht doppelt sehen (Original + virtueller Xbox-Controller).
/// Diese App wird dabei freigegeben und liest den Controller weiter. Das Einrichten braucht einmal Adminrechte
/// (Windows fragt nach) – nur auf Klick des Benutzers.
/// </summary>
internal static partial class HidHide
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

    /// <summary>Geplante Aufgabe (vom Installer angelegt, läuft als SYSTEM), die ohne UAC-Abfrage versteckt bzw. freigibt.</summary>
    public const string TaskName = "N-Connect HidHide";

    /// <summary>Erstes Feld im Argument der Aufgabe, wenn freigegeben statt versteckt werden soll: „unhide|id1|id2“.</summary>
    internal const string UnhideVerb = "unhide";

    /// <summary>Höchstens so viele Geräte je Aufruf (der Helfer nimmt nicht mehr an).</summary>
    internal const int MaxIds = 16;

    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// Ergebnis von <see cref="HideAsync"/> und <see cref="UnhideAsync"/>: erledigt (HidHideCLI ohne Fehler), UAC-Abfrage
    /// abgelehnt oder fehlgeschlagen (Grund im Protokoll).
    /// </summary>
    public enum Outcome { Done, Declined, Failed }

    /// <summary>
    /// Versteckt die Geräte (HID-Instanz-IDs) und gibt diese App frei. Ohne Rückfrage über die geplante Aufgabe des
    /// Installers, sonst per UAC-Abfrage. <see cref="Outcome.Done"/>, wenn HidHideCLI ohne Fehler durchlief;
    /// <see cref="Outcome.Declined"/> nur, wenn der Benutzer die UAC-Abfrage abgelehnt hat.
    /// </summary>
    public static async Task<Outcome> HideAsync(IEnumerable<string> instanceIds)
    {
        // Virtuelle Geräte (eigene ViGEm-Pads) nie verstecken – Spiele sähen sonst die Ausgabe von N-Connect nicht.
        var ids = instanceIds.Where(id =>
        {
            if (Usb.DeviceTree.IsPhysicalInstance(id))
                return true;
            Log.Warn($"HidHide: {id} ist virtuell oder nicht angeschlossen – wird nicht versteckt");
            return false;
        }).ToList();
        if (ids.Count == 0 || Environment.ProcessPath is not { } self)
            return Outcome.Failed;
        return await RunAsync(string.Join('|', ids), HideArguments(self, ids));
    }

    /// <summary>
    /// Gibt eigene virtuelle Pads in HidHide wieder frei. Bis 1.0.21 hielt N-Connect sein ViGEm-DualShock-4 für einen
    /// echten Controller und konnte es verstecken (Issue #1); der Eintrag bleibt in HidHide stehen, und ViGEm vergibt
    /// dieselbe Instanz-ID wieder – Steam und Spiele sähen die DS4-Ausgabe nie. Nur nachweislich virtuelle IDs
    /// (<see cref="IsVirtualPad"/>); Weg wie beim Verstecken (Aufgabe, sonst UAC).
    /// </summary>
    public static async Task<Outcome> UnhideAsync(IEnumerable<string> instanceIds)
    {
        var ids = instanceIds.Where(IsVirtualPad).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxIds).ToList();
        if (ids.Count == 0)
            return Outcome.Failed;
        return await RunAsync(string.Join('|', ids.Prepend(UnhideVerb)), UnhideArguments(ids));
    }

    /// <summary>Über die geplante Aufgabe (Argument für den Helfer), sonst per UAC direkt mit HidHideCLI.</summary>
    private static async Task<Outcome> RunAsync(string taskArgument, string cliArguments)
    {
        await Gate.WaitAsync();
        try
        {
            return await RunViaTaskAsync(taskArgument) ?? await RunElevatedAsync(cliArguments);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Startet die geplante Aufgabe mit dem Argument für den Helfer und wartet auf ihr Ergebnis. null, wenn die Aufgabe
    /// fehlt (portable Variante, Registrierung im Setup gescheitert, Installation außerhalb von „Programme“) oder für eine
    /// andere N-Connect.exe eingerichtet ist (z. B. Entwicklerversion) – dann der UAC-Weg.
    /// </summary>
    private static async Task<Outcome?> RunViaTaskAsync(string argument)
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
            task.Run(argument);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Fehlende Aufgabe meldet die Aufgabenplanung als FileNotFoundException (0x80070002, Issue #1), dazu
            // COMException, UnauthorizedAccessException, RuntimeBinderException … – in jedem Fall der UAC-Weg.
            Log.Info($"HidHide: Aufgabe nicht verfügbar ({Log.Reason(e)}) – frage per UAC");
            return null;
        }
        // Zustand: 2 = in Warteschlange, 4 = läuft; danach steht das Ergebnis (Rückgabewert des Helfers) bereit.
        try
        {
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
                    Log.Info($"HidHide (Aufgabe): {argument} → Ergebnis {result}");
                    return result == 0 ? Outcome.Done : Outcome.Failed;
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Die Aufgabe ist schon gestartet – nicht zusätzlich per UAC fragen.
            Log.Warn($"HidHide (Aufgabe): Ergebnis nicht lesbar ({Log.Reason(e)})");
            return Outcome.Failed;
        }
        Log.Warn("HidHide (Aufgabe): keine Rückmeldung binnen 30 s");
        return Outcome.Failed;
    }

    /// <summary>Windows-Fehlercode, wenn der Benutzer die UAC-Abfrage ablehnt (ERROR_CANCELLED).</summary>
    private const int UacDeclined = 1223;

    private static async Task<Outcome> RunElevatedAsync(string args)
    {
        if (CliPath is not { } cli)
            return Outcome.Failed;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(cli, args)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
                return Outcome.Failed;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Log.Info($"HidHide: {args} → Ergebnis {process.ExitCode}");
            return process.ExitCode == 0 ? Outcome.Done : Outcome.Failed;
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == UacDeclined)
        {
            Log.Info("HidHide: UAC-Abfrage abgelehnt – nicht eingerichtet");
            return Outcome.Declined;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or TimeoutException or InvalidOperationException)
        {
            Log.Warn($"HidHide: nicht eingerichtet ({Log.Reason(e)})");
            return Outcome.Failed;
        }
    }

    /// <summary>HidHideCLI-Argumente: diese EXE freigeben, die Geräte verstecken, Verstecken einschalten.</summary>
    internal static string HideArguments(string self, IEnumerable<string> instanceIds) =>
        string.Join(' ', new[] { "--app-reg", Quote(self) }
            .Concat(instanceIds.SelectMany(id => new[] { "--dev-hide", Quote(id) }))
            .Append("--cloak-on"));

    /// <summary>HidHideCLI-Argumente: nur die Geräte aus der Sperrliste nehmen (Freigaben und Verstecken bleiben).</summary>
    internal static string UnhideArguments(IEnumerable<string> instanceIds) =>
        string.Join(' ', instanceIds.SelectMany(id => new[] { "--dev-unhide", Quote(id) }));

    /// <summary>
    /// Nachweislich virtuelles Pad, das nie versteckt sein darf: das ViGEm-DualShock-4 an seiner Instanz-ID – auch wenn es
    /// gerade nicht angelegt ist, denn ViGEm vergibt bei jedem neuen Pad wieder dieselbe ID (<see cref="IsViGEmDs4Id"/>) –,
    /// sonst ein angeschlossenes Gerät am ViGEm-Bus (<see cref="Usb.DeviceTree.IsViGEmInstance"/>).
    /// </summary>
    public static bool IsVirtualPad(string instanceId) =>
        IsViGEmDs4Id(instanceId) || Usb.DeviceTree.IsViGEmInstance(instanceId);

    /// <summary>
    /// Instanz-ID eines ViGEm-DualShock-4 (Issue #1: HID\VID_054C&amp;PID_05C4&amp;REV_0100\2&amp;be03103&amp;0&amp;0000):
    /// „REV_0100“ ohne „MI_“ – echte DS4 sind per USB Verbundgeräte (…&amp;MI_03\…), per Bluetooth HID\{00001124-…} – und
    /// ein Elternpräfix der Ebene 2: Der USB-Knoten des Pads hängt direkt am Root-Knoten des ViGEm-Busses (ROOT\SYSTEM\…),
    /// echte USB-Geräte hängen viel tiefer (PCI → Root-Hub → …). Im Instanzteil nur Hex-Ziffern und „&amp;“: Der Helfer
    /// reicht eine solche ID vom Benutzer an HidHideCLI weiter – Leerzeichen, Anführungszeichen oder ein „\“ am Ende
    /// könnten dort weitere Argumente einschleusen.
    /// </summary>
    internal static bool IsViGEmDs4Id(string instanceId) => ViGEmDs4Id().IsMatch(instanceId);

    [GeneratedRegex(@"\AHID\\VID_054C&PID_05C4&REV_0100\\2&[0-9A-F]{1,8}&[0-9A-F]{1,4}&[0-9A-F]{1,4}\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ViGEmDs4Id();

    /// <summary>
    /// Argument der Aufgabe zerlegen: „id1|id2“ = verstecken, „unhide|id1|id2“ = freigeben; höchstens
    /// <see cref="MaxIds"/> Geräte, Groß-/Kleinschreibung egal.
    /// </summary>
    internal static (bool Unhide, HashSet<string> Ids) ParseHelperRequest(string argument)
    {
        var parts = argument.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool unhide = parts.Length > 0 && string.Equals(parts[0], UnhideVerb, StringComparison.OrdinalIgnoreCase);
        return (unhide, parts.Skip(unhide ? 1 : 0).Take(MaxIds).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Helfer der geplanten Aufgabe (läuft als SYSTEM, Aufruf <c>--hidhide-helper "id1|id2"</c> bzw.
    /// <c>--hidhide-helper "unhide|id1|id2"</c>). Die IDs kommen vom angemeldeten Benutzer und werden deshalb streng
    /// geprüft. Versteckt wird nur, was gerade als HID-Gerät angeschlossen, ein echtes Gerät (kein virtuelles Pad wie das
    /// eigene ViGEm-DualShock-4) und ein Nintendo-/Sony-Controller bzw. bekanntes Kabel-Pad ist; freigegeben wird dabei nur
    /// diese EXE selbst. Aus der Sperrliste genommen wird nur, was nachweislich ein virtuelles Pad ist: eine ID nach dem
    /// festen ViGEm-DualShock-4-Muster oder ein angeschlossener bekannter Controller am ViGEm-Bus. An
    /// HidHideCLI gehen nur IDs aus der Geräteliste von Windows bzw. nach diesem Muster. Schreibt keine Dateien.
    /// Rückgabe: 0 = erledigt, 2 = nichts Gültiges angefragt, 3 = HidHide fehlt, sonst Code von HidHideCLI.
    /// </summary>
    public static int RunHelper(string[] args)
    {
        int at = Array.IndexOf(args, "--hidhide-helper");
        if (at < 0 || at + 1 >= args.Length || CliPath is not { } cli || Environment.ProcessPath is not { } self)
            return CliPath is null ? 3 : 2;
        var (unhide, requested) = ParseHelperRequest(args[at + 1]);
        var ids = unhide ? requested.Where(IsViGEmDs4Id).ToList() : [];
        foreach (var path in Usb.UsbNative.GetInterfacePaths(Usb.UsbNative.HidInterface))
        {
            if (!IsKnownController(path) || Usb.UsbNative.GetInstanceId(path) is not { } id || !requested.Contains(id)
                || ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                continue;
            if (unhide ? Usb.DeviceTree.IsViGEmInstance(id) : Usb.DeviceTree.IsPhysicalInstance(id))
                ids.Add(id);
        }
        if (ids.Count == 0)
            return 2;
        using var process = Process.Start(new ProcessStartInfo(cli, unhide ? UnhideArguments(ids) : HideArguments(self, ids))
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
