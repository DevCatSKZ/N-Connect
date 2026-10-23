using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Google.Android.Material.Dialog;
using NConnect.Android.Kodi;
using NConnect.Android.Output;
using NConnect.Android.Touch;
using View = Android.Views.View;

namespace NConnect.Android.UI;

/// <summary>Anleitungen zum Aufklappen – in Alltagssprache, mit Knöpfen direkt zum richtigen Ort.</summary>
public sealed class HelpPage(MainActivity host) : IPage
{
    public string Title => L.T("Hilfe");

    public View Build()
    {
        var (scroll, col) = Ui.Page(host);

        col.AddView(Topic("So funktioniert SKZ Connect",
            "SKZ Connect verbindet deine <b>Switch-2-Controller</b> (Pro Controller 2, Joy-Con 2, GameCube-Controller) " +
            "per Bluetooth mit deinem Handy oder Tablet und gibt sie an Spiele weiter.<br><br>" +
            "Android kann diese Controller nicht von selbst nutzen – SKZ Connect übersetzt sie in einen ganz normalen " +
            "<b>Xbox-Controller</b>, den praktisch jedes Spiel und jeder Emulator versteht. Für Spiele ohne " +
            "Controller-Unterstützung gibt es zusätzlich die <b>Touch-Steuerung</b>.<br><br>" +
            "<b>Andere Controller:</b> SKZ Connect für Android unterstützt nur Switch-2-Controller (über Bluetooth LE). " +
            "<b>Xbox- und PlayStation-Controller</b> laufen unter Android direkt – einfach in den Bluetooth-Einstellungen " +
            "von Android koppeln. <b>Ältere Switch-Controller</b> (Pro Controller der Switch 1, Joy-Con der ersten Generation, " +
            "Nachbauten) verbindest du dort ebenfalls direkt mit Android – SKZ Connect erkennt sie nicht. Dann gehen in der " +
            "Regel nur Tasten und Sticks; Bewegungssteuerung und Vibration je nach Gerät nicht."));

        col.AddView(Topic("Controller verbinden",
            "<b>Beim ersten Mal:</b> Halte die kleine runde <b>SYNC-Taste</b> am Controller gedrückt, bis die Lichter " +
            "hin- und herlaufen. SKZ Connect findet ihn automatisch – du musst nichts antippen.<br><br>" +
            "<b>Danach:</b> Einfach eine Taste drücken – der Controller verbindet sich von selbst wieder.<br><br>" +
            "<b>Joy-Con:</b> Verbinde beide nacheinander. Sie werden automatisch zu einem Controller zusammengefasst " +
            "(abschaltbar in den Einstellungen).<br><br>" +
            "<b>Wichtig:</b> Ein Controller merkt sich immer nur <b>ein</b> Gerät. Willst du ihn wieder an der Switch 2 " +
            "nutzen, drücke dort erneut SYNC – danach am Handy ebenfalls."));

        var shizuku = Topic("Shizuku einrichten (ohne Root)",
            "Shizuku gibt SKZ Connect die Rechte, einen virtuellen Xbox-Controller anzulegen – ganz ohne Root.<br><br>" +
            "<b>1.</b> Installiere <b>Shizuku</b> aus dem Play Store.<br>" +
            "<b>2.</b> Aktiviere die <b>Entwickleroptionen</b>: Einstellungen → Über das Telefon → 7× auf <i>Build-Nummer</i> tippen.<br>" +
            "<b>3.</b> Verbinde dich mit einem <b>WLAN</b> und schalte in den Entwickleroptionen <b>Kabelloses Debugging</b> ein.<br>" +
            "<b>4.</b> Öffne Shizuku und tippe bei „Über kabelloses Debugging starten“ auf <b>Kopplung</b>. Folge der " +
            "Anleitung (Kopplungscode in den Benachrichtigungen eingeben), danach auf <b>Starten</b>.<br>" +
            "<b>5.</b> Zurück in SKZ Connect: Einrichtung öffnen und <b>Zugriff erlauben</b>.<br><br>" +
            "<b>Nach jedem Neustart</b> des Handys musst du in Shizuku einmal auf „Starten“ tippen (WLAN nötig). " +
            "Die Kopplung bleibt erhalten.",
            Ui.Buttons(host,
                Ui.Button(host, ShizukuShell.IsInstalled() ? "Shizuku öffnen" : "Shizuku installieren", OpenShizuku),
                Ui.Button(host, "Entwickleroptionen", () => SystemPages.DeveloperOptions(host), Ui.ButtonStyle.Outlined),
                Ui.Button(host, "Einrichtung", () => OpenWizard(), Ui.ButtonStyle.Text)));
        col.AddView(shizuku);

        col.AddView(Topic("Mit Root nutzen",
            "Ist dein Gerät gerootet (z. B. Magisk, KernelSU oder APatch), wähle in der Einrichtung <b>Root</b> und erlaube " +
            "SKZ Connect den Zugriff im Root-Dialog. Root funktioniert dauerhaft – auch nach einem Neustart, ohne WLAN."));

        col.AddView(Topic("Touch-Steuerung einrichten",
            "Für Spiele, die keinen Controller unterstützen, tippt SKZ Connect die Bildschirmknöpfe für dich.<br><br>" +
            "<b>1.</b> Einstellungen → Bedienungshilfen → <b>SKZ Connect Touch-Steuerung</b> einschalten.<br>" +
            "<b>2.</b> In SKZ Connect „Touch-Steuerung“ als Weg wählen oder zusätzlich einschalten.<br>" +
            "<b>3.</b> Spiel öffnen, Benachrichtigungsleiste herunterziehen und bei SKZ Connect auf " +
            "<b>Touch-Layout bearbeiten</b> tippen.<br>" +
            "<b>4.</b> Mit „Taste hinzufügen“ Tasten und Sticks anlegen und auf die Knöpfe des Spiels ziehen. " +
            "Gedrückt halten entfernt eine Taste. <b>Speichern</b> – fertig.<br><br>" +
            "Sticks werden zu einem ziehenden Finger – ideal für virtuelle Joysticks in Spielen.",
            Ui.Button(host, "Bedienungshilfen öffnen", () => TouchService.OpenSystemSettings(host), Ui.ButtonStyle.Outlined)));

        col.AddView(Topic("In Spielen und Emulatoren",
            "Spiele sehen einen <b>Xbox-Controller</b> (Name „SKZ Connect Spieler 1“ usw.).<br><br>" +
            "• <b>Emulatoren</b> (z. B. Dolphin, PPSSPP, RetroArch, Eden): In den Controller-Einstellungen des Emulators " +
            "„SKZ Connect Spieler 1“ auswählen und ggf. die Tasten zuweisen.<br>" +
            "• <b>Cloud-Gaming</b> (Xbox Cloud, GeForce NOW, Steam Link): funktioniert direkt.<br>" +
            "• <b>Tastenlayout:</b> Unter Einstellungen → Tastenbelegung wählst du, ob A/B nach Position (Xbox) oder " +
            "nach Beschriftung (Switch) zugeordnet wird.<br>" +
            "• <b>Bewegungssteuerung:</b> Unter „Gyro-Zielen“ steuert das Neigen des Controllers den rechten Stick."));

        col.AddView(Topic("Gyro für Emulatoren (DSU)",
            "Emulatoren mit <b>DSU-/Cemuhook-Client</b> bekommen von SKZ Connect die Bewegungsdaten (und Tasten) direkt.<br><br>" +
            "<b>1.</b> Einstellungen → Bewegungssteuerung → <b>Gyro an Emulatoren senden (DSU)</b> einschalten.<br>" +
            "<b>2.</b> Im Emulator bei DSU, Cemuhook oder „UDP-Server“ als Adresse <b>127.0.0.1</b> und als Port " +
            "<b>26760</b> eintragen.<br><br>" +
            "Der Server ist nur auf diesem Gerät erreichbar. Spieler 1 bis 4 belegen dort der Reihe nach die vier " +
            "Steckplätze (Slots)."));

        col.AddView(Topic("Profile je Spiel",
            "Unter Einstellungen → <b>Profile je Spiel verwalten</b> bekommt jedes Spiel seine eigene Tastenbelegung.<br><br>" +
            "<b>1.</b> Einmal „Zugriff auf Nutzungsdaten“ erlauben – daran erkennt SKZ Connect, welche App im Vordergrund " +
            "läuft (nur den Namen, keine Inhalte).<br>" +
            "<b>2.</b> <b>Neues Profil</b> antippen und das Spiel aus der Liste wählen. Das Profil startet mit einer Kopie " +
            "der Standardbelegung.<br>" +
            "<b>3.</b> <b>Tastenbelegung bearbeiten</b>: Controller wählen und jeder Taste eine andere Funktion geben – eine " +
            "Xbox-Taste, keine Funktion, Gyro als rechter Stick oder ein Makro (Tastenfolge).<br><br>" +
            "Das Profil gilt automatisch, solange das Spiel läuft. Unter „Aktives Profil“ wählst du eins auch fest."));

        col.AddView(Topic("Ohne App starten: Kachel und Autostart",
            "<b>Schnelleinstellungen:</b> Benachrichtigungsleiste ganz herunterziehen, auf den Stift (Bearbeiten) tippen und " +
            "die Kachel <b>SKZ Connect</b> in den aktiven Bereich ziehen. Ein Tippen startet oder beendet die " +
            "Controller-Verbindung. Fehlt noch eine Berechtigung, öffnet die Kachel SKZ-Connect.<br><br>" +
            "<b>Mit Android starten:</b> Unter Einstellungen → Verbindung einschalten – nach dem Einschalten des Geräts " +
            "verbinden sich deine Controller dann von selbst."));

        col.AddView(KodiTopic());

        col.AddView(Topic("Probleme lösen",
            "<b>Controller wird nicht gefunden:</b> Bluetooth an? SYNC-Taste lange genug gedrückt, bis die Lichter laufen? " +
            "Ist er gerade mit der Switch oder einem PC verbunden, schalte dort zuerst Bluetooth aus.<br><br>" +
            "<b>Verbunden, aber das Spiel reagiert nicht:</b> Auf der Controller-Startseite muss oben „Bereit“ stehen. " +
            "Steht dort „Shizuku ist nicht gestartet“, öffne Shizuku und tippe auf Starten.<br><br>" +
            "<b>Verbindung bricht beim Spielen ab:</b> Nimm SKZ Connect in den Akku-Einstellungen von der Optimierung aus " +
            "(Einstellungen → Apps → SKZ Connect → Akku → <i>Nicht eingeschränkt</i>).<br><br>" +
            "<b>Doppelte Eingaben:</b> Ist Touch-Steuerung zusätzlich an und das Spiel unterstützt Controller, schalte " +
            "Touch aus.<br><br>" +
            "<b>Controller verbindet sich nicht mehr von selbst:</b> Einmal SYNC drücken – dann merkt er sich dieses Gerät neu.",
            Ui.Button(host, "Akku-Einstellungen öffnen", OpenBatterySettings, Ui.ButtonStyle.Outlined)));

        col.AddView(Topic("Diagnose",
            "Wenn etwas nicht klappt, hilft das Protokoll bei der Fehlersuche. Es enthält keine persönlichen Daten außer " +
            "den Bluetooth-Adressen deiner Controller.",
            Ui.Buttons(host,
                Ui.Button(host, "Protokoll anzeigen", ShowLog, Ui.ButtonStyle.Outlined),
                Ui.Button(host, "Teilen", ShareLog, Ui.ButtonStyle.Text))));

        col.AddView(LicensesTopic());
        return scroll;
    }

    public void Refresh()
    {
    }

    /// <summary>Aufklappbare Karte: Titel antippen zeigt/versteckt den Text (<paramref name="open"/>: schon aufgeklappt).</summary>
    private View Topic(string title, string html, View? extra = null, bool open = false)
    {
        var header = new LinearLayout(host) { Orientation = Orientation.Horizontal, Clickable = true };
        header.SetGravity(GravityFlags.CenterVertical);
        var titleView = Ui.Title(host, title);
        header.AddView(titleView, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        var arrow = Ui.Title(host, open ? "▴" : "▾");
        header.AddView(arrow);
        var body = Ui.Column(host);
        body.Visibility = open ? ViewStates.Visible : ViewStates.Gone;
        body.SetPadding(0, Ui.Dp(host, 10), 0, 0);
        body.AddView(Ui.Rich(host, html));
        if (extra is not null)
            body.AddView(extra);
        header.Click += (_, _) =>
        {
            bool show = body.Visibility != ViewStates.Visible;
            body.Visibility = show ? ViewStates.Visible : ViewStates.Gone;
            arrow.Text = show ? "▴" : "▾";
        };
        return Ui.Card(host, header, body);
    }

    // ---------- Kodi: DevCatSKZ Repository ----------

    private View KodiTopic()
    {
        bool kodi = KodiRepository.IsKodiInstalled(host);
        bool direct = OutputRouter.Shell is { } s && s.HasPermission();
        // Nach einer Neuinstallation schreibt SKZ Connect womöglich in eine Ausweichdatei (KodiStatusWriter.FallbackName).
        string? fallback = kodi ? Core.ControllerService.Instance?.KodiStatusFallback : null;
        string html =
            (fallback is not null
                ? L.F("<b>Kodi findet den Status womöglich nicht:</b> Im Ordner Documents/SKZ Connect liegt noch eine " +
                      "status.json einer früheren Installation, die SKZ Connect nicht ändern darf – SKZ Connect schreibt " +
                      "deshalb in <i>{0}</i>. Lösche die alte status.json mit einem Dateimanager, beende SKZ Connect " +
                      "(Benachrichtigung → Beenden) und öffne es wieder.<br><br>", fallback)
                : "") +
            L.T("Über das <b>DevCatSKZ Repository</b> bekommst du unsere Kodi-Plugins – mit automatischen Updates.<br><br>") +
            (kodi
                ? (direct
                    ? L.F("<b>Direkt einrichten</b> legt das Repository in Kodi ab (über {0}). " +
                          "Danach in Kodi: <b>Add-ons → Meine Add-ons → Add-on-Repository → DevCatSKZ Repository → Aktivieren</b>.<br><br>",
                        OutputRouter.Shell!.Name)
                    : "") +
                  L.F("<b>Herunterladen</b> speichert die Datei im Download-Ordner. Danach in Kodi:<br>" +
                      "<b>1.</b> Einstellungen → System → Add-ons → <b>Unbekannte Quellen</b> einschalten.<br>" +
                      "<b>2.</b> Add-ons → Paket-Symbol → <b>Aus ZIP-Datei installieren</b> → Download → <i>{0}</i>.<br>" +
                      "<b>3.</b> „Aus Repository installieren“ → <b>DevCatSKZ Repository</b>.", KodiRepository.ZipName)
                : L.T("Kodi ist auf diesem Gerät nicht installiert."));
        View buttons = kodi
            ? Ui.Buttons(host,
                direct ? Ui.Button(host, "Direkt einrichten", () => _ = InstallDirectAsync()) : Ui.Button(host, "Herunterladen", () => _ = DownloadAsync()),
                direct ? Ui.Button(host, "Herunterladen", () => _ = DownloadAsync(), Ui.ButtonStyle.Outlined) : Ui.Button(host, "Kodi öffnen", () => KodiRepository.OpenKodi(host), Ui.ButtonStyle.Outlined),
                Ui.Button(host, direct ? "Kodi öffnen" : "Mehr Infos", direct ? () => KodiRepository.OpenKodi(host) : OpenRepoPage, Ui.ButtonStyle.Text))
            : Ui.Buttons(host,
                Ui.Button(host, "Kodi installieren", () => KodiRepository.OpenKodiInStore(host), Ui.ButtonStyle.Outlined),
                Ui.Button(host, "Mehr Infos", OpenRepoPage, Ui.ButtonStyle.Text));
        return Topic("Kodi: DevCatSKZ Repository", html, buttons, open: fallback is not null);
    }

    private void OpenRepoPage() =>
        host.StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse("https://github.com/DevCatSKZ/kodi-repo")));

    // ---------- Lizenzen ----------

    /// <summary>Asset aus THIRD-PARTY-NOTICES.md im Repository-Wurzelordner (siehe NConnect.Android.csproj).</summary>
    private const string NoticesAsset = "THIRD-PARTY-NOTICES.md";
    private const string NoticesUrl = "https://github.com/DevCatSKZ/SKZ-Connect/blob/main/THIRD-PARTY-NOTICES.md";

    /// <summary>
    /// Kurzliste der Komponenten Dritter im APK mit Lizenz und Copyright-Vermerk (Namen und Vermerke bleiben wörtlich);
    /// die vollständigen Lizenztexte und NOTICE-Dateien zeigt <see cref="ShowNotices"/>.
    /// </summary>
    private View LicensesTopic()
    {
        static string E(string text) => System.Net.WebUtility.HtmlEncode(text);
        static string Group(string title) => $"<br><b>{E(title)}</b><br>";
        static string Item(string name, string license, string copyright) =>
            $"• {E(name)} – {E(license)}<br><small>{E(copyright)}</small><br>";
        // Code Dritter in der Kotlin-Standardbibliothek (laut license/README.md des Kotlin-Projekts).
        string kotlinParts = L.T("enthält:") + " GWT, (C) 2007-08 Google Inc. (Apache-2.0); Guava UnsignedLongs, (C) 2011 " +
                             "The Guava Authors (Apache-2.0); ThreeTen-BP, Copyright (c) 2007-present, Stephen Colebourne & " +
                             "Michael Nascimento Santos (BSD-3-Clause); Boost Math, Copyright Eric Ford & Hubert Holin 2001 (BSL-1.0)";
        string html =
            E(L.T("SKZ Connect enthält Software, Quellcode und Daten Dritter. Die Rechte daran verbleiben bei ihren Inhabern.")) + "<br>" +
            Group(L.T("Laufzeit")) +
            Item(L.T(".NET für Android 36.1 und .NET-Laufzeit 10 (Mono)"), "MIT", "Copyright (c) .NET Foundation and Contributors") +
            Item(L.T(".NET-for-Android-Bindungen (Xamarin.*-Pakete)"), "MIT", "Copyright (c) .NET Foundation Contributors") +
            Group(L.T("Bibliotheken")) +
            Item("AndroidX (activity, annotation, appcompat, arch.core, cardview, collection, compose.runtime-annotation, " +
                 "concurrent-futures, constraintlayout, coordinatorlayout, core, cursoradapter, customview, drawerlayout, " +
                 "dynamicanimation, emoji2, fragment, graphics-shapes, interpolator, lifecycle, loader, navigationevent, " +
                 "profileinstaller, recyclerview, resourceinspection, savedstate, startup, tracing, transition, vectordrawable, " +
                 "versionedparcelable, viewpager, viewpager2)", "Apache-2.0", "Copyright (C) The Android Open Source Project") +
            Item("Material Components for Android 1.14.0", "Apache-2.0", "Copyright (C) The Android Open Source Project (Google LLC)") +
            Item(L.T("Kotlin-Standardbibliothek") + " 2.4.0", "Apache-2.0",
                "Copyright 2010-2024 JetBrains s.r.o. and Kotlin Programming Language contributors") +
            $"<small>{E(kotlinParts)}</small><br>" +
            Item("kotlinx.coroutines 1.11.0", "Apache-2.0", "Copyright 2016-2025 JetBrains s.r.o and contributors") +
            Item("kotlinx.serialization 1.11.0", "Apache-2.0", "Copyright 2017-2019 JetBrains s.r.o and respective authors and developers") +
            Item("kotlinx-atomicfu 0.33.0", "Apache-2.0", "Copyright 2017 JetBrains s.r.o. and respective authors and developers") +
            Item("Guava ListenableFuture 1.0", "Apache-2.0", "Copyright (C) 2007 The Guava Authors") +
            Item("Error Prone Annotations 2.50.0", "Apache-2.0",
                "Copyright The Error Prone Authors (Google Inc., Two Sigma Open Source, Picnic Technologies)") +
            Item("JetBrains Java Annotations 26.1.0", "Apache-2.0", "Copyright 2000-2016 JetBrains s.r.o.") +
            Item("JSpecify 1.0.0", "Apache-2.0", "Copyright The JSpecify Authors (Google LLC, Oracle America, Inc., SpotBugs Team)") +
            Item("Shizuku API 13.1.5 (api, provider, shared, aidl)", "MIT", "Copyright (c) 2021 RikkaW") +
            Group(L.T("Symbole")) +
            Item("Material Icons (check, close, warning, bluetooth, battery_full, videogame_asset, help_outline, tune)",
                "Apache-2.0", "Copyright Google LLC") +
            Group(L.T("Übernommener oder angepasster Code und Daten")) +
            Item("SDL – Simple DirectMedia Layer (" + L.T("HIDAPI-Treiber für Switch, Switch 2, PS4 und PS5; nach C# übertragen und angepasst") + ")",
                "zlib", "Copyright (C) 1997-2026 Sam Lantinga; Code and logic contributed by Valve Corporation") +
            Item("NS2Pro-Bridge-Windows (" + L.T("Bluetooth-Start-Sequenz und Vibrationspaket") + ")", "MIT",
                "Copyright (c) 2026 NS2 Pro Compatibility contributors") +
            Item("Switch2BTLink", "MIT", "Copyright (c) 2026 KumuIi") +
            Item("joycon2cpp", "MIT", "Copyright (c) 2025 Frano") +
            Item("Android Compatibility Test Suite (" + L.T("HID-Deskriptor des Xbox One S Controllers") + ")", "Apache-2.0",
                "Copyright (C) The Android Open Source Project") +
            Group(L.T("Protokoll-Dokumentation (kein übernommener Code)")) +
            E("ndeadly/switch2_controller_research, dekuNukem/Nintendo_Switch_Reverse_Engineering, Linux (hid-nintendo, " +
              "hid-wiimote, hid-playstation), WiiBrew, Dolphin, yuzu/Citron, Switch2Connect, Cemuhook/DSU, DS4Windows, " +
              "AOSP frameworks/base/cmds/hid") + "<br><br>" +
            E(L.T("Nintendo, Nintendo Switch, Joy-Con, Wii, Xbox, PlayStation, DualShock und DualSense sind Marken ihrer " +
                  "jeweiligen Inhaber. SKZ Connect ist ein unabhängiges Projekt."));
        return Topic("Lizenzen", html, Ui.Button(host, "Vollständige Lizenztexte anzeigen", ShowNotices, Ui.ButtonStyle.Outlined));
    }

    /// <summary>Vollständige THIRD-PARTY-NOTICES.md (Lizenztexte, NOTICE-Dateien, Versionen) zum Durchscrollen.</summary>
    private void ShowNotices()
    {
        string text;
        try
        {
            using var reader = new StreamReader(host.Assets!.Open(NoticesAsset));
            text = reader.ReadToEnd();
        }
        catch (Java.IO.IOException e)
        {
            Log.Error("Lizenztexte lesen", e);
            text = L.F("Die Lizenztexte fehlen in dieser Installation. Du findest sie unter {0}", NoticesUrl);
        }
        var view = new TextView(host) { Text = text, TextSize = 12 };
        view.SetTextIsSelectable(true);
        int pad = Ui.Dp(host, 20);
        view.SetPadding(pad, Ui.Dp(host, 8), pad, 0);
        var scroll = new ScrollView(host);
        scroll.AddView(view);
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Lizenztexte"))!
            .SetView(scroll)!
            .SetNegativeButton(L.T("Schließen"), (_, _) => { })!
            .Show();
    }

    private async Task DownloadAsync()
    {
        Ui.Toast(host, "Lade DevCatSKZ Repository …");
        try
        {
            string where = await KodiRepository.SaveToDownloadsAsync(host);
            host.RunOnUiThread(() => new MaterialAlertDialogBuilder(host)
                .SetTitle(L.T("Heruntergeladen"))!
                .SetMessage(L.F("Gespeichert unter: {0}\n\nIn Kodi: Add-ons → Paket-Symbol → „Aus ZIP-Datei installieren“ → " +
                            "Download → {1}.\n(Vorher in Kodi „Unbekannte Quellen“ erlauben.)", where, KodiRepository.ZipName))!
                .SetPositiveButton(L.T("Kodi öffnen"), (_, _) => KodiRepository.OpenKodi(host))!
                .SetNegativeButton(L.T("Schließen"), (_, _) => { })!
                .Show());
        }
        catch (Exception e)
        {
            Log.Error("DevCatSKZ Repository herunterladen", e);
            host.RunOnUiThread(() => Ui.Toast(host, "Download fehlgeschlagen – Internetverbindung prüfen."));
        }
    }

    private async Task InstallDirectAsync()
    {
        if (OutputRouter.Shell is not { } shell)
            return;
        Ui.Toast(host, "Richte DevCatSKZ Repository in Kodi ein …");
        try
        {
            await KodiRepository.InstallDirectAsync(host, shell);
            host.RunOnUiThread(() => new MaterialAlertDialogBuilder(host)
                .SetTitle(L.T("Fast fertig"))!
                .SetMessage(L.T("Das Repository liegt jetzt in Kodi. Öffne Kodi und schalte es ein:\n" +
                            "Add-ons → Meine Add-ons → Add-on-Repository → DevCatSKZ Repository → Aktivieren.\n\n" +
                            "Läuft Kodi gerade, beende es vorher einmal ganz."))!
                .SetPositiveButton(L.T("Kodi öffnen"), (_, _) => KodiRepository.OpenKodi(host))!
                .SetNegativeButton(L.T("Schließen"), (_, _) => { })!
                .Show());
        }
        catch (Exception e)
        {
            Log.Error("DevCatSKZ Repository direkt einrichten", e);
            host.RunOnUiThread(() => Ui.Toast(host, "Direkt einrichten ging nicht – nutze „Herunterladen“."));
        }
    }

    private void OpenWizard() => host.StartActivity(new Intent(host, typeof(SetupWizardActivity))
        .PutExtra(SetupWizardActivity.ExtraStartAtOutput, true));

    public static void OpenShizuku(Context ctx)
    {
        var launch = ctx.PackageManager!.GetLaunchIntentForPackage(ShizukuShell.PackageName);
        if (launch is not null)
        {
            ctx.StartActivity(launch);
            return;
        }
        try
        {
            ctx.StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse($"market://details?id={ShizukuShell.PackageName}")));
        }
        catch (ActivityNotFoundException)
        {
            ctx.StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse("https://shizuku.rikka.app/download/")));
        }
    }

    private void OpenShizuku() => OpenShizuku(host);

    private void OpenBatterySettings()
    {
        try
        {
            host.StartActivity(new Intent(global::Android.Provider.Settings.ActionIgnoreBatteryOptimizationSettings));
        }
        catch (ActivityNotFoundException)
        {
            Permissions.OpenAppSettings(host);
        }
    }

    private static string LogText() => string.Join("\n", Log.Recent());

    private void ShowLog()
    {
        var text = new TextView(host) { Text = LogText().Length > 0 ? LogText() : "Noch keine Einträge.", TextSize = 11 };
        text.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal);
        text.SetTextIsSelectable(true);
        int pad = Ui.Dp(host, 20);
        text.SetPadding(pad, Ui.Dp(host, 8), pad, 0);
        var scroll = new ScrollView(host);
        scroll.AddView(text);
        scroll.Post(() => scroll.FullScroll(FocusSearchDirection.Down));
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Protokoll"))!
            .SetView(scroll)!
            .SetPositiveButton(L.T("Teilen"), (_, _) => ShareLog())!
            .SetNegativeButton(L.T("Schließen"), (_, _) => { })!
            .Show();
    }

    private void ShareLog()
    {
        var intent = new Intent(Intent.ActionSend);
        intent.SetType("text/plain");
        intent.PutExtra(Intent.ExtraSubject, "SKZ Connect Protokoll");
        intent.PutExtra(Intent.ExtraText, "SKZ Connect für Android – Protokoll\nAndroid " + global::Android.OS.Build.VERSION.Release + ", " +
                                          global::Android.OS.Build.Manufacturer + " " + global::Android.OS.Build.Model + "\n" +
                                          $"Ausgabe: {OutputRouter.Describe()}\n\n{LogText()}");
        host.StartActivity(Intent.CreateChooser(intent, L.T("Protokoll teilen")));
    }
}
