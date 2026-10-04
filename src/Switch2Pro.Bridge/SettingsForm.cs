using System.Drawing;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Hauptfenster im Stil der Windows-11-Einstellungen: links die Navigation, rechts Seiten aus Einstellungszeilen.
/// Seite „Controller“ zeigt die verbundenen Controller (mit eigenen Einstellungen je Controller), die übrigen Seiten
/// die allgemeinen Werte. Alle Änderungen gelten sofort.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly Settings _settings;
    private readonly Action<bool> _changed; // true = Ausgabeart geändert
    private readonly ControllerManager? _manager;
    private readonly Action? _wiiPairing;
    private readonly MappingContext _mapping;
    private readonly ControllerOverview _overview;
    private readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = 16 }; // ~60 Bilder/s: Anzeige ohne spürbare Verzögerung
    private readonly ToolTip _tips = Theme.CreateToolTip();
    private bool _loading;

    // ---------- Seiten und Navigation ----------
    public const int PageControllers = 0, PageMapping = 1, PageSticks = 2, PageGyro = 3, PageJoyCon = 4, PageGeneral = 5;
    private readonly List<(NavItem Nav, Control Page)> _pages = [];
    private readonly Panel _host = new() { Dock = DockStyle.Fill };
    private int _page = -1;

    // ---------- Allgemein ----------
    private readonly ComboBox _output = Combo(260, "Xbox-360-Controller (empfohlen)", "PlayStation DualShock 4");
    private readonly Segmented _layout = new("Xbox", "Nintendo");
    private readonly ToggleSwitch _autoReconnect = new();
    private readonly ToggleSwitch _connectFeedback = new();
    private readonly ComboBox _inactivity = Combo(160);
    private static readonly int[] InactivityChoices = [0, 5, 10, 15, 30, 60];
    private readonly List<int> _inactivityValues = [];
    private readonly ToggleSwitch _autostart = new();
    private readonly ComboBox _themeMode = Combo(200, "Dunkel", "Hell", "Wie Windows");
    private readonly ToggleSwitch _transparency = new();
    private readonly ComboBox _language = Combo(220, "Automatisch (wie Windows)", "Deutsch", "English");
    private readonly ToggleSwitch _dsu = new();
    private readonly ToggleSwitch _updates = new();

    // ---------- Sticks & Vibration ----------
    private readonly Slider _deadzone = Bar(0, 30, v => $"{v} %");
    private readonly Slider _stickCurve = Bar(50, 250, v => v == 100 ? "linear" : $"{v / 100f:0.00}", step: 5);
    private readonly Slider _triggerDeadzone = Bar(0, 50, v => $"{v} %");
    private readonly Slider _triggerFull = Bar(50, 100, v => $"{v} %");
    private readonly ToggleSwitch _rumble = new();
    private readonly Slider _rumbleStrength = Bar(0, 100, v => $"{v} %", step: 5);
    private readonly Slider _turboRate = Bar(2, 30, v => $"{v}/s");
    private readonly ComboBox _tuneKind = Combo(300);
    private readonly TuningEditor _tuning;

    // ---------- Gyro & Maus ----------
    private readonly ComboBox _gyroStickMode = Combo(330, "Aus (nur per Taste „Gyro-Stick“)", "Immer", "Beim Zielen (solange ZL / LT gedrückt)");
    private readonly Slider _gyroStickSpeed = Bar(40, 400, v => $"{v} °/s", step: 10);
    private readonly Slider _gyroStickMin = Bar(0, 40, v => $"{v} %");
    private readonly ToggleSwitch _gyroStickInvert = new();
    private readonly Slider _gyroMouseSpeed = Bar(2, 80, v => $"{v}");
    private readonly ToggleSwitch _invertX = new();
    private readonly ToggleSwitch _invertY = new();

    // ---------- Joy-Con & Wii ----------
    private readonly ToggleSwitch _combine = new();
    private readonly ComboBox _gyroSource = Combo(240, "Rechter Joy-Con (wie Switch)", "Linker Joy-Con");
    private readonly ToggleSwitch _mouse = new();
    private readonly Slider _mouseSpeed = Bar(1, 50, v => $"{v / 10f:0.0}×");
    private readonly ToggleSwitch _wiiPointer = new();

    // ---------- Tastenbelegung ----------
    private readonly ComboBox _profileSelect = Combo(240);
    private readonly GlyphButton _profileNew = new("Neu", Glyph.Add);
    private readonly GlyphButton _profileMore = new("", Glyph.More) { Width = 36 };
    private readonly ContextMenuStrip _profileMenu = new();
    private readonly TextBox _programs = new() { Width = 300, PlaceholderText = "z. B. Cemu.exe, Ryujinx.exe" };
    private readonly GlyphButton _programAdd = new("Wählen …", Glyph.Folder);
    private readonly ComboBox _mapKind = Combo(300);
    private readonly Segmented _layer = new("Normal", "Shift-Ebene");
    private readonly MappingEditor _mapEditor;
    private readonly List<ControllerKind> _mapKinds = [], _tuneKinds = [];

    public SettingsForm(Settings settings, Action<bool> changed, ControllerManager? manager, Action? wiiPairing = null)
    {
        _settings = settings;
        _changed = changed;
        _manager = manager;
        _wiiPairing = wiiPairing;
        _mapping = new MappingContext(() => _settings, () => _changed(false), () => this);
        _overview = new ControllerOverview(manager, () => _settings, _mapping, () => _changed(false), ShowPage);
        _mapEditor = new MappingEditor(_mapping, ControllerKind.Pro2);
        _tuning = new TuningEditor(() => _settings, () => _changed(false), ControllerKind.Pro2);

        Text = "N-Connect";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1220, 860);
        MinimumSize = new Size(940, 600);
        Font = UiFonts.Body;
        AutoScaleMode = AutoScaleMode.Dpi;

        Controls.Add(_host);
        Controls.Add(BuildNavigation());
        AddPage("Controller", Glyph.Gamepad, BuildOverviewPage());
        AddPage("Tastenbelegung", Glyph.Keyboard, BuildMappingPage());
        AddPage("Sticks & Vibration", Glyph.Stick, BuildSticksPage());
        AddPage("Gyro & Maus", Glyph.Rotate, BuildGyroPage());
        AddPage("Joy-Con & Wii", Glyph.Swap, BuildJoyConPage());
        AddPage("Allgemein", Glyph.Settings, BuildGeneralPage());

        WireEvents();
        LoadValues();
        Theme.Apply(this);
        Tr.Apply(this);
        Tr.Apply(_profileMenu.Items);
        _mapping.Changed += OnMappingChanged;
        _liveTimer.Tick += (_, _) => Live();
        _liveTimer.Start();
        Shown += (_, _) => PrepareHiddenPages();
        ShowPage(Environment.GetCommandLineArgs().Contains("--settings") ? PageGeneral : PageControllers);
    }

    // ---------- Aufbau ----------

    private static ComboBox Combo(int width, params string[] items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
        combo.Items.AddRange(items);
        return combo;
    }

    private static Slider Bar(int min, int max, Func<int, string> format, int step = 1) =>
        new() { Minimum = min, Maximum = max, Format = format, SmallChange = step, Width = 300 };

    private readonly FlowLayoutPanel _navList = new()
    {
        FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 0),
    };

    private Control BuildNavigation()
    {
        var rail = new Panel { Dock = DockStyle.Left, Width = 250, BackColor = Theme.Backdrop };
        var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Theme.Backdrop };
        var logo = new PictureBox { Image = Branding.Render(64), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(20, 20, 32, 32) };
        var name = new Label { Text = "N-Connect", Font = UiFonts.Subtitle, AutoSize = true, Location = new Point(62, 24), Tag = Tr.UserData };
        header.Controls.AddRange([logo, name]);
        var version = new Label
        {
            Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(22, 0, 0, 10), TextAlign = ContentAlignment.MiddleLeft,
            Text = $"Version {UpdateCheck.Current.ToString(3)}", Tag = Theme.MutedTag, ForeColor = SystemColors.GrayText,
        };
        _navList.BackColor = Theme.Backdrop;
        rail.Controls.Add(_navList);
        rail.Controls.Add(version);
        rail.Controls.Add(header);
        return rail;
    }

    private void AddPage(string title, string glyph, Control page)
    {
        int index = _pages.Count;
        var nav = new NavItem(title, glyph, () => ShowPage(index)) { Width = 228, BackColor = Theme.Backdrop };
        _navList.Controls.Add(nav);
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _host.Controls.Add(page);
        _pages.Add((nav, page));
    }

    private Control BuildOverviewPage()
    {
        var page = new Panel { BackColor = Theme.Backdrop, Padding = new Padding(16, 12, 12, 8) };
        page.Controls.Add(_overview);
        page.Controls.Add(new Heading("Controller", page: true,
            "Verbundene Controller mit Live-Anzeige. Über „Einstellungen“ an einem Controller passt du genau diesen an.")
        { Dock = DockStyle.Top, Height = 74, Padding = new Padding(12, 0, 0, 0) });
        return page;
    }

    private static ScrollPage NewPage(string title, string subtitle)
    {
        var page = new ScrollPage();
        page.Content.Controls.Add(new Heading(title, page: true, subtitle));
        return page;
    }

    private SettingRow Row(string title, string? description, Control content, string? glyph = null)
    {
        if (content is ToggleSwitch or Slider or Segmented)
            content.BackColor = Theme.Current.Surface;
        return new SettingRow(title, description, content, glyph);
    }

    private Control BuildMappingPage()
    {
        var page = NewPage("Tastenbelegung",
            "Jede Taste kann ein Gamepad-Knopf, eine Tastaturtaste, ein Mausklick, Turbo oder ein Makro sein. " +
            "Tipp: Drückst du eine Taste am Controller, wird ihre Zeile hervorgehoben.");

        var profileBox = Inline(_profileSelect, _profileNew, _profileMore);
        _profileMenu.Renderer = Theme.MenuRenderer();
        _profileMenu.ForeColor = Theme.Current.Text;
        _profileMenu.Items.Add("Umbenennen …", null, (_, _) => RenameProfile());
        _profileMenu.Items.Add("Löschen", null, (_, _) => DeleteProfile());
        _profileMenu.Items.Add(new ToolStripSeparator());
        _profileMenu.Items.Add("Exportieren …", null, (_, _) => ExportProfile());
        _profileMenu.Items.Add("Importieren …", null, (_, _) => ImportProfile());
        _profileMore.Click += (_, _) => _profileMenu.Show(_profileMore, new Point(0, _profileMore.Height));
        _tips.SetToolTip(_profileMore, Tr.T("Umbenennen, löschen, exportieren, importieren"));

        page.AddGroup("Profil",
            Row("Profil", "Ein eigenes Profil gilt automatisch, solange eines seiner Programme im Vordergrund ist – sonst „Standard“.",
                profileBox, Glyph.Layers),
            Row("Aktiv bei Programm", "Programmdateien, bei denen dieses Profil gilt (mit Komma trennen).",
                Inline(_programs, _programAdd), Glyph.Folder));
        page.AddGroup("Bearbeiten",
            Row("Controller", "Jeder Controller hat seine eigene Belegung.", _mapKind, Glyph.Gamepad),
            Row("Ebene", "Die Shift-Ebene gilt, solange eine Taste mit „Shift-Ebene“ gehalten wird.", _layer, Glyph.Layers));
        page.Content.Controls.Add(new Heading("Tasten"));
        page.Content.Controls.Add(_mapEditor);
        return page;
    }

    /// <summary>Mehrere Bedienelemente nebeneinander (rechts in einer Zeile).</summary>
    private static Panel Inline(params Control[] controls)
    {
        var panel = new Panel { BackColor = Theme.Current.Surface };
        int x = 0, h = controls.Max(c => c.Height);
        foreach (var c in controls)
        {
            c.Location = new Point(x, (h - c.Height) / 2);
            panel.Controls.Add(c);
            x += c.Width + 8;
        }
        panel.Size = new Size(x - 8, h);
        return panel;
    }

    private Control BuildSticksPage()
    {
        var page = NewPage("Sticks & Vibration",
            "Standardwerte für alle Controller. Eigene Werte für einzelne Controller stellst du unten oder in der Übersicht ein.");
        page.AddGroup("Sticks",
            Row("Totzone", "So weit lässt sich ein Stick bewegen, bevor er wirkt (gegen Abdriften).", _deadzone, Glyph.Stick),
            Row("Kennlinie", "„linear“ = gleichmäßig. Höher = feiner um die Mitte (präzises Zielen), niedriger = schneller.", _stickCurve, Glyph.Sliders));
        page.AddGroup("Analoge Trigger (GameCube)",
            Row("Totzone", "So weit lässt sich ein Trigger drücken, bevor er wirkt.", _triggerDeadzone, Glyph.Gauge),
            Row("Voll gedrückt ab", "Ab hier gilt der Trigger als ganz gedrückt (100 % = erst am Anschlag).", _triggerFull, Glyph.Gauge));
        page.AddGroup("Vibration",
            Row("Vibration", "Spiele dürfen den Controller vibrieren lassen.", _rumble, Glyph.Vibrate),
            Row("Stärke", null, _rumbleStrength, Glyph.Volume));
        page.AddGroup("Turbo",
            Row("Turbo-Geschwindigkeit", "Wie oft pro Sekunde Tasten mit „Turbo“ auslösen.", _turboRate, Glyph.Timer));
        page.AddGroup("Eigene Werte je Controller",
            Row("Controller", "Werte nur für diesen Controller – ohne eigenen Wert gilt der allgemeine.", _tuneKind, Glyph.Gamepad));
        page.Content.Controls.Add(new Panel { Height = 8, BackColor = Theme.Backdrop });
        page.Content.Controls.Add(_tuning);
        return page;
    }

    private Control BuildGyroPage()
    {
        var page = NewPage("Gyro & Maus", "Bewegungssteuerung: Zielen per Gyro und den Controller als Maus nutzen.");
        page.AddGroup("Gyro als rechter Stick",
            Row("Modus", "Zum Zielen in Spielen ohne eigene Gyro-Steuerung. „Gyro-Stick“ kann auch einer Taste zugewiesen werden.", _gyroStickMode, Glyph.Rotate),
            Row("Empfindlichkeit", "Drehgeschwindigkeit für vollen Stick-Ausschlag – kleiner = empfindlicher.", _gyroStickSpeed, Glyph.Gauge),
            Row("Mindestausschlag", "Überwindet die Stick-Totzone des Spiels bei kleinen Bewegungen.", _gyroStickMin, Glyph.Sliders),
            Row("Hoch/runter umkehren", null, _gyroStickInvert, Glyph.Swap));
        page.AddGroup("Gyro-Maus",
            Row("Geschwindigkeit", "Mauszeiger per Bewegung – über eine Taste mit „Gyro-Maus“ (Tastenbelegung).", _gyroMouseSpeed, Glyph.Mouse));
        page.AddGroup("Mausrichtung (Gyro-Maus und Joy-Con-Maus)",
            Row("Links/rechts umkehren", null, _invertX, Glyph.Swap),
            Row("Hoch/runter umkehren", null, _invertY, Glyph.Swap));
        var toMapping = new SettingRow("Gyro-Funktionen einer Taste zuweisen", "Gyro-Maus oder Gyro-Stick ein/aus bzw. solange gehalten",
            null, Glyph.Keyboard) { Navigates = true };
        toMapping.Click += (_, _) => ShowPage(PageMapping);
        page.AddGroup(null, toMapping);
        return page;
    }

    private Control BuildJoyConPage()
    {
        var page = NewPage("Joy-Con & Wii", "Joy-Con als Paar oder einzeln, Joy-Con 2 als Maus und Wii-Controller.");
        page.AddGroup("Joy-Con",
            Row("Zu einem Controller zusammenfassen", "Ein linker und ein rechter Joy-Con werden automatisch ein Paar.", _combine, Glyph.Swap),
            Row("Gyro beim Paar vom", null, _gyroSource, Glyph.Rotate),
            new SettingRow("Paar lösen oder verbinden",
                "SL + SR eine Sekunde halten löst einen Joy-Con aus dem Paar. L am linken und R am rechten Joy-Con gleichzeitig " +
                "drücken verbindet sie wieder. Die Wahl wird je Joy-Con gemerkt.", null, Glyph.Info));
        page.AddGroup("Joy-Con 2 als Maus",
            Row("Als Maus, wenn er auf dem Tisch liegt", "Joy-Con 2 auf die Seite legen und wie eine Maus schieben.", _mouse, Glyph.Mouse),
            Row("Mausgeschwindigkeit", null, _mouseSpeed, Glyph.Gauge),
            new SettingRow("Tasten im Mausmodus",
                "R/L = Linksklick, ZR/ZL = Rechtsklick, Stick drücken = Mittelklick, Stick hoch/runter = Scrollen.", null, Glyph.Info));
        var pair = new GlyphButton("Koppeln …", Glyph.Bluetooth) { Enabled = _wiiPairing is not null };
        pair.Click += (_, _) => _wiiPairing?.Invoke();
        page.AddGroup("Wii",
            Row("Wii-Controller koppeln", "Wii-Fernbedienung und Wii U Pro Controller einmalig mit dem PC koppeln.", pair, Glyph.Bluetooth),
            Row("Zeiger steuert die Maus", "Wii-Fernbedienung auf die Sensorleiste richten, um den Mauszeiger zu bewegen.", _wiiPointer, Glyph.Pointer));
        return page;
    }

    private Control BuildGeneralPage()
    {
        var page = NewPage("Allgemein", "Wie Spiele die Controller sehen, Verbindung, Darstellung und Programm.");
        page.AddGroup("Ausgabe",
            Row("Controller erscheint als", "Xbox 360 läuft mit fast allen Spielen. DualShock 4 bietet Bewegungssteuerung (Steam, Emulatoren).", _output, Glyph.Gamepad),
            Row("Tastenanordnung A/B/X/Y", "Xbox: nach Position (untere Taste = A). Nintendo: nach Beschriftung (A bleibt A).", _layout, Glyph.Swap));
        page.AddGroup("Verbinden",
            Row("Per Tastendruck verbinden", "Gekoppelte Controller verbinden sich ohne SYNC-Taste.", _autoReconnect, Glyph.Bluetooth),
            Row("Beim Verbinden kurz vibrieren", null, _connectFeedback, Glyph.Vibrate),
            Row("Ohne Eingabe trennen nach", "Spart Akku, wenn ein Controller liegen bleibt.", _inactivity, Glyph.Timer),
            Row("Mit Windows starten", "N-Connect startet unsichtbar im Infobereich.", _autostart, Glyph.Power));
        page.AddGroup("Darstellung und Sprache",
            Row("Farbmodus", null, _themeMode, Glyph.Palette),
            Row("Mica-Effekt", "Durchscheinende Titelleiste (ab Windows 11).", _transparency, Glyph.Palette),
            Row("Sprache / Language", null, _language, Glyph.Globe));
        var reset = new GlyphButton("Zurücksetzen", Glyph.Refresh);
        reset.Click += (_, _) => ResetAll();
        page.AddGroup("Erweitert",
            Row("Gyro für Emulatoren (DSU)", "Cemuhook-Server auf Port 26760 – wirkt nach einem Neustart von N-Connect.", _dsu, Glyph.Rotate),
            Row("Nach Updates suchen", "Beim Start auf GitHub nach einer neuen Version suchen.", _updates, Glyph.Sync),
            Row("Alles auf Standard", "Tastenbelegungen und Werte zurücksetzen. Gekoppelte Controller bleiben erhalten.", reset, Glyph.Refresh));
        return page;
    }

    // ---------- Seiten ----------

    /// <summary>Seite zeigen (siehe Page…-Konstanten).</summary>
    internal void ShowPage(int page)
    {
        if (page < 0 || page >= _pages.Count || page == _page)
            return;
        long start = Environment.TickCount64;
        _page = page;
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Page.Visible = i == page;
            _pages[i].Nav.Selected = i == page;
        }
        if (page == PageControllers)
            _overview.UpdateView();
        if (page is PageMapping or PageSticks)
            FillKinds(); // verbundene Controller zuerst
        Update();
        long ms = Environment.TickCount64 - start;
        if (ms > 300)
            Log.Warn($"Seite {page} brauchte {ms} ms zum Anzeigen");
    }

    /// <summary>Seiten für die Prüfhilfe (--render-ui): Name und Inhalt.</summary>
    internal IReadOnlyList<(string Name, Control Page)> Pages => _pages.Select(p => (p.Nav.Text, p.Page)).ToList();

    internal ControllerOverview Overview => _overview;

    /// <summary>
    /// Steuerelemente der übrigen Seiten schon im Voraus erzeugen – je Seite ein kurzer Schritt, damit die Übersicht
    /// flüssig bleibt. Sonst dauert der erste Seitenwechsel spürbar.
    /// </summary>
    private void PrepareHiddenPages()
    {
        var pending = new Queue<Control>(_pages.Select(p => p.Page));
        var timer = new System.Windows.Forms.Timer { Interval = 60 };
        timer.Tick += (_, _) =>
        {
            if (IsDisposed || pending.Count == 0)
            {
                timer.Dispose();
                return;
            }
            CreateHandles(pending.Dequeue());
        };
        timer.Start();

        static void CreateHandles(Control c)
        {
            _ = c.Handle;
            foreach (Control child in c.Controls)
                CreateHandles(child);
        }
    }

    /// <summary>Live-Anzeige (~60-mal pro Sekunde): Übersicht bzw. gedrückte Tasten in der Tastenbelegung.</summary>
    private void Live()
    {
        if (WindowState == FormWindowState.Minimized)
            return;
        if (_page == PageControllers)
            _overview.UpdateView();
        else if (_page == PageMapping)
        {
            var player = _manager?.Players.FirstOrDefault(p => p.Kind == _mapEditor.Kind);
            var input = player is null ? null : ControllerOverview.LiveInput(player, _settings).Input;
            _mapEditor.Highlight(input?.Buttons ?? ProButtons.None);
        }
    }

    // ---------- Werte ----------

    private void WireEvents()
    {
        _output.SelectedIndexChanged += (_, _) => SetOutput(_output.SelectedIndex == 1 ? OutputMode.DualShock4 : OutputMode.Xbox360);
        _layout.SelectedIndexChanged += (_, _) => SetLayout(_layout.SelectedIndex == 1 ? FaceButtonLayout.Switch2 : FaceButtonLayout.Xbox);
        _rumble.CheckedChanged += (_, _) => { Apply(() => _settings.RumbleEnabled = _rumble.Checked); _rumbleStrength.Enabled = _rumble.Checked; };
        _rumbleStrength.ValueChanged += (_, _) => Apply(() => _settings.RumbleStrength = _rumbleStrength.Value / 100f);
        _deadzone.ValueChanged += (_, _) => { Apply(() => _settings.StickDeadzone = _deadzone.Value / 100f); RefreshTuning(); };
        _connectFeedback.CheckedChanged += (_, _) => Apply(() => _settings.ConnectFeedback = _connectFeedback.Checked);
        _autoReconnect.CheckedChanged += (_, _) => Apply(() => _settings.AutoReconnect = _autoReconnect.Checked);
        _autostart.CheckedChanged += (_, _) => { if (!_loading) Autostart.Set(_autostart.Checked); };
        _dsu.CheckedChanged += (_, _) => Apply(() => _settings.DsuServer = _dsu.Checked);
        _updates.CheckedChanged += (_, _) => Apply(() => _settings.CheckForUpdates = _updates.Checked);
        _language.SelectedIndexChanged += (_, _) => ChangeLanguage();
        _themeMode.SelectedIndexChanged += (_, _) => ChangeTheme();
        _transparency.CheckedChanged += (_, _) => ChangeTheme();
        _combine.CheckedChanged += (_, _) => Apply(() => _settings.CombineJoyCons = _combine.Checked);
        _mouse.CheckedChanged += (_, _) => Apply(() => _settings.JoyConMouse = _mouse.Checked);
        _mouseSpeed.ValueChanged += (_, _) => Apply(() => _settings.MouseSpeed = _mouseSpeed.Value / 10f);
        _invertX.CheckedChanged += (_, _) => Apply(() => _settings.MouseInvertX = _invertX.Checked);
        _invertY.CheckedChanged += (_, _) => Apply(() => _settings.MouseInvertY = _invertY.Checked);
        _wiiPointer.CheckedChanged += (_, _) => Apply(() => _settings.WiiPointerMouse = _wiiPointer.Checked);
        _inactivity.SelectedIndexChanged += (_, _) =>
            Apply(() => _settings.InactivityMinutes = _inactivityValues[Math.Max(0, _inactivity.SelectedIndex)]);
        _gyroSource.SelectedIndexChanged += (_, _) =>
            Apply(() => _settings.PairGyroSource = _gyroSource.SelectedIndex == 1 ? GyroSource.Left : GyroSource.Right);
        _gyroMouseSpeed.ValueChanged += (_, _) => { Apply(() => _settings.GyroMouseSpeed = _gyroMouseSpeed.Value); RefreshTuning(); };
        _gyroStickMode.SelectedIndexChanged += (_, _) => Apply(() => _settings.GyroStick = (GyroStickMode)Math.Max(0, _gyroStickMode.SelectedIndex));
        _gyroStickSpeed.ValueChanged += (_, _) => Apply(() => _settings.GyroStickFullSpeed = _gyroStickSpeed.Value);
        _gyroStickMin.ValueChanged += (_, _) => Apply(() => _settings.GyroStickAntiDeadzone = _gyroStickMin.Value / 100f);
        _gyroStickInvert.CheckedChanged += (_, _) => Apply(() => _settings.GyroStickInvertY = _gyroStickInvert.Checked);
        _stickCurve.ValueChanged += (_, _) => { Apply(() => _settings.StickCurve = _stickCurve.Value / 100f); RefreshTuning(); };
        _triggerDeadzone.ValueChanged += (_, _) => { Apply(() => _settings.TriggerDeadzone = _triggerDeadzone.Value / 100f); RefreshTuning(); };
        _triggerFull.ValueChanged += (_, _) => { Apply(() => _settings.TriggerFullAt = _triggerFull.Value / 100f); RefreshTuning(); };
        _rumbleStrength.ValueChanged += (_, _) => RefreshTuning();
        _turboRate.ValueChanged += (_, _) => Apply(() => _settings.TurboRate = _turboRate.Value);

        _profileSelect.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading)
                _mapping.SelectProfile(_profileSelect.SelectedIndex <= 0 ? null : _profileSelect.SelectedItem as string);
        };
        _profileNew.Click += (_, _) => NewProfile();
        _programs.Validated += (_, _) => SavePrograms();
        _programAdd.Click += (_, _) => AddProgram();
        _layer.SelectedIndexChanged += (_, _) => { if (!_loading) _mapping.SetShift(_layer.SelectedIndex == 1); };
        _mapKind.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _mapKind.SelectedIndex >= 0)
                _mapEditor.Kind = _mapKinds[_mapKind.SelectedIndex];
        };
        _tuneKind.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _tuneKind.SelectedIndex >= 0)
                _tuning.Kind = _tuneKinds[_tuneKind.SelectedIndex];
        };
        NoWheel(this);
    }

    /// <summary>
    /// Auswahllisten nicht per Mausrad verstellen: Das Rad (auch das Scrollen eines Joy-Con im Mausmodus) würde sonst
    /// unbemerkt Einstellungen ändern. Offene Listen bleiben scrollbar.
    /// </summary>
    private static void NoWheel(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            if (c is ComboBox)
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

    private void LoadValues()
    {
        _loading = true;
        static int Clamp(Slider s, float v) => Math.Clamp((int)MathF.Round(v), s.Minimum, s.Maximum);
        _output.SelectedIndex = _settings.OutputMode == OutputMode.DualShock4 ? 1 : 0;
        _layout.SelectedIndex = _settings.Layout == FaceButtonLayout.Switch2 ? 1 : 0;
        _rumble.Checked = _settings.RumbleEnabled;
        _rumbleStrength.Enabled = _settings.RumbleEnabled;
        _rumbleStrength.Value = Clamp(_rumbleStrength, _settings.RumbleStrength * 100);
        _deadzone.Value = Clamp(_deadzone, _settings.StickDeadzone * 100);
        _connectFeedback.Checked = _settings.ConnectFeedback;
        _autoReconnect.Checked = _settings.AutoReconnect;
        _autostart.Checked = Autostart.IsEnabled || Autostart.IsEnabledForAllUsers;
        _autostart.Enabled = !Autostart.IsEnabledForAllUsers; // alte Installation: für alle Benutzer eingetragen
        _dsu.Checked = _settings.DsuServer;
        _updates.Checked = _settings.CheckForUpdates;
        _language.SelectedIndex = _settings.Language switch { "de" => 1, "en" => 2, _ => 0 };
        _themeMode.SelectedIndex = _settings.Theme switch { "light" => 1, "system" => 2, _ => 0 };
        _transparency.Checked = _settings.Transparency;
        _combine.Checked = _settings.CombineJoyCons;
        _mouse.Checked = _settings.JoyConMouse;
        _mouseSpeed.Value = Clamp(_mouseSpeed, _settings.MouseSpeed * 10);
        _invertX.Checked = _settings.MouseInvertX;
        _invertY.Checked = _settings.MouseInvertY;
        _wiiPointer.Checked = _settings.WiiPointerMouse;
        // Auswahl jedes Mal neu aufbauen; ein von Hand eingetragener Wert (z. B. 20) erscheint als eigener Eintrag.
        _inactivityValues.Clear();
        _inactivityValues.AddRange(InactivityChoices);
        if (!_inactivityValues.Contains(_settings.InactivityMinutes))
            _inactivityValues.Add(_settings.InactivityMinutes);
        _inactivity.Items.Clear();
        foreach (int minutes in _inactivityValues)
            _inactivity.Items.Add(Tr.T(minutes == 0 ? "nie" : $"{minutes} Minuten"));
        _inactivity.SelectedIndex = _inactivityValues.IndexOf(_settings.InactivityMinutes);
        _gyroSource.SelectedIndex = _settings.PairGyroSource == GyroSource.Left ? 1 : 0;
        _gyroMouseSpeed.Value = Clamp(_gyroMouseSpeed, _settings.GyroMouseSpeed);
        _gyroStickMode.SelectedIndex = (int)_settings.GyroStick;
        _gyroStickSpeed.Value = Clamp(_gyroStickSpeed, _settings.GyroStickFullSpeed);
        _gyroStickMin.Value = Clamp(_gyroStickMin, _settings.GyroStickAntiDeadzone * 100);
        _gyroStickInvert.Checked = _settings.GyroStickInvertY;
        _stickCurve.Value = Clamp(_stickCurve, _settings.StickCurve * 100);
        _triggerDeadzone.Value = Clamp(_triggerDeadzone, _settings.TriggerDeadzone * 100);
        _triggerFull.Value = Clamp(_triggerFull, _settings.TriggerFullAt * 100);
        _turboRate.Value = Clamp(_turboRate, _settings.TurboRate);
        FillKinds();
        FillProfiles();
        _loading = false;
        _tuning.Reload();
    }

    private void RefreshTuning()
    {
        if (!_loading)
            _tuning.Reload();
    }

    /// <summary>Controller-Auswahl: verbundene Controller zuerst (mit Hinweis), dann alle übrigen.</summary>
    private void FillKinds()
    {
        bool was = _loading;
        _loading = true;
        var connected = _manager?.Players.Select(p => p.Kind).Distinct().ToList() ?? [];
        Fill(_mapKind, _mapKinds, _mapEditor.Kind);
        Fill(_tuneKind, _tuneKinds, _tuning.Kind);
        _loading = was;
        if (_mapKinds.Count > 0 && _mapEditor.Kind != _mapKinds[_mapKind.SelectedIndex])
            _mapEditor.Kind = _mapKinds[_mapKind.SelectedIndex];
        if (_tuneKinds.Count > 0 && _tuning.Kind != _tuneKinds[_tuneKind.SelectedIndex])
            _tuning.Kind = _tuneKinds[_tuneKind.SelectedIndex];

        void Fill(ComboBox combo, List<ControllerKind> order, ControllerKind current)
        {
            var newOrder = connected.Where(KindInfo.Configurable.Contains).ToList();
            newOrder.AddRange(KindInfo.Configurable.Where(k => !newOrder.Contains(k)));
            var labels = newOrder.Select(k => Tr.T(k.DisplayName() + (connected.Contains(k) ? "  (verbunden)" : ""))).ToList();
            if (order.SequenceEqual(newOrder) && combo.Items.Cast<string>().SequenceEqual(labels))
                return;
            order.Clear();
            order.AddRange(newOrder);
            combo.Items.Clear();
            combo.Items.AddRange(labels.ToArray<object>());
            // Vorher gewählten Controller behalten; beim ersten Mal den ersten verbundenen nehmen.
            int index = order.IndexOf(current);
            combo.SelectedIndex = connected.Count > 0 && combo.Tag is null ? 0 : Math.Max(0, index);
            combo.Tag = true;
        }
    }

    /// <summary>Profil oder Ebene wurde (auch in einer Controller-Karte) gewechselt: Auswahl hier angleichen.</summary>
    private void OnMappingChanged()
    {
        if (IsDisposed)
            return;
        bool was = _loading;
        _loading = true;
        int index = _mapping.EditProfile is null ? 0 : _settings.NamedProfiles.FindIndex(p => p.Name == _mapping.EditProfile) + 1;
        if (index != _profileSelect.SelectedIndex && index >= 0 && index < _profileSelect.Items.Count)
            _profileSelect.SelectedIndex = index;
        _layer.SelectedIndex = _mapping.Shift ? 1 : 0;
        LoadProfileFields();
        _loading = was;
    }

    // ---------- Profile ----------

    private void FillProfiles()
    {
        bool was = _loading;
        _loading = true;
        _profileSelect.Items.Clear();
        _profileSelect.Items.Add(Tr.T("Standard"));
        foreach (var p in _settings.NamedProfiles)
            _profileSelect.Items.Add(p.Name);
        int index = _mapping.EditProfile is null ? 0 : _settings.NamedProfiles.FindIndex(p => p.Name == _mapping.EditProfile) + 1;
        _profileSelect.SelectedIndex = Math.Max(0, index);
        _profileSelect.Tag = Tr.UserData;
        _layer.SelectedIndex = _mapping.Shift ? 1 : 0;
        LoadProfileFields();
        _loading = was;
    }

    private void LoadProfileFields()
    {
        var profile = _mapping.Profile;
        bool was = _loading;
        _loading = true;
        _programs.Text = profile is null ? "" : string.Join(", ", profile.Programs);
        _programs.Enabled = _programAdd.Enabled = profile is not null;
        _profileMenu.Items[0].Enabled = _profileMenu.Items[1].Enabled = profile is not null;
        _programs.PlaceholderText = Tr.T(profile is null ? "Standard gilt für alle Programme" : "z. B. Cemu.exe, Ryujinx.exe");
        _loading = was;
    }

    private static Dictionary<ControllerKind, Dictionary<ProButtons, string>> Copy(Dictionary<ControllerKind, Dictionary<ProButtons, string>> maps) =>
        maps.ToDictionary(m => m.Key, m => new Dictionary<ProButtons, string>(m.Value));

    private bool NameTaken(string name) =>
        name == "Standard" || _settings.NamedProfiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void NewProfile()
    {
        var name = Prompt.Ask(this, "Neues Profil", "Name des Profils (z. B. der Spielname):", "");
        if (name is null)
            return;
        if (NameTaken(name))
        {
            Tr.Show(this, "Ein Profil mit diesem Namen gibt es schon.", Text);
            return;
        }
        // Startet als Kopie der gerade bearbeiteten Belegung.
        var current = _mapping.Profile;
        var profile = new NamedProfile
        {
            Name = name,
            Buttons = Copy(current?.Buttons ?? _settings.Profiles),
            ShiftButtons = Copy(current?.ShiftButtons ?? _settings.ShiftProfiles),
        };
        Apply(() => _settings.NamedProfiles = [.. _settings.NamedProfiles, profile]);
        SelectProfile(name);
        _programs.Focus();
    }

    private void SelectProfile(string? name)
    {
        _mapping.SelectProfile(name);
        FillProfiles();
    }

    private void RenameProfile()
    {
        if (_mapping.Profile is not { } profile)
            return;
        var name = Prompt.Ask(this, "Profil umbenennen", "Neuer Name:", profile.Name);
        if (name is null || name == profile.Name)
            return;
        if (NameTaken(name))
        {
            Tr.Show(this, "Ein Profil mit diesem Namen gibt es schon.", Text);
            return;
        }
        Apply(() =>
        {
            _mapping.ReplaceProfile(profile, profile with { Name = name });
            if (_settings.ForcedProfile == profile.Name)
                _settings.ForcedProfile = name;
        });
        SelectProfile(name);
    }

    private void DeleteProfile()
    {
        if (_mapping.Profile is not { } profile)
            return;
        if (Tr.Show(this, $"Profil „{profile.Name}“ löschen?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        Apply(() =>
        {
            _mapping.ReplaceProfile(profile, null);
            if (_settings.ForcedProfile == profile.Name)
                _settings.ForcedProfile = null;
        });
        SelectProfile(null);
    }

    private const string ProfileFilter = "Controller-Profil (*.ncprofile.json)|*.ncprofile.json|JSON (*.json)|*.json";

    /// <summary>Gerade gewähltes Profil als Datei speichern (Standard wird als Profil „Standard“ exportiert).</summary>
    private void ExportProfile()
    {
        var profile = _mapping.Profile ?? new NamedProfile
        {
            Name = "Standard", Buttons = _settings.Profiles, ShiftButtons = _settings.ShiftProfiles,
        };
        using var dialog = new SaveFileDialog
        {
            Title = Tr.T("Profil exportieren"), Filter = Tr.T(ProfileFilter),
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
        using var dialog = new OpenFileDialog { Title = Tr.T("Profil importieren"), Filter = Tr.T(ProfileFilter) };
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
        for (int n = 2; NameTaken(name); n++)
            name = $"{baseName} ({n})";
        var imported = profile with { Name = name };
        Apply(() => _settings.NamedProfiles = [.. _settings.NamedProfiles, imported]);
        SelectProfile(name);
        Tr.Show(this, $"Profil „{name}“ importiert" +
            (imported.Programs.Count > 0 ? $" – aktiv bei: {string.Join(", ", imported.Programs)}." : "."), Text);
    }

    private void SavePrograms()
    {
        if (_loading || _mapping.Profile is not { } profile)
            return;
        var programs = _programs.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : p + ".exe")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (programs.SequenceEqual(profile.Programs))
            return;
        Apply(() => _mapping.ReplaceProfile(profile, profile with { Programs = programs }));
        _loading = true;
        _programs.Text = string.Join(", ", programs);
        _loading = false;
    }

    private void AddProgram()
    {
        using var dialog = new OpenFileDialog
        {
            Title = Tr.T("Spiel oder Programm wählen"), Filter = Tr.T("Programme (*.exe)|*.exe"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        var exe = Path.GetFileName(dialog.FileName);
        _programs.Text = string.IsNullOrWhiteSpace(_programs.Text) ? exe : $"{_programs.Text}, {exe}";
        SavePrograms();
    }

    // ---------- Übernehmen ----------

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
        _changed(false);
        _mapping.Refresh(); // „Standard: …“ neu beschriften
    }

    private void Apply(Action change)
    {
        if (_loading)
            return;
        change();
        _changed(false);
    }

    /// <summary>Darstellung wechseln: speichern und das Fenster in der neuen Darstellung neu öffnen.</summary>
    private void ChangeTheme()
    {
        if (_loading)
            return;
        string? mode = _themeMode.SelectedIndex switch { 1 => "light", 2 => "system", _ => null };
        if (mode == _settings.Theme && _transparency.Checked == _settings.Transparency)
            return;
        _settings.Theme = mode;
        _settings.Transparency = _transparency.Checked;
        _changed(false);
        Theme.Init(mode, _transparency.Checked);
        Reopen();
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
        Reopen();
    }

    /// <summary>Fenster schließen; die App öffnet es über das Signal sofort neu (auf derselben Seite).</summary>
    private void Reopen() =>
        BeginInvoke(() =>
        {
            ReopenPage = _page;
            Close();
            Program.ShowSignal?.Set();
        });

    /// <summary>Seite, auf der ein neu geöffnetes Fenster starten soll (nach Sprach-/Darstellungswechsel).</summary>
    public static int? ReopenPage { get; set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (ReopenPage is { } page)
        {
            ReopenPage = null;
            ShowPage(page);
        }
    }

    /// <summary>Nach dem Neuladen der Datei (von außen geändert) alle Felder neu befüllen.</summary>
    public void ReloadValues()
    {
        if (IsDisposed)
            return;
        LoadValues();
        _mapping.Refresh();
    }

    private void ResetAll()
    {
        if (Tr.Show(this, "Alle Einstellungen und Tastenbelegungen auf Standard zurücksetzen?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        var defaults = new Settings();
        bool outputChanged = _settings.OutputMode != defaults.OutputMode;
        // Bekannte Controller, Freigaben und gemessene Werte bleiben erhalten.
        defaults.KnownControllers = _settings.KnownControllers;
        defaults.AllowedControllers = _settings.AllowedControllers;
        defaults.SingleJoyCons = _settings.SingleJoyCons;
        defaults.UprightJoyCons = _settings.UprightJoyCons;
        defaults.GyroCalibration = _settings.GyroCalibration;
        defaults.HiddenDevices = _settings.HiddenDevices;
        defaults.ConsoleHintShown = _settings.ConsoleHintShown;
        defaults.AutostartConfigured = _settings.AutostartConfigured;
        defaults.Theme = _settings.Theme;
        defaults.Transparency = _settings.Transparency;
        defaults.Language = _settings.Language;
        _settings.CopyFrom(defaults);
        _mapping.SelectProfile(null);
        LoadValues();
        _mapping.Refresh();
        _changed(outputChanged);
    }


    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _liveTimer.Dispose();
        _tips.Dispose();
        _mapping.Changed -= OnMappingChanged;
        base.OnFormClosed(e);
    }
}
