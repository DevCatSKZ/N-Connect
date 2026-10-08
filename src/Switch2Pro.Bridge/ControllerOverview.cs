using System.Drawing;
using System.Drawing.Drawing2D;
using Switch2Pro.Bridge.Links;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Übersicht aller verbundenen Controller: je Spieler eine Karte mit gezeichnetem Controller (Live-Eingaben),
/// Eigenschaften (Akku, Verbindung …) und aufklappbaren Einstellungen genau für diesen Controller.
/// </summary>
internal sealed class ControllerOverview : Panel
{
    internal static Color Background => Theme.Backdrop;
    internal static Color CardColor => Theme.Current.Surface;
    internal static Color TextColor => Theme.Current.Text;
    internal static Color MutedColor => Theme.Current.TextMuted;
    internal static Color Accent => Theme.Accent;

    private readonly ControllerManager? _manager;
    private readonly Func<Settings> _settings;
    private readonly MappingContext _mapping;
    private readonly Action _save;
    private readonly Action<int> _showPage;
    private readonly FlowLayoutPanel _cards = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoScroll = true,
        BackColor = Background, Padding = new Padding(12, 4, 12, 12),
    };
    private readonly Panel _empty = new() { Dock = DockStyle.Fill, BackColor = Background, Visible = false };
    private readonly Label _emptyText = new()
    {
        AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = MutedColor, BackColor = Background, Font = UiFonts.Body,
        Text = "Kein Controller verbunden\n\n" +
               "Switch-2-Controller: kurz die SYNC-Taste drücken – danach reicht ein beliebiger Tastendruck.\n" +
               "Switch-1-, NSO- und Wii-Controller: SYNC-Taste drücken – N-Connect koppelt sie selbst.\n" +
               "Oder hier gezielt suchen und koppeln:",
    };
    /// <summary>Suche/Kopplung (Fenster „Controller koppeln“) – Knopf in der Übersicht und Leiste unten.</summary>
    private readonly Action? _pair;
    private readonly GlyphButton? _pairButton;
    private readonly Panel _pairBar = new() { Dock = DockStyle.Bottom, Height = 52, BackColor = Background, Visible = false };
    private readonly Dictionary<Player, Card> _byPlayer = [];
    /// <summary>Spieler-Reihenfolge (ab zwei Spielern sichtbar).</summary>
    private readonly PlayerOrderBar _order;

    public ControllerOverview(ControllerManager? manager, Func<Settings> settings, MappingContext mapping, Action save, Action<int> showPage, Action? pair = null)
    {
        _manager = manager;
        _settings = settings;
        _mapping = mapping;
        _save = save;
        _showPage = showPage;
        _pair = pair;
        Dock = DockStyle.Fill;
        BackColor = Background;
        if (pair is not null)
        {
            _pairButton = new GlyphButton("Controller suchen …", Glyph.Bluetooth, accent: true);
            _pairButton.Click += (_, _) => pair();
        }
        InitEmpty();
        InitPairBar();
        Controls.Add(_cards);
        Controls.Add(_empty);
        _order = new PlayerOrderBar((player, target) => _manager?.MovePlayer(player, target)) { Visible = false };
        Controls.Add(_order);
        Theme.DarkScroll(_cards);
        Tr.Apply(_empty);
        Tr.Apply(_pairBar);
        InitBanner(); // zuletzt hinzugefügt = zuerst angedockt (oben)
    }

    /// <summary>Leerzustand: Hinweistext mittig, darunter der Knopf „Controller suchen …“.</summary>
    private void InitEmpty()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Background, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var cell = new Panel { Dock = DockStyle.Fill, BackColor = Background };
        if (_pairButton is not null)
        {
            var button = new GlyphButton("Controller suchen …", Glyph.Bluetooth, accent: true);
            button.Click += (_, _) => _pair!();
            cell.Controls.Add(button);
            cell.Height = 44;
            // Mittig unter dem Text.
            cell.Resize += (_, _) => button.Location = new Point(
                Math.Max(0, (cell.Width - button.Width) / 2), (cell.Height - button.Height) / 2);
        }
        layout.Controls.Add(_emptyText, 0, 0);
        layout.Controls.Add(cell, 0, 1);
        _empty.Controls.Add(layout);
    }

    /// <summary>Leiste am unteren Rand: „Controller suchen …“, sichtbar sobald mind. ein Controller verbunden ist.</summary>
    private void InitPairBar()
    {
        if (_pairButton is null)
            return;
        var hint = new Label
        {
            AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MutedColor, BackColor = Background, Font = UiFonts.Small,
            Text = "Switch-1-, NSO- und Wii-Controller suchen und koppeln (SYNC-Taste drücken).",
            Padding = new Padding(8, 0, 0, 0),
        };
        var box = new Panel { Dock = DockStyle.Left, Width = _pairButton.Width + 18, BackColor = Background };
        _pairButton.Location = new Point(14, (_pairBar.Height - _pairButton.Height) / 2);
        box.Controls.Add(_pairButton);
        _pairBar.Controls.Add(hint);
        _pairBar.Controls.Add(box);
        Controls.Add(_pairBar);
    }

    /// <summary>Hinweisleiste oben bei Problemen (kein Bluetooth, ViGEmBus fehlt) mit passender Aktion.</summary>
    private readonly Panel _banner = new() { Dock = DockStyle.Top, Height = 54, BackColor = Color.FromArgb(120, 40, 40), Visible = false };
    private readonly Label _bannerText = new()
    {
        Dock = DockStyle.Fill, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 8, 0),
    };
    private readonly Button _bannerAction = new()
    {
        Dock = DockStyle.Right, Width = 230, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(150, 60, 60),
    };
    private string? _bannerShown;

    private void InitBanner()
    {
        _bannerAction.FlatAppearance.BorderColor = Color.FromArgb(220, 150, 150);
        _bannerAction.Click += (_, _) =>
        {
            string target = _manager is null ? "https://github.com/nefarius/ViGEmBus/releases" : "ms-settings:bluetooth";
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Log.Warn($"Öffnen fehlgeschlagen: {e.Message}"); }
        };
        _banner.Controls.Add(_bannerText);
        _banner.Controls.Add(_bannerAction);
        Controls.Add(_banner);
    }

    private void UpdateBanner()
    {
        string? text = _manager is null
            ? "ViGEmBus-Treiber fehlt – ohne ihn kann kein virtueller Controller erzeugt werden. Bitte das Setup erneut ausführen."
            : _manager.AdapterProblem is { } problem ? problem + " " + ControllerManager.AdapterHint(problem) : null;
        if (text == _bannerShown)
            return;
        _bannerShown = text;
        _banner.Visible = text is not null;
        if (text is null)
            return;
        _bannerText.Text = Tr.T(text);
        _bannerAction.Text = Tr.T(_manager is null ? "ViGEmBus herunterladen …" : "Bluetooth-Einstellungen öffnen");
    }

    public void UpdateView()
    {
        UpdateBanner();
        var players = _manager?.Players ?? [];
        _empty.Visible = players.Count == 0;
        _cards.Visible = players.Count > 0;
        _pairBar.Visible = players.Count > 0 && _pair is not null;
        _order.SetPlayers(players, _settings());
        foreach (var gone in _byPlayer.Keys.Except(players).ToList())
        {
            _cards.Controls.Remove(_byPlayer[gone]);
            _byPlayer[gone].Dispose();
            _byPlayer.Remove(gone);
        }
        foreach (var p in players)
        {
            if (!_byPlayer.TryGetValue(p, out var card))
            {
                card = new Card(this);
                _byPlayer[p] = card;
                _cards.Controls.Add(card);
                card.Width = CardWidth(card);
            }
            card.Show(p, _settings());
        }
        // Karten in Spielerreihenfolge (auch nach dem Umsortieren).
        for (int i = 0; i < players.Count; i++)
            if (_byPlayer.TryGetValue(players[i], out var ordered) && _cards.Controls.GetChildIndex(ordered) != i)
                _cards.Controls.SetChildIndex(ordered, i);
        // Zusammengeklappte Karten gleich hoch – nebeneinander wirkt die Übersicht so ruhig und symmetrisch.
        int body = _byPlayer.Values.Where(c => !c.IsExpanded).Select(c => c.NaturalBodyHeight).DefaultIfEmpty(0).Max();
        foreach (var card in _byPlayer.Values)
            if (card.BodyHeight != body)
            {
                card.BodyHeight = body;
                card.PerformLayout();
            }
        ApplyCardFilter();
    }

    internal Control? FirstCard => _byPlayer.Values.FirstOrDefault();

    /// <summary>Für die Prüfhilfe: alle Karten (in Spielerreihenfolge) und das Aufklappen eines Reiters.</summary>
    internal IReadOnlyList<Control> Cards => _cards.Controls.Cast<Control>().ToList();

    internal static bool ExpandCard(Control card, int tab) => ((Card)card).Expand(tab);

    /// <summary>Gefilterter Spieler – Unterpunkt in der Navigation zeigt nur seine Karte; null = alle.</summary>
    private Player? _cardFilter;

    /// <summary>Nur die Karte dieses Spielers zeigen; null = alle Karten.</summary>
    internal void ShowOnlyCard(Player? player)
    {
        _cardFilter = player;
        ApplyCardFilter();
    }

    /// <summary>Kartenfilter anwenden; fällt weg, wenn der gefilterte Controller nicht mehr verbunden ist.</summary>
    private void ApplyCardFilter()
    {
        if (_cardFilter is { } f && !_byPlayer.Keys.Any(k => ReferenceEquals(k, f)))
            _cardFilter = null;
        foreach (var (p, card) in _byPlayer)
            card.Visible = _cardFilter is null || ReferenceEquals(p, _cardFilter);
        if (_cardFilter is not null)
            _order.Visible = _pairBar.Visible = false;
    }

    /// <summary>Für die Prüfhilfe: Einstellungen der ersten Karte aufklappen und einen Reiter wählen.</summary>
    internal void ExpandFirst(int tab)
    {
        if (_byPlayer.Values.FirstOrDefault() is { } card)
            card.Expand(tab);
    }

    // ---------- Anordnung: zusammengeklappte Karten nebeneinander, wenn Platz ist ----------
    private const int Gap = 12, MinCardWidth = 700;

    private int Available => Math.Max(MinCardWidth, _cards.ClientSize.Width - _cards.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);

    private int CardWidth(Card card)
    {
        int full = Available - Gap;
        bool twoColumns = Available >= 2 * MinCardWidth + 2 * Gap;
        return card.IsExpanded || !twoColumns ? full : (Available - 2 * Gap) / 2;
    }

    private void LayoutCards()
    {
        _cards.SuspendLayout();
        foreach (var card in _byPlayer.Values)
        {
            int width = CardWidth(card);
            if (card.Width != width)
                card.Width = width;
        }
        _cards.ResumeLayout();
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        LayoutCards();
    }

    /// <summary>Live-Eingaben eines Spielers (für Anzeige und Hervorhebung in der Tastenbelegung).</summary>
    internal static (PadInput? Input, GamepadState Gamepad) LiveInput(Player player, Settings settings)
    {
        var links = player.Links;
        if (links.Count == 0 || links.Any(l => player.EffectiveState(l) is null))
            return (null, default);
        var input = Player.Combine(links, player.EffectiveState, settings);
        return (input, Mapping.ToGamepad(input, settings));
    }

    /// <summary>
    /// Eine Karte pro Spieler: oben Titel und Aktionen, darunter Grafik und Eigenschaften; „Einstellungen“ klappt
    /// Reiter mit Tastenbelegung, Feineinstellung, Gyro, Joy-Con und Extras für genau diesen Controller auf.
    /// </summary>
    private sealed class Card : Panel
    {
        private const int TabMapping = 0, TabTuning = 1, TabGyro = 2, TabJoyCon = 3, TabExtras = 4, TabDetails = 5;
        private readonly ControllerOverview _owner;
        private readonly InputView _view = new() { Size = new Size(400, 297) };
        private readonly InfoPanel _info = new();
        private readonly GlyphButton _identify = new("Vibrieren", Glyph.Vibrate);
        private readonly GlyphButton _disconnect = new("Trennen", Glyph.Power);
        /// <summary>Bei Joy-Con sichtbar: Paar lösen oder einzelnen Joy-Con zum Paar verbinden.</summary>
        private readonly GlyphButton _pairToggle = new("Paar", Glyph.Swap);
        private readonly GlyphButton _settingsButton = new("Einstellungen", Glyph.Settings) { Toggle = true, TrailingGlyph = Glyph.ChevronDown };
        private readonly ToolTip _tips = Theme.CreateToolTip();
        private readonly PivotTabs _tabs = new();
        private readonly StackPanel _panel = new() { Spacing = 0 };
        private readonly Control?[] _tabPages = new Control?[6];
        private Player? _player;
        private ControllerKind _builtFor = ControllerKind.Unknown;
        private bool _expanded;
        public bool IsExpanded => _expanded;

        /// <summary>Höhe, die die Karte zusammengeklappt selbst braucht.</summary>
        public int NaturalBodyHeight { get; private set; }

        /// <summary>Vorgabe der Übersicht: alle zusammengeklappten Karten gleich hoch.</summary>
        public int BodyHeight { get; set; }
        private InfoPanel? _details;
        private string? _title;
        private string? _titleShort;
        private string? _rawTitle;

        private ControllerManager Manager => _owner._manager!;
        private Settings CurrentSettings => _owner._settings();

        public Card(ControllerOverview owner)
        {
            _owner = owner;
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Background;
            Margin = new Padding(0, 0, Gap, Gap);
            Height = 460;
            foreach (var b in new[] { _identify, _disconnect, _pairToggle, _settingsButton })
                b.BackColor = CardColor;
            _tabs.BackColor = CardColor;
            _panel.BackColor = Theme.Backdrop;
            _tabs.Add("Tasten");
            _tabs.Add("Feineinstellung");
            _tabs.Add("Gyro");
            _tabs.Add("Joy-Con");
            _tabs.Add("Extras");
            _tabs.Add("Details");
            _tabs.Visible = _panel.Visible = false;
            _tabs.SelectedIndexChanged += (_, _) => ShowTab();

            _identify.Click += (_, _) => _player?.IdentifyAsync().Forget("Vibrieren");
            _disconnect.Click += (_, _) => { if (_player is not null) Manager.Disconnect(_player); };
            _pairToggle.Click += (_, _) =>
            {
                if (_player is null)
                    return;
                if (_player.IsPair)
                    Manager.SplitPair(_player);
                else
                    Manager.PairWithAnySingle(_player);
            };
            _settingsButton.Click += (_, _) => { if (_expanded) Collapse(); else Expand(_tabs.SelectedIndex); };
            _tips.SetToolTip(_disconnect, Tr.T("Verbindung trennen. Der Controller verbindet sich beim nächsten Tastendruck wieder."));
            _tips.SetToolTip(_identify, Tr.T("Controller kurz vibrieren lassen – zeigt, welcher Controller dieser Spieler ist."));
            _tips.SetToolTip(_settingsButton, Tr.T("Tastenbelegung und Einstellungen nur für diesen Controller"));

            Controls.AddRange([_view, _info, _identify, _disconnect, _pairToggle, _settingsButton, _tabs, _panel]);
            Tr.Apply(this);
        }

        // ---------- Aufklappen ----------

        public bool Expand(int tab)
        {
            if (_player is null)
                return false;
            _expanded = true;
            _settingsButton.Checked = true;
            _settingsButton.TrailingGlyph = Glyph.ChevronUp;
            _settingsButton.Invalidate();
            BuildPages();
            bool shown = _tabs.IsShown(tab);
            if (!shown)
                tab = TabMapping;
            if (tab != _tabs.SelectedIndex)
                _tabs.SelectedIndex = tab;
            _tabs.Visible = _panel.Visible = true;
            _owner.LayoutCards(); // aufgeklappt über die ganze Breite
            ShowTab();
            return shown;
        }

        private void Collapse()
        {
            _expanded = false;
            _settingsButton.Checked = false;
            _settingsButton.TrailingGlyph = Glyph.ChevronDown;
            _settingsButton.Invalidate();
            _tabs.Visible = _panel.Visible = false;
            PerformLayout();
            _owner.LayoutCards();
        }

        /// <summary>Kind, dessen Belegung gilt: hochkant gehaltene einzelne Joy-Con nutzen die des Joy-Con-Paars.</summary>
        private ControllerKind MappingKind(Player player, Settings settings)
        {
            var links = player.Links;
            if (links is [{ Kind: var k, Address: var address }] && k.IsJoyCon() && settings.IsUprightJoyCon(address))
                return ControllerKind.JoyConPair;
            return player.Kind;
        }

        /// <summary>Reiterseiten für den aktuellen Controller (neu bei anderer Controller-Art).</summary>
        private void BuildPages()
        {
            var player = _player!;
            var kind = MappingKind(player, CurrentSettings);
            if (kind == _builtFor)
                return;
            _builtFor = kind;
            // Alte Seiten verwerfen; neue entstehen erst, wenn ihr Reiter gewählt wird (schnelles Aufklappen).
            foreach (Control c in _panel.Controls.Cast<Control>().ToList())
            {
                _panel.Controls.Remove(c);
                c.Dispose();
            }
            Array.Clear(_tabPages);
            if (_profile is not null)
                _owner._mapping.Changed -= SyncProfile;
            _profile = null;
            _layer = null;
            _editor = null;
            _calibrate = null;
            _joyConGroup = null;
            _extrasGroup = null;
            var links = player.Links;
            _tabs.SetShown(TabTuning, KindInfo.HasSticks(kind) || KindInfo.HasAnalogTriggers(kind) || KindInfo.HasRumble(kind));
            _tabs.SetShown(TabGyro, KindInfo.HasMotion(kind));
            _tabs.SetShown(TabJoyCon, links.Count > 0 && links.All(l => l.Kind.IsJoyCon()));
            _tabs.SetShown(TabExtras, ExtrasAvailable(CurrentSettings));
            _details = null;
        }

        private Control? BuildTab(int tab, ControllerKind kind) => tab switch
        {
            TabMapping => MappingPage(kind),
            TabTuning => TuningPage(kind),
            TabGyro => GyroPage(kind),
            TabJoyCon => JoyConPage(),
            TabExtras => ExtrasPage(),
            TabDetails => DetailsPage(),
            _ => null,
        };

        /// <summary>Feineinstellung: Stick-Kalibrierung (falls möglich), dann Totzone, Kennlinie, Trigger, Vibration.</summary>
        private Control TuningPage(ControllerKind kind)
        {
            var column = Column(
                new TuningEditor(_owner._settings, _owner._save, kind, TuningParts.Sticks | TuningParts.Triggers | TuningParts.Rumble),
                Hint("Ohne eigenen Wert gilt der allgemeine Wert der Seite „Sticks & Vibration“."));
            if (_player is { } player && StickCalibrationForm.CanCalibrate(player))
            {
                var calibrate = new GlyphButton("Kalibrieren …", Glyph.Stick);
                calibrate.Click += (_, _) =>
                {
                    if (_player is null)
                        return;
                    using var form = new StickCalibrationForm(_player, _owner._settings, _owner._save);
                    form.ShowDialog(FindForm());
                };
                var group = Group(Row("Sticks kalibrieren", "Gegen Drift oder zu kleinen Ausschlag: Mitte und Rand neu messen. " +
                    "Wird nur in N-Connect gespeichert, der Controller bleibt unverändert.", calibrate, Glyph.Stick));
                column.Controls.Add(group);
                column.Controls.SetChildIndex(group, 0);
            }
            return column;
        }

        private void ShowTab()
        {
            int selected = _tabs.SelectedIndex;
            if (_tabPages[selected] is null && BuildTab(selected, _builtFor) is { } built)
            {
                _tabPages[selected] = built;
                Theme.ApplyControls(built);
                Tr.Apply(built);
                _panel.Controls.Add(built);
            }
            for (int i = 0; i < _tabPages.Length; i++)
                if (_tabPages[i] is { } page)
                    _panel.SetShown(page, i == selected);
            PerformLayout();
        }

        private static StackPanel Column(params Control[] children)
        {
            var column = new StackPanel { Spacing = 8, BackColor = Theme.Backdrop };
            column.Controls.AddRange(children);
            return column;
        }

        private static SettingsGroup Group(params Control[] rows)
        {
            var group = new SettingsGroup();
            group.Controls.AddRange(rows);
            return group;
        }

        private static Heading Hint(string text) => new(text, hint: true) { Margin = new Padding(4, 2, 0, 2) };

        private SettingRow Link(string title, string description, int page)
        {
            var row = new SettingRow(title, description, null, Glyph.Settings) { Navigates = true };
            row.Click += (_, _) => _owner._showPage(page);
            return row;
        }

        private static SettingRow Row(string title, string? description, Control content, string glyph)
        {
            content.BackColor = Theme.Current.Surface;
            return new SettingRow(title, description, content, glyph);
        }

        // Tasten: Profil und Ebene (gemeinsam mit der Seite „Tastenbelegung“), darunter alle Tasten.
        private ComboBox? _profile;
        private Segmented? _layer;
        private MappingEditor? _editor;

        private Control MappingPage(ControllerKind kind)
        {
            _profile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Tag = Tr.UserData };
            _layer = new Segmented("Normal", "Shift-Ebene");
            var manage = new GlyphButton("Profile", Glyph.Layers);
            manage.Click += (_, _) => _owner._showPage(SettingsForm.PageMapping);
            _tips.SetToolTip(manage, Tr.T("Profile anlegen, umbenennen, exportieren …"));
            var profileBox = new Panel { Size = new Size(_profile.Width + 8 + manage.Width, 32) };
            _profile.Location = new Point(0, (32 - _profile.Height) / 2);
            manage.Location = new Point(_profile.Width + 8, 0);
            profileBox.Controls.AddRange([_profile, manage]);
            FillProfiles();
            _profile.SelectedIndexChanged += (_, _) =>
            {
                if (!_syncing)
                    _owner._mapping.SelectProfile(_profile.SelectedIndex <= 0 ? null : _profile.SelectedItem as string);
            };
            _layer.SelectedIndexChanged += (_, _) => { if (!_syncing) _owner._mapping.SetShift(_layer.SelectedIndex == 1); };
            _owner._mapping.Changed += SyncProfile;
            _editor = new MappingEditor(_owner._mapping, kind);
            bool native = _player?.Links is { } links && links.Count > 0 && links.All(l => l.Native);
            var column = Column(
                Group(Row("Profil", "Belegung für alle Spiele (Standard) oder ein eigenes Profil.", profileBox, Glyph.Layers),
                      Row("Ebene", "Die Shift-Ebene gilt, solange eine Taste mit „Shift-Ebene“ gehalten wird.", _layer, Glyph.Layers)),
                Hint("Drück eine Taste am Controller – ihre Zeile leuchtet auf. ⌨ weist eine Tastaturtaste zu, ↺ setzt zurück."),
                _editor);
            Control outputRow;
            if (native)
            {
                // Xbox über XInput ist in Spielen schon der Controller selbst – nichts umzuleiten.
                outputRow = new SettingRow("Erscheint als", "Der echte Xbox-Controller – Spiele sehen ihn von Windows aus direkt. " +
                    "Belegungen hier wirken auf Sonderaktionen (Tastatur, Makros, Gyro), nicht auf den Controller selbst.",
                    null, Glyph.Gamepad);
            }
            else
            {
                // Ausgabeart nur für diesen Controller (z. B. Pro Controller als DualShock 4 mit Gyro in Steam).
                var output = new Segmented("Wie allgemein", "Xbox 360", "DualShock 4");
                var own = _player?.Links.Select(l => CurrentSettings.OutputFor(l.Address)).FirstOrDefault(m => m is not null);
                output.SelectedIndex = own switch { OutputMode.Xbox360 => 1, OutputMode.DualShock4 => 2, _ => 0 };
                output.SelectedIndexChanged += (_, _) =>
                {
                    if (_player is { } p)
                        Manager.SetPlayerOutput(p, output.SelectedIndex switch { 1 => OutputMode.Xbox360, 2 => OutputMode.DualShock4, _ => null });
                };
                outputRow = Row("Erscheint als", "Nur für diesen Controller. Xbox 360 läuft überall; DualShock 4 bringt zusätzlich Gyro " +
                          "nach Steam und in Emulatoren. „Wie allgemein“ folgt der Seite „Allgemein“.", output, Glyph.Gamepad);
            }
            var outputGroup = Group(outputRow);
            column.Controls.Add(outputGroup);
            column.Controls.SetChildIndex(outputGroup, 0);
            return column;
        }

        private bool _syncing;

        private void FillProfiles()
        {
            if (_profile is null)
                return;
            _syncing = true;
            var s = CurrentSettings;
            _profile.Items.Clear();
            _profile.Items.Add(Tr.T("Standard"));
            foreach (var p in s.NamedProfiles)
                _profile.Items.Add(p.Name);
            int index = _owner._mapping.EditProfile is null ? 0 : s.NamedProfiles.FindIndex(p => p.Name == _owner._mapping.EditProfile) + 1;
            _profile.SelectedIndex = Math.Max(0, index);
            if (_layer is not null)
                _layer.SelectedIndex = _owner._mapping.Shift ? 1 : 0;
            _syncing = false;
        }

        private void SyncProfile()
        {
            if (!IsDisposed)
                FillProfiles();
        }

        // Gyro: Nullpunkt kalibrieren, Gyro-Maus-Tempo dieses Controllers, Verweis auf Gyro-Stick.
        private GlyphButton? _calibrate;
        private bool _calibrating;

        private Control? GyroPage(ControllerKind kind)
        {
            if (!KindInfo.HasMotion(kind))
                return null;
            _calibrate = new GlyphButton("Kalibrieren", Glyph.Gauge);
            _calibrate.Click += async (_, _) => await CalibrateAsync();
            var setup = new GlyphButton("Einrichten …", Glyph.Rotate, accent: true);
            setup.Click += (_, _) =>
            {
                if (_player is null)
                    return;
                using var form = new GyroSetupForm(_player, _owner._settings, _owner._save);
                form.ShowDialog(FindForm());
            };
            return Column(
                Group(Row("Zielen per Bewegung einrichten", "Schritt für Schritt: Nullpunkt, wann der Gyro zielt, Empfindlichkeit – mit Vorschau.",
                    setup, Glyph.Rotate),
                    Row("Gyro kalibrieren", "Controller ruhig auf den Tisch legen und klicken: misst den Nullpunkt neu (gegen Abdriften).",
                    _calibrate, Glyph.Gauge)),
                new TuningEditor(_owner._settings, _owner._save, kind, TuningParts.GyroMouse),
                Group(Link("Gyro als rechter Stick und Mausrichtung", "Gilt für alle Controller", SettingsForm.PageGyro)));
        }

        // Joy-Con: Paar lösen/verbinden, Haltung des einzelnen Joy-Con.
        private GlyphButton? _pairButton;
        private Segmented? _orientation;
        private SettingRow? _pairRow, _orientationRow;
        private SettingsGroup? _joyConGroup;

        private Control? JoyConPage()
        {
            if (_player is not { Links: { Count: > 0 } links } || !links.All(l => l.Kind.IsJoyCon()))
                return null;
            _pairButton = new GlyphButton("Joy-Con trennen", Glyph.Swap);
            _pairButton.Click += (_, _) =>
            {
                if (_player is null)
                    return;
                if (_player.IsPair)
                    Manager.SplitPair(_player);
                else
                    Manager.PairWithAnySingle(_player);
            };
            _orientation = new Segmented("Quer", "Hochkant");
            _orientation.SelectedIndexChanged += (_, _) =>
            {
                if (_syncing || _player?.Links is not [{ Address: { } address }])
                    return;
                CurrentSettings.SetUprightJoyCon(address, _orientation.SelectedIndex == 1);
                Manager.RequestSave();
            };
            _pairRow = Row("Paar", "Joy-Con quer halten und SL oder SR drücken löst das Paar, L + R gleichzeitig verbindet es.", _pairButton, Glyph.Swap);
            _orientationRow = Row("Haltung", "Quer wie an der Switch oder hochkant – hochkant gilt die Belegung von „Joy-Con-Paar“.", _orientation, Glyph.Rotate);
            _joyConGroup = Group(_pairRow, _orientationRow);
            return Column(_joyConGroup, Group(Link("Joy-Con 2 als Maus", "Mausmodus, Geschwindigkeit und Tasten", SettingsForm.PageJoyCon)));
        }

        // Extras: amiibo, Ring-Con, IR-Kamera, USB-Controller vor Spielen verstecken.
        private GlyphButton? _amiibo, _ringCon, _irCamera, _hide;
        private SettingRow? _amiiboRow, _ringRow, _irRow, _hideRow;
        private SettingsGroup? _extrasGroup;

        private Control ExtrasPage()
        {
            _amiibo = new GlyphButton("Lesen …", Glyph.Nfc);
            _amiibo.Click += async (_, _) => await ReadAmiiboAsync();
            _ringCon = new GlyphButton("Einschalten", Glyph.Ring);
            _ringCon.Click += async (_, _) => await ToggleRingConAsync();
            _irCamera = new GlyphButton("Öffnen", Glyph.Video);
            _irCamera.Click += (_, _) =>
            {
                if (_player?.Links.OfType<Switch1HidLink>().FirstOrDefault(l => l.HasIrCamera) is { } ir)
                    IrCameraForm.ShowFor(ir, FindForm());
            };
            _hide = new GlyphButton("Verstecken", Glyph.Eye);
            _hide.Click += async (_, _) => await HideAsync();
            _amiiboRow = Row("amiibo lesen", "amiibo an den NFC-Leser halten und als Datei (.bin) speichern – z. B. für Emulatoren.", _amiibo, Glyph.Nfc);
            _ringRow = Row("Ring-Con", "Zusammendrücken = rechter Trigger, auseinanderziehen = linker Trigger. Beim Einschalten nicht berühren.", _ringCon, Glyph.Ring);
            _irRow = Row("IR-Kamera", "Live-Bild der Infrarotkamera im rechten Joy-Con.", _irCamera, Glyph.Video);
            _hideRow = Row("Doppelt angezeigt?", "Versteckt den Original-Controller vor Steam und Spielen (HidHide) – sie sehen dann nur den virtuellen Controller.", _hide, Glyph.Eye);
            _extrasGroup = Group(_amiiboRow, _ringRow, _irRow, _hideRow);
            UpdateExtras(CurrentSettings);
            return Column(_extrasGroup);
        }

        /// <summary>Welche Extras gerade möglich sind (Ring-Con-Zustand, USB …) – günstig, läuft mit der Live-Anzeige.</summary>
        private (bool Amiibo, Switch1HidLink? Ring, bool Hide) Extras(Settings settings)
        {
            var links = _player?.Links ?? [];
            return (links.OfType<Switch1HidLink>().Any(l => l.HasNfc),
                links.OfType<Switch1HidLink>().FirstOrDefault(l => l.Kind == ControllerKind.JoyCon1Right),
                links.Any(l => l.HidInstanceId is { } id && !settings.IsHidden(id)));
        }

        private bool ExtrasAvailable(Settings settings)
        {
            var (amiibo, ring, hide) = Extras(settings);
            return amiibo || ring is not null || hide;
        }

        private void UpdateExtras(Settings settings)
        {
            if (_player is null)
                return;
            var (amiibo, ring, hide) = Extras(settings);
            _tabs.SetShown(TabExtras, amiibo || ring is not null || hide);
            if (_extrasGroup is null)
                return;
            Show(_extrasGroup, _amiiboRow!, amiibo);
            Show(_extrasGroup, _ringRow!, ring is not null);
            Show(_extrasGroup, _irRow!, ring is { HasIrCamera: true });
            Show(_extrasGroup, _hideRow!, hide);
            if (ring is not null && _ringCon!.Enabled)
                SetText(_ringCon, ring.RingConActive ? "Ausschalten" : "Einschalten");

            static void Show(StackPanel group, Control row, bool shown)
            {
                if (group.IsShown(row) != shown)
                    group.SetShown(row, shown);
            }
        }

        private void UpdateJoyCon(Settings settings)
        {
            if (_joyConGroup is null || _player is null)
                return;
            var links = _player.Links;
            bool single = links.Count == 1;
            if (_player.IsPair)
                SetText(_pairButton!, "Joy-Con trennen");
            else
                SetText(_pairButton!, "Zum Paar verbinden");
            _pairButton!.Enabled = _player.IsPair || Manager.HasPartner(_player);
            if (_joyConGroup.IsShown(_orientationRow!) != single)
                _joyConGroup.SetShown(_orientationRow!, single);
            if (single)
            {
                _syncing = true;
                _orientation!.SelectedIndex = settings.IsUprightJoyCon(links[0].Address) ? 1 : 0;
                _syncing = false;
            }
        }

        private static void SetText(Control c, string german)
        {
            // Selbst übersetzende Knöpfe bekommen den deutschen Text, alle anderen den übersetzten.
            string text = c is ISelfTranslating ? german : Tr.T(german);
            if (c.Text != text)
                c.Text = text;
        }

        /// <summary>Alle Eigenschaften (Adresse, Seriennummer, Firmware …) – in der Karte stehen nur die wichtigsten.</summary>
        private Control DetailsPage()
        {
            _details = new InfoPanel { Compact = false, Margin = new Padding(16, 12, 16, 12) };
            var group = new SettingsGroup();
            group.Controls.Add(_details);
            return Column(group);
        }

        // ---------- Anordnung ----------

        protected override void OnLayout(LayoutEventArgs levent)
        {
            const int pad = 20, top = 62;
            // Vibrieren/Trennen immer als Symbolknöpfe (mit Tooltip), damit der Titel Platz hat.
            SetText(_identify, "");
            SetText(_disconnect, "");
            SetText(_pairToggle, "");
            int x = Width - pad;
            foreach (var b in new[] { _settingsButton, _disconnect, _pairToggle, _identify })
            {
                if (!b.Visible)
                    continue;
                x -= b.Width;
                b.Location = new Point(x, 16);
                x -= 8;
            }
            // Grafik wächst mit der Karte (Seitenverhältnis der Zeichnung 580 × 430).
            int viewWidth = Math.Clamp((Width - 2 * pad) * 46 / 100, 280, 380);
            _view.Bounds = new Rectangle(pad - 4, top, viewWidth, viewWidth * 430 / 580);
            _info.Location = new Point(_view.Right + 20, top + 8); // Infospalte immer oben bündig
            _info.Width = Math.Max(240, Width - _info.Left - pad);
            NaturalBodyHeight = Math.Max(_view.Bottom, _info.Bottom) + 12;
            // Gleich hohe Karten nebeneinander: Grafik im (ggf. höheren) Feld senkrecht mittig.
            int bodyBottom = Math.Max(NaturalBodyHeight, _expanded ? 0 : BodyHeight);
            _view.Top = top + (bodyBottom - 12 - top - _view.Height) / 2;
            int height = bodyBottom;
            if (_expanded)
            {
                _divider = bodyBottom;
                _tabs.SetBounds(pad - 4, bodyBottom + 6, Width - 2 * pad, 40);
                _panel.SetBounds(pad, _tabs.Bottom + 8, Width - 2 * pad, _panel.HeightFor(Width - 2 * pad));
                height = _panel.Bottom + pad;
            }
            if (Height != height)
                Height = height;
        }

        private int _divider;

        /// <summary>Karte wie eine Windows-11-Kachel; aufgeklappt ist der untere Teil leicht abgesetzt.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8);
            using (var fill = new SolidBrush(CardColor))
                g.FillPath(fill, path);
            if (_expanded)
            {
                g.SetClip(path);
                using (var lower = new SolidBrush(CardColor))
                    g.FillRectangle(lower, 0, _divider, Width, 52);
                using (var recess = new SolidBrush(Theme.Backdrop))
                    g.FillRectangle(recess, 0, _divider + 52, Width, Height - _divider - 52);
                using (var line = new Pen(Theme.Current.Border))
                {
                    g.DrawLine(line, 0, _divider, Width, _divider);
                    g.DrawLine(line, 0, _divider + 52, Width, _divider + 52);
                }
                g.ResetClip();
            }
            using var pen = new Pen(Theme.Current.Border, 1f);
            g.DrawPath(pen, path);
            // Titel mit kleinem Pfeil: Klick öffnet Spielerplatz und Umbenennen.
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            int maxWidth = Math.Max(100, _identify.Left - 40);
            // Passt „Spieler n · Name“ nicht, zeigt die Karte nur den Controllernamen (Spieler steht in der Leiste oben).
            string title = _title ?? "";
            if (_titleShort is { } shortTitle
                && TextRenderer.MeasureText(g, title, UiFonts.Subtitle).Width > maxWidth)
                title = shortTitle;
            int width = Math.Min(maxWidth, TextRenderer.MeasureText(g, title, UiFonts.Subtitle, new Size(maxWidth, 36), flags).Width);
            var titleBox = new Rectangle(20, 14, width, 36);
            if (_titleHover)
                using (var hover = new SolidBrush(Theme.Current.SurfaceHover))
                using (var round = Theme.RoundedRect(new RectangleF(titleBox.X - 8, titleBox.Y + 2, titleBox.Width + 34, titleBox.Height - 4), 6))
                    g.FillPath(hover, round);
            TextRenderer.DrawText(g, title, UiFonts.Subtitle, titleBox, TextColor, flags);
            TextRenderer.DrawText(g, "▾", UiFonts.Body, new Rectangle(titleBox.Right + 4, 14, 18, 36), MutedColor, TextFormatFlags.VerticalCenter);
            _titleRect = new Rectangle(titleBox.X - 8, titleBox.Y, titleBox.Width + 34, titleBox.Height);
        }

        private Rectangle _titleRect;
        private bool _titleHover;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = _titleRect.Contains(e.Location);
            if (hover == _titleHover)
                return;
            _titleHover = hover;
            Cursor = hover ? Cursors.Hand : Cursors.Default;
            Invalidate(new Rectangle(0, 0, Width, 60));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_titleHover)
                return;
            _titleHover = false;
            Cursor = Cursors.Default;
            Invalidate(new Rectangle(0, 0, Width, 60));
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_titleRect.Contains(e.Location) && _player is not null)
                ShowPlayerMenu(new Point(_titleRect.Left, _titleRect.Bottom));
        }

        /// <summary>Menü am Titel: Spielerplatz wählen (belegter Platz = tauschen) und Controller umbenennen.</summary>
        private void ShowPlayerMenu(Point at)
        {
            var player = _player!;
            var settings = CurrentSettings;
            var others = Manager.Players.ToDictionary(p => p.Index);
            var menu = new ContextMenuStrip { Renderer = Theme.MenuRenderer(), ForeColor = Theme.Current.Text, ShowCheckMargin = true };
            // Nur belegte Plätze (die Spieler rücken immer lückenlos auf): wählen = tauschen.
            for (int i = 0; i < others.Count; i++)
            {
                int slot = i;
                string text = others.TryGetValue(i, out var occupant) && occupant != player
                    ? $"Spieler {i + 1} – tauschen mit {occupant.DisplayName(settings)}"
                    : $"Spieler {i + 1}";
                var item = new ToolStripMenuItem(Tr.T(text)) { Checked = i == player.Index };
                item.Click += (_, _) => Manager.MovePlayer(player, slot);
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            var links = player.Links;
            foreach (var link in links)
            {
                if (link.Address is not { } address)
                    continue;
                string label = links.Count == 2
                    ? link.Kind.IsLeftJoyCon() ? "Linken Joy-Con umbenennen …" : "Rechten Joy-Con umbenennen …"
                    : "Umbenennen …";
                var rename = new ToolStripMenuItem(Tr.T(label));
                rename.Click += (_, _) => Rename(address, link.Kind);
                menu.Items.Add(rename);
            }
            menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
            menu.Show(this, at);
        }

        private void Rename(string address, ControllerKind kind)
        {
            var settings = CurrentSettings;
            string? name = Prompt.Ask(FindForm()!, Tr.T("Controller umbenennen"),
                Tr.T($"Name für {kind.DisplayName()} (leer = Standardname):"), settings.NameFor(address) ?? "", allowEmpty: true);
            if (name is null)
                return;
            settings.SetName(address, name);
            _owner._save();
            _rawTitle = null; // Titel neu aufbauen
            if (_player is { } p)
                Show(p, settings);
        }

        // ---------- Live (~60-mal pro Sekunde) ----------

        public void Show(Player player, Settings settings)
        {
            _player = player;
            var links = player.Links;
            string title = $"Spieler {player.Index + 1}  ·  {player.DisplayName(settings)}" + (player.GyroMouseActive ? "  ·  Gyro-Maus" : "") + (player.GyroStickActive ? "  ·  Gyro-Stick" : "");
            if (title != _rawTitle)
            {
                _rawTitle = title;
                _title = Tr.T(title);
                _titleShort = Tr.T(player.DisplayName(settings));
                Invalidate(new Rectangle(0, 0, Width, 60));
            }

            var (input, gamepad) = LiveInput(player, settings);
            bool joyCon = links.Count > 0 && links.All(l => l.Kind.IsJoyCon());
            bool upright = links.Count == 1 && joyCon && settings.IsUprightJoyCon(links[0].Address);
            // Kopf-Knopf nur bei Joy-Con: Paar lösen bzw. zum Paar verbinden, ohne die Karte aufzuklappen.
            if (_pairToggle.Visible != joyCon)
            {
                _pairToggle.Visible = joyCon;
                PerformLayout();
            }
            if (joyCon)
            {
                _pairToggle.Enabled = player.IsPair || Manager.HasPartner(player);
                string tip = Tr.T(player.IsPair
                    ? "Joy-Con trennen – beide werden einzelne Controller"
                    : "Zum Paar verbinden – sucht einen freien Joy-Con");
                if (_tips.GetToolTip(_pairToggle) != tip)
                    _tips.SetToolTip(_pairToggle, tip);
            }
            if (joyCon && links.All(l => l.LastState is not null))
            {
                var parts = links.Select(l => new InputView.JoyConPart(l.Kind, l.LastState!, Player.CalibrationFor(l, settings), l.Info,
                    player.MouseActive(l), l.InGrip, upright)).ToList();
                _view.ShowJoyCons(parts, input);
            }
            else
            {
                var first = links.FirstOrDefault();
                if (first is not null)
                    _view.SetColors(first.Info.BodyColor, first.Info.ButtonColor, first.Info.GripColor);
                _view.WiiExtension = first is WiimoteHidLink wii ? wii.Extension : WiiExtension.None;
                _view.Show(input, gamepad);
            }
            // LEDs in der Grafik wie am Controller: mit „Spieler-LED vom Spiel“ der Xbox-Platz von Windows.
            _view.PlayerIndex = settings.GameLeds && player.GameSlot is { } slot ? slot : player.Index;
            // Bei Sony-Pads: Lichtleiste in der Grafik in der Farbe, die das Spiel gesetzt hat.
            _view.LightbarTint = player.Lightbar is { } lb ? Color.FromArgb(lb.R, lb.G, lb.B) : null;
            var mouse = links.Where(player.MouseActive).ToList();
            _info.Game = links.Any(l => l.Native)
                // Xbox über XInput: Spiele sehen den echten Controller selbst – kein virtueller nötig.
                ? (player.GameSlot is { } ns ? $"Xbox · Platz {ns + 1} · nativ" : "Xbox · nativ", null)
                : player.Output == OutputMode.DualShock4
                    ? ("DualShock 4", player.Lightbar is { } bar ? Color.FromArgb(bar.R, bar.G, bar.B) : null)
                    : (player.GameSlot is { } s ? $"Xbox 360 · Platz {s + 1}" : "Xbox 360", null);
            _info.Show(links, input, mouse);
            if (_expanded && _details is { Visible: true } details)
                details.Show(links, input, mouse);

            if (_expanded)
            {
                // Anderer Controller in derselben Karte (z. B. Joy-Con-Paar getrennt): Seiten neu bauen.
                if (MappingKind(player, settings) != _builtFor)
                {
                    BuildPages();
                    ShowTab();
                }
                _editor?.Highlight(input?.Buttons ?? ProButtons.None);
                UpdateExtras(settings);
                UpdateJoyCon(settings);
                if (_calibrate is not null && !_calibrating)
                    _calibrate.Enabled = links.Any(l => l.LastState?.Motion is not null) && links.All(l => l.Address is not null);
            }
            if (!_expanded && Height != Math.Max(NaturalBodyHeight, BodyHeight))
                PerformLayout();
        }

        // ---------- Aktionen ----------

        private async Task ToggleRingConAsync()
        {
            var link = _player?.Links.OfType<Switch1HidLink>().FirstOrDefault(l => l.Kind == ControllerKind.JoyCon1Right);
            if (link is null || _ringCon is null)
                return;
            _ringCon.Enabled = false;
            try
            {
                if (link.RingConActive)
                {
                    await link.DisableRingConAsync();
                }
                else
                {
                    _ringCon.Text = "Wird gesucht …";
                    if (!await link.EnableRingConAsync(CancellationToken.None))
                        Tr.Show(FindForm(), "Kein Ring-Con gefunden. Den rechten Joy-Con (Switch 1) fest in den Ring-Con " +
                            "schieben und erneut versuchen.", "Ring-Con");
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
            {
                Tr.Show(FindForm(), $"Ring-Con: {e.Message}", "Ring-Con");
            }
            finally
            {
                if (!IsDisposed)
                    _ringCon.Enabled = true;
            }
        }

        /// <summary>amiibo über den NFC-Leser lesen und als .bin speichern (Rohabbild, 540 Byte).</summary>
        private async Task ReadAmiiboAsync()
        {
            var link = _player?.Links.OfType<Switch1HidLink>().FirstOrDefault(l => l.HasNfc);
            if (link is null || _amiibo is null || _amiiboRow is null)
                return;
            _amiibo.Enabled = false;
            string? original = _amiiboRow.Description;
            try
            {
                var result = await link.ReadAmiiboAsync(TimeSpan.FromSeconds(20),
                    message =>
                    {
                        if (IsDisposed || !IsHandleCreated)
                            return;
                        try { BeginInvoke(() => { if (!IsDisposed) _amiiboRow.Description = message; }); }
                        catch (InvalidOperationException) { /* Karte geschlossen */ }
                    },
                    CancellationToken.None);
                if (result is not { } amiibo)
                {
                    Tr.Show(FindForm(), "Kein amiibo erkannt. Bitte das amiibo flach an den NFC-Leser halten " +
                        "(Joy-Con: auf den rechten Stick, Pro Controller: auf das NFC-Logo) und erneut versuchen.", "amiibo lesen");
                    return;
                }
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "amiibo");
                Directory.CreateDirectory(folder);
                using var dialog = new SaveFileDialog
                {
                    Title = Tr.T("amiibo speichern"), Filter = Tr.T("amiibo-Abbild (*.bin)|*.bin"), InitialDirectory = folder,
                    FileName = $"amiibo_{Convert.ToHexString(amiibo.Uid)}.bin",
                };
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                    await File.WriteAllBytesAsync(dialog.FileName, amiibo.Data);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException or ObjectDisposedException)
            {
                Tr.Show(FindForm(), $"amiibo konnte nicht gelesen werden: {e.Message}", "amiibo lesen",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (!IsDisposed)
                {
                    _amiiboRow.Description = original;
                    _amiibo.Enabled = true;
                }
            }
        }

        /// <summary>Original-Controller per HidHide vor Steam und Spielen verstecken (einmal Adminrechte).</summary>
        private async Task HideAsync()
        {
            var ids = _player?.Links.Select(l => l.HidInstanceId).OfType<string>().ToList() ?? [];
            if (ids.Count == 0 || _hide is null)
                return;
            if (!HidHide.IsInstalled)
            {
                if (Tr.Show(FindForm(),
                        "Dafür wird das kostenlose Programm HidHide gebraucht (von den Machern von ViGEmBus).\n\n" +
                        "Jetzt die Download-Seite öffnen? Nach der Installation hier erneut klicken.",
                        "Doppelte Controller verhindern", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(HidHide.Download) { UseShellExecute = true });
                return;
            }
            _hide.Enabled = false;
            try
            {
                if (await HidHide.HideAsync(ids))
                {
                    var s = CurrentSettings;
                    s.HiddenDevices = [.. s.HiddenDevices, .. ids.Where(id => !s.IsHidden(id))];
                    Manager.RequestSave();
                }
                else
                {
                    Tr.Show(FindForm(), "HidHide konnte nicht eingerichtet werden (abgebrochen oder fehlgeschlagen). Details im Protokoll.",
                        "Doppelte Controller verhindern", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                if (!IsDisposed)
                    _hide.Enabled = true;
            }
        }

        private async Task CalibrateAsync()
        {
            if (_player is null || _calibrating || _calibrate is null)
                return;
            _calibrating = true;
            _calibrate.Text = "Ruhig liegen lassen …";
            _calibrate.Enabled = false;
            try
            {
                var result = await _player.CalibrateGyroAsync(TimeSpan.FromSeconds(2));
                if (result is null)
                {
                    Tr.Show(FindForm(), "Der Controller hat sich bewegt oder keine Bewegungsdaten geliefert.\n" +
                        "Bitte flach auf den Tisch legen, nicht berühren und erneut versuchen.", "Gyro kalibrieren",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var s = CurrentSettings;
                var map = new Dictionary<string, GyroBias>(s.GyroCalibration, StringComparer.OrdinalIgnoreCase);
                foreach (var (address, bias) in result)
                    map[address] = bias;
                s.GyroCalibration = map; // neue Kopie: der Bluetooth-Thread liest gleichzeitig
                Manager.RequestSave();
                _calibrate.Text = "Kalibriert ✓";
                await Task.Delay(1500);
            }
            finally
            {
                _calibrating = false;
                if (!IsDisposed)
                {
                    _calibrate.Enabled = true;
                    _calibrate.Text = "Kalibrieren";
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _owner._mapping.Changed -= SyncProfile;
                _tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Eigenschaften eines Spielers (auch beider Joy-Con) als Liste mit Akkubalken.</summary>
    private sealed class InfoPanel : Control
    {
        private IReadOnlyList<IControllerLink> _links = [];
        private PadInput? _input;
        private const int LabelWidth = 104, RowHeight = 26;

        /// <summary>Kompakt (Karte): nur Akku, Verbindung, Griff/Maus und gedrückte Tasten; sonst alle Eigenschaften.</summary>
        public bool Compact { get; init; } = true;

        /// <summary>Wie Spiele den Controller sehen (Art, Xbox-Platz) und ggf. die vom Spiel gesetzte Lichtleiste.</summary>
        public (string Text, Color? Lightbar)? Game
        {
            get => _game;
            set
            {
                if (_game == value)
                    return;
                _game = value;
                Invalidate();
            }
        }

        private (string Text, Color? Lightbar)? _game;

        public InfoPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = CardColor;
            Height = 270;
        }

        private IReadOnlyList<IControllerLink> _mouse = [];

        public void Show(IReadOnlyList<IControllerLink> links, PadInput? input, IReadOnlyList<IControllerLink> mouse)
        {
            // Gedrückte Tasten sofort zeigen, Akku/Berichte/s reichen 4-mal pro Sekunde (Text zeichnen ist teuer).
            var pressed = input?.Buttons;
            long now = Environment.TickCount64;
            bool changed = !links.SequenceEqual(_links) || !mouse.SequenceEqual(_mouse) || !Equals(pressed, _input?.Buttons)
                           || now - _lastPaint >= 250;
            _links = links;
            _mouse = mouse;
            _input = input;
            int height = Walk(null) + 4; // gleiche Zeilen wie beim Zeichnen, nur gezählt
            if (Height != height)
                Height = height;
            if (changed)
            {
                _lastPaint = now;
                Invalidate();
            }
        }

        private long _lastPaint;

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Walk(e.Graphics);
        }

        /// <summary>Alle Zeilen zeichnen (g = null: nur die Höhe ermitteln).</summary>
        private int Walk(Graphics? g)
        {
            int y = 0;
            foreach (var link in _links)
            {
                if (_links.Count > 1)
                {
                    if (g is not null)
                        TextRenderer.DrawText(g, Tr.T(link.Kind.DisplayName()), UiFonts.Strong, new Point(0, y), Accent, TextFormatFlags.NoPrefix);
                    y += RowHeight + 2;
                }
                Row(g, ref y, "Akku", null);
                if (g is not null)
                    DrawBattery(g, LabelWidth, y - RowHeight, link.LastState);
                Row(g, ref y, "Verbindung", Compact ? Transport(link.Transport) : $"{Transport(link.Transport)} · {link.ReportRate:F0} Berichte/s");
                if (!Compact)
                {
                    if (link.Address is { } address)
                        Row(g, ref y, "Adresse", address);
                    if (link.Info.SerialNumber is { } serial)
                        Row(g, ref y, "Seriennummer", serial);
                    if (link.Info.Firmware is { } firmware)
                        Row(g, ref y, "Firmware", firmware);
                }
                if (link.InGrip)
                    Row(g, ref y, "Griff", "Charging Grip – GL/GR aktiv");
                if (link.Kind is ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right)
                    Row(g, ref y, "Maus", _mouse.Contains(link) ? "aktiv (liegt auf dem Tisch)" : "bereit (auf den Tisch legen)");
                y += 8;
            }
            if (_game is { } game)
            {
                Row(g, ref y, "Im Spiel", game.Text);
                if (g is not null && game.Lightbar is { } color)
                {
                    // Lichtleiste, die das Spiel gesetzt hat, als kleiner Balken hinter dem Text.
                    int x = LabelWidth + TextRenderer.MeasureText(Tr.T(game.Text), UiFonts.Body).Width + 8;
                    using var path = Theme.RoundedRect(new RectangleF(x, y - RowHeight + 5, 28, 12), 4);
                    using var fill = new SolidBrush(color);
                    g.FillPath(fill, path);
                    using var pen = new Pen(Theme.Current.Border);
                    g.DrawPath(pen, path);
                }
            }
            var pressed = _input is null ? [] : Enum.GetValues<ProButtons>()
                .Where(b => b != ProButtons.None && _input.Has(b)).Select(ButtonName).ToList();
            Row(g, ref y, "Gedrückt", pressed.Count == 0 ? "–" : string.Join("  ", pressed));
            return y;
        }

        private void Row(Graphics? g, ref int y, string label, string? value)
        {
            if (g is not null)
            {
                TextRenderer.DrawText(g, Tr.T(label), UiFonts.Body, new Point(0, y), MutedColor, TextFormatFlags.NoPrefix);
                if (value is not null)
                {
                    int labelW = Math.Max(LabelWidth, TextRenderer.MeasureText(Tr.T(label), UiFonts.Body).Width + 16);
                    TextRenderer.DrawText(g, Tr.T(value), UiFonts.Body, new Rectangle(labelW, y, Math.Max(40, Width - labelW), RowHeight),
                        TextColor, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                }
            }
            y += RowHeight;
        }

        private static void DrawBattery(Graphics g, int x, int y, ControllerState? st)
        {
            var frame = new RectangleF(x, y + 3, 40, 15);
            using (var path = Theme.RoundedRect(frame, 3))
            using (var pen = new Pen(MutedColor, 1.3f))
                g.DrawPath(pen, path);
            using (var tip = new SolidBrush(MutedColor))
                g.FillRectangle(tip, frame.Right + 1, frame.Y + 4.5f, 2.5f, 6);
            int percent = st?.BatteryPercent is { } raw ? Math.Min(raw, 100) : -1;
            if (percent >= 0)
            {
                var level = percent < 15 ? Color.FromArgb(235, 80, 70) : percent < 35 ? Color.FromArgb(240, 180, 40) : Color.FromArgb(70, 200, 110);
                using var fill = new SolidBrush(level);
                using var bar = Theme.RoundedRect(new RectangleF(frame.X + 2.5f, frame.Y + 2.5f, Math.Max(2, (frame.Width - 5) * percent / 100f), frame.Height - 5), 1.5f);
                g.FillPath(fill, bar);
            }
            string text = percent < 0 ? "unbekannt"
                : $"{percent} %{(st!.BatteryMillivolts > 0 ? $"  ({st.BatteryMillivolts / 1000.0:F2} V)" : "")}{(st.Charging ? "  ⚡ lädt" : "")}";
            TextRenderer.DrawText(g, Tr.T(text), UiFonts.Body, new Point(x + 52, y), TextColor, TextFormatFlags.NoPrefix);
        }

        private static string Transport(Transport t) => t switch
        {
            Links.Transport.BluetoothLE => "Bluetooth LE",
            Links.Transport.Bluetooth => "Bluetooth",
            Links.Transport.XInput => "XInput (USB, Bluetooth oder Xbox-Adapter)",
            _ => "USB",
        };

        private static string ButtonName(ProButtons b) => b switch
        {
            ProButtons.LeftStick => "L-Stick",
            ProButtons.RightStick => "R-Stick",
            ProButtons.Minus => "−",
            ProButtons.Plus => "+",
            ProButtons.Capture => "Aufnahme",
            ProButtons.Up => "▲",
            ProButtons.Down => "▼",
            ProButtons.Left => "◀",
            ProButtons.Right => "▶",
            _ => b.ToString(),
        };
    }
}
