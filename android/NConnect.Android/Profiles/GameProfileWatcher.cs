using Android.App;
using Android.App.Usage;
using Android.Content;
using Android.Provider;

namespace NConnect.Android.Profiles;

/// <summary>
/// Erkennt das gerade laufende Spiel und aktiviert das zugehörige Profil (<see cref="Switch2Pro.Protocol.Settings.NamedProfiles"/>,
/// dort stehen Paketnamen wie „com.mojang.minecraftpe“ in <c>Programs</c>). Portierung der Windows-Erkennung
/// (TrayApp.DetectProfile): Statt der EXE wird per UsageStats die App im Vordergrund gelesen – dafür muss der
/// Nutzer einmal „Zugriff auf Nutzungsdaten“ erlauben (<see cref="OpenAccessSettings"/>).
/// </summary>
public static class GameProfileWatcher
{
    /// <summary>„Zugriff auf Nutzungsdaten“ erteilt?</summary>
    public static bool HasAccess(Context ctx)
    {
        var ops = (AppOpsManager?)ctx.GetSystemService(Context.AppOpsService);
        if (ops is null)
            return false;
#pragma warning disable CA1416, CA1422 // UnsafeCheckOpNoThrow: ab API 29, seit 36 als veraltet markiert – keine Alternative
        var mode = ops.UnsafeCheckOpNoThrow(AppOpsManager.OpstrGetUsageStats!,
            global::Android.OS.Process.MyUid(), ctx.PackageName!);
#pragma warning restore CA1416, CA1422
        return mode == AppOpsManagerMode.Allowed;
    }

    /// <summary>Systemliste „Apps mit Zugriff auf Nutzungsdaten“ öffnen (dort N-Connect einschalten).</summary>
    public static void OpenAccessSettings(Context ctx)
    {
        var intent = new Intent(Settings.ActionUsageAccessSettings).AddFlags(ActivityFlags.NewTask);
        ctx.StartActivity(intent);
    }

    /// <summary>
    /// Regelmäßig vom ControllerService gerufen: App im Vordergrund bestimmen und <c>DetectedProfile</c> setzen.
    /// Eigene App und Launcher ignorieren – sonst springt das Profil bei jeder Bedienung um.
    /// </summary>
    public static void Poll(Context ctx, Action? onChanged = null)
    {
        var settings = AppPrefs.Settings;
        if (settings.NamedProfiles.Count == 0 || !HasAccess(ctx))
            return;
        string? package = ForegroundPackage(ctx);
        if (package is null || package == ctx.PackageName)
            return;
        string? detected = settings.NamedProfiles.FirstOrDefault(p => p.MatchesProgram(package))?.Name;
        if (detected == settings.DetectedProfile)
            return;
        settings.DetectedProfile = detected;
        Log.Info($"Spiel im Vordergrund: {package} → Profil {detected ?? "Standard"}");
        onChanged?.Invoke();
    }

    /// <summary>Paketname der App, die zuletzt in den Vordergrund kam (null ohne Zugriff oder ohne Treffer).</summary>
    public static string? ForegroundPackage(Context ctx)
    {
        var usage = (UsageStatsManager?)ctx.GetSystemService(Context.UsageStatsService);
        if (usage is null)
            return null;
        long now = System.Environment.TickCount64;
        long wallNow = Java.Lang.JavaSystem.CurrentTimeMillis();
        using var events = usage.QueryEvents(wallNow - 15000, wallNow);
        if (events is null)
            return null;
        using var ev = new UsageEvents.Event();
        string? foreground = null;
        long best = 0;
        while (events.HasNextEvent)
        {
            events.GetNextEvent(ev);
            // ActivityResumed (= das alte MoveToForeground) – numerisch 1, Enum-Name je nach Bindung anders.
            if ((int)ev.EventType == 1 && ev.TimeStamp >= best)
            {
                best = ev.TimeStamp;
                foreground = ev.PackageName;
            }
        }
        return foreground;
    }
}
