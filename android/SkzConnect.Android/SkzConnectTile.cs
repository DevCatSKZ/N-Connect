using Android.App;
using Android.Content;
using Android.Service.QuickSettings;

namespace SkzConnect.Android;

/// <summary>
/// Schnelleinstellungen-Kachel (Quick Settings Tile): Controller-Verbindung direkt aus der Benachrichtigungsleiste
/// starten oder beenden – ohne die App öffnen zu müssen. Zeigt an, ob der Dienst läuft. Bewusst keine „aktive“ Kachel
/// (ACTIVE_TILE): Android fragt den Zustand dann bei jedem Öffnen der Schnelleinstellungen ab und zeigt nie einen
/// veralteten Stand, auch wenn der Dienst ohne OnDestroy endet. Nach dem Antippen steht sofort der Zielzustand da;
/// zusätzlich meldet der Dienst Start und Ende (<see cref="RequestUpdate"/>, auf manchen Android-Versionen nur für
/// aktive Kacheln wirksam). Fehlt noch die Einrichtung oder „Geräte in der Nähe“, öffnet die Kachel die App.
/// </summary>
[Service(Label = "@string/app_name", Icon = "@drawable/ic_stat_controller", Exported = true,
    Permission = "android.permission.BIND_QUICK_SETTINGS_TILE")]
[IntentFilter(["android.service.quicksettings.action.QS_TILE"])]
public sealed class SkzConnectTile : TileService
{
    public override void OnStartListening()
    {
        base.OnStartListening();
        Update(Core.ControllerService.Instance is not null);
    }

    public override void OnClick()
    {
        base.OnClick();
        if (Core.ControllerService.Instance is not null)
        {
            // Laufenden Dienst über seine eigene Stopp-Aktion beenden (Notification entfernen, Suche stoppen).
            StartService(new Intent(this, typeof(Core.ControllerService)).SetAction(Core.ControllerService.ActionStop));
            Update(running: false); // der Dienst endet erst gleich – Zielzustand sofort zeigen
            return;
        }
        if (!AppPrefs.SetupDone || !UI.Permissions.HasBluetooth(this))
        {
            Log.Info("Kachel: Einrichtung oder Berechtigung „Geräte in der Nähe“ fehlt – öffne die App");
            OpenApp();
            return;
        }
        try
        {
            Core.ControllerService.CreateChannel(this);
            Core.ControllerService.Start(this);
            Update(running: true);
        }
        catch (Java.Lang.IllegalStateException e)
        {
            // Android lässt den Vordergrunddienst aus dem Hintergrund nicht starten (ForegroundServiceStartNotAllowed) –
            // die App startet ihn selbst.
            Log.Warn($"Kachel: Dienst ließ sich nicht starten ({Log.Reason(e)}) – öffne die App");
            OpenApp();
        }
    }

    /// <summary>App öffnen und die Schnelleinstellungen schließen (ab Android 14 nur noch über einen PendingIntent).</summary>
    private void OpenApp()
    {
        var intent = new Intent(this, typeof(UI.MainActivity)).AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            StartActivityAndCollapse(PendingIntent.GetActivity(this, 0, intent,
                PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!);
        }
        else
        {
#pragma warning disable CA1422 // ab Android 14 die Variante mit PendingIntent (oben)
            StartActivityAndCollapse(intent);
#pragma warning restore CA1422
        }
    }

    private void Update(bool running)
    {
        if (QsTile is { } tile)
        {
            tile.State = running ? TileState.Active : TileState.Inactive;
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                tile.Subtitle = L.T(running ? "An – Controller verbinden" : "Aus");
            tile.UpdateTile();
        }
    }

    /// <summary>Vom Dienst bei Start und Ende: Android ruft dann <see cref="OnStartListening"/> mit dem neuen Zustand.</summary>
    public static void RequestUpdate(Context context)
    {
        try
        {
            RequestListeningState(context, new ComponentName(context, Java.Lang.Class.FromType(typeof(SkzConnectTile))));
        }
        catch (Exception e)
        {
            // Gerät ohne Schnelleinstellungen o. Ä. – der Stand gilt dann beim nächsten Öffnen.
            Log.Info($"Kachel nicht aktualisiert: {Log.Reason(e)}");
        }
    }
}
