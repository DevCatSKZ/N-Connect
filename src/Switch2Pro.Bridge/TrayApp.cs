using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>Symbol im Infobereich: Status, Einstellungen, Beenden.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string ViGEmDownload = "https://github.com/nefarius/ViGEmBus/releases";

    private readonly NotifyIcon _icon;
    private readonly SynchronizationContext _ui;
    private readonly PadFactory? _factory;
    private readonly ControllerManager? _manager;
    private readonly FileSystemWatcher? _settingsWatcher;
    private readonly SwitchCardWatcher? _cardWatcher;
    private Settings _settings;
    private SettingsForm? _settingsForm;
    private ControllerWidget? _widget;
    private Font? _boldFont;
    private DsuServer? _dsu;
    private readonly System.Windows.Forms.Timer _reloadTimer = new() { Interval = 300 };
    /// <summary>Prüft jede Sekunde das Programm im Vordergrund und wählt das passende Profil.</summary>
    private readonly System.Windows.Forms.Timer _profileTimer = new() { Interval = 1000 };

    public TrayApp()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        bool firstRun = !File.Exists(Paths.SettingsFile);
        _settings = Settings.Load(Paths.SettingsFile);
        Tr.Init(_settings.Language);
        Theme.Init(_settings.Theme, _settings.Transparency, _settings.ColorScheme);
        EnableAutostartOnce();
        if (_settings.LoadError is { } err)
            Log.Warn($"settings.json fehlerhaft, nutze Standardwerte: {err}");

        _icon = new NotifyIcon { Icon = CreateIcon(), Visible = true, Text = "N-Connect" };
        _icon.ContextMenuStrip = new ContextMenuStrip { Renderer = Theme.MenuRenderer(), ForeColor = Theme.Current.Text };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        try
        {
            _cardWatcher = new SwitchCardWatcher();
            _cardWatcher.CardArrived += OnSwitchCard;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Log.Warn($"SD-Karten-Erkennung nicht verfügbar: {Log.Reason(e)}");
        }
        // Linksklick = Einstellungen, Rechtsklick = Menü.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowSettings();
        };

        _factory = PadFactory.TryCreate();
        if (_factory is null)
        {
            _icon.Text = "N-Connect: ViGEmBus fehlt";
            Balloon(10000, "ViGEmBus-Treiber fehlt",
                "Ohne ViGEmBus kann kein virtueller Controller erzeugt werden. Bitte das Setup erneut ausführen " +
                "oder ViGEmBus installieren (Rechtsklick auf das Symbol → ViGEmBus herunterladen).", ToolTipIcon.Error);
            BuildMenu();
            return;
        }

        // HidHide aktualisiert (= neu installiert): seine Geräteliste ist dann leer – Controller beim Verbinden neu verstecken.
        if (HidHideUpdate.Installed?.ToString() is { } hidHide && hidHide != _settings.HidHideVersion)
        {
            if (_settings.HidHideVersion is not null)
            {
                Log.Info($"HidHide {_settings.HidHideVersion} → {hidHide}: Controller werden neu versteckt");
                _settings.HiddenDevices = [];
            }
            _settings.HidHideVersion = hidHide;
            SaveSettings();
        }

        _manager = new ControllerManager(() => _settings, _factory);
        _manager.Changed += () => _ui.Post(_ => UpdateTooltip(), null);
        _manager.Notify += message => _ui.Post(_ =>
        {
            _balloonUrl = null;
            Balloon(2500, "N-Connect", message, ToolTipIcon.Info);
        }, null);
        // Verbinden/Trennen: Meldung von N-Connect nur, wenn sie auch gewählt ist (Standard);
        // die Windows-Gerätemeldungen blendet ToastSenders.Apply ein bzw. aus.
        _manager.ConnectionNotify += message => _ui.Post(_ =>
        {
            if (_settings.ConnectNotify is not (ConnectNotifications.NConnect or ConnectNotifications.Both))
                return;
            _balloonUrl = null;
            Balloon(2500, "N-Connect", message, ToolTipIcon.Info);
        }, null);
        ApplyConnectNotify();
        // Einblendung beim Verbinden (abschaltbar unter Allgemein → Verbinden).
        _manager.LinkConnected += link => _ui.Post(_ =>
        {
            if (!_settings.ConnectOverlay || _manager.Players.FirstOrDefault(p => p.Links.Contains(link)) is not { } player)
                return;
            try { ConnectOverlay.ShowFor(player, link, _settings); }
            catch (Exception e) { Log.Warn($"Einblendung: {Log.Reason(e)}"); }
        }, null);
        _manager.ControllerConnected += (address, _) => _ui.Post(_ => RememberController(address), null);
        _manager.JoyConModeChanged += (address, single) => _ui.Post(_ =>
        {
            if (_settings.SetSingleJoyCon(address, single))
                SaveSettings();
        }, null);
        _manager.SaveRequested += () => _ui.Post(_ => SaveSettings(), null);
        if (_settings.DsuServer)
            _dsu = DsuServer.Start();
        _manager.StartAsync().Forget("Controller-Suche starten");
        bool demo = Environment.GetCommandLineArgs().Any(a => a.StartsWith("--demo", StringComparison.Ordinal));
        if (demo)
            _manager.StartDemo();
        else
            Task.Run(() => AutoPairLoopAsync(_autoPairCts.Token)).Forget("Controller automatisch koppeln");

        _settingsWatcher = new FileSystemWatcher(Paths.SettingsDir, Path.GetFileName(Paths.SettingsFile))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        // Editoren speichern oft in mehreren Schritten: entprellen statt die Oberfläche zu blockieren.
        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); ReloadSettings(); };
        void Changed(object? sender, FileSystemEventArgs e) => _ui.Post(_ => { _reloadTimer.Stop(); _reloadTimer.Start(); }, null);
        _settingsWatcher.Changed += Changed;
        // Viele Editoren (und N-Connect selbst) speichern über eine Zwischendatei, die die alte ersetzt.
        _settingsWatcher.Created += Changed;
        _settingsWatcher.Renamed += (s, e) => Changed(s, e);

        if (_settings.LoadError is not null)
            Balloon(5000, "Einstellungen fehlerhaft",
                "settings.json konnte nicht gelesen werden – es gelten die Standardwerte.", ToolTipIcon.Warning);
        UpdateTooltip();
        BuildMenu();
        SyncWidget();
        _profileTimer.Tick += (_, _) => DetectProfile();
        _profileTimer.Start();
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_balloonUrl is { } url)
                Open(url);
            else
                _balloonAction?.Invoke();
            _balloonAction = null;
        };
        _icon.BalloonTipClosed += (_, _) => { _balloonUrl = null; _balloonAction = null; };
        CheckForUpdateAsync().Forget("Update-Prüfung");

        if (Program.ShowSignal is { } signal)
        {
            ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => _ui.Post(_ => ShowSettings(), null), null, -1, executeOnlyOnce: false);
        }
        // Bluetooth-/WinRT-Initialisierung kann auf diesem Thread einen DPI-unaware-Kontext hinterlassen
        // (Fenster skalieren dann falsch); den UI-Thread zurück auf PerMonitorV2 setzen.
        Dpi.BeginPerMonitorV2();
        if (firstRun)
            _ui.Post(_ => ShowWelcome(), null);
        else if (!Environment.GetCommandLineArgs().Contains("--autostart"))
            _ui.Post(_ => ShowSettings(), null); // von Hand gestartet: Fenster gleich zeigen
    }

    private void RememberController(string address)
    {
        if (_settings.IsKnown(address))
            return;
        _settings.KnownControllers = [.. _settings.KnownControllers, address];
        SaveSettings();
    }

    private readonly CancellationTokenSource _autoPairCts = new();

    /// <summary>
    /// Switch-1-, NSO- und Wii-Controller im Kopplungsmodus im Hintergrund selbst koppeln (Einstellung „Neue Controller
    /// automatisch koppeln“). Jede Suche belegt den Bluetooth-Adapter ~2,5 s – deshalb nur, solange niemand spielt.
    /// </summary>
    private async Task AutoPairLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(5000, ct);
            long lastScan = 0;
            while (!ct.IsCancellationRequested)
            {
                // Mit verbundenen Switch-2-Controllern (Bluetooth LE) seltener suchen: jede Suche nimmt ihnen kurz Funkzeit.
                int interval = SwitchTwoConnected() ? 15_000 : 6_000;
                if (_settings.AutoPair && Environment.TickCount64 - lastScan >= interval && CanScanInBackground())
                {
                    lastScan = Environment.TickCount64;
                    var paired = ControllerPairing.BackgroundScan();
                    if (paired.Count > 0)
                    {
                        _ui.Post(_ =>
                        {
                            _balloonUrl = null;
                            Balloon(4000, "Controller gekoppelt",
                                $"Gekoppelt: {string.Join(", ", paired)}\nDer Controller erscheint gleich in der Übersicht.", ToolTipIcon.Info);
                        }, null);
                    }
                }
                await Task.Delay(2000, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or System.ComponentModel.Win32Exception)
        {
            Log.Warn($"Automatisches Koppeln nicht verfügbar: {Log.Reason(e)}");
        }
    }

    /// <summary>
    /// Darf jetzt im Hintergrund gesucht werden? Eine Suche belegt den Bluetooth-Funk – ein Switch-2-Controller, der
    /// sich gerade verbindet, scheitert dabei („Unreachable“), und wer spielt, merkt Aussetzer. Deshalb nur, wenn
    /// gerade keiner verbunden wird und seit 20 Sekunden niemand spielt (Suchlauf nur 1,3 s, siehe ControllerPairing).
    /// </summary>
    private bool CanScanInBackground()
    {
        if (_manager is null || _manager.IsConnecting)
            return false;
        long now = Environment.TickCount64;
        return !_manager.Players.Any(p => now - p.LastActivity < 20_000);
    }

    private bool SwitchTwoConnected() =>
        _manager?.Players.Any(p => p.Links.Any(l => l.Transport == Links.Transport.BluetoothLE)) == true;

    private PairForm? _wiiPairing;

    /// <summary>Fenster „Controller koppeln“ (Switch 1, Nintendo Switch Online, Wii) – gezielt suchen.</summary>
    internal void ShowWiiPairing()
    {
        if (_wiiPairing is { IsDisposed: false })
        {
            _wiiPairing.Activate();
            return;
        }
        _wiiPairing = new PairForm(_manager);
        _wiiPairing.Show();
    }

    private PairingDataForm? _pairingData;

    /// <summary>Fenster „Kopplungsdaten“ (Switch-SD-Karte, anderer PC); mit Karte gleich deren Daten zeigen.</summary>
    internal void ShowPairingData(SwitchCardInfo? card)
    {
        if (_pairingData is { IsDisposed: false })
        {
            if (card is null)
            {
                _pairingData.Activate();
                return;
            }
            _pairingData.Close(); // neu öffnen, damit die eben eingesteckte Karte gezeigt wird
        }
        _pairingData = new PairingDataForm(_settings, () =>
        {
            SaveSettings();
            Theme.Init(_settings.Theme, _settings.Transparency, _settings.ColorScheme);
            if (_settingsForm is { IsDisposed: false } form)
                form.ReloadValues();
        }, card);
        _pairingData.Show();
        _pairingData.Activate();
    }

    /// <summary>Switch-SD-Karte eingesteckt: Übernahme anbieten (abschaltbar unter „Joy-Con &amp; Wii“).</summary>
    private void OnSwitchCard(SwitchCardInfo card)
    {
        if (!_settings.SwitchCardHint)
            return;
        _balloonUrl = null;
        Balloon(8000, "Switch-SD-Karte erkannt", card.HasPairingExport
            ? "Kopplungsdaten der Controller gefunden – klicken, um sie anzusehen und zu übernehmen."
            : "Klicken, um Kopplungsdaten der Controller zu übernehmen.", ToolTipIcon.Info, () => ShowPairingData(card));
    }

    private void ShowWelcome()
    {
        using var welcome = new WelcomeForm();
        welcome.ShowDialog();
        if (welcome.OpenSettings)
            ShowSettings();
    }

    /// <summary>
    /// Nach der Installation startet N-Connect standardmäßig mit Windows (unsichtbar im Infobereich). Einmalig beim
    /// ersten Start eingerichtet – danach entscheidet nur noch der Schalter unter „Allgemein“.
    /// </summary>
    private void EnableAutostartOnce()
    {
        if (_settings.AutostartConfigured || _settings.LoadError is not null)
            return;
        try
        {
            if (!Autostart.IsEnabled && !Autostart.IsEnabledForAllUsers)
                Autostart.Set(true);
            _settings.AutostartConfigured = true;
            SaveSettings();
            Log.Info("Autostart eingerichtet");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn($"Autostart nicht eingerichtet: {Log.Reason(e)}");
        }
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_settings, outputChanged =>
        {
            SaveSettings();
            SyncWidget();
            if (outputChanged)
                _manager?.ApplyOutputMode();
        }, _manager, ShowWiiPairing, () => ShowPairingData(null));
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void ReloadSettings()
    {
        if (!File.Exists(Paths.SettingsFile))
            return; // gelöscht: aktuelle Werte behalten statt alles auf Standard zu setzen
        var fresh = Settings.Load(Paths.SettingsFile);
        if (fresh.LoadError is not null)
        {
            Log.Warn($"settings.json fehlerhaft, Änderung ignoriert: {fresh.LoadError}");
            return;
        }
        // Werte übernehmen statt das Objekt zu ersetzen – das offene Einstellungsfenster arbeitet darauf.
        _settings.CopyFrom(fresh);
        Theme.Init(_settings.Theme, _settings.Transparency, _settings.ColorScheme);
        _icon.ContextMenuStrip!.Renderer = Theme.MenuRenderer();
        _icon.ContextMenuStrip.ForeColor = Theme.Current.Text;
        _manager?.ApplyOutputMode(); // allgemein oder je Controller geändert – nur Abweichende werden neu angelegt
        ApplyConnectNotify();
        if (_settingsForm is { IsDisposed: false } form)
            form.ReloadValues(); // sonst arbeitet das Fenster mit veralteten Profilen weiter
        SyncWidget();
        Log.Info("Einstellungen neu geladen");
    }

    /// <summary>Desktop-Widget nach den Einstellungen zeigen, ausblenden oder auffrischen (Farbschema, Optionen).</summary>
    private void SyncWidget()
    {
        if (_manager is null)
            return;
        if (!_settings.DesktopWidget)
        {
            _widget?.Close();
            _widget = null;
            return;
        }
        if (_widget is { IsDisposed: false } widget)
        {
            widget.UpdateContent(force: true);
            return;
        }
        try
        {
            // Änderungen aus dem Widget-Menü (Position, Optionen) speichern und im offenen Einstellungsfenster zeigen.
            _widget = new ControllerWidget(_manager, () => _settings, () =>
            {
                SaveSettings();
                SyncWidget();
                if (_settingsForm is { IsDisposed: false } form)
                    form.ReloadValues();
            }, ShowSettings);
            _widget.Show();
        }
        catch (Exception e)
        {
            Log.Warn($"Desktop-Widget: {Log.Reason(e)}");
            _widget = null;
        }
    }

    private void SaveSettings()
    {
        if (_settingsWatcher is not null)
            _settingsWatcher.EnableRaisingEvents = false;
        try
        {
            _settings.Save(Paths.SettingsFile);
        }
        catch (Exception e)
        {
            Log.Error("Einstellungen speichern", e);
        }
        finally
        {
            if (_settingsWatcher is not null)
                _settingsWatcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>
    /// Einstellung „Meldung bei Verbinden“ anwenden: Meldet N-Connect (Standard) oder gar niemand,
    /// werden die Gerätemeldungen von Windows stummgeschaltet; bei „Windows“ bzw. „Beide“ kommen sie wieder.
    /// </summary>
    private void ApplyConnectNotify() =>
        ToastSenders.Apply(_settings.ConnectNotify is ConnectNotifications.NConnect or ConnectNotifications.None);

    private void UpdateTooltip()
    {
        string text;
        if (_manager is null)
            text = "N-Connect: ViGEmBus fehlt";
        else if (_manager.AdapterProblem is { } problem)
            text = $"N-Connect: {problem}";
        else
        {
            var players = _manager.Players;
            text = players.Count == 0
                ? "N-Connect: warte auf Controller (SYNC drücken)"
                : string.Join("\n", players.Select(p => $"P{p.Index + 1} {ShortLabel(p)} {Battery(p)}"));
        }
        // NotifyIcon.Text ist auf 127 Zeichen begrenzt.
        text = Tr.T(text);
        _icon.Text = text.Length > 127 ? text[..127] : text;
        UpdateIcon();
    }

    /// <summary>Einblendung im Infobereich (in der Sprache der Oberfläche).</summary>
    private void Balloon(int milliseconds, string title, string text, ToolTipIcon icon, Action? onClick = null)
    {
        _balloonAction = onClick;
        _icon.ShowBalloonTip(milliseconds, Tr.T(title), Tr.T(text), icon);
    }

    /// <summary>Aktion beim Klick auf die zuletzt gezeigte Einblendung (z. B. Kopplungsdaten der SD-Karte zeigen).</summary>
    private Action? _balloonAction;

    private UpdateCheck.Update? _update;
    /// <summary>Ziel beim Klick auf die zuletzt gezeigte Einblendung (nur bei der Update-Meldung gesetzt).</summary>
    private string? _balloonUrl;

    private async Task CheckForUpdateAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        var update = _settings.CheckForUpdates ? await UpdateCheck.FindAsync(CancellationToken.None) : null;
        if (update is null)
        {
            // Erst N-Connect, dann HidHide – nie zwei Update-Meldungen auf einmal.
            await CheckForHidHideUpdateAsync();
            return;
        }
        _update = update;
        Log.Info($"Neue Version {update.Version} verfügbar (installiert: {UpdateCheck.Current})");
        if (update.Setup is null)
        {
            _balloonUrl = update.Url; // kein Installer im Release: Seite öffnen
            Balloon(8000, "Neue Version verfügbar",
                $"Version {update.Version} ist erschienen (installiert: {UpdateCheck.Current}). Klicken zum Herunterladen.", ToolTipIcon.Info);
            return;
        }
        _balloonUrl = null;
        Balloon(8000, "Neue Version verfügbar",
            $"Version {update.Version} ist erschienen (installiert: {UpdateCheck.Current}). Klicken zum Installieren.", ToolTipIcon.Info,
            () => InstallUpdateAsync().Forget("Update installieren"));
    }

    private bool _updating;

    /// <summary>
    /// Ein-Klick-Update: nachfragen, Installer herunterladen und prüfen, still installieren lassen, N-Connect beenden.
    /// Der Installer schließt N-Connect ohnehin und startet es danach wieder.
    /// </summary>
    private async Task InstallUpdateAsync()
    {
        if (_update is not { Setup: { } setup } update || _updating)
            return;
        bool portable = setup.IsZip;
        if (Tr.Show(null, $"Version {update.Version} herunterladen und installieren? N-Connect wird dafür kurz beendet und " +
                          "danach wieder gestartet." + (portable ? "" : " Windows fragt dabei nach Administratorrechten."),
                "N-Connect aktualisieren", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _updating = true;
        try
        {
            _balloonUrl = null;
            Balloon(3000, "N-Connect aktualisieren", $"Lade Version {update.Version} herunter …", ToolTipIcon.Info);
            string? path = await UpdateCheck.DownloadAsync(setup, null, CancellationToken.None);
            if (path is null)
            {
                if (Tr.Show(null, "Der Download hat nicht geklappt oder die Datei war fehlerhaft. Die Release-Seite im Browser öffnen?",
                        "N-Connect aktualisieren", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    Open(update.Url);
                return;
            }
            if (portable)
            {
                // ZIP: neue EXE neben die laufende kopieren – geht erst, nachdem dieser Prozess beendet ist.
                // Ein kleines Skript wartet darauf, ersetzt die Datei und startet N-Connect wieder.
                if (!PreparePortableUpdate(path))
                {
                    Log.Warn("Update: ZIP konnte nicht entpackt werden");
                    return;
                }
                Log.Info($"Update auf {update.Version}: portable EXE vorbereitet, N-Connect wird beendet");
                ExitThread();
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART")
                    { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                // Administratorrechte abgelehnt o. Ä.: N-Connect läuft einfach weiter.
                Log.Info($"Update: Installer nicht gestartet: {Log.Reason(e)}");
                return;
            }
            Log.Info($"Update auf {update.Version}: Installer gestartet, N-Connect wird beendet");
            ExitThread();
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>
    /// Portable-Update: ZIP entpacken, ein Hilfsskript schreiben, das nach unserem Prozess-Ende die neue EXE
    /// neben die alte kopiert und N-Connect wieder startet. Liefert false, wenn das ZIP kein N-Connect.exe enthielt.
    /// </summary>
    private bool PreparePortableUpdate(string zipPath)
    {
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "N-Connect-Update", "portable");
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, dir);
            string? exe = Directory.GetFiles(dir, "N-Connect.exe", SearchOption.AllDirectories).FirstOrDefault();
            string? target = Environment.ProcessPath;
            if (exe is null || target is null)
                return false;
            string script = Path.Combine(Path.GetTempPath(), "N-Connect-Update", "update.cmd");
            int pid = Environment.ProcessId;
            // „%“ in Pfaden würde cmd als Variable lesen.
            static string Batch(string path) => path.Replace("%", "%%");
            // UTF-8 ohne BOM + chcp 65001: sonst liest cmd Umlaute im Pfad (z. B. Benutzername „Jürgen“) in der
            // OEM-Codepage falsch und findet weder die neue noch die alte EXE. Kopieren mehrmals versuchen, weil die
            // EXE kurz nach Prozess-Ende noch gesperrt sein kann.
            File.WriteAllText(script,
                "@echo off\r\n" +
                "chcp 65001 >nul\r\n" +
                ":wait\r\n" +
                $"tasklist /FI \"PID eq {pid}\" | findstr /C:\" {pid} \" >nul\r\n" +
                "if not errorlevel 1 (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                "set tries=0\r\n" +
                ":copy\r\n" +
                $"copy /y \"{Batch(exe)}\" \"{Batch(target)}\" >nul && goto run\r\n" +
                "set /a tries+=1\r\n" +
                "if %tries% lss 10 (timeout /t 1 /nobreak >nul & goto copy)\r\n" +
                ":run\r\n" +
                $"start \"\" \"{Batch(target)}\"\r\n" +
                "(goto) 2>nul & del \"%~f0\"\r\n",
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden });
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
                                      or InvalidDataException or ArgumentException)
        {
            Log.Warn($"Update: portable EXE nicht vorbereitet: {Log.Reason(e)}");
            return false;
        }
    }

    private async Task CheckForHidHideUpdateAsync()
    {
        if (!_settings.CheckHidHideUpdates || HidHideUpdate.Installed is not { } installed)
            return;
        var update = await HidHideUpdate.FindAsync(installed, CancellationToken.None);
        if (update is null)
            return;
        Log.Info($"HidHide {update.Version} verfügbar (installiert: {installed})");
        _balloonUrl = null;
        Balloon(8000, "HidHide-Update verfügbar",
            $"HidHide {update.Version} ist erschienen (installiert: {installed}). Klicken zum Installieren.", ToolTipIcon.Info,
            () => InstallHidHideUpdateAsync(update).Forget("HidHide aktualisieren"));
    }

    /// <summary>
    /// HidHide-Update: nachfragen, Installer von GitHub laden, Signatur von Nefarius prüfen und das offizielle Setup
    /// (mit Oberfläche) starten. Es entfernt die alte Version und verlangt einen Neustart; danach versteckt N-Connect
    /// die Controller neu (siehe <see cref="Settings.HidHideVersion"/>).
    /// </summary>
    private async Task InstallHidHideUpdateAsync(HidHideUpdate.Update update)
    {
        if (_updating)
            return;
        if (Tr.Show(null, $"HidHide {update.Version} herunterladen und installieren? Das HidHide-Setup entfernt zuerst die alte " +
                          "Version und verlangt einen Neustart von Windows – bitte seinen Anweisungen folgen. Windows fragt dabei " +
                          "nach Administratorrechten. Danach versteckt N-Connect die Controller automatisch neu.",
                "HidHide aktualisieren", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _updating = true;
        try
        {
            _balloonUrl = null;
            Balloon(3000, "HidHide aktualisieren", $"Lade HidHide {update.Version} herunter …", ToolTipIcon.Info);
            string? path = await UpdateCheck.DownloadAsync(update.Setup, null, CancellationToken.None);
            if (path is not null && !HidHideUpdate.IsSignedByNefarius(path))
            {
                try { File.Delete(path); } catch (Exception) { /* bleibt im Temp-Ordner */ }
                path = null;
            }
            if (path is null)
            {
                if (Tr.Show(null, "Der Download hat nicht geklappt oder die Datei war fehlerhaft. Die Release-Seite im Browser öffnen?",
                        "HidHide aktualisieren", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    Open(HidHideUpdate.ReleasesPage);
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                Log.Info($"HidHide-Update auf {update.Version}: Setup gestartet");
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                Log.Info($"HidHide-Update: Setup nicht gestartet: {Log.Reason(e)}");
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private static string Battery(Player p)
    {
        var states = p.Links.Select(l => l.LastState).Where(s => s is { BatteryPercent: >= 0 }).ToList();
        if (states.Count == 0)
            return "";
        var low = states.MinBy(s => s!.BatteryPercent)!;
        return $"{low.BatteryPercent} %{(low.Charging ? " ⚡" : "")}";
    }

    private static string ShortName(ControllerKind kind) => kind switch
    {
        ControllerKind.Pro2 => "Pro 2",
        ControllerKind.Pro1 => "Pro",
        ControllerKind.JoyCon2Left => "Joy-Con 2 L",
        ControllerKind.JoyCon2Right => "Joy-Con 2 R",
        ControllerKind.JoyCon1Left => "Joy-Con L",
        ControllerKind.JoyCon1Right => "Joy-Con R",
        ControllerKind.JoyConPair => "Joy-Con L+R",
        ControllerKind.GameCube2 => "GameCube",
        ControllerKind.NesController => "NES",
        ControllerKind.SnesController => "SNES",
        ControllerKind.N64Controller => "N64",
        ControllerKind.MegaDrive => "Mega Drive",
        ControllerKind.WiiRemote => "Wii",
        ControllerKind.WiiUPro => "Wii U Pro",
        _ => "Controller",
    };

    /// <summary>Kurzer Name für den Tooltip (max. 127 Zeichen insgesamt): eigener Name, sonst die Kurzform der Art.</summary>
    private string ShortLabel(Player p)
    {
        var names = p.Links.Select(l => _settings.NameFor(l.Address)).Where(n => n is not null).ToList();
        string label = names.Count > 0 ? string.Join("+", names) : ShortName(p.Kind);
        return label.Length > 18 ? label[..17] + "…" : label;
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        foreach (var item in menu.Items.Cast<ToolStripItem>().ToList())
            item.Dispose(); // entfernt das Element zugleich aus dem Menü
        menu.Items.Add(new ToolStripMenuItem("N-Connect") { Enabled = false, Font = _boldFont ??= new Font(menu.Font, FontStyle.Bold) });

        if (_manager is null)
        {
            menu.Items.Add(new ToolStripMenuItem("ViGEmBus-Treiber fehlt") { Enabled = false });
            menu.Items.Add("ViGEmBus herunterladen …", null, (_, _) => Open(ViGEmDownload));
        }
        else if (_manager.AdapterProblem is { } problem)
        {
            menu.Items.Add(new ToolStripMenuItem(problem) { Enabled = false });
        }
        else
        {
            var players = _manager.Players;
            if (players.Count == 0)
                menu.Items.Add(new ToolStripMenuItem(_manager.IsConnecting
                    ? "Verbinde …"
                    : "Kein Controller – SYNC-Taste am Controller kurz drücken") { Enabled = false });
            // Je Spieler ein Untermenü: wer ist das (vibrieren), Platz wechseln, trennen.
            foreach (var p in players)
            {
                var player = p;
                var item = new ToolStripMenuItem($"Spieler {p.Index + 1} · {p.DisplayName(_settings)} {Battery(p)}");
                item.DropDownItems.Add("Vibrieren", null, (_, _) => player.IdentifyAsync().Forget("Vibrieren"));
                var slots = new ToolStripMenuItem("Spielerplatz");
                for (int i = 0; i < players.Count; i++)
                {
                    int slot = i;
                    slots.DropDownItems.Add(Radio($"Spieler {i + 1}", player.Index == i, () => _manager.MovePlayer(player, slot)));
                }
                item.DropDownItems.Add(slots);
                var own = player.Links.Select(l => _settings.OutputFor(l.Address)).FirstOrDefault(m => m is not null);
                var appears = new ToolStripMenuItem("Erscheint als");
                appears.DropDownItems.Add(Radio("Wie allgemein", own is null, () => _manager.SetPlayerOutput(player, null)));
                appears.DropDownItems.Add(Radio("Xbox 360", own == OutputMode.Xbox360, () => _manager.SetPlayerOutput(player, OutputMode.Xbox360)));
                appears.DropDownItems.Add(Radio("DualShock 4", own == OutputMode.DualShock4, () => _manager.SetPlayerOutput(player, OutputMode.DualShock4)));
                item.DropDownItems.Add(appears);
                item.DropDownItems.Add("Trennen", null, (_, _) => _manager.Disconnect(player));
                menu.Items.Add(item);
            }
            if (players.Count > 1)
                menu.Items.Add("Alle Controller trennen", null, (_, _) =>
                {
                    foreach (var p in _manager.Players)
                        _manager.Disconnect(p);
                });
        }

        menu.Items.Add(new ToolStripSeparator());
        var output = new ToolStripMenuItem("Ausgabe als");
        output.DropDownItems.Add(Radio("Xbox-360-Controller (empfohlen)", _settings.OutputMode == OutputMode.Xbox360,
            () => SetOutput(OutputMode.Xbox360)));
        output.DropDownItems.Add(Radio("DualShock 4 (mit Gyro für Steam/Emulatoren)", _settings.OutputMode == OutputMode.DualShock4,
            () => SetOutput(OutputMode.DualShock4)));
        menu.Items.Add(output);

        var layout = new ToolStripMenuItem("Tastenbelegung");
        layout.DropDownItems.Add(Radio("Xbox-Belegung (untere Taste = A)", _settings.Layout == FaceButtonLayout.Xbox,
            () => { _settings.Layout = FaceButtonLayout.Xbox; SaveSettings(); }));
        layout.DropDownItems.Add(Radio("Switch-2-Pro-Belegung (A bleibt A)", _settings.Layout == FaceButtonLayout.Switch2,
            () => { _settings.Layout = FaceButtonLayout.Switch2; SaveSettings(); }));
        menu.Items.Add(layout);

        if (_settings.NamedProfiles.Count > 0)
        {
            var profiles = new ToolStripMenuItem($"Profil: {_settings.CurrentProfile()?.Name ?? "Standard"}");
            profiles.DropDownItems.Add(Radio("Automatisch (nach Spiel/Programm)", _settings.ForcedProfile is null, () => ForceProfile(null)));
            profiles.DropDownItems.Add(new ToolStripSeparator());
            profiles.DropDownItems.Add(Radio("Standard", _settings.ForcedProfile == "", () => ForceProfile("")));
            foreach (var p in _settings.NamedProfiles)
            {
                string name = p.Name;
                var item = Radio(name, _settings.ForcedProfile == name, () => ForceProfile(name));
                item.Tag = Tr.UserData; // Profilname nicht übersetzen
                profiles.DropDownItems.Add(item);
            }
            menu.Items.Add(profiles);
        }

        menu.Items.Add(new ToolStripMenuItem("Vibration", null, (_, _) => { _settings.RumbleEnabled = !_settings.RumbleEnabled; SaveSettings(); })
            { Checked = _settings.RumbleEnabled });
        menu.Items.Add(new ToolStripMenuItem("Mit Windows starten", null, (_, _) => Autostart.Set(!Autostart.IsEnabled))
        {
            Checked = Autostart.IsEnabled || Autostart.IsEnabledForAllUsers,
            Enabled = !Autostart.IsEnabledForAllUsers, // vom Installer für alle Benutzer eingetragen
        });

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Einstellungen …", null, (_, _) => ShowSettings()) { Font = _boldFont ??= new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add("Controller koppeln …", null, (_, _) => ShowWiiPairing());
        menu.Items.Add("Kurzanleitung", null, (_, _) => ShowWelcome());
        menu.Items.Add("Protokoll öffnen", null, (_, _) => Open(Paths.LogFile));
        if (_update is { } update)
            menu.Items.Add(update.Setup is null
                ? new ToolStripMenuItem($"⬇ Neue Version {update.Version} herunterladen …", null, (_, _) => Open(update.Url)) { Font = _boldFont }
                : new ToolStripMenuItem($"⬇ Auf Version {update.Version} aktualisieren …", null,
                    (_, _) => InstallUpdateAsync().Forget("Update installieren")) { Font = _boldFont });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitThread());
        Tr.Apply(menu.Items);
    }

    /// <summary>Profil fest wählen ("" = Standard) oder null = automatisch nach Programm.</summary>
    private void ForceProfile(string? name)
    {
        _settings.ForcedProfile = name;
        SaveSettings();
        Log.Info($"Profil: {(name is null ? "automatisch" : name == "" ? "Standard (fest)" : $"{name} (fest)")}");
    }

    /// <summary>Profil zum Programm im Vordergrund suchen (nur wenn es benannte Profile gibt).</summary>
    private void DetectProfile()
    {
        if (_settings.NamedProfiles.Count == 0)
        {
            _settings.DetectedProfile = null;
            return;
        }
        string? exe = ForegroundProgram();
        if (exe is null || exe.Equals(Path.GetFileName(Environment.ProcessPath), StringComparison.OrdinalIgnoreCase))
            return; // eigenes Fenster (z. B. beim Bearbeiten der Profile): Profil beibehalten
        string? detected = _settings.NamedProfiles.FirstOrDefault(p => p.MatchesProgram(exe))?.Name;
        if (detected == _settings.DetectedProfile)
            return;
        _settings.DetectedProfile = detected;
        Log.Info($"Programm im Vordergrund: {exe} → Profil {detected ?? "Standard"}");
        if (_settings.ForcedProfile is null)
        {
            _balloonUrl = null;
            Balloon(1500, "N-Connect", $"Profil „{detected ?? "Standard"}“ aktiv", ToolTipIcon.None);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private static string? ForegroundProgram()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out uint pid) == 0)
                return null;
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName + ".exe";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null; // Prozess schon beendet oder geschützt
        }
    }

    private static ToolStripMenuItem Radio(string text, bool selected, Action onClick) =>
        new(text, null, (_, _) => onClick()) { Checked = selected };

    private void SetOutput(OutputMode mode)
    {
        if (_settings.OutputMode == mode)
            return;
        _settings.OutputMode = mode;
        SaveSettings();
        _manager?.ApplyOutputMode();
    }

    private static void Open(string target, string? args = null)
    {
        try
        {
            if (args is null && !target.StartsWith("http", StringComparison.Ordinal) && !File.Exists(target))
                return;
            Process.Start(new ProcessStartInfo(target, args ?? "") { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Error($"Öffnen fehlgeschlagen: {target}", e);
        }
    }

    /// <summary>Symbol zur Laufzeit zeichnen (stilisierter Controller), damit keine Binärdatei nötig ist.</summary>
    private static Icon CreateIcon() => DrawIcon(null, false);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Controller-Symbol, mit Akku: unten ein Balken in der Farbe des Ladestands (grün/gelb/rot) des schwächsten
    /// Controllers, beim Laden mit Blitz. So sieht man den Akku, ohne das Fenster zu öffnen.
    /// </summary>
    private static Icon DrawIcon(int? battery, bool charging)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            // Logo; mit Akkuanzeige etwas kleiner und nach oben gerückt.
            Branding.DrawLogo(g, battery is null ? new RectangleF(0, 0, 32, 32) : new RectangleF(5, 0, 22, 22));
            if (battery is { } percent)
            {
                var frame = new RectangleF(1, 23, 27, 8);
                using (var back = new SolidBrush(Color.FromArgb(230, 20, 20, 24)))
                    g.FillRectangle(back, frame.X - 1, frame.Y - 1, frame.Width + 5, frame.Height + 2);
                using (var pen = new Pen(Color.White, 1.2f))
                    g.DrawRectangle(pen, frame.X, frame.Y, frame.Width, frame.Height);
                using (var tip = new SolidBrush(Color.White))
                    g.FillRectangle(tip, frame.Right + 1, frame.Y + 2, 2, 4);
                var level = percent < 15 ? Color.FromArgb(235, 70, 60) : percent < 35 ? Color.FromArgb(245, 185, 40) : Color.FromArgb(80, 210, 110);
                using (var fill = new SolidBrush(level))
                    g.FillRectangle(fill, frame.X + 1.5f, frame.Y + 1.5f, Math.Max(2f, (frame.Width - 3) * percent / 100f), frame.Height - 3);
                if (charging)
                {
                    using var bolt = new SolidBrush(Color.White);
                    g.FillPolygon(bolt, [new PointF(16, 22), new PointF(11, 28), new PointF(15, 28), new PointF(13, 33), new PointF(19, 26), new PointF(15, 26)]);
                }
            }
        }
        // Kopie anlegen und das Windows-Handle sofort freigeben (sonst wächst der Handle-Verbrauch bei jeder Änderung).
        IntPtr handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private (int? Battery, bool Charging) _shownBattery = (null, false);

    /// <summary>Symbol nur neu zeichnen, wenn sich die angezeigte Akkustufe ändert (5-%-Schritte).</summary>
    private void UpdateIcon()
    {
        var states = _manager?.Players.SelectMany(p => p.Links).Select(l => l.LastState).Where(s => s is { BatteryPercent: >= 0 }).ToList() ?? [];
        var low = states.MinBy(s => s!.BatteryPercent);
        (int?, bool) wanted = low is null ? (null, false) : (low.BatteryPercent / 5 * 5, low.Charging);
        if (wanted == _shownBattery)
            return;
        _shownBattery = wanted;
        var old = _icon.Icon;
        _icon.Icon = DrawIcon(wanted.Item1, wanted.Item2);
        old?.Dispose();
    }

    protected override void ExitThreadCore()
    {
        _autoPairCts.Cancel();
        _icon.Visible = false;
        _settingsForm?.Close();
        _widget?.Close();
        _settingsWatcher?.Dispose();
        _cardWatcher?.Dispose();
        _pairingData?.Close();
        _reloadTimer.Dispose();
        _profileTimer.Dispose();
        if (_manager is not null)
        {
            // Sitzungen sauber beenden (Vibration stoppen, virtuelle Controller entfernen).
            // Der Manager wartet laufende Verbindungsversuche ab; erst danach den ViGEm-Client freigeben.
            Task.Run(async () => await _manager.DisposeAsync()).Wait(TimeSpan.FromSeconds(8));
        }
        _dsu?.Dispose();
        _factory?.Dispose();
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
