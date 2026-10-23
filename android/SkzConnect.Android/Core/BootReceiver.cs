using Android.App;
using Android.Content;

namespace SkzConnect.Android.Core;

/// <summary>
/// „Mit Android starten“: nach dem Einschalten des Geräts (oder nach einem Update der App) den Controller-Dienst
/// wieder hochfahren, damit sich die Controller ohne App-Start verbinden. Läuft nur, wenn es in den Einstellungen
/// eingeschaltet ist (<see cref="AppPrefs.StartOnBoot"/>) und „Geräte in der Nähe“ erlaubt ist – ohne die Berechtigung
/// würde Android den Dienst beim Start beenden (SecurityException).
/// Nicht exportiert: Die beiden System-Broadcasts kommen trotzdem an, andere Apps können den Dienst darüber aber nicht
/// starten. Nicht DirectBootAware: vor dem ersten Entsperren wären die Einstellungen (AppPrefs) gar nicht lesbar.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter([BootCompleted, PackageReplaced])]
public sealed partial class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || !StartsService(intent?.Action) || !AppPrefs.StartOnBoot || !AppPrefs.SetupDone)
            return;
        if (!UI.Permissions.HasBluetooth(context))
        {
            Log.Warn("Autostart übersprungen: Berechtigung „Geräte in der Nähe“ fehlt (in der App erneut erlauben)");
            return;
        }
        try
        {
            ControllerService.CreateChannel(context);
            ControllerService.Start(context);
            Log.Info("Autostart: Controller-Dienst nach Systemstart hochgefahren");
        }
        catch (Exception e)
        {
            Log.Warn($"Autostart fehlgeschlagen: {Log.Reason(e)}");
        }
    }
}
