using System.IO.Compression;
using System.Text;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// „Diagnose exportieren“: Protokoll, Einstellungen und Systeminfos als ZIP – zum Anhängen an eine Fehlermeldung.
/// Kopplungsschlüssel speichert N-Connect nicht in den Einstellungen; es landen also keine Geheimnisse in der Datei.
/// </summary>
internal static class Diagnostics
{
    /// <summary>ZIP schreiben; <paramref name="manager"/> liefert die verbundenen Controller (darf null sein).</summary>
    public static void Export(string zipPath, ControllerManager? manager)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        AddFile(zip, Paths.LogFile, "bridge.log");
        AddFile(zip, Paths.LogFile + ".1", "bridge.log.1");
        AddFile(zip, Paths.SettingsFile, "settings.json");
        var entry = zip.CreateEntry("system.txt");
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(SystemInfo(manager));
    }

    /// <summary>Datei kopieren, auch wenn N-Connect sie gerade offen hat (Protokoll).</summary>
    private static void AddFile(ZipArchive zip, string path, string name)
    {
        if (!File.Exists(path))
            return;
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            source.CopyTo(target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Diagnose: {name} nicht lesbar ({Log.Reason(e)})");
        }
    }

    private static string SystemInfo(ControllerManager? manager)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"N-Connect {UpdateCheck.Current} ({(Paths.IsPortable ? "portabel" : "installiert")})");
        sb.AppendLine($"Windows {Environment.OSVersion.Version} · {(Environment.Is64BitOperatingSystem ? "64 Bit" : "32 Bit")}");
        sb.AppendLine($"Sprache {Tr.Lang} · Windows-Sprache {System.Globalization.CultureInfo.CurrentUICulture.Name}");
        sb.AppendLine($"Skalierung {UiScale.Factor * 100:0} % · Darstellung {(Theme.Dark ? "dunkel" : "hell")}, Schema {Theme.ActiveScheme.Name}");
        sb.AppendLine($"ViGEmBus: {(manager is null ? "fehlt oder nicht erreichbar" : "bereit")} · HidHide: {(HidHide.IsInstalled ? "installiert" : "fehlt")}");
        if (manager is not null)
        {
            sb.AppendLine($"Bluetooth: {manager.AdapterProblem ?? "bereit"}");
            sb.AppendLine();
            sb.AppendLine("Verbundene Controller:");
            foreach (var player in manager.Players)
                foreach (var link in player.Links)
                    sb.AppendLine($"  Spieler {player.Index + 1}: {link.Kind.DisplayName()} · {link.Transport} · {link.ReportRate:F0} Berichte/s" +
                                  $" · Firmware {link.Info.Firmware ?? "?"} · Akku {link.LastState?.BatteryPercent ?? -1} %");
            if (manager.Players.Count == 0)
                sb.AppendLine("  keine");
        }
        sb.AppendLine();
        sb.AppendLine($"Erstellt {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return sb.ToString();
    }
}
