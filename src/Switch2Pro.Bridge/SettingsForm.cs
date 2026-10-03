using System.Drawing;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Einstellungsfenster: alles per Klick, Änderungen gelten sofort.
/// Unten zeigt ein Live-Test, welche Tasten gerade gedrückt sind.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly Settings _settings;
    private readonly Action<bool> _changed; // true = Ausgabeart geändert
    private readonly Func<ControllerSession?> _firstController;
    private readonly Label _live = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = 100 };
    private readonly Dictionary<ProButtons, ComboBox> _combos = [];
    private bool _loading;

    private static readonly (ProButtons Button, string Name)[] Buttons =
    [
        (ProButtons.A, "A"), (ProButtons.B, "B"), (ProButtons.X, "X"), (ProButtons.Y, "Y"),
        (ProButtons.L, "L"), (ProButtons.R, "R"), (ProButtons.ZL, "ZL"), (ProButtons.ZR, "ZR"),
        (ProButtons.Minus, "−  (Minus)"), (ProButtons.Plus, "+  (Plus)"), (ProButtons.Home, "HOME"),
        (ProButtons.Capture, "Aufnahme-Taste"), (ProButtons.C, "C-Taste"),
        (ProButtons.GL, "GL (Rücktaste links)"), (ProButtons.GR, "GR (Rücktaste rechts)"),
        (ProButtons.LeftStick, "Linker Stick drücken"), (ProButtons.RightStick, "Rechter Stick drücken"),
        (ProButtons.Up, "Steuerkreuz hoch"), (ProButtons.Down, "Steuerkreuz runter"),
        (ProButtons.Left, "Steuerkreuz links"), (ProButtons.Right, "Steuerkreuz rechts"),
    ];

    private static readonly Dictionary<ExtraButtonTarget, string> TargetNames = new()
    {
        [ExtraButtonTarget.None] = "— keine Funktion —",
        [ExtraButtonTarget.A] = "A (Xbox) / Kreuz (PS)",
        [ExtraButtonTarget.B] = "B (Xbox) / Kreis (PS)",
        [ExtraButtonTarget.X] = "X (Xbox) / Viereck (PS)",
        [ExtraButtonTarget.Y] = "Y (Xbox) / Dreieck (PS)",
        [ExtraButtonTarget.LB] = "LB / L1",
        [ExtraButtonTarget.RB] = "RB / R1",
        [ExtraButtonTarget.LT] = "LT / L2",
        [ExtraButtonTarget.RT] = "RT / R2",
        [ExtraButtonTarget.Back] = "Ansicht (Back) / Share",
        [ExtraButtonTarget.Start] = "Menü (Start) / Options",
        [ExtraButtonTarget.Guide] = "Xbox-Taste / PS-Taste",
        [ExtraButtonTarget.LS] = "Linker Stick-Klick",
        [ExtraButtonTarget.RS] = "Rechter Stick-Klick",
        [ExtraButtonTarget.Up] = "Steuerkreuz hoch",
        [ExtraButtonTarget.Down] = "Steuerkreuz runter",
        [ExtraButtonTarget.Left] = "Steuerkreuz links",
        [ExtraButtonTarget.Right] = "Steuerkreuz rechts",
        [ExtraButtonTarget.Touchpad] = "Touchpad-Klick (nur DualShock 4)",
    };

    private readonly RadioButton _xbox360 = new() { Text = "Xbox-360-Controller  (empfohlen – läuft mit fast allen Spielen)", AutoSize = true };
    private readonly RadioButton _ds4 = new() { Text = "PlayStation DualShock 4  (mit Bewegungssteuerung/Gyro – für Steam & Emulatoren)", AutoSize = true };
    private readonly RadioButton _layoutXbox = new() { Text = "Xbox-Belegung  (nach Position: untere Taste = A)", AutoSize = true };
    private readonly RadioButton _layoutSwitch = new() { Text = "Switch-2-Pro-Belegung  (nach Beschriftung: A bleibt A)", AutoSize = true };
    private readonly CheckBox _rumble = new() { Text = "Vibration", AutoSize = true };
    private readonly TrackBar _rumbleStrength = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 220 };
    private readonly TrackBar _deadzone = new() { Minimum = 0, Maximum = 30, TickFrequency = 5, Width = 220 };
    private readonly CheckBox _connectFeedback = new() { Text = "Beim Verbinden kurz vibrieren", AutoSize = true };
    private readonly CheckBox _autoReconnect = new() { Text = "Bekannte Controller per Tastendruck verbinden (ohne SYNC)", AutoSize = true };
    private readonly CheckBox _autostart = new() { Text = "Automatisch mit Windows starten", AutoSize = true };

    public SettingsForm(Settings settings, Action<bool> changed, Func<ControllerSession?> firstController)
    {
        _settings = settings;
        _changed = changed;
        _firstController = firstController;

        Text = "Switch 2 Pro Controller – Einstellungen";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(760, 760);
        MinimumSize = new Size(640, 560);
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(Group("1. Windows und Spiele sehen den Controller als …", _xbox360, _ds4));
        root.Controls.Add(Group("2. Tastenbelegung", _layoutXbox, _layoutSwitch));

        var misc = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        misc.Controls.Add(_rumble);
        misc.Controls.Add(new Label { Text = "Stärke:", AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
        misc.Controls.Add(_rumbleStrength);
        misc.Controls.Add(new Label { Text = "Stick-Totzone:", AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
        misc.Controls.Add(_deadzone);
        misc.Controls.Add(_connectFeedback);
        misc.Controls.Add(_autoReconnect);
        misc.Controls.Add(_autostart);
        root.Controls.Add(WrapGroup("3. Vibration und Sticks", misc));

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var (button, name) in Buttons)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
            combo.SelectedIndexChanged += (_, _) => OnRemapChanged(button, combo);
            _combos[button] = combo;
            grid.Controls.Add(new Label { Text = name, AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            grid.Controls.Add(combo);
        }
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(grid);
        var remapGroup = WrapGroup("4. Einzelne Tasten umbelegen  (Controller-Taste → wird zu)", scroll);
        remapGroup.Dock = DockStyle.Fill;
        root.Controls.Add(remapGroup);

        var liveGroup = WrapGroup("Live-Test: drück eine Taste am Controller", _live);
        liveGroup.Dock = DockStyle.Fill;
        root.Controls.Add(liveGroup);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Schließen", AutoSize = true };
        close.Click += (_, _) => Close();
        var reset = new Button { Text = "Alles auf Standard", AutoSize = true };
        reset.Click += (_, _) => ResetAll();
        buttons.Controls.Add(close);
        buttons.Controls.Add(reset);
        root.Controls.Add(buttons);
        AcceptButton = close;

        WireEvents();
        LoadValues();
        _liveTimer.Tick += (_, _) => UpdateLive();
        _liveTimer.Start();
    }

    private static GroupBox Group(string title, params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        flow.Controls.AddRange(controls);
        return WrapGroup(title, flow);
    }

    private static GroupBox WrapGroup(string title, Control content)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        group.Controls.Add(content);
        return group;
    }

    private void WireEvents()
    {
        _xbox360.CheckedChanged += (_, _) => { if (_xbox360.Checked) SetOutput(OutputMode.Xbox360); };
        _ds4.CheckedChanged += (_, _) => { if (_ds4.Checked) SetOutput(OutputMode.DualShock4); };
        _layoutXbox.CheckedChanged += (_, _) => { if (_layoutXbox.Checked) SetLayout(FaceButtonLayout.Xbox); };
        _layoutSwitch.CheckedChanged += (_, _) => { if (_layoutSwitch.Checked) SetLayout(FaceButtonLayout.Switch2); };
        _rumble.CheckedChanged += (_, _) => Apply(() => _settings.RumbleEnabled = _rumble.Checked);
        _rumbleStrength.ValueChanged += (_, _) => Apply(() => _settings.RumbleStrength = _rumbleStrength.Value / 100f);
        _deadzone.ValueChanged += (_, _) => Apply(() => _settings.StickDeadzone = _deadzone.Value / 100f);
        _connectFeedback.CheckedChanged += (_, _) => Apply(() => _settings.ConnectFeedback = _connectFeedback.Checked);
        _autoReconnect.CheckedChanged += (_, _) => Apply(() => _settings.AutoReconnect = _autoReconnect.Checked);
        _autostart.CheckedChanged += (_, _) => { if (!_loading) Autostart.Set(_autostart.Checked); };
    }

    private void LoadValues()
    {
        _loading = true;
        _xbox360.Checked = _settings.OutputMode == OutputMode.Xbox360;
        _ds4.Checked = _settings.OutputMode == OutputMode.DualShock4;
        _layoutXbox.Checked = _settings.Layout == FaceButtonLayout.Xbox;
        _layoutSwitch.Checked = _settings.Layout == FaceButtonLayout.Switch2;
        _rumble.Checked = _settings.RumbleEnabled;
        _rumbleStrength.Value = (int)MathF.Round(_settings.RumbleStrength * 100);
        _deadzone.Value = Math.Clamp((int)MathF.Round(_settings.StickDeadzone * 100), 0, 30);
        _connectFeedback.Checked = _settings.ConnectFeedback;
        _autoReconnect.Checked = _settings.AutoReconnect;
        _autostart.Checked = Autostart.IsEnabled;
        FillCombos();
        _loading = false;
    }

    /// <summary>Erster Eintrag jeder Liste ist „Standard (…)“ – folgt der gewählten Belegung.</summary>
    private void FillCombos()
    {
        foreach (var (button, _) in Buttons)
        {
            var combo = _combos[button];
            combo.Items.Clear();
            combo.Items.Add($"Standard: {TargetNames[Mapping.DefaultTarget(button, _settings.Layout)]}");
            foreach (var name in TargetNames.Values)
                combo.Items.Add(name);
            combo.SelectedIndex = _settings.Remap.TryGetValue(button, out var target)
                ? 1 + TargetNames.Keys.ToList().IndexOf(target)
                : 0;
        }
    }

    private void OnRemapChanged(ProButtons button, ComboBox combo)
    {
        if (_loading || combo.SelectedIndex < 0)
            return;
        Apply(() =>
        {
            // Neue Kopie statt Änderung: der Bluetooth-Thread liest gleichzeitig.
            var remap = new Dictionary<ProButtons, ExtraButtonTarget>(_settings.Remap);
            if (combo.SelectedIndex == 0)
                remap.Remove(button);
            else
                remap[button] = TargetNames.Keys.ElementAt(combo.SelectedIndex - 1);
            _settings.Remap = remap;
        });
    }

    private void SetOutput(OutputMode mode)
    {
        if (_loading || _settings.OutputMode == mode)
            return;
        _settings.OutputMode = mode;
        _changed(true);
    }

    private void SetLayout(FaceButtonLayout layout)
    {
        if (_loading)
            return;
        _settings.Layout = layout;
        _loading = true;
        FillCombos(); // „Standard: …“ neu beschriften
        _loading = false;
        _changed(false);
    }

    private void Apply(Action change)
    {
        if (_loading)
            return;
        change();
        _changed(false);
    }

    private void ResetAll()
    {
        if (MessageBox.Show(this, "Alle Einstellungen auf Standard zurücksetzen?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        var defaults = new Settings();
        bool outputChanged = _settings.OutputMode != defaults.OutputMode;
        _settings.OutputMode = defaults.OutputMode;
        _settings.Layout = defaults.Layout;
        _settings.RumbleEnabled = defaults.RumbleEnabled;
        _settings.RumbleStrength = defaults.RumbleStrength;
        _settings.StickDeadzone = defaults.StickDeadzone;
        _settings.ConnectFeedback = defaults.ConnectFeedback;
        _settings.AutoReconnect = defaults.AutoReconnect;
        _settings.Remap = defaults.Remap;
        LoadValues();
        _changed(outputChanged);
    }

    private void UpdateLive()
    {
        var session = _firstController();
        if (session?.LastState is not { } state)
        {
            _live.Text = "Kein Controller verbunden – SYNC-Taste oben am Controller kurz drücken.";
            return;
        }
        var pressed = Buttons.Where(b => state.Has(b.Button)).Select(b => b.Name).ToList();
        var (left, right) = session.Calibration;
        var g = Mapping.ToGamepad(state, _settings, left, right);
        string sticks = $"Stick L {g.LeftX * 100 / 32767,4}/{g.LeftY * 100 / 32767,4}   R {g.RightX * 100 / 32767,4}/{g.RightY * 100 / 32767,4}";
        string battery = state.BatteryPercent >= 0 ? $"   Akku {state.BatteryPercent} %{(state.Charging ? " (lädt)" : "")}" : "";
        _live.Text = $"Spieler {session.PlayerIndex + 1}:  " +
                     (pressed.Count == 0 ? "keine Taste" : string.Join(" + ", pressed)) + $"     {sticks}{battery}";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _liveTimer.Dispose();
        base.OnFormClosed(e);
    }
}

/// <summary>Autostart über HKCU\…\Run (kein Adminrecht nötig).</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Switch2ProBridge";

    public static bool IsEnabled
    {
        get
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(RunValue, throwOnMissingValue: false);
    }
}
