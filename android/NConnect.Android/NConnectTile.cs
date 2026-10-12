using Android.Content;
using Android.Service.QuickSettings;

namespace NConnect.Android;

/// <summary>
/// Schnelleinstellungen-Kachel (Quick Settings Tile): Controller-Verbindung direkt aus der Benachrichtigungsleiste
/// starten oder beenden – ohne die App öffnen zu müssen. Zeigt an, ob der Dienst läuft.
/// </summary>
[Service(Label = "@string/app_name", Icon = "@mipmap/appicon", Exported = true,
    Permission = "android.permission.BIND_QUICK_SETTINGS_TILE")]
[IntentFilter(["android.service.quicksettings.action.QS_TILE"])]
[MetaData("android.service.quicksettings.ACTIVE_TILE", Value = "true")]
public sealed class NConnectTile : TileService
{
    public override void OnStartListening()
    {
        base.OnStartListening();
        Update();
    }

    public override void OnClick()
    {
        base.OnClick();
        if (Core.ControllerService.Instance is { } service)
        {
            // Laufenden Dienst über seine eigene Stopp-Aktion beenden (Notification entfernen, Suche stoppen).
            StartService(new Intent(this, typeof(Core.ControllerService)).SetAction(Core.ControllerService.ActionStop));
        }
        else
        {
            Core.ControllerService.CreateChannel(this);
            Core.ControllerService.Start(this);
        }
        Update();
    }

    private void Update()
    {
        if (QsTile is { } tile)
        {
            tile.State = Core.ControllerService.Instance is null ? TileState.Inactive : TileState.Active;
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                tile.Subtitle = L.T(Core.ControllerService.Instance is null ? "Aus" : "An – Controller verbinden");
            tile.UpdateTile();
        }
    }
}
