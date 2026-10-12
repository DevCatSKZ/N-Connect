using Android.App;
using Android.Content;

namespace NConnect.Android.Core;

/// <summary>
/// „Mit Android starten“: nach dem Einschalten des Geräts (oder nach einem Update der App) den Controller-Dienst
/// wieder hochfahren, damit sich die Controller ohne App-Start verbinden. Läuft nur, wenn es in den Einstellungen
/// eingeschaltet ist (<see cref="AppPrefs.StartOnBoot"/>).
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true, DirectBootAware = true)]
[IntentFilter(["android.intent.action.BOOT_COMPLETED", "android.intent.action.MY_PACKAGE_REPLACED"])]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || !AppPrefs.StartOnBoot || !AppPrefs.SetupDone)
            return;
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
