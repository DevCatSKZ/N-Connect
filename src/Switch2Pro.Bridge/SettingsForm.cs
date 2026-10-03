using System.Drawing;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Hauptfenster: Reiter „Controller“ (Übersicht mit Live-Anzeige) und „Einstellungen“.
/// Alle Einstellungen sind optional und gelten sofort.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly Settings _settings;
    private readonly Action<bool> _changed; // true = Ausgabeart geändert
    private readonly ControllerManager? _manager;
    private readonly ControllerOverview _overview;
    private readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = 16 }; // ~60 Bilder/s: Anzeige ohne spürbare Verzögerung
    private bool _loading;

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

    /// <summary>Eintrag in der Belegungs-Auswahl: angezeigter Text und gespeicherte Aktion.</summary>
    private sealed record Choice(string Text, string? Action)
    {
        public override string ToString() => Tr.T(Text);
    }

    private const string CaptureKeys = "\u0001capture";
    private const string ChooseTurbo = "\u0001turbo";
    private const string ChooseMacro = "\u0001macro";
    private static readonly Choice KeyboardChoice = new("⌨  Andere Taste / Tastenkombination aufnehmen …", CaptureKeys);

    /// <summary>Sonderaktionen und fertige Windows-Tastenkürzel.</summary>
    private static IEnumerable<Choice> Presets(bool shiftLayer)
    {
        yield return new Choice("🖱  Maus: Linksklick", nameof(SpecialAction.MouseLeft));
        yield return new Choice("🖱  Maus: Rechtsklick", nameof(SpecialAction.MouseRight));
        yield return new Choice("🖱  Maus: Mittelklick", nameof(SpecialAction.MouseMiddle));
        yield return new Choice("🎯  Gyro-Maus (solange gehalten)", nameof(SpecialAction.GyroMouse));
        yield return new Choice("🎯  Gyro-Maus ein/aus", nameof(SpecialAction.GyroMouseToggle));
        if (!shiftLayer)
            yield return new Choice("⇧  Shift-Ebene (solange gehalten)", nameof(SpecialAction.Shift));
        yield return new Choice("🎯  Gyro als rechter Stick (solange gehalten)", nameof(SpecialAction.GyroStick));
        yield return new Choice("🎯  Gyro als rechter Stick ein/aus", nameof(SpecialAction.GyroStickToggle));
        yield return new Choice("🔁  Turbo / Dauerfeuer …", ChooseTurbo);
        yield return new Choice("⏯  Makro (Tastenfolge) …", ChooseMacro);
        yield return new Choice("📷  Bildschirmfoto speichern (Win+Druck)", "Key:Win+Print");
        yield return new Choice("📷  Bildschirmausschnitt (Win+Umschalt+S)", "Key:Win+Shift+S");
        yield return new Choice("⏺  Letzte 30 Sekunden aufnehmen (Win+Alt+G)", "Key:Win+Alt+G");
        yield return new Choice("⏺  Aufnahme starten/stoppen (Win+Alt+R)", "Key:Win+Alt+R");
        yield return new Choice("🎮  Xbox Game Bar (Win+G)", "Key:Win+G");
        yield return new Choice("🔊  Lauter", "Key:VolUp");
        yield return new Choice("🔉  Leiser", "Key:VolDown");
        yield return new Choice("⏯  Wiedergabe/Pause", "Key:PlayPause");
    }

    /// <summary>Alle Controller-Arten, für die sich eine eigene Belegung festlegen lässt.</summary>
    private static readonly ControllerKind[] ProfileKinds =
    [
        ControllerKind.Pro2, ControllerKind.JoyConPair, ControllerKind.JoyCon2Left, ControllerKind.JoyCon2Right,
        ControllerKind.GameCube2, ControllerKind.Pro1, ControllerKind.JoyCon1Left, ControllerKind.JoyCon1Right,
        ControllerKind.NesController, ControllerKind.SnesController, ControllerKind.N64Controller, ControllerKind.MegaDrive,
        ControllerKind.WiiRemote, ControllerKind.WiiUPro,
    ];

    private readonly RadioButton _xbox360 = new() { Text = "Xbox-360-Controller  (empfohlen – läuft mit fast allen Spielen)", AutoSize = true };
    private readonly RadioButton _ds4 = new() { Text = "PlayStation DualShock 4  (mit Bewegungssteuerung/Gyro – für Steam && Emulatoren)", AutoSize = true };
    private readonly RadioButton _layoutXbox = new() { Text = "Xbox-Belegung  (nach Position: untere Taste = A)", AutoSize = true };
    private readonly RadioButton _layoutSwitch = new() { Text = "Nintendo-Belegung  (nach Beschriftung: A bleibt A)", AutoSize = true };
    private readonly CheckBox _rumble = new() { Text = "Vibration", AutoSize = true };
    private readonly TrackBar _rumbleStrength = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 200 };
    private readonly TrackBar _deadzone = new() { Minimum = 0, Maximum = 30, TickFrequency = 5, Width = 200 };
    private readonly CheckBox _connectFeedback = new() { Text = "Beim Verbinden kurz vibrieren", AutoSize = true };
    private readonly CheckBox _autoReconnect = new() { Text = "Gekoppelte Controller per Tastendruck verbinden (ohne SYNC)", AutoSize = true };
    private readonly CheckBox _autostart = new() { Text = "Automatisch mit Windows starten", AutoSize = true };
    private readonly CheckBox _dsu = new() { Text = "Gyro für Emulatoren bereitstellen (Cemuhook/DSU, Port 26760 – wirkt nach Neustart)", AutoSize = true };
    private readonly CheckBox _updates = new() { Text = "Beim Start nach neuer Version suchen (GitHub)", AutoSize = true };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };

    private readonly CheckBox _combine = new() { Text = "Zwei Joy-Con automatisch zu einem Controller zusammenfassen", AutoSize = true };
    private readonly CheckBox _mouse = new() { Text = "Joy-Con 2 als Maus, wenn er auf dem Tisch liegt", AutoSize = true };
    private readonly TrackBar _mouseSpeed = new() { Minimum = 1, Maximum = 50, TickFrequency = 5, Width = 200 };
    private readonly CheckBox _invertX = new() { Text = "Maus: links/rechts umkehren", AutoSize = true };
    private readonly CheckBox _invertY = new() { Text = "Maus: hoch/runter umkehren", AutoSize = true };

    private readonly ComboBox _inactivity = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private static readonly int[] InactivityChoices = [0, 5, 10, 15, 30, 60];
    private readonly List<int> _inactivityValues = [];
    private readonly ComboBox _gyroSource = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TrackBar _gyroMouseSpeed = new() { Minimum = 2, Maximum = 80, TickFrequency = 10, Width = 200 };

    private readonly ComboBox _gyroStickMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
    private readonly TrackBar _gyroStickSpeed = new() { Minimum = 40, Maximum = 400, TickFrequency = 40, Width = 200 };
    private readonly TrackBar _gyroStickMin = new() { Minimum = 0, Maximum = 40, TickFrequency = 5, Width = 160 };
    private readonly CheckBox _gyroStickInvert = new() { Text = "hoch/runter umkehren", AutoSize = true };
    private readonly TrackBar _stickCurve = new() { Minimum = 50, Maximum = 250, TickFrequency = 25, Width = 200 };
    private readonly TrackBar _triggerDeadzone = new() { Minimum = 0, Maximum = 50, TickFrequency = 5, Width = 160 };
    private readonly TrackBar _triggerFull = new() { Minimum = 50, Maximum = 100, TickFrequency = 5, Width = 160 };
    private readonly TrackBar _turboRate = new() { Minimum = 2, Maximum = 30, TickFrequency = 2, Width = 160 };
    private readonly ToolTip _tips = new();
    private readonly Label _aimInfo = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 6, 0, 0) };

    private readonly ComboBox _profileSelect = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly Button _profileNew = new() { Text = "Neu …", AutoSize = true };
    private readonly Button _profileRename = new() { Text = "Umbenennen …", AutoSize = true };
    private readonly Button _profileDelete = new() { Text = "Löschen", AutoSize = true };
    private readonly Button _profileExport = new() { Text = "Exportieren …", AutoSize = true };
    private readonly Button _profileImport = new() { Text = "Importieren …", AutoSize = true };
    private readonly TextBox _programs = new() { Width = 520, PlaceholderText = "z. B. Cemu.exe, Ryujinx.exe" };
    private readonly Button _programAdd = new() { Text = "Programm wählen …", AutoSize = true };
    private readonly RadioButton _layerNormal = new() { Text = "Normale Belegung", AutoSize = true, Checked = true };
    private readonly RadioButton _layerShift = new() { Text = "Shift-Ebene (gilt, solange eine Taste mit „Shift-Ebene“ gehalten wird)", AutoSize = true };
    private readonly CheckBox _ownDeadzone = new() { Text = "Eigene Stick-Totzone für diesen Controller:", AutoSize = true };
    private readonly TrackBar _kindDeadzone = new() { Minimum = 0, Maximum = 30, TickFrequency = 5, Width = 200 };

    private readonly ComboBox _profileKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
    private readonly TableLayoutPanel _grid = new() { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
    private readonly List<ControllerKind> _kindOrder = [];

    public SettingsForm(Settings settings, Action<bool> changed, ControllerManager? manager)
    {
        _settings = settings;
        _changed = changed;
        _manager = manager;
        _overview = new ControllerOverview(manager, () => _settings);

        Text = "Nintendo Controller für Windows";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1000, 780);
        MinimumSize = new Size(700, 560);
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var overviewPage = new TabPage("Controller") { Padding = new Padding(8) };
        overviewPage.Controls.Add(_overview);
        var settingsPage = new TabPage("Einstellungen (optional)") { AutoScroll = true };
        tabs.TabPages.Add(overviewPage);
        tabs.TabPages.Add(settingsPage);
        Controls.Add(tabs);
        if (Environment.GetCommandLineArgs().Contains("--settings"))
            tabs.SelectedTab = settingsPage;

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(12),
        };
        settingsPage.Controls.Add(root);

        root.Controls.Add(Group("1. Windows und Spiele sehen den Controller als …", _xbox360, _ds4));
        root.Controls.Add(Group("2. Tasten A/B/X/Y", _layoutXbox, _layoutSwitch));

        var misc = Flow();
        misc.Controls.Add(_rumble);
        misc.Controls.Add(new Label { Text = "Stärke:", AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
        misc.Controls.Add(_rumbleStrength);
        misc.Controls.Add(new Label { Text = "Stick-Totzone (alle Controller):", AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
        misc.Controls.Add(_deadzone);
        misc.Controls.Add(_connectFeedback);
        misc.Controls.Add(_autoReconnect);
        misc.Controls.Add(_autostart);
        misc.Controls.Add(_dsu);
        misc.Controls.Add(_updates);
        misc.Controls.Add(new Label { Text = "Sprache / Language:", AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
        misc.Controls.Add(_language);
        misc.Controls.Add(new Label { Text = "Ohne Eingabe automatisch trennen nach:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        misc.Controls.Add(_inactivity);
        misc.Controls.Add(new Label { Text = "Gyro-Maus-Geschwindigkeit:", AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
        misc.Controls.Add(_gyroMouseSpeed);
        misc.SetFlowBreak(_deadzone, true);
        misc.SetFlowBreak(_language, true);
        _language.Items.AddRange(["Automatisch (wie Windows)", "Deutsch", "English"]);
        root.Controls.Add(WrapGroup("3. Vibration, Sticks, Verbinden, Gyro-Maus", misc));

        _gyroSource.Items.AddRange(["rechter Joy-Con (wie Switch)", "linker Joy-Con"]);

        var joyCon = Flow();
        joyCon.Controls.Add(_combine);
        joyCon.Controls.Add(new Label { Text = "Gyro beim Paar vom:", AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
        joyCon.Controls.Add(_gyroSource);
        joyCon.Controls.Add(_mouse);
        joyCon.Controls.Add(new Label { Text = "Mausgeschwindigkeit:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        joyCon.Controls.Add(_mouseSpeed);
        joyCon.Controls.Add(_invertX);
        joyCon.Controls.Add(_invertY);
        joyCon.SetFlowBreak(_gyroSource, true);
        joyCon.SetFlowBreak(_mouseSpeed, true);
        joyCon.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(880, 0), ForeColor = SystemColors.GrayText, Padding = new Padding(0, 6, 0, 0),
            Text = "Wie an der Switch: SL + SR eine Sekunde halten löst einen Joy-Con aus dem Paar. L am linken und R am " +
                   "rechten einzelnen Joy-Con gleichzeitig drücken verbindet sie wieder zum Paar. Die Wahl wird je Joy-Con gemerkt " +
                   "(auch nach dem nächsten Verbinden). Im Mausmodus: R/L = Linksklick, " +
                   "ZR/ZL = Rechtsklick, Stick drücken = Mittelklick, Stick hoch/runter = Scrollen.",
        });
        root.Controls.Add(WrapGroup("4. Joy-Con", joyCon));

        var aim = Flow();
        _gyroStickMode.Items.AddRange(["aus (nur per Taste „Gyro-Stick“)", "immer", "beim Zielen (solange ZL / linker Trigger gedrückt)"]);
        aim.Controls.Add(new Label { Text = "Gyro als rechter Stick:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        aim.Controls.Add(_gyroStickMode);
        aim.Controls.Add(_gyroStickInvert);
        aim.SetFlowBreak(_gyroStickInvert, true);
        aim.Controls.Add(new Label { Text = "Empfindlichkeit:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        aim.Controls.Add(_gyroStickSpeed);
        aim.Controls.Add(new Label { Text = "Mindestausschlag:", AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
        aim.Controls.Add(_gyroStickMin);
        aim.SetFlowBreak(_gyroStickMin, true);
        aim.Controls.Add(new Label { Text = "Stick-Kennlinie:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        aim.Controls.Add(_stickCurve);
        aim.Controls.Add(new Label { Text = "Turbo (pro Sekunde):", AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
        aim.Controls.Add(_turboRate);
        aim.SetFlowBreak(_turboRate, true);
        aim.Controls.Add(new Label { Text = "Analoge Trigger (GameCube) – Totzone:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        aim.Controls.Add(_triggerDeadzone);
        aim.Controls.Add(new Label { Text = "voll gedrückt ab:", AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
        aim.Controls.Add(_triggerFull);
        aim.SetFlowBreak(_triggerFull, true);
        aim.Controls.Add(_aimInfo);
        root.Controls.Add(WrapGroup("5. Zielen, Sticks, Trigger, Turbo", aim));
        foreach (var (bar, tip) in new (Control, string)[]
                 {
                     (_gyroStickSpeed, "Links = empfindlicher (kleine Drehung, großer Ausschlag), rechts = ruhiger."),
                     (_gyroStickMin, "Mindestausschlag des Sticks bei Bewegung – überwindet die Totzone des Spiels."),
                     (_stickCurve, "Mitte = linear. Rechts = feiner um die Mitte (präzises Zielen), links = schneller."),
                     (_triggerDeadzone, "So weit lässt sich ein analoger Trigger drücken, bevor er wirkt."),
                     (_triggerFull, "Ab hier gilt der Trigger als ganz gedrückt (100 = erst am Anschlag)."),
                     (_turboRate, "Wie oft pro Sekunde Tasten mit „Turbo“ auslösen."),
                 })
            _tips.SetToolTip(bar, Tr.T(tip));

        var profileRow = Flow();
        profileRow.Controls.Add(new Label { Text = "Profil:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        profileRow.Controls.AddRange([_profileSelect, _profileNew, _profileRename, _profileDelete, _profileExport, _profileImport]);
        var programRow = Flow();
        programRow.Controls.Add(new Label { Text = "Aktiv bei Programm:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        programRow.Controls.AddRange([_programs, _programAdd]);
        var kindRow = Flow();
        kindRow.Controls.Add(new Label { Text = "Controller:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        kindRow.Controls.Add(_profileKind);
        kindRow.Controls.Add(_ownDeadzone);
        kindRow.Controls.Add(_kindDeadzone);
        var layerRow = Flow();
        layerRow.Controls.AddRange([_layerNormal, _layerShift]);
        var profile = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        profile.Controls.Add(profileRow);
        profile.Controls.Add(programRow);
        profile.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(880, 0), ForeColor = SystemColors.GrayText,
            Text = "Ein eigenes Profil wird automatisch aktiv, solange eines seiner Programme im Vordergrund ist (sonst gilt „Standard“). " +
                   "Im Infobereich-Menü lässt sich ein Profil auch fest wählen.",
        });
        profile.Controls.Add(kindRow);
        profile.Controls.Add(layerRow);
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        profile.Controls.Add(_grid);
        root.Controls.Add(WrapGroup("6. Tastenbelegung und Profile  (Taste → Gamepad, Tastatur, Maus, Gyro-Maus, Shift-Ebene)", profile));

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var reset = new Button { Text = "Alles auf Standard", AutoSize = true };
        reset.Click += (_, _) => ResetAll();
        var close = new Button { Text = "Schließen", AutoSize = true };
        close.Click += (_, _) => Close();
        buttons.Controls.Add(reset);
        buttons.Controls.Add(close);
        root.Controls.Add(buttons);

        WireEvents();
        NoWheel(root);
        LoadValues();
        Tr.Apply(this);
        _liveTimer.Tick += (_, _) => _overview.UpdateView();
        _liveTimer.Start();
        _overview.UpdateView();
    }

    /// <summary>
    /// Regler und Auswahllisten nicht per Mausrad verstellen: Das Rad (auch das Scrollen eines Joy-Con im Mausmodus)
    /// würde sonst unbemerkt Einstellungen ändern. Offene Listen bleiben scrollbar.
    /// </summary>
    private static void NoWheel(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            if (c is TrackBar or ComboBox)
                c.MouseWheel += (sender, e) =>
                {
                    if (sender is ComboBox { DroppedDown: true })
                        return;
                    if (e is HandledMouseEventArgs handled)
                        handled.Handled = true;
                };
            NoWheel(c);
        }
    }

    private static FlowLayoutPanel Flow() =>
        new() { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(900, 0) };

    private static GroupBox Group(string title, params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        flow.Controls.AddRange(controls);
        return WrapGroup(title, flow);
    }

    private static GroupBox WrapGroup(string title, Control content)
    {
        var group = new GroupBox { Text = title, AutoSize = true, Padding = new Padding(8), MinimumSize = new Size(920, 0) };
        group.Controls.Add(content);
        content.Dock = DockStyle.Fill;
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
        _dsu.CheckedChanged += (_, _) => Apply(() => _settings.DsuServer = _dsu.Checked);
        _updates.CheckedChanged += (_, _) => Apply(() => _settings.CheckForUpdates = _updates.Checked);
        _language.SelectedIndexChanged += (_, _) => ChangeLanguage();
        _combine.CheckedChanged += (_, _) => Apply(() => _settings.CombineJoyCons = _combine.Checked);
        _mouse.CheckedChanged += (_, _) => Apply(() => _settings.JoyConMouse = _mouse.Checked);
        _mouseSpeed.ValueChanged += (_, _) => Apply(() => _settings.MouseSpeed = _mouseSpeed.Value / 10f);
        _invertX.CheckedChanged += (_, _) => Apply(() => _settings.MouseInvertX = _invertX.Checked);
        _invertY.CheckedChanged += (_, _) => Apply(() => _settings.MouseInvertY = _invertY.Checked);
        _profileKind.SelectedIndexChanged += (_, _) => { if (!_loading) { LoadKindDeadzone(); BuildProfileGrid(); } };
        _inactivity.SelectedIndexChanged += (_, _) =>
            Apply(() => _settings.InactivityMinutes = _inactivityValues[Math.Max(0, _inactivity.SelectedIndex)]);
        _gyroSource.SelectedIndexChanged += (_, _) =>
            Apply(() => _settings.PairGyroSource = _gyroSource.SelectedIndex == 1 ? GyroSource.Left : GyroSource.Right);
        _gyroMouseSpeed.ValueChanged += (_, _) => Apply(() => _settings.GyroMouseSpeed = _gyroMouseSpeed.Value);
        _gyroStickMode.SelectedIndexChanged += (_, _) => Apply(() => _settings.GyroStick = (GyroStickMode)Math.Max(0, _gyroStickMode.SelectedIndex));
        _gyroStickSpeed.ValueChanged += (_, _) => { Apply(() => _settings.GyroStickFullSpeed = _gyroStickSpeed.Value); ShowAimInfo(); };
        _gyroStickMin.ValueChanged += (_, _) => { Apply(() => _settings.GyroStickAntiDeadzone = _gyroStickMin.Value / 100f); ShowAimInfo(); };
        _gyroStickInvert.CheckedChanged += (_, _) => Apply(() => _settings.GyroStickInvertY = _gyroStickInvert.Checked);
        _stickCurve.ValueChanged += (_, _) => { Apply(() => _settings.StickCurve = _stickCurve.Value / 100f); ShowAimInfo(); };
        _triggerDeadzone.ValueChanged += (_, _) => { Apply(() => _settings.TriggerDeadzone = _triggerDeadzone.Value / 100f); ShowAimInfo(); };
        _triggerFull.ValueChanged += (_, _) => { Apply(() => _settings.TriggerFullAt = _triggerFull.Value / 100f); ShowAimInfo(); };
        _turboRate.ValueChanged += (_, _) => { Apply(() => _settings.TurboRate = _turboRate.Value); ShowAimInfo(); };

        _profileSelect.SelectedIndexChanged += (_, _) => { if (!_loading) LoadProfileEditor(); };
        _profileNew.Click += (_, _) => NewProfile();
        _profileRename.Click += (_, _) => RenameProfile();
        _profileDelete.Click += (_, _) => DeleteProfile();
        _profileExport.Click += (_, _) => ExportProfile();
        _profileImport.Click += (_, _) => ImportProfile();
        _programs.Validated += (_, _) => SavePrograms();
        _programAdd.Click += (_, _) => AddProgram();
        _layerNormal.CheckedChanged += (_, _) => { if (!_loading && _layerNormal.Checked) BuildProfileGrid(); };
        _layerShift.CheckedChanged += (_, _) => { if (!_loading && _layerShift.Checked) BuildProfileGrid(); };
        _ownDeadzone.CheckedChanged += (_, _) => SaveKindDeadzone();
        _kindDeadzone.ValueChanged += (_, _) => SaveKindDeadzone();
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
        _autostart.Checked = Autostart.IsEnabled || Autostart.IsEnabledForAllUsers;
        _autostart.Enabled = !Autostart.IsEnabledForAllUsers; // vom Installer für alle Benutzer eingetragen
        _dsu.Checked = _settings.DsuServer;
        _updates.Checked = _settings.CheckForUpdates;
        _language.SelectedIndex = _settings.Language switch { "de" => 1, "en" => 2, _ => 0 };
        _combine.Checked = _settings.CombineJoyCons;
        _mouse.Checked = _settings.JoyConMouse;
        _mouseSpeed.Value = Math.Clamp((int)MathF.Round(_settings.MouseSpeed * 10), 1, 50);
        _invertX.Checked = _settings.MouseInvertX;
        _invertY.Checked = _settings.MouseInvertY;
        // Auswahl jedes Mal neu aufbauen; ein von Hand eingetragener Wert (z. B. 20) erscheint als eigener Eintrag.
        _inactivityValues.Clear();
        _inactivityValues.AddRange(InactivityChoices);
        if (!_inactivityValues.Contains(_settings.InactivityMinutes))
            _inactivityValues.Add(_settings.InactivityMinutes);
        _inactivity.Items.Clear();
        foreach (int minutes in _inactivityValues)
            _inactivity.Items.Add(minutes == 0 ? "nie" : $"{minutes} Minuten");
        _inactivity.SelectedIndex = _inactivityValues.IndexOf(_settings.InactivityMinutes);
        Tr.Apply(_inactivity);
        _gyroSource.SelectedIndex = _settings.PairGyroSource == GyroSource.Left ? 1 : 0;
        _gyroMouseSpeed.Value = Math.Clamp((int)MathF.Round(_settings.GyroMouseSpeed), _gyroMouseSpeed.Minimum, _gyroMouseSpeed.Maximum);
        static int Bar(TrackBar bar, float value) => Math.Clamp((int)MathF.Round(value), bar.Minimum, bar.Maximum);
        _gyroStickMode.SelectedIndex = (int)_settings.GyroStick;
        _gyroStickSpeed.Value = Bar(_gyroStickSpeed, _settings.GyroStickFullSpeed);
        _gyroStickMin.Value = Bar(_gyroStickMin, _settings.GyroStickAntiDeadzone * 100);
        _gyroStickInvert.Checked = _settings.GyroStickInvertY;
        _stickCurve.Value = Bar(_stickCurve, _settings.StickCurve * 100);
        _triggerDeadzone.Value = Bar(_triggerDeadzone, _settings.TriggerDeadzone * 100);
        _triggerFull.Value = Bar(_triggerFull, _settings.TriggerFullAt * 100);
        _turboRate.Value = Bar(_turboRate, _settings.TurboRate);
        ShowAimInfo();
        FillProfileKinds();
        FillProfiles(_editProfile);
        _loading = false;
        LoadProfileEditor();
    }

    /// <summary>Sprache wechseln: speichern und das Fenster in der neuen Sprache neu öffnen.</summary>
    private void ChangeLanguage()
    {
        if (_loading)
            return;
        string? language = _language.SelectedIndex switch { 1 => "de", 2 => "en", _ => null };
        if (language == _settings.Language)
            return;
        _settings.Language = language;
        _changed(false);
        Tr.Init(language);
        // Neu aufbauen: Fenster schließen, die App öffnet es über das Signal sofort wieder.
        BeginInvoke(() =>
        {
            Close();
            Program.ShowSignal?.Set();
        });
    }

    /// <summary>Aktuelle Werte der Regler in Klartext (Regler zeigen keine Zahlen).</summary>
    private void ShowAimInfo() =>
        _aimInfo.Text = Tr.T($"Gyro-Stick: voller Ausschlag bei {_gyroStickSpeed.Value} °/s, mindestens {_gyroStickMin.Value} %   ·   " +
                        $"Kennlinie {_stickCurve.Value / 100f:0.00}   ·   Trigger: ab {_triggerDeadzone.Value} %, voll ab {_triggerFull.Value} %   ·   " +
                        $"Turbo {_turboRate.Value}× pro Sekunde");

    /// <summary>Nach dem Neuladen der Datei (von außen geändert) alle Felder neu befüllen.</summary>
    public void ReloadValues()
    {
        if (!IsDisposed)
            LoadValues();
    }

    // ---------- Profile ----------

    /// <summary>Bearbeitetes Profil: null = Standard, sonst Name eines benannten Profils.</summary>
    private string? _editProfile;

    private void FillProfiles(string? select)
    {
        bool was = _loading;
        _loading = true;
        _profileSelect.Items.Clear();
        _profileSelect.Items.Add(Tr.T("Standard"));
        foreach (var p in _settings.NamedProfiles)
            _profileSelect.Items.Add(p.Name);
        int index = select is null ? 0 : _settings.NamedProfiles.FindIndex(p => p.Name == select) + 1;
        _profileSelect.SelectedIndex = Math.Max(0, index);
        _editProfile = _profileSelect.SelectedIndex <= 0 ? null : _profileSelect.SelectedItem as string;
        _loading = was;
    }

    private NamedProfile? EditedProfile => _editProfile is null ? null : _settings.NamedProfiles.FirstOrDefault(p => p.Name == _editProfile);

    private void LoadProfileEditor()
    {
        // Name aus der Liste selbst (nicht per Index in NamedProfiles – die Einstellungen können neu geladen sein).
        _editProfile = _profileSelect.SelectedIndex <= 0 ? null : _profileSelect.SelectedItem as string;
        var profile = EditedProfile;
        bool was = _loading;
        _loading = true;
        _programs.Text = profile is null ? "" : string.Join(", ", profile.Programs);
        _programs.Enabled = _programAdd.Enabled = _profileRename.Enabled = _profileDelete.Enabled = profile is not null;
        _programs.PlaceholderText = Tr.T(profile is null ? "Standard gilt immer, wenn kein anderes Profil passt" : "z. B. Cemu.exe, Ryujinx.exe");
        _loading = was;
        LoadKindDeadzone();
        BuildProfileGrid();
    }

    /// <summary>Belegungen der gerade bearbeiteten Ebene (Standard/Profil, normal/Shift).</summary>
    private Dictionary<ControllerKind, Dictionary<ProButtons, string>> EditedMaps
    {
        get
        {
            var profile = EditedProfile;
            return _layerShift.Checked ? profile?.ShiftButtons ?? _settings.ShiftProfiles : profile?.Buttons ?? _settings.Profiles;
        }
        set
        {
            var profile = EditedProfile;
            if (profile is null)
            {
                if (_editProfile is not null)
                    return; // Profil inzwischen gelöscht (z. B. Datei neu geladen): nicht in „Standard“ schreiben
                if (_layerShift.Checked) _settings.ShiftProfiles = value; else _settings.Profiles = value;
                return;
            }
            var changed = _layerShift.Checked ? profile with { ShiftButtons = value } : profile with { Buttons = value };
            ReplaceProfile(profile, changed);
        }
    }

    private void ReplaceProfile(NamedProfile old, NamedProfile? replacement)
    {
        // Neue Liste statt Änderung: der Bluetooth-Thread liest gleichzeitig.
        // Per Name suchen: nach einem Neuladen sind die Objekte andere, der Name bleibt.
        var list = _settings.NamedProfiles.ToList();
        int i = list.FindIndex(p => p.Name == old.Name);
        if (i < 0)
            return;
        if (replacement is null) list.RemoveAt(i); else list[i] = replacement;
        _settings.NamedProfiles = list;
    }

    private static Dictionary<ControllerKind, Dictionary<ProButtons, string>> Copy(Dictionary<ControllerKind, Dictionary<ProButtons, string>> maps) =>
        maps.ToDictionary(m => m.Key, m => new Dictionary<ProButtons, string>(m.Value));

    private void NewProfile()
    {
        var name = Prompt.Ask(this, "Neues Profil", "Name des Profils (z. B. der Spielname):", "");
        if (name is null)
            return;
        if (_settings.NamedProfiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) || name == "Standard")
        {
            Tr.Show(this, "Ein Profil mit diesem Namen gibt es schon.", Text);
            return;
        }
        // Startet als Kopie der gerade bearbeiteten Belegung.
        var current = EditedProfile;
        var profile = new NamedProfile
        {
            Name = name,
            Buttons = Copy(current?.Buttons ?? _settings.Profiles),
            ShiftButtons = Copy(current?.ShiftButtons ?? _settings.ShiftProfiles),
        };
        Apply(() => _settings.NamedProfiles = [.. _settings.NamedProfiles, profile]);
        FillProfiles(name);
        LoadProfileEditor();
        _programs.Focus();
    }

    private void RenameProfile()
    {
        if (EditedProfile is not { } profile)
            return;
        var name = Prompt.Ask(this, "Profil umbenennen", "Neuer Name:", profile.Name);
        if (name is null || name == profile.Name)
            return;
        if (_settings.NamedProfiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) || name == "Standard")
        {
            Tr.Show(this, "Ein Profil mit diesem Namen gibt es schon.", Text);
            return;
        }
        Apply(() =>
        {
            ReplaceProfile(profile, profile with { Name = name });
            if (_settings.ForcedProfile == profile.Name)
                _settings.ForcedProfile = name;
        });
        FillProfiles(name);
        LoadProfileEditor();
    }

    private void DeleteProfile()
    {
        if (EditedProfile is not { } profile)
            return;
        if (Tr.Show(this, $"Profil „{profile.Name}“ löschen?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        Apply(() =>
        {
            ReplaceProfile(profile, null);
            if (_settings.ForcedProfile == profile.Name)
                _settings.ForcedProfile = null;
        });
        FillProfiles(null);
        LoadProfileEditor();
    }

    private const string ProfileFilter = "Controller-Profil (*.ncprofile.json)|*.ncprofile.json|JSON (*.json)|*.json";

    /// <summary>Gerade gewähltes Profil als Datei speichern (Standard wird als Profil „Standard“ exportiert).</summary>
    private void ExportProfile()
    {
        var profile = EditedProfile ?? new NamedProfile
        {
            Name = "Standard", Buttons = _settings.Profiles, ShiftButtons = _settings.ShiftProfiles,
        };
        using var dialog = new SaveFileDialog
        {
            Title = "Profil exportieren", Filter = ProfileFilter,
            FileName = string.Concat(profile.Name.Split(Path.GetInvalidFileNameChars())) + ".ncprofile.json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            File.WriteAllText(dialog.FileName, profile.ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Tr.Show(this, $"Speichern fehlgeschlagen: {e.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Profil aus einer Datei hinzufügen (bei gleichem Namen mit Zusatz „(2)“ usw.).</summary>
    private void ImportProfile()
    {
        using var dialog = new OpenFileDialog { Title = "Profil importieren", Filter = ProfileFilter };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        NamedProfile? profile;
        try
        {
            profile = NamedProfile.FromJson(File.ReadAllText(dialog.FileName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Tr.Show(this, $"Lesen fehlgeschlagen: {e.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (profile is null)
        {
            Tr.Show(this, "Die Datei enthält kein gültiges Controller-Profil.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string name = profile.Name, baseName = profile.Name;
        for (int n = 2; name == "Standard" || _settings.NamedProfiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); n++)
            name = $"{baseName} ({n})";
        var imported = profile with { Name = name };
        Apply(() => _settings.NamedProfiles = [.. _settings.NamedProfiles, imported]);
        FillProfiles(name);
        LoadProfileEditor();
        Tr.Show(this, $"Profil „{name}“ importiert" +
            (imported.Programs.Count > 0 ? $" – aktiv bei: {string.Join(", ", imported.Programs)}." : "."), Text);
    }

    private void SavePrograms()
    {
        if (_loading || EditedProfile is not { } profile)
            return;
        var programs = _programs.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : p + ".exe")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (programs.SequenceEqual(profile.Programs))
            return;
        Apply(() => ReplaceProfile(profile, profile with { Programs = programs }));
        _loading = true;
        _programs.Text = string.Join(", ", programs);
        _loading = false;
    }

    private void AddProgram()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Spiel oder Programm wählen", Filter = "Programme (*.exe)|*.exe",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        var exe = Path.GetFileName(dialog.FileName);
        _programs.Text = string.IsNullOrWhiteSpace(_programs.Text) ? exe : $"{_programs.Text}, {exe}";
        SavePrograms();
    }

    // ---------- Totzone je Controller-Art ----------

    private void LoadKindDeadzone()
    {
        bool was = _loading;
        _loading = true;
        var kind = SelectedKind;
        bool own = _settings.Deadzones.ContainsKey(kind);
        _ownDeadzone.Checked = own;
        _kindDeadzone.Enabled = own;
        _kindDeadzone.Value = Math.Clamp((int)MathF.Round(_settings.DeadzoneFor(kind) * 100), 0, 30);
        _loading = was;
    }

    private void SaveKindDeadzone()
    {
        if (_loading)
            return;
        var kind = SelectedKind;
        _kindDeadzone.Enabled = _ownDeadzone.Checked;
        Apply(() =>
        {
            var map = new Dictionary<ControllerKind, float>(_settings.Deadzones);
            if (_ownDeadzone.Checked)
                map[kind] = _kindDeadzone.Value / 100f;
            else
                map.Remove(kind);
            _settings.Deadzones = map;
        });
    }

    /// <summary>Controller-Auswahl: verbundene Controller zuerst (mit Hinweis), dann alle übrigen.</summary>
    private void FillProfileKinds()
    {
        var connected = _manager?.Players.Select(p => p.Kind).Distinct().ToList() ?? [];
        var previous = _profileKind.SelectedIndex >= 0 && _profileKind.SelectedIndex < _kindOrder.Count
            ? _kindOrder[_profileKind.SelectedIndex] : (ControllerKind?)null;
        _kindOrder.Clear();
        _kindOrder.AddRange(connected.Where(ProfileKinds.Contains));
        _kindOrder.AddRange(ProfileKinds.Where(k => !_kindOrder.Contains(k)));
        _profileKind.Items.Clear();
        foreach (var kind in _kindOrder)
            _profileKind.Items.Add(Tr.T(kind.DisplayName() + (connected.Contains(kind) ? "  (verbunden)" : "")));
        int index = previous is { } p ? _kindOrder.IndexOf(p) : 0;
        _profileKind.SelectedIndex = Math.Max(0, index);
    }

    private ControllerKind SelectedKind => _kindOrder.Count > 0 && _profileKind.SelectedIndex >= 0
        ? _kindOrder[_profileKind.SelectedIndex] : ControllerKind.Pro2;

    /// <summary>Für den gewählten Controller nur dessen Tasten anzeigen, beschriftet wie am Gerät.</summary>
    private void BuildProfileGrid()
    {
        var kind = SelectedKind;
        _grid.SuspendLayout();
        foreach (Control c in _grid.Controls.Cast<Control>().ToList())
        {
            _grid.Controls.Remove(c);
            c.Dispose();
        }
        _grid.RowStyles.Clear();
        _grid.RowCount = 0;
        foreach (var button in ControllerButtons.For(kind))
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
            FillCombo(combo, button, kind);
            combo.SelectedIndexChanged += (_, _) => OnActionChosen(combo, button, kind);
            _grid.Controls.Add(new Label { Text = ControllerButtons.Label(button, kind), AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            _grid.Controls.Add(combo);
        }
        NoWheel(_grid);
        Tr.Apply(_grid);
        _grid.ResumeLayout();
    }

    /// <summary>
    /// Einträge: „Standard: …“ (auf der Shift-Ebene „wie normale Belegung“), Gamepad-Ziele, Maus/Gyro/Shift,
    /// fertige Windows-Kürzel, ggf. der gespeicherte eigene Hotkey, „Taste aufnehmen …“.
    /// </summary>
    private void FillCombo(ComboBox combo, ProButtons button, ControllerKind kind)
    {
        bool was = _loading;
        _loading = true;
        bool shift = _layerShift.Checked;
        combo.Items.Clear();
        string standard = shift
            ? "wie normale Belegung"
            : $"Standard: {TargetNames[Mapping.DefaultTarget(button, _settings.Layout, kind)]}";
        combo.Items.Add(new Choice(standard, null));
        foreach (var (target, name) in TargetNames)
            combo.Items.Add(new Choice(name, target.ToString()));
        foreach (var preset in Presets(shift))
            combo.Items.Add(preset);

        int selected = 0;
        if (EditedMaps.TryGetValue(kind, out var map) && map.TryGetValue(button, out var text))
        {
            string normalized = ButtonAction.Parse(text).ToString();
            selected = combo.Items.Cast<Choice>().ToList()
                .FindIndex(c => c.Action is not null && !c.Action.StartsWith('\u0001')
                                && string.Equals(ButtonAction.Parse(c.Action).ToString(), normalized, StringComparison.OrdinalIgnoreCase));
            if (selected < 0)
            {
                var action = ButtonAction.Parse(text);
                string label = action switch
                {
                    { IsMacro: true } => $"⏯  Makro: {action.Macro}",
                    { Turbo: true } => $"🔁  Turbo: {(action.IsKeyboard ? $"Taste {action.Keys}" : TargetNames.GetValueOrDefault(action.Target, action.Target.ToString()))}",
                    { IsKeyboard: true } => $"⌨  Taste: {action.Keys}",
                    _ => normalized,
                };
                combo.Items.Add(new Choice(label, normalized));
                selected = combo.Items.Count - 1;
            }
        }
        combo.Items.Add(KeyboardChoice);
        combo.SelectedIndex = selected;
        combo.Tag = selected;
        _loading = was;
    }

    private void OnActionChosen(ComboBox combo, ProButtons button, ControllerKind kind)
    {
        if (_loading || combo.SelectedItem is not Choice choice)
            return;
        string? action = choice.Action;
        if (action == CaptureKeys)
        {
            using var dialog = new KeyCaptureDialog(ControllerButtons.Label(button, kind));
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Combo is null)
            {
                _loading = true;
                combo.SelectedIndex = combo.Tag is int previous ? previous : 0;
                _loading = false;
                return;
            }
            action = ButtonAction.Keyboard(dialog.Combo).ToString();
        }
        else if (action is ChooseTurbo or ChooseMacro)
        {
            // Vorhandene Belegung als Vorschlag übernehmen.
            string? current = EditedMaps.TryGetValue(kind, out var m) && m.TryGetValue(button, out var t) ? t : null;
            string label = ControllerButtons.Label(button, kind);
            string? result = action == ChooseTurbo ? TurboDialog.Ask(this, label, current) : MacroDialog.Ask(this, label, current);
            if (result is null)
            {
                _loading = true;
                combo.SelectedIndex = combo.Tag is int previous ? previous : 0;
                _loading = false;
                return;
            }
            action = result;
        }

        Apply(() =>
        {
            // Neue Kopien statt Änderung: der Bluetooth-Thread liest gleichzeitig.
            var maps = Copy(EditedMaps);
            if (!maps.TryGetValue(kind, out var map))
                maps[kind] = map = [];
            if (action is null)
                map.Remove(button);
            else
                map[button] = action;
            if (map.Count == 0)
                maps.Remove(kind);
            EditedMaps = maps;
        });
        FillCombo(combo, button, kind);
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
        BuildProfileGrid(); // „Standard: …“ neu beschriften
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
        if (Tr.Show(this, "Alle Einstellungen und Tastenbelegungen auf Standard zurücksetzen?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        var defaults = new Settings();
        bool outputChanged = _settings.OutputMode != defaults.OutputMode;
        // Bekannte Controller und Freigaben bleiben erhalten.
        defaults.KnownControllers = _settings.KnownControllers;
        defaults.AllowedControllers = _settings.AllowedControllers;
        defaults.SingleJoyCons = _settings.SingleJoyCons;
        defaults.UprightJoyCons = _settings.UprightJoyCons;
        defaults.GyroCalibration = _settings.GyroCalibration; // gemessene Werte je Controller
        _editProfile = null;
        _settings.CopyFrom(defaults);
        LoadValues();
        _changed(outputChanged);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _liveTimer.Dispose();
        _tips.Dispose();
        base.OnFormClosed(e);
    }
}

/// <summary>Nimmt eine Taste oder Tastenkombination auf (z. B. Strg+Umschalt+S, F5, Alt+Tab).</summary>
internal sealed class KeyCaptureDialog : Form
{
    private readonly Label _shown = new()
    {
        AutoSize = false, Dock = DockStyle.Top, Height = 60, TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI Semibold", 16f), Text = "…",
    };

    /// <summary>Aufgenommene Kombination im Format von <see cref="WindowsInput.ParseCombo"/>, z. B. "Ctrl+Shift+S".</summary>
    public string? Combo { get; private set; }

    public KeyCaptureDialog(string buttonName)
    {
        Text = "Tastatur-Taste festlegen";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        ClientSize = new Size(440, 170);
        Font = new Font("Segoe UI", 9.5f);

        var hint = new Label
        {
            AutoSize = false, Dock = DockStyle.Top, Height = 48, TextAlign = ContentAlignment.MiddleCenter,
            Text = $"Controller-Taste „{buttonName}“:\nJetzt die gewünschte Taste oder Tastenkombination drücken.",
        };
        var ok = new Button { Text = "Übernehmen", DialogResult = DialogResult.OK, Enabled = false, AutoSize = true };
        var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, AutoSize = true };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);
        Controls.Add(_shown);
        Controls.Add(hint);
        Controls.Add(bar);
        CancelButton = cancel;
        Tr.Apply(this);

        KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (KeyName(e.KeyCode) is not { } key)
                return; // nur Zusatztaste gedrückt – auf die eigentliche Taste warten
            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Shift) parts.Add("Shift");
            if (e.Alt) parts.Add("Alt");
            if ((GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0) parts.Add("Win");
            parts.Add(key);
            Combo = string.Join("+", parts);
            _shown.Text = Combo;
            ok.Enabled = WindowsInput.ParseCombo(Combo) is not null;
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Tab, Pfeiltasten, Enter usw. sollen aufgenommen werden statt den Fokus zu wechseln.
        if (keyData is Keys.Escape)
            return base.ProcessCmdKey(ref msg, keyData);
        OnKeyDown(new KeyEventArgs(keyData));
        return true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    private static string? KeyName(Keys key) => key switch
    {
        >= Keys.A and <= Keys.Z => key.ToString(),
        >= Keys.D0 and <= Keys.D9 => ((int)(key - Keys.D0)).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => $"Num{(int)(key - Keys.NumPad0)}",
        >= Keys.F1 and <= Keys.F24 => key.ToString(),
        Keys.Space => "Space",
        Keys.Enter => "Enter",
        Keys.Tab => "Tab",
        Keys.Back => "Backspace",
        Keys.Delete => "Delete",
        Keys.Insert => "Insert",
        Keys.Home => "Home",
        Keys.End => "End",
        Keys.PageUp => "PageUp",
        Keys.PageDown => "PageDown",
        Keys.Up => "Up",
        Keys.Down => "Down",
        Keys.Left => "Left",
        Keys.Right => "Right",
        Keys.PrintScreen => "Print",
        Keys.Pause => "Pause",
        Keys.VolumeUp => "VolUp",
        Keys.VolumeDown => "VolDown",
        Keys.VolumeMute => "Mute",
        Keys.MediaPlayPause => "PlayPause",
        _ => null,
    };
}

/// <summary>Turbo festlegen: welche Gamepad-Taste bzw. Tastaturtaste im Dauerfeuer ausgelöst wird.</summary>
internal static class TurboDialog
{
    private static readonly ExtraButtonTarget[] Targets =
    [
        ExtraButtonTarget.A, ExtraButtonTarget.B, ExtraButtonTarget.X, ExtraButtonTarget.Y, ExtraButtonTarget.LB, ExtraButtonTarget.RB,
        ExtraButtonTarget.LT, ExtraButtonTarget.RT, ExtraButtonTarget.Up, ExtraButtonTarget.Down, ExtraButtonTarget.Left,
        ExtraButtonTarget.Right, ExtraButtonTarget.LS, ExtraButtonTarget.RS, ExtraButtonTarget.Start, ExtraButtonTarget.Back,
    ];

    /// <summary>Ergebnis als Aktionstext („Turbo:A“, „Turbo:Key:Space“) oder null bei Abbruch.</summary>
    public static string? Ask(IWin32Window owner, string button, string? current)
    {
        using var form = new Form
        {
            Text = "Turbo / Dauerfeuer", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(440, 170),
            Font = new Font("Segoe UI", 9.5f),
        };
        var info = new Label
        {
            Text = $"Solange „{button}“ gehalten wird, wird diese Taste schnell wiederholt gedrückt\n(Geschwindigkeit unter 5. „Turbo“):",
            AutoSize = true, Location = new Point(12, 12),
        };
        var target = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(12, 60), Width = 416 };
        foreach (var t in Targets)
            target.Items.Add($"Gamepad: {t}");
        target.Items.Add("Tastatur-Taste aufnehmen …");
        var old = ButtonAction.Parse(current);
        string? keys = old.Turbo && old.IsKeyboard ? old.Keys : null;
        if (keys is not null)
        {
            target.Items.Insert(target.Items.Count - 1, $"Tastatur: {keys}");
            target.SelectedIndex = target.Items.Count - 2;
        }
        else
        {
            target.SelectedIndex = Math.Max(0, Array.IndexOf(Targets, old.Turbo ? old.Target : ExtraButtonTarget.A));
        }
        target.SelectedIndexChanged += (_, _) =>
        {
            if (target.SelectedIndex != target.Items.Count - 1)
                return;
            using var capture = new KeyCaptureDialog(button);
            if (capture.ShowDialog(form) == DialogResult.OK && capture.Combo is { } combo)
            {
                keys = combo;
                if (target.Items[target.Items.Count - 2] is string s && s.StartsWith("Tastatur:", StringComparison.Ordinal))
                    target.Items.RemoveAt(target.Items.Count - 2);
                target.Items.Insert(target.Items.Count - 1, $"Tastatur: {keys}");
                target.SelectedIndex = target.Items.Count - 2;
            }
            else
            {
                target.SelectedIndex = 0;
            }
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(256, 120), AutoSize = true };
        var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Location = new Point(342, 120), AutoSize = true };
        form.Controls.AddRange([info, target, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        Tr.Apply(form);
        if (form.ShowDialog(owner) != DialogResult.OK)
            return null;
        int i = target.SelectedIndex;
        if (i < Targets.Length)
            return ButtonAction.Gamepad(Targets[i]) with { Turbo = true } is var a ? a.ToString() : null;
        return keys is null ? null : (ButtonAction.Keyboard(keys) with { Turbo = true }).ToString();
    }
}

/// <summary>Makro festlegen: Tastenfolge als Text mit Prüfung und Beispielen.</summary>
internal static class MacroDialog
{
    public static string? Ask(IWin32Window owner, string button, string? current)
    {
        using var form = new Form
        {
            Text = "Makro (Tastenfolge)", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(560, 300),
            Font = new Font("Segoe UI", 9.5f),
        };
        var info = new Label
        {
            AutoSize = true, MaximumSize = new Size(536, 0), Location = new Point(12, 10),
            Text = $"Beim Drücken von „{button}“ wird diese Folge einmal abgespielt. Schritte durch Komma trennen, je Schritt " +
                   "was gehalten wird und wie lange (ms). Gamepad: A B X Y LB RB LT RT Up Down Left Right LS RS Start Back Guide, " +
                   "mehrere gleichzeitig mit +. Tastatur: Key:Ctrl+C. Warten: Pause.\n" +
                   "Beispiele:  „A 80, Pause 60, A 80“ (Doppeltipp)  ·  „Down+B 150“  ·  „Key:Ctrl+S 50“",
        };
        var old = ButtonAction.Parse(current);
        var box = new TextBox
        {
            Location = new Point(12, 120), Width = 536, Height = 80, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Text = old.IsMacro ? old.Macro : "A 80, Pause 60, A 80",
        };
        var status = new Label { AutoSize = true, Location = new Point(12, 210) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(376, 255), AutoSize = true };
        var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Location = new Point(462, 255), AutoSize = true };
        void Check()
        {
            bool valid = MacroScript.TryParse((box.Text ?? "").Replace("\r", "").Replace('\n', ','), out var script);
            ok.Enabled = valid;
            status.ForeColor = valid ? SystemColors.ControlText : Color.Firebrick;
            status.Text = Tr.T(valid ? $"✓ {script.Steps.Count} Schritte, Dauer {script.TotalMs} ms" : "Ungültig – bitte Schreibweise prüfen (siehe oben).");
        }
        box.TextChanged += (_, _) => Check();
        Check();
        form.Controls.AddRange([info, box, status, ok, cancel]);
        form.CancelButton = cancel;
        Tr.Apply(form);
        if (form.ShowDialog(owner) != DialogResult.OK)
            return null;
        return ButtonAction.Play((box.Text ?? "").Replace("\r", "").Replace('\n', ',').Trim()).ToString();
    }
}

/// <summary>Einfache Texteingabe (Profilname).</summary>
internal static class Prompt
{
    public static string? Ask(IWin32Window owner, string title, string question, string initial)
    {
        using var form = new Form
        {
            Text = title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(420, 130),
            Font = new Font("Segoe UI", 9.5f),
        };
        var label = new Label { Text = question, AutoSize = true, Location = new Point(12, 14) };
        var box = new TextBox { Text = initial, Location = new Point(12, 40), Width = 396 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(236, 88), AutoSize = true };
        var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Location = new Point(322, 88), AutoSize = true };
        form.Controls.AddRange([label, box, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        Tr.Apply(form);
        if (form.ShowDialog(owner) != DialogResult.OK)
            return null;
        var text = box.Text.Trim();
        return text.Length == 0 ? null : text;
    }
}

/// <summary>Autostart über HKCU\…\Run (kein Adminrecht nötig).</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Switch2ProBridge";

    /// <summary>Autostart für den angemeldeten Benutzer (von der App gesetzt).</summary>
    public static bool IsEnabled
    {
        get
        {
            using var user = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return user?.GetValue(RunValue) is string;
        }
    }

    /// <summary>Autostart für alle Benutzer, vom Installer eingetragen (nur per Setup änderbar).</summary>
    public static bool IsEnabledForAllUsers
    {
        get
        {
            using var machine = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(RunKey);
            return machine?.GetValue(RunValue) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --autostart"); // im Infobereich, ohne Fenster
        else
            key.DeleteValue(RunValue, throwOnMissingValue: false);
    }
}
