using Android;
using Android.Content;
using Android.Content.PM;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace SkzConnect.Android.UI;

/// <summary>
/// Laufzeit-Berechtigungen. Ab Android 12 heißt Bluetooth „Geräte in der Nähe“ (ohne Standort), davor braucht die
/// Bluetooth-Suche den Standort. Benachrichtigungen (ab Android 13) sind für die Statusanzeige beim Spielen nötig.
/// </summary>
public static class Permissions
{
    public const int RequestCode = 100;

    public static string[] BluetoothPermissions => OperatingSystem.IsAndroidVersionAtLeast(31)
        ? [Manifest.Permission.BluetoothScan, Manifest.Permission.BluetoothConnect]
        : [Manifest.Permission.AccessFineLocation];

    public static bool HasBluetooth(Context ctx) => BluetoothPermissions.All(p => Granted(ctx, p));

    public static bool HasNotifications(Context ctx) =>
        !OperatingSystem.IsAndroidVersionAtLeast(33) || Granted(ctx, Manifest.Permission.PostNotifications);

    public static void Request(Activity activity)
    {
        var all = BluetoothPermissions.ToList();
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            all.Add(Manifest.Permission.PostNotifications);
        AskedBefore = true;
        ActivityCompat.RequestPermissions(activity, [.. all], RequestCode);
    }

    /// <summary>Nur „Geräte in der Nähe“ anfragen.</summary>
    public static void RequestBluetooth(Activity activity)
    {
        AskedBefore = true;
        ActivityCompat.RequestPermissions(activity, BluetoothPermissions, RequestCode);
    }

    /// <summary>Nur Benachrichtigungen anfragen; wurden sie schon abgelehnt, direkt zu deren Einstellungsseite.</summary>
    public static void RequestNotifications(Activity activity)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return;
        bool askedNotify = Prefs.GetBoolean("asked_notify_permission", false);
        if (askedNotify && !ActivityCompat.ShouldShowRequestPermissionRationale(activity, Manifest.Permission.PostNotifications))
        {
            SystemPages.Notifications(activity);
            return;
        }
        Prefs.Edit()!.PutBoolean("asked_notify_permission", true)!.Apply();
        ActivityCompat.RequestPermissions(activity, [Manifest.Permission.PostNotifications], RequestCode);
    }

    private static ISharedPreferences Prefs =>
        global::Android.App.Application.Context.GetSharedPreferences("skzconnect", FileCreationMode.Private)!;

    /// <summary>
    /// Hat der Nutzer „Nicht mehr fragen“ gewählt? Dann hilft nur noch die App-Info. Android meldet vor der ERSTEN
    /// Anfrage dasselbe (keine Begründung nötig) – deshalb zählt das erst, nachdem wir schon einmal gefragt haben.
    /// </summary>
    public static bool PermanentlyDenied(Activity activity) =>
        AskedBefore && BluetoothPermissions.Any(p => !Granted(activity, p) && !ActivityCompat.ShouldShowRequestPermissionRationale(activity, p));

    private static bool AskedBefore
    {
        get => global::Android.App.Application.Context.GetSharedPreferences("skzconnect", FileCreationMode.Private)!
            .GetBoolean("asked_bt_permission", false);
        set => global::Android.App.Application.Context.GetSharedPreferences("skzconnect", FileCreationMode.Private)!
            .Edit()!.PutBoolean("asked_bt_permission", value)!.Apply();
    }

    public static void OpenAppSettings(Context ctx)
    {
        var intent = new Intent(global::Android.Provider.Settings.ActionApplicationDetailsSettings,
            global::Android.Net.Uri.FromParts("package", ctx.PackageName, null));
        intent.AddFlags(ActivityFlags.NewTask);
        ctx.StartActivity(intent);
    }

    private static bool Granted(Context ctx, string permission) =>
        ContextCompat.CheckSelfPermission(ctx, permission) == Permission.Granted;
}
