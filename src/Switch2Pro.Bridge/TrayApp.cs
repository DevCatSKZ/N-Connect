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
    private Settings _settings;
    private SettingsForm? _settingsForm;
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
        Theme.Init(_settings.Theme, _settings.Transparency);
        if (_settings.LoadError is { } err)
            Log.Warn($"settings.json fehlerhaft, nutze Standardwerte: {err}");

        _icon = new NotifyIcon { Icon = CreateIcon(), Visible = true, Text = "N-Connect" };
        _icon.ContextMenuStrip = new ContextMenuStrip { Renderer = Theme.MenuRenderer(), ForeColor = Theme.Current.Text };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
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

        _manager = new ControllerManager(() => _settings, _factory);
        _manager.Changed += () => _ui.Post(_ => UpdateTooltip(), null);
        _manager.Notify += message => _ui.Post(_ =>
        {
            _balloonUrl = null;
            Balloon(2500, "N-Connect", message, ToolTipIcon.Info);
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
        if (Environment.GetCommandLineArgs().Any(a => a.StartsWith("--demo", StringComparison.Ordinal)))
            _manager.StartDemo();

        _settingsWatcher = new FileSystemWatcher(Paths.SettingsDir, Path.GetFileName(Paths.SettingsFile))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        // Editoren speichern oft in mehreren Schritten: entprellen statt die Oberfläche zu blockieren.
        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); ReloadSettings(); };
        _settingsWatcher.Changed += (_, _) => _ui.Post(_ => { _reloadTimer.Stop(); _reloadTimer.Start(); }, null);

        if (_settings.LoadError is not null)
            Balloon(5000, "Einstellungen fehlerhaft",
                "settings.json konnte nicht gelesen werden – es gelten die Standardwerte.", ToolTipIcon.Warning);
        UpdateTooltip();
        BuildMenu();
        _profileTimer.Tick += (_, _) => DetectProfile();
        _profileTimer.Start();
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_balloonUrl is { } url)
                Open(url);
        };
        _icon.BalloonTipClosed += (_, _) => _balloonUrl = null;
        CheckForUpdateAsync().Forget("Update-Prüfung");

        if (Program.ShowSignal is { } signal)
        {
            ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => _ui.Post(_ => ShowSettings(), null), null, -1, executeOnlyOnce: false);
        }
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

    private WiiPairForm? _wiiPairing;

    /// <summary>Wii-Fernbedienung / Wii U Pro Controller mit Windows koppeln (einmalig).</summary>
    internal void ShowWiiPairing()
    {
        if (_wiiPairing is { IsDisposed: false })
        {
            _wiiPairing.Activate();
            return;
        }
        _wiiPairing = new WiiPairForm();
        _wiiPairing.Show();
    }

    private void ShowWelcome()
    {
        using var welcome = new WelcomeForm();
        welcome.ShowDialog();
        if (welcome.OpenSettings)
            ShowSettings();
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
            if (outputChanged)
                _manager?.ApplyOutputMode(_settings.OutputMode);
        }, _manager);
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
        var oldMode = _settings.OutputMode;
        // Werte übernehmen statt das Objekt zu ersetzen – das offene Einstellungsfenster arbeitet darauf.
        _settings.CopyFrom(fresh);
        Theme.Init(_settings.Theme, _settings.Transparency);
        _icon.ContextMenuStrip!.Renderer = Theme.MenuRenderer();
        _icon.ContextMenuStrip.ForeColor = Theme.Current.Text;
        if (fresh.OutputMode != oldMode)
            _manager?.ApplyOutputMode(fresh.OutputMode);
        if (_settingsForm is { IsDisposed: false } form)
            form.ReloadValues(); // sonst arbeitet das Fenster mit veralteten Profilen weiter
        Log.Info("Einstellungen neu geladen");
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
                : string.Join("\n", players.Select(p => $"P{p.Index + 1} {ShortName(p.Kind)} {Battery(p)}"));
        }
        // NotifyIcon.Text ist auf 127 Zeichen begrenzt.
        text = Tr.T(text);
        _icon.Text = text.Length > 127 ? text[..127] : text;
        UpdateIcon();
    }

    /// <summary>Einblendung im Infobereich (in der Sprache der Oberfläche).</summary>
    private void Balloon(int milliseconds, string title, string text, ToolTipIcon icon) =>
        _icon.ShowBalloonTip(milliseconds, Tr.T(title), Tr.T(text), icon);

    private UpdateCheck.Update? _update;
    /// <summary>Ziel beim Klick auf die zuletzt gezeigte Einblendung (nur bei der Update-Meldung gesetzt).</summary>
    private string? _balloonUrl;

    private async Task CheckForUpdateAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        if (!_settings.CheckForUpdates)
            return;
        var update = await UpdateCheck.FindAsync(CancellationToken.None);
        if (update is null)
            return;
        _update = update;
        Log.Info($"Neue Version {update.Version} verfügbar (installiert: {UpdateCheck.Current})");
        _balloonUrl = update.Url;
        Balloon(8000, "Neue Version verfügbar",
            $"Version {update.Version} ist erschienen (installiert: {UpdateCheck.Current}). Klicken zum Herunterladen.", ToolTipIcon.Info);
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
            foreach (var p in players)
                menu.Items.Add(new ToolStripMenuItem($"Spieler {p.Index + 1} · {p.Kind.DisplayName()} {Battery(p)}") { Enabled = false });
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
        menu.Items.Add("Wii-Controller koppeln …", null, (_, _) => ShowWiiPairing());
        menu.Items.Add("Kurzanleitung", null, (_, _) => ShowWelcome());
        menu.Items.Add("Protokoll öffnen", null, (_, _) => Open(Paths.LogFile));
        if (_update is { } update)
            menu.Items.Add(new ToolStripMenuItem($"⬇ Neue Version {update.Version} herunterladen …", null, (_, _) => Open(update.Url)) { Font = _boldFont });
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
        _manager?.ApplyOutputMode(mode);
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
        _icon.Visible = false;
        _settingsForm?.Close();
        _settingsWatcher?.Dispose();
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
