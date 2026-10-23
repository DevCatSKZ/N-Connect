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
    public string Title => L.T("Einstellungen");

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
                TouchService.Instance is null ? "Erst die Bedienungshilfe „SKZ Connect Touch-Steuerung“ einschalten."
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
            v => v < 0.005f ? L.T("Aus – jede kleinste Bewegung zählt") : L.F("{0} % – kleine Bewegungen um die Mitte werden ignoriert", (v * 100).ToString("0")),
            S.StickDeadzone, 0f, 0.3f, 0.01f, v => { S.StickDeadzone = v; AppPrefs.SaveSettings(); }));
        col.AddView(Ui.SliderRow(host, "Trigger-Totzone (ZL/ZR)",
            v => L.F("{0} % – erst ab hier zählt der Trigger als gedrückt", (v * 100).ToString("0")),
            S.TriggerDeadzone, 0f, 0.4f, 0.01f, v => { S.TriggerDeadzone = v; AppPrefs.SaveSettings(); }));

        // ---- Profile ----
        col.AddView(Ui.Section(host, "PROFILE"));
        col.AddView(Ui.ClickRow(host, "Aktives Profil",
            S.ForcedProfile is { Length: > 0 } forced ? L.F("„{0}“ (fest gewählt)", forced)
                : S.ForcedProfile == "" ? L.T("Standard (fest gewählt)")
                : S.DetectedProfile is { } detected ? L.F("„{0}“ – automatisch zum laufenden Spiel", detected)
                : L.T("Automatisch – wechselt mit dem Spiel im Vordergrund"), ChooseProfile));
        col.AddView(Ui.ClickRow(host, "Profile je Spiel verwalten",
            S.NamedProfiles.Count == 0 ? "Eigene Tastenbelegungen, die automatisch zum laufenden Spiel passen."
                : L.F("{0} Profil(e) – je Spiel eigene Belegung", S.NamedProfiles.Count), ManageProfiles));

        // ---- Bewegungssteuerung ----
        col.AddView(Ui.Section(host, "BEWEGUNGSSTEUERUNG"));
        col.AddView(Ui.ClickRow(host, "Gyro-Zielen", GyroText(S.GyroStick), ChooseGyro));
        col.AddView(Ui.SliderRow(host, "Gyro-Empfindlichkeit",
            v => L.F("Volle Stickauslenkung bei {0} °/s – kleiner = empfindlicher", v.ToString("0")),
            S.GyroStickFullSpeed, 40f, 400f, 10f, v => { S.GyroStickFullSpeed = v; AppPrefs.SaveSettings(); }));
        col.AddView(Ui.SwitchRow(host, "Gyro hoch/runter umkehren", "Für Spiele mit umgekehrter Kamera.",
            S.GyroStickInvertY, on => { S.GyroStickInvertY = on; AppPrefs.SaveSettings(); }));
        col.AddView(Ui.SwitchRow(host, "Gyro an Emulatoren senden (DSU)",
            "Für Emulatoren mit DSU-/Cemuhook-Client auf diesem Gerät: dort Server 127.0.0.1, Port 26760 eintragen (nur lokal erreichbar).",
            AppPrefs.DsuEnabled, on =>
            {
                AppPrefs.DsuEnabled = on;
                if (on)
                    DsuServer.Start(network: false, Log.Info, Log.Warn);
                else
                    DsuServer.Instance?.Dispose();
            }));

        // ---- Verbindung ----
        col.AddView(Ui.Section(host, "VERBINDUNG"));
        col.AddView(Ui.SwitchRow(host, "Beim Öffnen automatisch suchen",
            "SKZ Connect startet die Controller-Suche, sobald du die App öffnest.", AppPrefs.AutoStart,
            on => AppPrefs.AutoStart = on));
        col.AddView(Ui.SwitchRow(host, "Mit Android starten",
            "Nach dem Einschalten des Geräts verbinden sich deine Controller von selbst – ohne die App zu öffnen.",
            AppPrefs.StartOnBoot, on => AppPrefs.StartOnBoot = on));
        string address = AppPrefs.HostAddress ?? "";
        col.AddView(Ui.ClickRow(host, "Bluetooth-Adresse dieses Geräts",
            address.Length > 0 ? L.F("{0} – Controller verbinden sich ohne SYNC neu", address)
                : "Unbekannt – ohne sie muss bei jeder Verbindung SYNC gedrückt werden", EditAddress));

        // ---- Darstellung (wie SKZ Connect für Windows) ----
        col.AddView(Ui.Section(host, "DARSTELLUNG"));
        col.AddView(Ui.ClickRow(host, "Farbschema",
            L.F("{0} – dieselben Schemata wie in SKZ Connect für Windows", Look.Scheme.Name), ChooseScheme));
        col.AddView(Ui.ClickRow(host, "Hell oder dunkel",
            Look.Modes.First(m => m.Id == Look.Mode).Label, ChooseMode));

        // ---- Allgemein ----
        col.AddView(Ui.Section(host, "ALLGEMEIN"));
        col.AddView(Ui.ClickRow(host, "Einrichtung erneut starten", "Ausgabeweg, Berechtigungen und Shizuku/Root/Touch neu einrichten.",
            () => host.StartActivity(new Intent(host, typeof(SetupWizardActivity)))));
        col.AddView(Ui.ClickRow(host, "Belegung zurücksetzen", "Alle Controller-Einstellungen auf die Standardwerte.", ResetSettings));
        col.AddView(Ui.ClickRow(host, "Nach Updates suchen", "Prüft auf GitHub, ob es eine neuere Version gibt.", CheckUpdate));
        string version = host.PackageManager!.GetPackageInfo(host.PackageName!, 0)!.VersionName ?? "?";
        col.AddView(Ui.ClickRow(host, "Über SKZ Connect", L.F("Version {0} · Open Source (MIT) · von devcatskz", version),
            () => host.StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse("https://github.com/DevCatSKZ/SKZ-Connect")))));
        return scroll;
    }

    /// <summary>Geladene Update-APK, die auf die Freigabe „Unbekannte Apps installieren“ wartet.</summary>
    private Java.IO.File? _pendingApk;

    public void Refresh()
    {
        // Zurück aus der Systemseite „Unbekannte Apps installieren“: sobald erlaubt, die geladene APK einspielen.
        if (_pendingApk is { } apk && UpdateCheck.CanInstall(host))
        {
            _pendingApk = null;
            UpdateCheck.Install(host, apk);
        }
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
        string[] labels = [L.T("Shizuku (empfohlen, ohne Root)"), "Root", L.T("Touch-Steuerung"), L.T("Automatisch")];
        int current = Array.IndexOf(options, AppPrefs.Mode);
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Wie sollen Spiele deine Controller sehen?"))!
            .SetSingleChoiceItems(labels, current, (s, e) =>
            {
                ((global::Android.App.Dialog)s!).Dismiss();
                var chosen = options[e.Which];
                AppPrefs.Mode = chosen;
                // Neuer Weg braucht evtl. Einrichtung (Shizuku-Freigabe, Root-Dialog, Bedienungshilfe).
                host.StartActivity(new Intent(host, typeof(SetupWizardActivity)).PutExtra(SetupWizardActivity.ExtraStartAtOutput, true));
            })!
            .SetNegativeButton(L.T("Abbrechen"), (_, _) => { })!
            .Show();
    }

    private void ChooseLayout()
    {
        string[] labels = [L.T("Nach Position (Xbox-Stil, untere Taste = A)"), L.T("Nach Beschriftung (wie an der Switch)")];
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Tastenbelegung A/B/X/Y"))!
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
        string[] labels = [L.T("Aus"), L.T("Beim Zielen (ZL gedrückt)"), L.T("Immer")];
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Gyro-Zielen"))!
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
            .SetTitle(L.T("Farbschema"))!
            .SetSingleChoiceItems(schemes.Select(s => s.Name).ToArray(), Array.IndexOf(schemes, Look.Scheme), (s, e) =>
            {
                S.ColorScheme = schemes[e.Which].Id;
                AppPrefs.SaveSettings();
                ((global::Android.App.Dialog)s!).Dismiss();
                host.Recreate(); // ganze Oberfläche im neuen Schema
            })!
            .SetNegativeButton(L.T("Abbrechen"), (_, _) => { })!
            .Show();
    }

    private void ChooseMode()
    {
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Hell oder dunkel"))!
            .SetSingleChoiceItems(Look.Modes.Select(m => m.Label).ToArray(), Array.FindIndex(Look.Modes, m => m.Id == Look.Mode), (s, e) =>
            {
                S.Theme = Look.Modes[e.Which].Id;
                AppPrefs.SaveSettings();
                ((global::Android.App.Dialog)s!).Dismiss();
                Look.ApplyNightMode();
                host.Recreate();
            })!
            .SetNegativeButton(L.T("Abbrechen"), (_, _) => { })!
            .Show();
    }

    private void ChooseProfile()
    {
        // Auswahl wie unter Windows: automatisch (nach Spiel), Standard fest, oder ein benanntes Profil fest.
        var names = new List<string> { L.T("Automatisch – nach dem Spiel im Vordergrund"), L.T("Standard") };
        names.AddRange(S.NamedProfiles.Select(p => p.Name));
        int current = S.ForcedProfile is null ? 0
            : S.ForcedProfile == "" ? 1
            : Math.Max(2, names.FindIndex(n => n == S.ForcedProfile));
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Aktives Profil"))!
            .SetSingleChoiceItems([.. names], current, (s, e) =>
            {
                S.ForcedProfile = e.Which == 0 ? null : e.Which == 1 ? "" : names[e.Which];
                AppPrefs.SaveSettings();
                ((global::Android.App.Dialog)s!).Dismiss();
                host.Rebuild();
            })!
            .SetNegativeButton(L.T("Abbrechen"), (_, _) => { })!
            .Show();
    }

    private void ManageProfiles()
    {
        if (!Profiles.GameProfileWatcher.HasAccess(host))
        {
            // Ohne „Zugriff auf Nutzungsdaten“ kann SKZ Connect nicht sehen, welches Spiel läuft.
            new MaterialAlertDialogBuilder(host)
                .SetTitle(L.T("Profile je Spiel"))!
                .SetMessage(L.T("Damit SKZ Connect erkennt, welches Spiel gerade läuft, erlaube einmal „Zugriff auf " +
                            "Nutzungsdaten“. Die App liest damit nur den Namen der App im Vordergrund – keine Inhalte."))!
                .SetPositiveButton(L.T("Einstellungen öffnen"), (_, _) => Profiles.GameProfileWatcher.OpenAccessSettings(host))!
                .SetNegativeButton(L.T("Später"), (_, _) => { })!
                .Show();
            return;
        }
        new ProfileDialogs(host).Manage();
    }

    private void CheckUpdate()
    {
        Ui.Toast(host, L.T("Suche nach einer neuen Version …"));
        _ = Task.Run(async () =>
        {
            // FindAsync wirft nicht: Netz- und API-Fehler (offline, Rate-Limit 403, 5xx) kommen als Failed zurück – die
            // dürfen nicht als „auf dem neuesten Stand“ erscheinen.
            var result = await UpdateCheck.FindAsync(host, CancellationToken.None);
            host.RunOnUiThread(() =>
            {
                if (host.IsFinishing || host.IsDestroyed)
                    return; // App inzwischen verlassen – ein Dialog auf einer beendeten Activity würde abstürzen
                if (result is { Status: UpdateCheck.CheckStatus.Available, Update: { } update })
                    OfferUpdate(update);
                else if (result.Status == UpdateCheck.CheckStatus.UpToDate)
                    Ui.Toast(host, L.T("SKZ Connect ist auf dem neuesten Stand"));
                else
                    Ui.Toast(host, L.T("Update-Prüfung fehlgeschlagen – bitte später erneut versuchen"));
            });
        });
    }

    private void OfferUpdate(UpdateCheck.Update update)
    {
        var apk = update.File;
        string size = apk.Size > 0 ? L.F(" · {0} MB", (apk.Size / 1048576.0).ToString("0.#")) : "";
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.F("Version {0} verfügbar", update.Version))!
            .SetMessage(L.F("Auf diesem Gerät läuft {0}.{1}\n\nHerunterladen und installieren?",
                UpdateCheck.Current(host), size))!
            .SetPositiveButton(L.T("Herunterladen"), (_, _) => _ = DownloadUpdateAsync(apk))!
            .SetNegativeButton(L.T("Später"), (_, _) => { })!
            .Show();
    }

    private async Task DownloadUpdateAsync(UpdateCheck.Apk apk)
    {
        // Fortschrittsbalken im Dialog (ProgressDialog ist veraltet).
        var bar = new ProgressBar(host, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal)
        {
            Max = 100,
            Indeterminate = false,
        };
        var box = Ui.Column(host);
        int pad = Ui.Dp(host, 24);
        box.SetPadding(pad, Ui.Dp(host, 16), pad, 0);
        box.AddView(bar);
        var dialog = new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Lade Update …"))!
            .SetView(box)!
            .SetCancelable(false)!
            .Create()!;
        dialog.Show();
        Java.IO.File? file = null;
        try
        {
            var reporter = new Progress<int>(p => bar.Progress = p);
            file = await UpdateCheck.DownloadAsync(host, apk, reporter, CancellationToken.None);
        }
        catch (Exception e)
        {
            // DownloadAsync fängt Netz- und Dateifehler selbst – das hier nur, damit nie etwas still hängen bleibt.
            Log.Error("Update herunterladen", e);
        }
        finally
        {
            // Der Dialog lässt sich nicht wegtippen: in jedem Fall schließen.
            if (!host.IsDestroyed)
                dialog.Dismiss();
        }
        if (host.IsFinishing || host.IsDestroyed)
            return;
        if (file is null)
        {
            Ui.Toast(host, L.T("Download fehlgeschlagen – bitte später erneut versuchen"));
            return;
        }
        if (UpdateCheck.CanInstall(host))
        {
            UpdateCheck.Install(host, file);
            return;
        }
        // Erst jetzt, wo wirklich installiert wird, nach der Freigabe fragen. Danach spielt Refresh die Datei ein.
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Installieren erlauben?"))!
            .SetMessage(L.T("Android fragt einmal, ob SKZ Connect Aktualisierungen einspielen darf."))!
            .SetPositiveButton(L.T("Einstellungen öffnen"), (_, _) =>
            {
                _pendingApk = file;
                UpdateCheck.OpenInstallPermission(host);
            })!
            .SetNegativeButton(L.T("Später"), (_, _) => { })!
            .Show();
    }

    private void EditAddress()
    {
        var layout = new TextInputLayout(host) { Hint = L.T("z. B. 04:C8:B0:F5:B2:27") };
        var input = new TextInputEditText(layout.Context!) { Text = AppPrefs.HostAddress ?? "" };
        input.InputType = InputTypes.ClassText | InputTypes.TextFlagCapCharacters;
        layout.AddView(input);
        var box = Ui.Column(host);
        int pad = Ui.Dp(host, 24);
        box.SetPadding(pad, Ui.Dp(host, 8), pad, 0);
        box.AddView(Ui.Caption(host, "Mit Shizuku oder Root liest SKZ Connect die Adresse selbst aus. Sonst findest du sie unter " +
                                     "Einstellungen → Über das Telefon → Bluetooth-Adresse."));
        box.AddView(layout);
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Bluetooth-Adresse dieses Geräts"))!
            .SetView(box)!
            .SetPositiveButton(L.T("Speichern"), (_, _) =>
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
            .SetNegativeButton(L.T("Abbrechen"), (_, _) => { })!
            .Show();
    }

    private void ResetSettings()
    {
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Belegung zurücksetzen?"))!
            .SetMessage(L.T("Alle Controller-Einstellungen (Tasten, Sticks, Gyro, Vibration) werden auf die Standardwerte gesetzt. " +
                        "Kopplungen, Ausgabeweg, Profile je Spiel und Farbschema bleiben erhalten."))!
            .SetPositiveButton(L.T("Zurücksetzen"), (_, _) =>
            {
                // Profile je Spiel und Aussehen sind keine Controller-Einstellungen – sie bleiben.
                var keep = (S.NamedProfiles, S.ColorScheme, S.Theme);
                S.CopyFrom(new Settings());
                (S.NamedProfiles, S.ColorScheme, S.Theme) = keep;
                AppPrefs.SaveSettings();
                host.Rebuild();
                Ui.Toast(host, "Standardwerte wiederhergestellt");
            })!
            .SetNegativeButton(L.T("Abbrechen"), (_, _) => { })!
            .Show();
    }

    private void AskEnableTouchService()
    {
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Touch-Steuerung einschalten"))!
            .SetMessage(L.T("Tippe in der folgenden Liste auf „SKZ Connect Touch-Steuerung“ (ggf. unter „Installierte Apps“) " +
                        "und schalte sie ein. SKZ Connect führt damit nur Berührungen aus und liest keine Inhalte."))!
            .SetPositiveButton(L.T("Zu den Bedienungshilfen"), (_, _) => TouchService.OpenSystemSettings(host))!
            .SetNegativeButton(L.T("Später"), (_, _) => { })!
            .Show();
    }
}
