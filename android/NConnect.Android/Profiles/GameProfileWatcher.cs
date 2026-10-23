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
public static partial class GameProfileWatcher
{
    /// <summary>„Zugriff auf Nutzungsdaten“ erteilt?</summary>
    public static bool HasAccess(Context ctx)
    {
        var ops = (AppOpsManager?)ctx.GetSystemService(Context.AppOpsService);
        if (ops is null)
            return false;
        // UnsafeCheckOpNoThrow gibt es erst ab API 29 (darunter fehlt die Java-Methode → Absturz), CheckOpNoThrow ist ab 29
        // veraltet. UnsafeCheckOpNoThrow ist seit API 36 ebenfalls als veraltet markiert – ohne Alternative.
#pragma warning disable CA1422
        var mode = OperatingSystem.IsAndroidVersionAtLeast(29)
            ? ops.UnsafeCheckOpNoThrow(AppOpsManager.OpstrGetUsageStats!, global::Android.OS.Process.MyUid(), ctx.PackageName!)
            : ops.CheckOpNoThrow(AppOpsManager.OpstrGetUsageStats!, global::Android.OS.Process.MyUid(), ctx.PackageName!);
#pragma warning restore CA1422
        return mode == AppOpsManagerMode.Allowed;
    }

    /// <summary>Systemliste „Apps mit Zugriff auf Nutzungsdaten“ öffnen (dort SKZ Connect einschalten).</summary>
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
        if (package is null)
            return;
        string? detected = settings.NamedProfiles.FirstOrDefault(p => p.MatchesProgram(package))?.Name;
        if (detected == settings.DetectedProfile)
            return;
        settings.DetectedProfile = detected;
        Log.Info($"Spiel im Vordergrund: {package} → Profil {detected ?? "Standard"}");
        onChanged?.Invoke();
    }

    /// <summary>
    /// Paketname der App, die zuletzt in den Vordergrund kam – ohne SKZ Connect selbst und ohne Startbildschirm
    /// (null ohne Zugriff oder ohne Treffer in den letzten 15 s).
    /// </summary>
    public static string? ForegroundPackage(Context ctx)
    {
        var usage = (UsageStatsManager?)ctx.GetSystemService(Context.UsageStatsService);
        if (usage is null)
            return null;
        long wallNow = Java.Lang.JavaSystem.CurrentTimeMillis();
        using var events = usage.QueryEvents(wallNow - 15000, wallNow);
        if (events is null)
            return null;
        var resumed = new List<(long Time, string Package)>();
        using var ev = new UsageEvents.Event();
        while (events.HasNextEvent)
        {
            events.GetNextEvent(ev);
            // ActivityResumed (= das alte MoveToForeground) – numerisch 1, Enum-Name je nach Bindung anders.
            if ((int)ev.EventType == 1 && ev.PackageName is { } package)
                resumed.Add((ev.TimeStamp, package));
        }
        return Pick(resumed, ctx.PackageName!, Launchers(ctx));
    }

    private static HashSet<string> _launchers = [];
    private static long _launchersUntil;

    /// <summary>
    /// Pakete mit einem Startbildschirm (HOME, auch auf Fernsehern). Der Wartungstakt fragt alle 5 s; die Liste ändert
    /// sich selten, daher höchstens alle 10 min neu lesen.
    /// </summary>
    public static HashSet<string> Launchers(Context ctx)
    {
        lock (LauncherLock)
        {
            long now = System.Environment.TickCount64;
            if (now >= _launchersUntil)
            {
                using var home = new Intent(Intent.ActionMain).AddCategory(Intent.CategoryHome)!;
                _launchers = QueryActivities(ctx, home).ToHashSet(StringComparer.Ordinal);
                _launchersUntil = now + 600_000;
            }
            return _launchers;
        }
    }

    private static readonly object LauncherLock = new();

    /// <summary>Paketnamen aller Activities, die auf <paramref name="intent"/> antworten.</summary>
    internal static List<string> QueryActivities(Context ctx, Intent intent)
    {
        var result = new List<string>();
        try
        {
            var list = OperatingSystem.IsAndroidVersionAtLeast(33)
                ? ctx.PackageManager!.QueryIntentActivities(intent, global::Android.Content.PM.PackageManager.ResolveInfoFlags.Of(0))
#pragma warning disable CA1422 // ab API 33 die Variante mit ResolveInfoFlags (oben)
                : ctx.PackageManager!.QueryIntentActivities(intent, 0);
#pragma warning restore CA1422
            foreach (var info in list)
            {
                using (info)
                {
                    if (info.ActivityInfo?.PackageName is { } package)
                        result.Add(package);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"App-Liste nicht lesbar: {Log.Reason(e)}");
        }
        return result;
    }
}
