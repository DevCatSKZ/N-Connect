using NConnect.Android.Touch;
using Exception = System.Exception;

namespace NConnect.Android.Output;

/// <summary>Zustand eines Ausgabewegs für die Anzeige (Einrichtung, Übersicht).</summary>
public enum PathState
{
    /// <summary>Nicht vorhanden (kein Root / Shizuku nicht installiert / Bedienungshilfe aus).</summary>
    Missing,
    /// <summary>Vorhanden, aber noch nicht bereit (Shizuku nicht gestartet / Freigabe fehlt).</summary>
    NeedsAction,
    /// <summary>Bereit.</summary>
    Ready,
}

/// <summary>
/// Wählt den Ausgabeweg (Root → Shizuku → Touch, je nach Einstellung) und legt pro Spieler den passenden
/// virtuellen Controller an.
/// </summary>
public static class OutputRouter
{
    /// <summary>Aktiver Weg für virtuelle Controller (null = keiner, nur Touch oder gar keine Ausgabe).</summary>
    public static IPrivilegedShell? Shell { get; private set; }

    public static event Action? Changed;

    public static PathState RootState()
    {
        if (RootShell.Instance.HasPermission())
            return PathState.Ready;
        return RootShell.Instance.IsAvailable() ? PathState.NeedsAction : PathState.Missing;
    }

    public static PathState ShizukuState()
    {
        if (!ShizukuShell.IsInstalled())
            return PathState.Missing;
        return ShizukuShell.Instance.HasPermission() ? PathState.Ready : PathState.NeedsAction;
    }

    public static PathState TouchState() =>
        TouchService.Instance is not null ? PathState.Ready : PathState.Missing;

    /// <summary>
    /// Ermittelt den Weg für virtuelle Controller. <paramref name="interactive"/> = darf Freigaben anfragen
    /// (Root-/Shizuku-Dialog); im Hintergrund nur bereits erteilte Freigaben nutzen.
    /// </summary>
    public static Task<IPrivilegedShell?> ResolveAsync(bool interactive) =>
        // Nie auf dem Hauptthread: Shizuku-/Root-Aufrufe und das Auslesen der Adresse können dauern (sonst „App reagiert nicht“).
        Task.Run(() => ResolveCoreAsync(interactive));

    private static async Task<IPrivilegedShell?> ResolveCoreAsync(bool interactive)
    {
        IPrivilegedShell? shell = null;
        switch (AppPrefs.Mode)
        {
            case OutputPath.Root:
                shell = await TryAsync(RootShell.Instance, interactive);
                break;
            case OutputPath.Shizuku:
                shell = await TryAsync(ShizukuShell.Instance, interactive);
                break;
            case OutputPath.Auto:
                if (RootShell.Instance.IsAvailable())
                    shell = await TryAsync(RootShell.Instance, interactive);
                if (shell is null && ShizukuShell.Instance.IsAvailable())
                    shell = await TryAsync(ShizukuShell.Instance, interactive);
                break;
            case OutputPath.Touch:
                break;
        }
        Shell = shell;
        if (shell is not null)
            ReadHostAddress(shell);
        Changed?.Invoke();
        return shell;
    }

    private static async Task<IPrivilegedShell?> TryAsync(IPrivilegedShell shell, bool interactive)
    {
        if (shell.HasPermission())
            return shell;
        if (!interactive || !shell.IsAvailable())
            return null;
        return await shell.RequestPermissionAsync() ? shell : null;
    }

    /// <summary>Bluetooth-Adresse dieses Geräts einmal mit Shell-Rechten auslesen (normale Apps dürfen das nicht).</summary>
    private static void ReadHostAddress(IPrivilegedShell shell)
    {
        if (AppPrefs.ParseAddress(AppPrefs.HostAddress) != 0)
            return;
        try
        {
            var p = shell.Start("settings", "get", "secure", "bluetooth_address");
            var read = Task.Run(() => RootShell.ReadAll(p.InputStream!).Trim());
            if (!read.Wait(TimeSpan.FromSeconds(4)))
            {
                p.Destroy();
                Log.Warn("Bluetooth-Adresse: keine Antwort von " + shell.Name);
                return;
            }
            string text = read.Result;
            if (AppPrefs.ParseAddress(text) != 0)
            {
                AppPrefs.HostAddress = text.ToUpperInvariant();
                Log.Info($"Bluetooth-Adresse dieses Geräts: {AppPrefs.HostAddress}");
            }
        }
        catch (Exception e)
        {
            Log.Warn($"Bluetooth-Adresse nicht lesbar: {Log.Reason(e)}");
        }
    }

    /// <summary>Ausgabe für einen Spieler anlegen: virtueller Controller (falls Weg vorhanden) und/oder Touch.</summary>
    public static IVirtualPad? CreatePad(int playerIndex, string name)
    {
        var pads = new List<IVirtualPad>();
        if (Shell is { } shell)
        {
            try
            {
                pads.Add(UhidPad.Create(shell, playerIndex, name));
            }
            catch (Exception e)
            {
                Log.Error($"Virtueller Controller über {shell.Name} nicht möglich", e);
            }
        }
        bool touch = AppPrefs.Mode == OutputPath.Touch || AppPrefs.TouchEnabled;
        if (touch && playerIndex == 0)
            pads.Add(new TouchPad());
        return pads.Count switch
        {
            0 => null,
            1 => pads[0],
            _ => new CompositePad(pads),
        };
    }

    /// <summary>Kurzbeschreibung für die Übersicht.</summary>
    public static string Describe()
    {
        bool touch = AppPrefs.Mode == OutputPath.Touch || AppPrefs.TouchEnabled;
        string pad = Shell is null ? "" : $"Virtueller Xbox-Controller ({Shell.Name})";
        string tap = touch ? (TouchService.Instance is null ? "Touch (Bedienungshilfe aus)" : "Touch-Steuerung") : "";
        string both = string.Join(" + ", new[] { pad, tap }.Where(s => s.Length > 0));
        return both.Length > 0 ? both : "Keine Ausgabe eingerichtet";
    }
}
