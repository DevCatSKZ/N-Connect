using Android.Content;
using Android.OS;
using Android.Provider;
using NConnect.Android.Touch;
using Uri = Android.Net.Uri;

namespace NConnect.Android.UI;

/// <summary>
/// Springt direkt an die passende Stelle der Systemeinstellungen (statt in allgemeine Menüs) – jeweils mit Rückfall,
/// falls ein Hersteller die Seite nicht anbietet.
/// </summary>
public static class SystemPages
{
    /// <summary>Benachrichtigungs-Einstellungen von N-Connect.</summary>
    public static void Notifications(Context ctx) => Open(ctx,
        new Intent(Settings.ActionAppNotificationSettings).PutExtra(Settings.ExtraAppPackage, ctx.PackageName),
        AppDetails(ctx));

    /// <summary>
    /// Direkt die Schalterseite „N-Connect Touch-Steuerung“ in den Bedienungshilfen (ab Android 11),
    /// sonst die Liste der Bedienungshilfen.
    /// </summary>
    public static void TouchService(Context ctx)
    {
        var list = new Intent(Settings.ActionAccessibilitySettings);
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var component = new ComponentName(ctx, Java.Lang.Class.FromType(typeof(TouchService)));
            var details = new Intent("android.settings.ACCESSIBILITY_DETAILS_SETTINGS")
                .PutExtra(Intent.ExtraComponentName, component.FlattenToString());
            Open(ctx, details, list);
        }
        else
        {
            Open(ctx, list, null);
        }
    }

    /// <summary>
    /// Fragt direkt im System-Dialog, ob N-Connect im Hintergrund uneingeschränkt laufen darf (verhindert Abbrüche beim
    /// Spielen). Sonst die Liste der Akku-Optimierungen.
    /// </summary>
    public static void BatteryUnrestricted(Context ctx) => Open(ctx,
        new Intent(Settings.ActionRequestIgnoreBatteryOptimizations, Uri.Parse("package:" + ctx.PackageName)),
        new Intent(Settings.ActionIgnoreBatteryOptimizationSettings));

    public static bool IsBatteryUnrestricted(Context ctx) =>
        ctx.GetSystemService(Context.PowerService) is PowerManager pm && pm.IsIgnoringBatteryOptimizations(ctx.PackageName);

    /// <summary>Entwickleroptionen (für „Kabelloses Debugging“ beim Einrichten von Shizuku).</summary>
    public static void DeveloperOptions(Context ctx) => Open(ctx,
        new Intent(Settings.ActionApplicationDevelopmentSettings),
        new Intent(Settings.ActionDeviceInfoSettings));

    /// <summary>App-Info von N-Connect (dort unter „Berechtigungen“).</summary>
    public static Intent AppDetails(Context ctx) =>
        new(Settings.ActionApplicationDetailsSettings, Uri.FromParts("package", ctx.PackageName, null));

    private static void Open(Context ctx, Intent first, Intent? fallback)
    {
        if (ctx is not global::Android.App.Activity)
        {
            first.AddFlags(ActivityFlags.NewTask);
            fallback?.AddFlags(ActivityFlags.NewTask);
        }
        try
        {
            ctx.StartActivity(first);
        }
        catch (ActivityNotFoundException) when (fallback is not null)
        {
            ctx.StartActivity(fallback);
        }
        catch (Java.Lang.SecurityException) when (fallback is not null)
        {
            ctx.StartActivity(fallback);
        }
    }
}
