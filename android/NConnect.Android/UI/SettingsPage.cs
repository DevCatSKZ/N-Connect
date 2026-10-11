using Android.Content;
using Android.Text;
using Android.Widget;
using Google.Android.Material.Dialog;
using Google.Android.Material.TextField;
using NConnect.Android.Core;
using NConnect.Android.Output;
using NConnect.Android.Touch;
using Switch2Pro.Protocol;
using View = Android.Views.View;

namespace NConnect.Android.UI;

/// <summary>Einstellungen in Gruppen: Ausgabe, Controller, Bewegungssteuerung, Verbindung, Allgemein.</summary>
public sealed class SettingsPage(MainActivity host) : IPage
{
    public string Title => "Einstellungen";

    private static Settings S => AppPrefs.Settings;

    public View Build()
    {
        var (scroll, col) = Ui.Page(host);

        // ---- Ausgabe ----
        col.AddView(Ui.Section(host, "AUSGABE"));
        col.AddView(Ui.ClickRow(host, "Wie Spiele deine Controller sehen",
            $"{Texts.PathName(AppPrefs.Mode)} – {ControllersPage.OutputStatus().Title}", ChooseOutputPath));
        if (AppPrefs.Mode != OutputPath.Touch)
        {
            col.AddView(Ui.SwitchRow(host, "Touch-Steuerung zusätzlich",
                "Für Spiele ohne Controller-Unterstützung: Tasten tippen zusätzlich auf den Bildschirm.",
                AppPrefs.TouchEnabled, on =>
                {
                    AppPrefs.TouchEnabled = on;
                    if (on && TouchService.Instance is null)
                        AskEnableTouchService();
                    _ = ControllerService.Instance?.ReapplyOutputAsync();
                    host.Rebuild();
                }));
        }
        if (AppPrefs.TouchEnabled || AppPrefs.Mode == OutputPath.Touch)
        {
            col.AddView(Ui.ClickRow(host, "Touch-Layout bearbeiten",
                TouchService.Instance is null ? "Erst die Bedienungshilfe „N-Connect Touch-Steuerung“ einschalten."
                    : "Legt fest, wohin welche Taste tippt. Am besten direkt im Spiel über die Benachrichtigung.",
                () =>
                {
                    if (TouchService.Instance is { } t)
                        t.ShowEditor();
                    else
                        AskEnableTouchService();
                }));
        }

        // ---- Controller ----
        col.AddView(Ui.Section(host, "CONTROLLER"));
        col.AddView(Ui.ClickRow(host, "Tastenbelegung A/B/X/Y",
            S.Layout == FaceButtonLayout.Xbox ? "Nach Position wie am Xbox-Controller (untere Taste = A)"
                : "Nach Beschriftung wie an der Switch (A bleibt A)", ChooseLayout));
        col.AddView(Ui.SwitchRow(host, "Joy-Con zu einem Controller zusammenfassen",
            "Linker und rechter Joy-Con 2 ergeben zusammen einen Spieler – wie an der Switch.",
            S.CombineJoyCons, on => { S.CombineJoyCons = on; AppPrefs.SaveSettings(); }));
        col.AddView(Ui.SwitchRow(host, "Vibration",
            "Spiele dürfen deine Controller vibrieren lassen.", S.RumbleEnabled,
            on => { S.RumbleEnabled = on; AppPrefs.SaveSettings(); }));
        col.AddView(Ui.SliderRow(host, "Vibrationsstärke", v => $"{v * 100:0} %", S.RumbleStrength, 0.1f, 1f, 0.05f,
            v => { S.RumbleStrength = v; AppPrefs.SaveSettings(); }));
        col.AddView(Ui.SliderRow(host, "Stick-Totzone",
            v => v < 0.005f ? "Aus – jede kleinste Bewegung zählt" : $"{v * 100:0} % – kleine Bewegungen um die Mitte werden ignoriert",
            S.StickDeadzone, 0f, 0.3f, 0.01f, v => { S.StickDeadzone = v; AppPrefs.SaveSettings(); }));
        col.AddView(Ui.SliderRow(host, "Trigger-Totzone (ZL/ZR)",
            v => $"{v * 100:0} % – erst ab hier zählt der Trigger als gedrückt",
            S.TriggerDeadzone, 0f, 0.4f, 0.01f, v => { S.TriggerDeadzone = v; AppPrefs.SaveSettings(); }));

        // ---- Bewegungssteuerung ----
        col.AddView(Ui.Section(host, "BEWEGUNGSSTEUERUNG"));
        col.AddView(Ui.ClickRow(host, "Gyro-Zielen", GyroText(S.GyroStick), ChooseGyro));
        col.AddView(Ui.SliderRow(host, "Gyro-Empfindlichkeit",
            v => $"Volle Stickauslenkung bei {v:0} °/s – kleiner = empfindlicher",
            S.GyroStickFullSpeed, 40f, 400f, 10f, v => { S.GyroStickFullSpeed = v; AppPrefs.SaveSettings(); }));
        col.AddView(Ui.SwitchRow(host, "Gyro hoch/runter umkehren", "Für Spiele mit umgekehrter Kamera.",
            S.GyroStickInvertY, on => { S.GyroStickInvertY = on; AppPrefs.SaveSettings(); }));

        // ---- Verbindung ----
        col.AddView(Ui.Section(host, "VERBINDUNG"));
        col.AddView(Ui.SwitchRow(host, "Beim Öffnen automatisch suchen",
            "N-Connect startet die Controller-Suche, sobald du die App öffnest.", AppPrefs.AutoStart,
            on => AppPrefs.AutoStart = on));
        string address = AppPrefs.HostAddress ?? "";
        col.AddView(Ui.ClickRow(host, "Bluetooth-Adresse dieses Geräts",
            address.Length > 0 ? $"{address} – Controller verbinden sich ohne SYNC neu"
                : "Unbekannt – ohne sie muss bei jeder Verbindung SYNC gedrückt werden", EditAddress));

        // ---- Darstellung (wie N-Connect für Windows) ----
        col.AddView(Ui.Section(host, "DARSTELLUNG"));
        col.AddView(Ui.ClickRow(host, "Farbschema",
            $"{Look.Scheme.Name} – dieselben Schemata wie in N-Connect für Windows", ChooseScheme));
        col.AddView(Ui.ClickRow(host, "Hell oder dunkel",
            Look.Modes.First(m => m.Id == Look.Mode).Label, ChooseMode));

        // ---- Allgemein ----
        col.AddView(Ui.Section(host, "ALLGEMEIN"));
        col.AddView(Ui.ClickRow(host, "Einrichtung erneut starten", "Ausgabeweg, Berechtigungen und Shizuku/Root/Touch neu einrichten.",
            () => host.StartActivity(new Intent(host, typeof(SetupWizardActivity)))));
        col.AddView(Ui.ClickRow(host, "Belegung zurücksetzen", "Alle Controller-Einstellungen auf die Standardwerte.", ResetSettings));
        string version = host.PackageManager!.GetPackageInfo(host.PackageName!, 0)!.VersionName ?? "?";
        col.AddView(Ui.ClickRow(host, "Über N-Connect", $"Version {version} · Open Source (MIT) · von devcatskz",
            () => host.StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse("https://github.com/DevCatSKZ/N-Connect")))));
        return scroll;
    }

    public void Refresh()
    {
    }

    private static string GyroText(GyroStickMode m) => m switch
    {
        GyroStickMode.Always => "Immer an – Bewegung des Controllers steuert den rechten Stick",
        GyroStickMode.WhileAiming => "Beim Zielen – solange ZL gedrückt ist (wie in vielen Shootern)",
        _ => "Aus (nur über eine Taste, die du auf „Gyro“ legst)",
    };

    private void ChooseOutputPath()
    {
        OutputPath[] options = [OutputPath.Shizuku, OutputPath.Root, OutputPath.Touch, OutputPath.Auto];
        string[] labels = ["Shizuku (empfohlen, ohne Root)", "Root", "Touch-Steuerung", "Automatisch"];
        int current = Array.IndexOf(options, AppPrefs.Mode);
        new MaterialAlertDialogBuilder(host)
            .SetTitle("Wie sollen Spiele deine Controller sehen?")!
            .SetSingleChoiceItems(labels, current, (s, e) =>
            {
                ((global::Android.App.Dialog)s!).Dismiss();
                var chosen = options[e.Which];
                AppPrefs.Mode = chosen;
                // Neuer Weg braucht evtl. Einrichtung (Shizuku-Freigabe, Root-Dialog, Bedienungshilfe).
                host.StartActivity(new Intent(host, typeof(SetupWizardActivity)).PutExtra(SetupWizardActivity.ExtraStartAtOutput, true));
            })!
            .SetNegativeButton("Abbrechen", (_, _) => { })!
            .Show();
    }

    private void ChooseLayout()
    {
        string[] labels = ["Nach Position (Xbox-Stil, untere Taste = A)", "Nach Beschriftung (wie an der Switch)"];
        new MaterialAlertDialogBuilder(host)
            .SetTitle("Tastenbelegung A/B/X/Y")!
            .SetSingleChoiceItems(labels, S.Layout == FaceButtonLayout.Xbox ? 0 : 1, (s, e) =>
            {
                S.Layout = e.Which == 0 ? FaceButtonLayout.Xbox : FaceButtonLayout.Switch2;
                AppPrefs.SaveSettings();
                ((global::Android.App.Dialog)s!).Dismiss();
                host.Rebuild();
            })!
            .Show();
    }

    private void ChooseGyro()
    {
        GyroStickMode[] modes = [GyroStickMode.Off, GyroStickMode.WhileAiming, GyroStickMode.Always];
        string[] labels = ["Aus", "Beim Zielen (ZL gedrückt)", "Immer"];
        new MaterialAlertDialogBuilder(host)
            .SetTitle("Gyro-Zielen")!
            .SetSingleChoiceItems(labels, Array.IndexOf(modes, S.GyroStick), (s, e) =>
            {
                S.GyroStick = modes[e.Which];
                AppPrefs.SaveSettings();
                ((global::Android.App.Dialog)s!).Dismiss();
                host.Rebuild();
            })!
            .Show();
    }

    private void ChooseScheme()
    {
        var schemes = ColorSchemes.All;
        new MaterialAlertDialogBuilder(host)
            .SetTitle("Farbschema")!
            .SetSingleChoiceItems(schemes.Select(s => s.Name).ToArray(), Array.IndexOf(schemes, Look.Scheme), (s, e) =>
            {
                S.ColorScheme = schemes[e.Which].Id;
                AppPrefs.SaveSettings();
                ((global::Android.App.Dialog)s!).Dismiss();
                host.Recreate(); // ganze Oberfläche im neuen Schema
            })!
            .SetNegativeButton("Abbrechen", (_, _) => { })!
            .Show();
    }

    private void ChooseMode()
    {
        new MaterialAlertDialogBuilder(host)
            .SetTitle("Hell oder dunkel")!
            .SetSingleChoiceItems(Look.Modes.Select(m => m.Label).ToArray(), Array.FindIndex(Look.Modes, m => m.Id == Look.Mode), (s, e) =>
            {
                S.Theme = Look.Modes[e.Which].Id;
                AppPrefs.SaveSettings();
                ((global::Android.App.Dialog)s!).Dismiss();
                Look.ApplyNightMode();
                host.Recreate();
            })!
            .SetNegativeButton("Abbrechen", (_, _) => { })!
            .Show();
    }

    private void EditAddress()
    {
        var layout = new TextInputLayout(host) { Hint = "z. B. 04:C8:B0:F5:B2:27" };
        var input = new TextInputEditText(layout.Context!) { Text = AppPrefs.HostAddress ?? "" };
        input.InputType = InputTypes.ClassText | InputTypes.TextFlagCapCharacters;
        layout.AddView(input);
        var box = Ui.Column(host);
        int pad = Ui.Dp(host, 24);
        box.SetPadding(pad, Ui.Dp(host, 8), pad, 0);
        box.AddView(Ui.Caption(host, "Mit Shizuku oder Root liest N-Connect die Adresse selbst aus. Sonst findest du sie unter " +
                                     "Einstellungen → Über das Telefon → Bluetooth-Adresse."));
        box.AddView(layout);
        new MaterialAlertDialogBuilder(host)
            .SetTitle("Bluetooth-Adresse dieses Geräts")!
            .SetView(box)!
            .SetPositiveButton("Speichern", (_, _) =>
            {
                string text = input.Text?.Trim() ?? "";
                if (text.Length == 0)
                    AppPrefs.HostAddress = null;
                else if (AppPrefs.ParseAddress(text) != 0)
                    AppPrefs.HostAddress = text.ToUpperInvariant();
                else
                    Ui.Toast(host, "Ungültige Adresse – Format: 12 Hex-Zeichen, z. B. 04:C8:B0:F5:B2:27");
                host.Rebuild();
            })!
            .SetNegativeButton("Abbrechen", (_, _) => { })!
            .Show();
    }

    private void ResetSettings()
    {
        new MaterialAlertDialogBuilder(host)
            .SetTitle("Belegung zurücksetzen?")!
            .SetMessage("Alle Controller-Einstellungen (Tasten, Sticks, Gyro, Vibration) werden auf die Standardwerte gesetzt. " +
                        "Kopplungen und Ausgabeweg bleiben erhalten.")!
            .SetPositiveButton("Zurücksetzen", (_, _) =>
            {
                S.CopyFrom(new Settings());
                AppPrefs.SaveSettings();
                host.Rebuild();
                Ui.Toast(host, "Standardwerte wiederhergestellt");
            })!
            .SetNegativeButton("Abbrechen", (_, _) => { })!
            .Show();
    }

    private void AskEnableTouchService()
    {
        new MaterialAlertDialogBuilder(host)
            .SetTitle("Touch-Steuerung einschalten")!
            .SetMessage("Tippe in der folgenden Liste auf „N-Connect Touch-Steuerung“ (ggf. unter „Installierte Apps“) " +
                        "und schalte sie ein. N-Connect führt damit nur Berührungen aus und liest keine Inhalte.")!
            .SetPositiveButton("Zu den Bedienungshilfen", (_, _) => TouchService.OpenSystemSettings(host))!
            .SetNegativeButton("Später", (_, _) => { })!
            .Show();
    }
}
