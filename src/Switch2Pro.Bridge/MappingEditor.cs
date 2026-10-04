using System.Drawing;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Gemeinsamer Stand aller Belegungs-Editoren (Seite „Tastenbelegung“ und die Editoren in den Controller-Karten):
/// welches Profil und welche Ebene bearbeitet wird, Lesen/Schreiben der Belegungen, Speichern.
/// </summary>
internal sealed class MappingContext
{
    private readonly Func<Settings> _settings;
    private readonly Action _save;

    public MappingContext(Func<Settings> settings, Action save, Func<IWin32Window?> owner)
    {
        _settings = settings;
        _save = save;
        Owner = owner;
    }

    public Settings Settings => _settings();
    public Func<IWin32Window?> Owner { get; }

    /// <summary>Bearbeitetes Profil: null = Standard, sonst Name eines benannten Profils.</summary>
    public string? EditProfile { get; private set; }

    /// <summary>Shift-Ebene statt normaler Belegung bearbeiten.</summary>
    public bool Shift { get; private set; }

    /// <summary>Profil, Ebene oder Belegungen haben sich geändert – alle Editoren neu befüllen.</summary>
    public event Action? Changed;

    public NamedProfile? Profile => EditProfile is null ? null : Settings.NamedProfiles.FirstOrDefault(p => p.Name == EditProfile);

    public void SelectProfile(string? name)
    {
        EditProfile = name;
        Changed?.Invoke();
    }

    public void SetShift(bool shift)
    {
        if (Shift == shift)
            return;
        Shift = shift;
        Changed?.Invoke();
    }

    /// <summary>Nach dem Neuladen der Einstellungen: gelöschtes Profil verlassen, alle Editoren auffrischen.</summary>
    public void Refresh()
    {
        if (EditProfile is not null && Profile is null)
            EditProfile = null;
        Changed?.Invoke();
    }

    public void Save() => _save();

    /// <summary>Belegungen der gerade bearbeiteten Ebene (Standard/Profil, normal/Shift).</summary>
    public Dictionary<ControllerKind, Dictionary<ProButtons, string>> Maps
    {
        get
        {
            var profile = Profile;
            return Shift ? profile?.ShiftButtons ?? Settings.ShiftProfiles : profile?.Buttons ?? Settings.Profiles;
        }
        private set
        {
            var profile = Profile;
            if (profile is null)
            {
                if (EditProfile is not null)
                    return; // Profil inzwischen gelöscht: nicht in „Standard“ schreiben
                if (Shift) Settings.ShiftProfiles = value; else Settings.Profiles = value;
                return;
            }
            ReplaceProfile(profile, Shift ? profile with { ShiftButtons = value } : profile with { Buttons = value });
        }
    }

    public string? ActionFor(ControllerKind kind, ProButtons button) =>
        Maps.TryGetValue(kind, out var map) && map.TryGetValue(button, out var text) ? text : null;

    public void SetAction(ControllerKind kind, ProButtons button, string? action)
    {
        // Neue Kopien statt Änderung: der Bluetooth-Thread liest gleichzeitig.
        var maps = Maps.ToDictionary(m => m.Key, m => new Dictionary<ProButtons, string>(m.Value));
        if (!maps.TryGetValue(kind, out var map))
            maps[kind] = map = [];
        if (action is null)
            map.Remove(button);
        else
            map[button] = action;
        if (map.Count == 0)
            maps.Remove(kind);
        Maps = maps;
        _save();
        Changed?.Invoke();
    }

    public void ReplaceProfile(NamedProfile old, NamedProfile? replacement)
    {
        // Per Name suchen: nach einem Neuladen sind die Objekte andere, der Name bleibt.
        var list = Settings.NamedProfiles.ToList();
        int i = list.FindIndex(p => p.Name == old.Name);
        if (i < 0)
            return;
        if (replacement is null) list.RemoveAt(i); else list[i] = replacement;
        Settings.NamedProfiles = list;
    }
}

/// <summary>Auswahllisten-Inhalte der Belegung (Gamepad-Ziele, Sonderaktionen, Windows-Kürzel).</summary>
internal static class MappingChoices
{
    public static readonly Dictionary<ExtraButtonTarget, string> TargetNames = new()
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
    public sealed record Choice(string Text, string? Action)
    {
        public override string ToString() => Tr.T(Text);
    }

    public const string CaptureKeys = "\u0001capture";
    public const string ChooseTurbo = "\u0001turbo";
    public const string ChooseMacro = "\u0001macro";
    public static readonly Choice KeyboardChoice = new("⌨  Andere Taste / Tastenkombination aufnehmen …", CaptureKeys);

    /// <summary>Sonderaktionen und fertige Windows-Tastenkürzel.</summary>
    public static IEnumerable<Choice> Presets(bool shiftLayer)
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
        yield return new Choice("✋  Gyro anhalten (solange gehalten, „Ratchet“)", nameof(SpecialAction.GyroPause));
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

    /// <summary>Alle Einträge für eine Taste und der Index der aktuellen Belegung.</summary>
    public static (List<Choice> Items, int Selected) For(ProButtons button, ControllerKind kind, bool shift, FaceButtonLayout layout, string? current)
    {
        var items = new List<Choice>
        {
            new(shift ? "wie normale Belegung" : $"Standard: {TargetNames[Mapping.DefaultTarget(button, layout, kind)]}", null),
        };
        foreach (var (target, name) in TargetNames)
            items.Add(new Choice(name, target.ToString()));
        items.AddRange(Presets(shift));
        int selected = 0;
        if (current is not null)
        {
            string normalized = ButtonAction.Parse(current).ToString();
            selected = items.FindIndex(c => c.Action is not null && !c.Action.StartsWith('\u0001')
                && string.Equals(ButtonAction.Parse(c.Action).ToString(), normalized, StringComparison.OrdinalIgnoreCase));
            if (selected < 0)
            {
                items.Add(new Choice(Describe(current), normalized));
                selected = items.Count - 1;
            }
        }
        items.Add(KeyboardChoice);
        return (items, selected);
    }

    /// <summary>Kurzbeschreibung einer gespeicherten Aktion (Tastatur, Turbo, Makro).</summary>
    public static string Describe(string text)
    {
        var action = ButtonAction.Parse(text);
        return action switch
        {
            { IsMacro: true } => $"⏯  Makro: {action.Macro}",
            { Turbo: true } => $"🔁  Turbo: {(action.IsKeyboard ? $"Taste {action.Keys}" : TargetNames.GetValueOrDefault(action.Target, action.Target.ToString()))}",
            { IsKeyboard: true } => $"⌨  Taste: {action.Keys}",
            _ => action.ToString(),
        };
    }
}

/// <summary>
/// Belegung aller Tasten eines Controllers: je Taste eine Zeile mit Auswahl, ⌨ (Tastaturtaste aufnehmen) und ↺
/// (zurück auf Standard). Eine am Controller gedrückte Taste hebt ihre Zeile hervor.
/// </summary>
internal sealed class MappingEditor : SettingsGroup
{
    private readonly MappingContext _context;
    private readonly ToolTip _tips = Theme.CreateToolTip();
    private ControllerKind _kind;
    private bool _built;

    public MappingEditor(MappingContext context, ControllerKind kind)
    {
        _context = context;
        _kind = kind;
        _context.Changed += OnContextChanged;
        Build();
    }

    public ControllerKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value && _built)
                return;
            _kind = value;
            Build();
        }
    }

    private IEnumerable<MapRow> Rows => Controls.OfType<MapRow>();

    // ---------- Anordnung: bei genug Breite zweispaltig (halb so lang) ----------
    private const int RowHeight = 44, TwoColumnWidth = 980, ColumnGap = 1;

    private static int Columns(int width) => width >= TwoColumnWidth ? 2 : 1;

    private int RowsPerColumn(int width)
    {
        int n = Controls.Count;
        return (n + Columns(width) - 1) / Columns(width);
    }

    public override int HeightFor(int width) => Padding.Vertical + RowsPerColumn(width) * RowHeight;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        int cols = Columns(Width), perColumn = RowsPerColumn(Width);
        int inner = ClientSize.Width - Padding.Horizontal;
        int colWidth = (inner - (cols - 1) * ColumnGap) / cols;
        int i = 0;
        foreach (Control c in Controls)
        {
            int col = i / perColumn, row = i % perColumn;
            c.SetBounds(Padding.Left + col * (colWidth + ColumnGap), Padding.Top + row * RowHeight, colWidth, RowHeight);
            i++;
        }
        int total = HeightFor(Width);
        if (Height != total)
            Height = total;
        Invalidate();
    }

    /// <summary>Erste Zeile einer Spalte (ohne Trennlinie oben).</summary>
    private bool FirstInColumn(Control row) => Controls.GetChildIndex(row) % RowsPerColumn(Width) == 0;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Columns(Width) == 2)
            using (var line = new Pen(Theme.Current.Border))
            {
                int x = Padding.Left + (ClientSize.Width - Padding.Horizontal - ColumnGap) / 2;
                e.Graphics.DrawLine(line, x, Padding.Top, x, Height - Padding.Bottom);
            }
    }

    private void Build()
    {
        SuspendLayout();
        foreach (Control c in Controls.Cast<Control>().ToList())
        {
            Controls.Remove(c);
            c.Dispose();
        }
        foreach (var button in ControllerButtons.For(_kind))
            Controls.Add(new MapRow(this, button));
        _built = true;
        Refill();
        ResumeLayout();
        PerformLayout();
    }

    private void OnContextChanged()
    {
        if (!IsDisposed)
            Refill();
    }

    private void Refill()
    {
        foreach (var row in Rows)
            row.Refill();
    }

    /// <summary>Am Controller gedrückte Tasten hervorheben (aus der Live-Anzeige, ~60-mal pro Sekunde).</summary>
    public void Highlight(ProButtons pressed)
    {
        foreach (var row in Rows)
            row.Pressed = (pressed & row.Button) != 0;
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _context.Changed -= OnContextChanged;
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Eine Zeile: Tastenname, Auswahl, ⌨ und ↺ – komplett selbst gezeichnet (ein Fenster statt vier), damit viele
    /// Zeilen sofort erscheinen. Die echte Auswahlliste entsteht erst beim Klick und verschwindet nach der Wahl.
    /// </summary>
    private sealed class MapRow : Control, IHeightForWidth, IExtraTexts, ISelfTranslating
    {
        private enum Part { None, Combo, Key, Reset }

        private readonly MappingEditor _editor;
        private List<MappingChoices.Choice> _items = [];
        private int _selected;
        private bool _pressed, _hasOwn, _opening;
        private Part _hover, _down, _focusPart = Part.Combo;
        private ComboBox? _combo;

        public ProButtons Button { get; }
        private ControllerKind Kind => _editor._kind;
        private MappingContext Context => _editor._context;

        public MapRow(MappingEditor editor, ProButtons button)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            _editor = editor;
            Button = button;
            Text = ControllerButtons.Label(button, editor._kind);
            BackColor = Theme.Current.Surface;
            TabStop = true;
        }

        public IEnumerable<string> ExtraTexts => _items.Select(c => c.ToString());

        public bool Pressed
        {
            get => _pressed;
            set
            {
                if (_pressed == value)
                    return;
                _pressed = value;
                Invalidate();
            }
        }

        public int HeightFor(int width) => RowHeight;

        // ---------- Bereiche ----------
        private int LabelWidth => Math.Clamp(Width * 30 / 100, 120, 240);
        private Rectangle ResetBounds => new(Width - 16 - 36, (Height - 32) / 2, 36, 32);
        private Rectangle KeyBounds => new(Width - 16 - 36 - 6 - 36, (Height - 32) / 2, 36, 32);

        private Rectangle ComboBounds
        {
            get
            {
                int x = 16 + LabelWidth;
                return new Rectangle(x, (Height - 30) / 2, Math.Max(150, KeyBounds.Left - 10 - x), 30);
            }
        }

        private Part HitTest(Point p) =>
            ComboBounds.Contains(p) ? Part.Combo : KeyBounds.Contains(p) ? Part.Key : ResetBounds.Contains(p) && _hasOwn ? Part.Reset : Part.None;

        // ---------- Inhalt ----------

        public void Refill()
        {
            var current = Context.ActionFor(Kind, Button);
            (_items, _selected) = MappingChoices.For(Button, Kind, Context.Shift, Context.Settings.Layout, current);
            _hasOwn = current is not null;
            Invalidate();
        }

        // ---------- Zeichnen ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.Current;
            g.Clear(_pressed ? Theme.Blend(p.Surface, Theme.Accent, 0.28f) : p.Surface);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (!_editor.FirstInColumn(this))
                using (var line = new Pen(Theme.Dark ? Color.FromArgb(0x1F, 0x1F, 0x1F) : Color.FromArgb(0xEA, 0xEA, 0xEA)))
                    g.DrawLine(line, 0, 0, Width, 0);
            if (_pressed)
                using (var bar = new SolidBrush(Theme.Accent))
                    g.FillRectangle(bar, 0, 6, 3, Height - 12);
            TextRenderer.DrawText(g, Tr.T(Text), _pressed ? UiFonts.Strong : UiFonts.Body, new Rectangle(16, 0, LabelWidth - 8, Height),
                p.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            // Auswahlfeld wie eine Windows-11-Auswahlliste; eigene Belegung kräftig, Standard etwas zurückgenommen.
            var combo = ComboBounds;
            bool comboFocus = Focused && _focusPart == Part.Combo;
            var back = _hover == Part.Combo ? Theme.Blend(p.SurfaceHover, p.Text, 0.06f) : p.SurfaceHover;
            using (var path = Theme.RoundedRect(new RectangleF(combo.X + 0.5f, combo.Y + 0.5f, combo.Width - 1, combo.Height - 1), 4))
            {
                using var fill = new SolidBrush(back);
                g.FillPath(fill, path);
                using var pen = new Pen(comboFocus ? Theme.Accent : p.Border, comboFocus ? 1.5f : 1f);
                g.DrawPath(pen, path);
            }
            string text = _items.Count > 0 ? _items[_selected].ToString() : "";
            TextRenderer.DrawText(g, text, _hasOwn ? UiFonts.Strong : UiFonts.Body, new Rectangle(combo.X + 10, combo.Y, combo.Width - 40, combo.Height),
                _hasOwn ? p.Text : Theme.Blend(p.Text, p.TextMuted, 0.4f),
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Glyph.ChevronDown, Glyph.Font(8f), new Rectangle(combo.Right - 28, combo.Y, 20, combo.Height), p.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            DrawButton(g, KeyBounds, Glyph.Keyboard, Part.Key, true);
            DrawButton(g, ResetBounds, Glyph.Undo, Part.Reset, _hasOwn);
        }

        private void DrawButton(Graphics g, Rectangle r, string glyph, Part part, bool enabled)
        {
            var p = Theme.Current;
            var back = !enabled ? p.Surface : _down == part ? Theme.Blend(p.SurfaceHover, p.Text, 0.14f)
                : _hover == part ? Theme.Blend(p.SurfaceHover, p.Text, 0.08f) : p.SurfaceHover;
            using (var path = Theme.RoundedRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 5))
            {
                using var fill = new SolidBrush(back);
                g.FillPath(fill, path);
                bool focus = Focused && _focusPart == part;
                using var pen = new Pen(focus ? p.Text : p.Border, focus ? 1.5f : 1f);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, glyph, Glyph.Font(10.5f), r, enabled ? p.Text : p.Border,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // ---------- Maus und Tastatur ----------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var part = HitTest(e.Location);
            if (part == _hover)
                return;
            _hover = part;
            Cursor = part == Part.None ? Cursors.Default : Cursors.Hand;
            _editor._tips.SetToolTip(this, part switch
            {
                Part.Key => Tr.T("Tastatur-Taste zuweisen: klicken und die gewünschte Taste oder Kombination drücken."),
                Part.Reset => Tr.T("Zurück auf die Standardbelegung"),
                _ => null,
            });
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = _down = Part.None;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            _down = HitTest(e.Location);
            if (_down != Part.None)
                _focusPart = _down;
            Focus();
            Invalidate();
            if (_down == Part.Combo)
            {
                _down = Part.None;
                OpenList();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var part = _down;
            _down = Part.None;
            Invalidate();
            if (part != Part.None && part == HitTest(e.Location))
                Activate(part);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left:
                    _focusPart = _focusPart == Part.Reset ? Part.Key : Part.Combo;
                    Invalidate();
                    break;
                case Keys.Right:
                    _focusPart = _focusPart == Part.Combo ? Part.Key : _hasOwn ? Part.Reset : Part.Key;
                    Invalidate();
                    break;
                case Keys.Space or Keys.Enter or Keys.F4:
                    if (_focusPart == Part.Combo) OpenList(); else Activate(_focusPart);
                    break;
            }
        }

        private void Activate(Part part)
        {
            if (part == Part.Key)
                CaptureKey();
            else if (part == Part.Reset && _hasOwn)
                Context.SetAction(Kind, Button, null);
        }

        /// <summary>Echte Auswahlliste über dem gezeichneten Feld öffnen; nach dem Schließen wieder entfernen.</summary>
        private void OpenList()
        {
            if (_combo is not null || _opening || _items.Count == 0)
                return;
            _opening = true;
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = ComboBounds, MaxDropDownItems = 18 };
            combo.Items.AddRange(_items.ToArray<object>());
            combo.SelectedIndex = _selected;
            Controls.Add(combo);
            Theme.ApplyControls(this);
            int width = _items.Max(i => TextRenderer.MeasureText(i.ToString(), combo.Font).Width) + 30;
            combo.DropDownWidth = Math.Max(combo.Width, width);
            _combo = combo;
            combo.SelectionChangeCommitted += (_, _) =>
            {
                int index = combo.SelectedIndex;
                BeginInvoke(() => Choose(index)); // erst nach dem Schließen der Liste (Dialoge, Neuaufbau)
            };
            combo.DropDownClosed += (_, _) => BeginInvoke(CloseList);
            combo.Focus();
            combo.DroppedDown = true;
            _opening = false;
        }

        private void CloseList()
        {
            if (_combo is null || IsDisposed)
                return;
            var combo = _combo;
            _combo = null;
            bool focused = combo.Focused;
            Controls.Remove(combo);
            combo.Dispose();
            if (focused)
                Focus();
            Invalidate();
        }

        private void CaptureKey()
        {
            using var dialog = new KeyCaptureDialog(Tr.T(Text));
            if (dialog.ShowDialog(Context.Owner()) == DialogResult.OK && dialog.Combo is { } combo)
                Context.SetAction(Kind, Button, ButtonAction.Keyboard(combo).ToString());
        }

        private void Choose(int index)
        {
            if (IsDisposed || index < 0 || index >= _items.Count || index == _selected)
                return;
            string? action = _items[index].Action;
            string label = Tr.T(Text);
            if (action == MappingChoices.CaptureKeys)
            {
                CaptureKey();
                return;
            }
            if (action is MappingChoices.ChooseTurbo or MappingChoices.ChooseMacro)
            {
                string? current = Context.ActionFor(Kind, Button);
                string? result = action == MappingChoices.ChooseTurbo
                    ? TurboDialog.Ask(Context.Owner()!, label, current)
                    : MacroDialog.Ask(Context.Owner()!, label, current);
                if (result is null)
                    return;
                action = result;
            }
            Context.SetAction(Kind, Button, action);
        }
    }
}
