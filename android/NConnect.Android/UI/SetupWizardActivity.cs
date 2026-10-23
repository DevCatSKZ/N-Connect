using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Activity;
using AndroidX.AppCompat.App;
using AndroidX.Core.View;
using Google.Android.Material.AppBar;
using Google.Android.Material.Button;
using Google.Android.Material.Card;
using Google.Android.Material.Dialog;
using Google.Android.Material.ProgressIndicator;
using NConnect.Android.Core;
using NConnect.Android.Output;
using NConnect.Android.Touch;
using View = Android.Views.View;

namespace NConnect.Android.UI;

/// <summary>
/// Einrichtungs-Assistent (erster Start, später über Einstellungen erneut): Berechtigungen, Ausgabeweg wählen
/// (Shizuku, Root oder Touch), diesen Weg Schritt für Schritt einrichten und testen, ersten Controller verbinden.
/// </summary>
[Activity(Label = "@string/setup_title", Theme = "@style/AppTheme", Exported = false,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class SetupWizardActivity : AppCompatActivity
{
    public const string ExtraStartAtOutput = "start_at_output";
    private const int StepWelcome = 0, StepPermissions = 1, StepChoose = 2, StepSetup = 3, StepController = 4, StepDone = 5;
    private const int StepCount = 6;

    private int _step;
    private MaterialToolbar _toolbar = null!;
    private LinearProgressIndicator _progress = null!;
    private FrameLayout _content = null!;
    private MaterialButton _back = null!, _next = null!;
    private Handler? _ticker;
    private string _signature = "";
    private bool _busy;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Look.ApplyNightMode();
        base.OnCreate(savedInstanceState);
        WindowCompat.SetDecorFitsSystemWindows(Window!, false);
        var root = Ui.Column(this);
        _toolbar = new MaterialToolbar(this) { Title = L.T("SKZ Connect einrichten") };
        root.AddView(_toolbar);
        _progress = new LinearProgressIndicator(this) { Max = StepCount };
        root.AddView(_progress, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        _content = new FrameLayout(this);
        root.AddView(_content, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        int pad = Ui.Dp(this, 16);
        bar.SetPadding(pad, Ui.Dp(this, 8), pad, Ui.Dp(this, 12));
        _back = Ui.Button(this, "Zurück", Back, Ui.ButtonStyle.Text);
        _next = Ui.Button(this, "Weiter", Next);
        var spacer = new View(this);
        bar.AddView(_back);
        bar.AddView(spacer, new LinearLayout.LayoutParams(0, 1, 1f));
        bar.AddView(_next);
        root.AddView(bar);
        SetContentView(root);
        Chrome.Apply(this, root, _toolbar, progress: _progress);
        ViewCompat.SetOnApplyWindowInsetsListener(root, new Insets(bar, pad));

        // Vom Hauptfenster („Beheben“): direkt zur Wegwahl. Sonst dort weitermachen, wo der Assistent zuletzt war.
        _step = Intent?.GetBooleanExtra(ExtraStartAtOutput, false) == true ? StepChoose
            : AppPrefs.SetupDone ? StepWelcome : Math.Clamp(AppPrefs.WizardStep, StepWelcome, StepDone);
        OnBackPressedDispatcher.AddCallback(this, new BackCallback(this));
        Show();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _ticker = new Handler(Looper.MainLooper!);
        Tick();
        ControllerService.Changed += Refresh;
        TouchService.StateChanged += Refresh;
    }

    protected override void OnPause()
    {
        ControllerService.Changed -= Refresh;
        TouchService.StateChanged -= Refresh;
        _ticker?.RemoveCallbacksAndMessages(null);
        _ticker = null;
        base.OnPause();
    }

    private void Tick()
    {
        Refresh();
        _ticker?.PostDelayed(Tick, 1000);
    }

    /// <summary>Zustandsabhängige Schritte neu zeichnen, wenn sich etwas geändert hat (Freigabe erteilt, Controller da …).</summary>
    private void Refresh() => RunOnUiThread(() =>
    {
        string sig = Signature();
        if (sig != _signature)
            Show();
    });

    private string Signature() => _step switch
    {
        StepPermissions => $"{Permissions.HasBluetooth(this)}{Permissions.HasNotifications(this)}{SystemPages.IsBatteryUnrestricted(this)}",
        StepChoose => $"{AppPrefs.Mode}{OutputRouter.RootState()}{OutputRouter.ShizukuState()}",
        StepSetup => $"{AppPrefs.Mode}{OutputRouter.RootState()}{ShizukuShell.IsInstalled()}{ShizukuShell.Instance.IsAvailable()}" +
                     $"{ShizukuShell.Instance.HasPermission()}{TouchService.Instance is null}{AppPrefs.TouchEnabled}{OutputRouter.Shell?.Name}{_busy}",
        StepController => string.Join(",", ControllerService.Instance?.Players.Select(p => p.Name + p.Index) ?? []) +
                          $"{ControllerService.Instance is null}{ControllerService.Instance?.BluetoothOn}",
        _ => _step.ToString(),
    };

    private void Show()
    {
        _signature = Signature();
        _progress.SetProgressCompat(_step + 1, true);
        _back.Visibility = _step == StepWelcome ? ViewStates.Invisible : ViewStates.Visible;
        _next.Text = L.T(_step switch
        {
            StepWelcome => "Los geht's",
            StepPermissions when !Permissions.HasBluetooth(this) => "Erlauben",
            StepDone => "Fertig",
            _ => "Weiter",
        });
        var (scroll, col) = Ui.Page(this);
        switch (_step)
        {
            case StepWelcome: Welcome(col); break;
            case StepPermissions: PermissionsStep(col); break;
            case StepChoose: Choose(col); break;
            case StepSetup: Setup(col); break;
            case StepController: Controller(col); break;
            case StepDone: Done(col); break;
        }
        int y = _content.ChildCount > 0 && _content.GetChildAt(0) is ScrollView old ? old.ScrollY : 0;
        _content.RemoveAllViews();
        _content.AddView(scroll);
        scroll.Post(() => scroll.ScrollTo(0, y));
    }

    // ---------- Schritte ----------

    private void Welcome(LinearLayout col)
    {
        col.AddView(Ui.Headline(this, "Willkommen bei SKZ Connect"));
        col.AddView(Ui.Body(this, "Spiele mit deinen Nintendo-Switch-2-Controllern auf diesem Gerät – in Android-Spielen, " +
                                  "Emulatoren und beim Cloud-Gaming."));
        col.AddView(Ui.Card(this,
            Ui.Title(this, "Unterstützte Controller"),
            Ui.Space(this, 4),
            Ui.Body(this, "• Pro Controller 2\n• Joy-Con 2 (einzeln oder als Paar)\n• GameCube-Controller (Switch 2)")));
        col.AddView(Ui.Card(this,
            Ui.Title(this, "Was dich erwartet"),
            Ui.Space(this, 4),
            Ui.Body(this, "In wenigen Schritten erlaubst du Bluetooth, wählst aus, wie Spiele deine Controller sehen sollen, " +
                          "und verbindest deinen ersten Controller. Das dauert etwa zwei Minuten.")));
        col.AddView(Ui.Caption(this, "SKZ Connect unterstützt nur Switch-2-Controller. Xbox- und PlayStation-Controller laufen " +
                                     "direkt über die Bluetooth-Einstellungen von Android. Ältere Switch-Controller (Switch 1, " +
                                     "Nachbauten) koppelst du ebenfalls dort – meist nur mit Tasten und Sticks, ohne " +
                                     "Bewegungssteuerung und Vibration."));
    }

    private void PermissionsStep(LinearLayout col)
    {
        col.AddView(Ui.Headline(this, "Berechtigungen"));
        col.AddView(Ui.Body(this, "Tippe bei jedem Punkt auf den Knopf – du landest direkt an der richtigen Stelle " +
                                  "und kommst danach automatisch hierher zurück."));
        bool bt = Permissions.HasBluetooth(this);
        bool notify = Permissions.HasNotifications(this);
        bool battery = SystemPages.IsBatteryUnrestricted(this);
        col.AddView(Ui.Card(this,
            Ui.StatusRow(this, bt ? PathState.Ready : PathState.NeedsAction, "Geräte in der Nähe (Bluetooth)",
                bt ? "Erlaubt." : "Nötig, um Controller zu finden und zu verbinden. SKZ Connect fragt nicht nach deinem Standort.",
                bt ? null : "Erlauben", AskBluetooth),
            Ui.Divider(this),
            Ui.StatusRow(this, notify ? PathState.Ready : PathState.NeedsAction, "Benachrichtigungen",
                notify ? "Erlaubt." : "Zeigt beim Spielen, welche Controller verbunden sind, und bietet Schnellaktionen.",
                notify ? null : "Erlauben", () => Permissions.RequestNotifications(this)),
            Ui.Divider(this),
            Ui.StatusRow(this, battery ? PathState.Ready : PathState.NeedsAction, "Im Hintergrund weiterlaufen (empfohlen)",
                battery ? "Nicht eingeschränkt – die Verbindung bleibt beim Spielen stabil."
                    : "Verhindert, dass Android SKZ Connect beim Spielen beendet und die Controller trennt.",
                battery ? null : "Nicht einschränken", () => SystemPages.BatteryUnrestricted(this))));
    }

    /// <summary>„Geräte in der Nähe“: System-Dialog; wurde „Nicht mehr fragen“ gewählt, direkt die App-Info mit Hinweis.</summary>
    private void AskBluetooth()
    {
        if (!Permissions.PermanentlyDenied(this))
        {
            Permissions.RequestBluetooth(this);
            return;
        }
        Ui.Toast(this, "Tippe auf „Berechtigungen“ → „Geräte in der Nähe“ → „Zulassen“");
        StartActivity(SystemPages.AppDetails(this));
    }
    private void Choose(LinearLayout col)
    {
        // Empfehlung vorauswählen: Root, wenn vorhanden, sonst Shizuku.
        if (AppPrefs.Mode == OutputPath.Auto)
            AppPrefs.Mode = OutputRouter.RootState() == PathState.Missing ? OutputPath.Shizuku : OutputPath.Root;
        col.AddView(Ui.Headline(this, "Wie sollen Spiele deine Controller sehen?"));
        col.AddView(Ui.Body(this, "Wähle einen Weg. Du kannst ihn später in den Einstellungen jederzeit ändern."));
        col.AddView(Option(OutputPath.Shizuku, "Shizuku – empfohlen",
            "Deine Controller erscheinen als echter Xbox-Controller in allen Spielen und Emulatoren. Kein Root nötig – " +
            "du richtest einmal die kostenlose App Shizuku ein.",
            ShizukuShell.IsInstalled() ? "Shizuku ist installiert." : "Shizuku wird im nächsten Schritt installiert."));
        var rootState = OutputRouter.RootState();
        col.AddView(Option(OutputPath.Root, "Root",
            "Wie Shizuku, aber dauerhaft ohne erneutes Starten nach einem Neustart. Nur auf gerooteten Geräten.",
            rootState == PathState.Missing ? "Auf diesem Gerät wurde kein Root gefunden." : "Root ist vorhanden."));
        col.AddView(Option(OutputPath.Touch, "Touch-Steuerung",
            "Keine Einrichtung außer einer Bedienungshilfe. Tasten werden zu Bildschirmberührungen – für Spiele ohne " +
            "Controller-Unterstützung. Etwas mehr Verzögerung, kein echter Controller.",
            "Lässt sich auch zusätzlich zu Shizuku oder Root einschalten."));
    }

    private View Option(OutputPath path, string title, string text, string note)
    {
        bool selected = AppPrefs.Mode == path;
        var card = (MaterialCardView)Ui.Card(this, Ui.Title(this, (selected ? "◉  " : "○  ") + L.T(title)), Ui.Space(this, 4),
            Ui.Body(this, text), Ui.Space(this, 6), Ui.Caption(this, note));
        card.Checkable = true;
        card.Checked = selected;
        card.StrokeWidth = Ui.Dp(this, selected ? 2 : 1);
        card.StrokeColor = Look.C(selected ? Look.AccentLine(this) : Look.P(this).ControlBorder).ToArgb();
        card.Clickable = true;
        card.Click += (_, _) =>
        {
            AppPrefs.Mode = path;
            Show();
        };
        return card;
    }

    private void Setup(LinearLayout col)
    {
        switch (AppPrefs.Mode)
        {
            case OutputPath.Root:
                SetupRoot(col);
                break;
            case OutputPath.Touch:
                SetupTouch(col, primary: true);
                break;
            default:
                SetupShizuku(col);
                break;
        }
        if (AppPrefs.Mode is OutputPath.Shizuku or OutputPath.Root or OutputPath.Auto)
        {
            col.AddView(Ui.Section(this, "OPTIONAL"));
            col.AddView(Ui.Card(this, Ui.SwitchRow(this, "Touch-Steuerung zusätzlich",
                "Für Spiele ohne Controller-Unterstützung tippen deine Tasten zusätzlich auf den Bildschirm.",
                AppPrefs.TouchEnabled, on => { AppPrefs.TouchEnabled = on; Show(); })));
            if (AppPrefs.TouchEnabled)
                SetupTouch(col, primary: false);
        }
    }

    private void SetupShizuku(LinearLayout col)
    {
        col.AddView(Ui.Headline(this, "Shizuku einrichten"));
        col.AddView(Ui.Body(this, "Shizuku erlaubt SKZ Connect, einen virtuellen Xbox-Controller anzulegen. Drei Schritte:"));
        bool installed = ShizukuShell.IsInstalled();
        bool running = installed && ShizukuShell.Instance.IsAvailable();
        bool granted = running && ShizukuShell.Instance.HasPermission();
        var card = Ui.Card(this,
            Ui.StatusRow(this, installed ? PathState.Ready : PathState.NeedsAction, "1. Shizuku installieren",
                installed ? "Installiert." : "Kostenlos im Play Store.",
                installed ? null : "Shizuku installieren", () => HelpPage.OpenShizuku(this)),
            Ui.Divider(this),
            Ui.StatusRow(this, running ? PathState.Ready : installed ? PathState.NeedsAction : PathState.Missing, "2. Shizuku starten",
                running ? "Läuft." : "In Shizuku: „Über kabelloses Debugging starten“ → Kopplung → Starten. Nach jedem Neustart einmal „Starten“.",
                running || !installed ? null : "Shizuku öffnen", () => HelpPage.OpenShizuku(this)),
            Ui.Divider(this),
            Ui.StatusRow(this, granted ? PathState.Ready : running ? PathState.NeedsAction : PathState.Missing, "3. SKZ Connect den Zugriff erlauben",
                granted ? "Erlaubt." : "Shizuku fragt einmal, ob SKZ Connect es nutzen darf.",
                granted || !running ? null : "Zugriff erlauben", () => _ = GrantAndTest(ShizukuShell.Instance)));
        if (installed && !running)
            Ui.Inner(card).AddView(Ui.Button(this, "Ausführliche Anleitung", ShowShizukuGuide, Ui.ButtonStyle.Text));
        col.AddView(card);
        if (granted)
            AddTestCard(col);
    }

    private void SetupRoot(LinearLayout col)
    {
        col.AddView(Ui.Headline(this, "Root-Zugriff"));
        var state = OutputRouter.RootState();
        col.AddView(Ui.Card(this, Ui.StatusRow(this, state, "Root-Zugriff für SKZ Connect",
            state switch
            {
                PathState.Ready => "Erlaubt.",
                PathState.NeedsAction => "Tippe auf „Zugriff anfragen“ und erlaube SKZ Connect im Root-Dialog (z. B. Magisk).",
                _ => "Auf diesem Gerät wurde kein Root gefunden. Wähle stattdessen Shizuku.",
            },
            state == PathState.NeedsAction ? "Zugriff anfragen" : null, () => _ = GrantAndTest(RootShell.Instance))));
        if (state == PathState.Missing)
            col.AddView(Ui.Button(this, "Stattdessen Shizuku verwenden", () => { AppPrefs.Mode = OutputPath.Shizuku; Show(); },
                Ui.ButtonStyle.Outlined));
        if (state == PathState.Ready)
            AddTestCard(col);
    }

    private void SetupTouch(LinearLayout col, bool primary)
    {
        if (primary)
        {
            col.AddView(Ui.Headline(this, "Touch-Steuerung einschalten"));
            col.AddView(Ui.Body(this, "Die Touch-Steuerung ist eine Bedienungshilfe. Sie führt nur Berührungen aus und liest " +
                                      "keine Inhalte vom Bildschirm."));
        }
        bool on = TouchService.Instance is not null;
        var card = Ui.Card(this,
            Ui.StatusRow(this, on ? PathState.Ready : PathState.NeedsAction, "Bedienungshilfe „SKZ Connect Touch-Steuerung“",
                on ? "Eingeschaltet. Das Touch-Layout legst du später direkt im Spiel fest."
                    : "Der Knopf öffnet direkt den Schalter für „SKZ Connect Touch-Steuerung“ – einschalten und zurückkehren.",
                on ? null : "Einschalten", () => TouchService.OpenSystemSettings(this)));
        if (!on && OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            // Android 13+: Bedienungshilfen von Apps außerhalb des Play Store sind zunächst gesperrt.
            Ui.Inner(card).AddView(Ui.Divider(this));
            Ui.Inner(card).AddView(Ui.Caption(this, "Ist der Schalter ausgegraut oder erscheint „Eingeschränkte Einstellung“? " +
                "Dann in der App-Info oben rechts auf ⋮ tippen → „Eingeschränkte Einstellungen zulassen“ und danach erneut einschalten."));
            Ui.Inner(card).AddView(Ui.Button(this, "App-Info öffnen", () => StartActivity(SystemPages.AppDetails(this)), Ui.ButtonStyle.Text));
        }
        col.AddView(card);
    }

    private void AddTestCard(LinearLayout col)
    {
        col.AddView(Ui.Card(this,
            Ui.Title(this, "Alles bereit"),
            Ui.Space(this, 4),
            Ui.Body(this, "Teste kurz, ob Android den virtuellen Xbox-Controller annimmt."),
            Ui.Button(this, _busy ? "Teste …" : "Virtuellen Controller testen", () => _ = TestVirtualPad(), Ui.ButtonStyle.Outlined)));
    }

    private void Controller(LinearLayout col)
    {
        col.AddView(Ui.Headline(this, "Controller verbinden"));
        var service = ControllerService.Instance;
        if (service is null && Permissions.HasBluetooth(this))
        {
            ControllerService.Start(this);
        }
        if (service is not null && !service.BluetoothOn)
        {
            col.AddView(Ui.Card(this, Ui.StatusRow(this, PathState.NeedsAction, "Bluetooth ist aus", "Schalte Bluetooth ein.",
                "Einschalten", () => StartActivity(new Intent(global::Android.Bluetooth.BluetoothAdapter.ActionRequestEnable)))));
        }
        col.AddView(Ui.Card(this, Ui.Title(this, "So geht's"), Ui.Space(this, 4), Ui.Body(this, Texts.PairHowTo)));
        var players = service?.Players ?? [];
        if (players.Count == 0)
        {
            col.AddView(Ui.Card(this, Ui.StatusRow(this, PathState.NeedsAction, "Warte auf einen Controller …",
                "Halte die SYNC-Taste gedrückt, bis die Lichter laufen.")));
        }
        else
        {
            var card = Ui.Card(this);
            foreach (var p in players)
                Ui.Inner(card).AddView(Ui.StatusRow(this, PathState.Ready, L.F("{0} verbunden", p.Name),
                    L.F("Spieler {0} · {1}", p.Index + 1, Texts.Battery(p.BatteryPercent, p.Charging))));
            col.AddView(card);
            col.AddView(Ui.Caption(this, "Du kannst weitere Controller verbinden oder auf „Weiter“ tippen."));
        }
    }

    private void Done(LinearLayout col)
    {
        col.AddView(Ui.Headline(this, "Fertig!"));
        col.AddView(Ui.Body(this, "SKZ Connect läuft jetzt im Hintergrund, solange die Benachrichtigung zu sehen ist. " +
                                  "Starte einfach dein Spiel."));
        col.AddView(Ui.Card(this,
            Ui.Title(this, "Gut zu wissen"),
            Ui.Space(this, 4),
            Ui.Body(this, string.Join("\n", new[]
            {
                "• Gekoppelte Controller verbinden sich per Tastendruck von selbst wieder.",
                "• Im Spiel heißt der Controller „SKZ Connect Spieler 1“.",
                AppPrefs.Mode == OutputPath.Shizuku ? "• Nach einem Neustart: Shizuku einmal öffnen und „Starten“ tippen." : "",
                AppPrefs.TouchEnabled || AppPrefs.Mode == OutputPath.Touch
                    ? "• Touch-Layout: im Spiel die Benachrichtigung herunterziehen → „Touch-Layout bearbeiten“." : "",
                "• Anleitungen und Problemlösungen findest du unter „Hilfe“.",
            }.Where(s => s.Length > 0).Select(L.T)))));
    }

    // ---------- Aktionen ----------

    private async Task GrantAndTest(IPrivilegedShell shell)
    {
        _busy = true;
        Refresh();
        try
        {
            if (await shell.RequestPermissionAsync())
            {
                await OutputRouter.ResolveAsync(interactive: true);
                await TestVirtualPad();
            }
            else
            {
                RunOnUiThread(() => Ui.Toast(this, L.F("{0}: Zugriff wurde nicht erlaubt", shell.Name)));
            }
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    /// <summary>Legt kurz einen virtuellen Controller an und prüft, ob Android ihn als Gamepad meldet.</summary>
    private async Task TestVirtualPad()
    {
        _busy = true;
        Refresh();
        string message;
        try
        {
            var shell = OutputRouter.Shell ?? await OutputRouter.ResolveAsync(interactive: true);
            if (shell is null)
            {
                message = "Kein Zugriff – bitte zuerst die Schritte oben erledigen.";
            }
            else
            {
                using var pad = UhidPad.Create(shell, 7, "SKZ Connect Test");
                bool found = false;
                for (int i = 0; i < 20 && !found; i++)
                {
                    await Task.Delay(150);
                    found = global::Android.Views.InputDevice.GetDeviceIds()!
                        .Select(global::Android.Views.InputDevice.GetDevice)
                        .Any(d => d?.Name == "SKZ Connect Test");
                }
                message = found ? "Funktioniert! Android hat den virtuellen Xbox-Controller erkannt."
                    : "Android hat den virtuellen Controller nicht gemeldet. Siehe Hilfe → Probleme lösen.";
                Log.Info($"Test virtueller Controller ({shell.Name}): {(found ? "erkannt" : "nicht erkannt")}");
            }
        }
        catch (Exception e)
        {
            Log.Error("Test virtueller Controller", e);
            message = L.F("Test fehlgeschlagen: {0}", e.Message);
        }
        finally
        {
            _busy = false;
        }
        RunOnUiThread(() =>
        {
            new MaterialAlertDialogBuilder(this).SetTitle(L.T("Test"))!.SetMessage(L.T(message))!
                .SetPositiveButton(L.T("OK"), (_, _) => { })!.Show();
            Show();
        });
    }

    private void ShowShizukuGuide()
    {
        new MaterialAlertDialogBuilder(this)
            .SetTitle(L.T("Shizuku starten"))!
            .SetMessage(L.T("1. Einstellungen → Über das Telefon → 7× auf „Build-Nummer“ tippen (Entwickleroptionen an).\n\n" +
                        "2. Mit einem WLAN verbinden. Einstellungen → System → Entwickleroptionen → „Kabelloses Debugging“ einschalten.\n\n" +
                        "3. Shizuku öffnen → „Über kabelloses Debugging starten“ → „Kopplung“. Shizuku zeigt eine Benachrichtigung " +
                        "zum Eingeben des Kopplungscodes.\n\n" +
                        "4. In den Entwickleroptionen unter „Kabelloses Debugging“ → „Gerät über Kopplungscode koppeln“ tippen und " +
                        "den Code in die Shizuku-Benachrichtigung eintippen.\n\n" +
                        "5. In Shizuku auf „Starten“ tippen. Fertig – zurück zu SKZ-Connect."))!
            .SetPositiveButton(L.T("Shizuku öffnen"), (_, _) => HelpPage.OpenShizuku(this))!
            .SetNeutralButton(L.T("Entwickleroptionen"), (_, _) => SystemPages.DeveloperOptions(this))!
            .SetNegativeButton(L.T("Schließen"), (_, _) => { })!
            .Show();
    }

    // ---------- Navigation ----------

    private void Next()
    {
        switch (_step)
        {
            case StepPermissions when !Permissions.HasBluetooth(this):
                AskBluetooth();
                return;
            case StepSetup when !IsOutputReady():
                new MaterialAlertDialogBuilder(this)
                    .SetTitle(L.T("Noch nicht fertig eingerichtet"))!
                    .SetMessage(L.T("Ohne diesen Schritt sehen Spiele deine Controller noch nicht. Du kannst die Einrichtung später " +
                                "auf der Startseite über „Beheben“ fortsetzen."))!
                    .SetPositiveButton(L.T("Trotzdem weiter"), (_, _) => Go(_step + 1))!
                    .SetNegativeButton(L.T("Zurück zur Einrichtung"), (_, _) => { })!
                    .Show();
                return;
            case StepDone:
                Finish(true);
                return;
        }
        Go(_step + 1);
    }

    private void Back()
    {
        if (_step == StepWelcome || (_step == StepChoose && Intent?.GetBooleanExtra(ExtraStartAtOutput, false) == true))
        {
            Finish(AppPrefs.SetupDone);
            return;
        }
        Go(_step - 1);
    }

    private void Go(int step)
    {
        _step = Math.Clamp(step, 0, StepCount - 1);
        if (!AppPrefs.SetupDone)
            AppPrefs.WizardStep = _step;
        if (_step == StepSetup)
            _ = OutputRouter.ResolveAsync(interactive: false);
        Show();
    }

    private static bool IsOutputReady() => AppPrefs.Mode switch
    {
        OutputPath.Touch => TouchService.Instance is not null,
        OutputPath.Root => RootShell.Instance.HasPermission(),
        _ => ShizukuShell.Instance.HasPermission(),
    };

    private void Finish(bool openMain)
    {
        if (_step == StepDone)
        {
            AppPrefs.SetupDone = true;
            AppPrefs.WizardStep = 0;
        }
        _ = ControllerService.Instance?.ReapplyOutputAsync();
        if (openMain && AppPrefs.SetupDone)
            StartActivity(new Intent(this, typeof(MainActivity)).AddFlags(global::Android.Content.ActivityFlags.ClearTop | global::Android.Content.ActivityFlags.SingleTop));
        Finish();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        Show(); // auf der Seite bleiben: Benachrichtigungen und Akku folgen dort noch
    }

    private sealed class Insets(View bottom, int pad) : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(View? v, WindowInsetsCompat? insets)
        {
            if (v is null || insets is null)
                return insets;
            var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout())!;
            v.SetPadding(bars.Left, bars.Top, bars.Right, 0); // Rahmen statt Titelleiste (feste Höhe)
            bottom.SetPadding(pad, Ui.Dp(v.Context!, 8), pad, Ui.Dp(v.Context!, 12) + bars.Bottom);
            return insets;
        }
    }

    private sealed class BackCallback(SetupWizardActivity a) : OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed() => a.Back();
    }
}
