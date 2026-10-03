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

    public TrayApp()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        bool firstRun = !File.Exists(Paths.SettingsFile);
        _settings = Settings.Load(Paths.SettingsFile);
        if (_settings.LoadError is { } err)
            Log.Warn($"settings.json fehlerhaft, nutze Standardwerte: {err}");

        _icon = new NotifyIcon { Icon = CreateIcon(), Visible = true, Text = "Switch 2 Pro Controller" };
        _icon.ContextMenuStrip = new ContextMenuStrip();
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
            _icon.Text = "Switch 2 Pro: ViGEmBus fehlt";
            _icon.ShowBalloonTip(10000, "ViGEmBus-Treiber fehlt",
                "Ohne ViGEmBus kann kein virtueller Controller erzeugt werden. Bitte das Setup erneut ausführen " +
                "oder ViGEmBus installieren (Rechtsklick auf das Symbol → ViGEmBus herunterladen).", ToolTipIcon.Error);
            BuildMenu();
            return;
        }

        _manager = new ControllerManager(() => _settings, _factory);
        _manager.Changed += () => _ui.Post(_ => UpdateTooltip(), null);
        _manager.Notify += message => _ui.Post(_ => _icon.ShowBalloonTip(2500, "Switch 2 Pro Controller", message, ToolTipIcon.Info), null);
        _manager.ControllerConnected += address => _ui.Post(_ => RememberController(address), null);
        _ = _manager.StartAsync();

        _settingsWatcher = new FileSystemWatcher(Paths.SettingsDir, Path.GetFileName(Paths.SettingsFile))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _settingsWatcher.Changed += (_, _) => _ui.Post(_ => ReloadSettings(), null);

        if (_settings.LoadError is not null)
            _icon.ShowBalloonTip(5000, "Einstellungen fehlerhaft",
                "settings.json konnte nicht gelesen werden – es gelten die Standardwerte.", ToolTipIcon.Warning);
        UpdateTooltip();
        BuildMenu();

        // Der Installer startet uns nach der Installation mit --autostart (im Kontext des
        // angemeldeten Benutzers), damit der Autostart für genau diesen Benutzer eingetragen wird.
        if (Environment.GetCommandLineArgs().Contains("--autostart") && !Autostart.IsEnabled)
            Autostart.Set(true);
        if (firstRun)
            _ui.Post(_ => ShowWelcome(), null);
    }

    private void RememberController(string address)
    {
        if (_settings.IsKnown(address))
            return;
        _settings.KnownControllers = [.. _settings.KnownControllers, address];
        SaveSettings();
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
        }, () => _manager?.Sessions.FirstOrDefault());
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void ReloadSettings()
    {
        // Editor speichern oft in mehreren Schritten: kurz warten.
        Thread.Sleep(150);
        var fresh = Settings.Load(Paths.SettingsFile);
        if (fresh.LoadError is not null)
        {
            Log.Warn($"settings.json fehlerhaft, Änderung ignoriert: {fresh.LoadError}");
            return;
        }
        var oldMode = _settings.OutputMode;
        // Werte übernehmen statt das Objekt zu ersetzen – das offene Einstellungsfenster arbeitet darauf.
        _settings.CopyFrom(fresh);
        if (fresh.OutputMode != oldMode)
            _manager?.ApplyOutputMode(fresh.OutputMode);
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
            text = "Switch 2 Pro: ViGEmBus fehlt";
        else if (_manager.AdapterProblem is { } problem)
            text = $"Switch 2 Pro: {problem}";
        else
        {
            var sessions = _manager.Sessions;
            text = sessions.Count == 0
                ? "Switch 2 Pro: warte auf Controller (SYNC drücken)"
                : "Switch 2 Pro: " + string.Join(", ", sessions.Select(s => $"P{s.PlayerIndex + 1} {Battery(s)}"));
        }
        // NotifyIcon.Text ist auf 127 Zeichen begrenzt.
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    private static string Battery(ControllerSession s) => s.LastState is { BatteryPercent: >= 0 } st
        ? $"{st.BatteryPercent} %{(st.Charging ? " ⚡" : "")}"
        : "";

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();
        menu.Items.Add(new ToolStripMenuItem("Switch 2 Pro Controller") { Enabled = false, Font = new Font(menu.Font, FontStyle.Bold) });

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
            var sessions = _manager.Sessions;
            if (sessions.Count == 0)
                menu.Items.Add(new ToolStripMenuItem(_manager.IsConnecting
                    ? "Verbinde …"
                    : "Kein Controller – SYNC-Taste oben am Controller kurz drücken") { Enabled = false });
            foreach (var s in sessions)
                menu.Items.Add(new ToolStripMenuItem($"Spieler {s.PlayerIndex + 1} · {s.AddressText} {Battery(s)}") { Enabled = false });
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

        menu.Items.Add(new ToolStripMenuItem("Vibration", null, (_, _) => { _settings.RumbleEnabled = !_settings.RumbleEnabled; SaveSettings(); })
            { Checked = _settings.RumbleEnabled });
        menu.Items.Add(new ToolStripMenuItem("Mit Windows starten", null, (_, _) => Autostart.Set(!Autostart.IsEnabled)) { Checked = Autostart.IsEnabled });

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Einstellungen …", null, (_, _) => ShowSettings()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add("Kurzanleitung", null, (_, _) => ShowWelcome());
        menu.Items.Add("Protokoll öffnen", null, (_, _) => Open(Paths.LogFile));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitThread());
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
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(Color.FromArgb(45, 45, 50));
            using var path = new GraphicsPath();
            path.AddEllipse(1, 8, 14, 20);
            path.AddEllipse(17, 8, 14, 20);
            path.AddRectangle(new Rectangle(8, 8, 16, 13));
            g.FillPath(body, path);
            using var red = new SolidBrush(Color.FromArgb(230, 0, 18));
            g.FillEllipse(red, 5, 11, 6, 6);
            g.FillEllipse(red, 20, 15, 6, 6);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _settingsForm?.Close();
        _settingsWatcher?.Dispose();
        if (_manager is not null)
        {
            // Sitzungen sauber beenden (Vibration stoppen, virtuelle Controller entfernen).
            Task.Run(async () => await _manager.DisposeAsync()).Wait(TimeSpan.FromSeconds(3));
        }
        _factory?.Dispose();
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
