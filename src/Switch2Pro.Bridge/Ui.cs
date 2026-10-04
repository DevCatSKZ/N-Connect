using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Switch2Pro.Bridge;

/// <summary>Symbole aus der Windows-Schrift „Segoe MDL2 Assets“ (Windows 10 und 11).</summary>
internal static class Glyph
{
    public const string Settings = "", Gamepad = "", Keyboard = "", Globe = "", Palette = "",
        Power = "", Sync = "", Undo = "", Info = "", Warning = "", More = "",
        Import = "", Export = "", Add = "", Delete = "", Edit = "", Swap = "",
        Layers = "", Mouse = "", Sliders = "", Vibrate = "", Key = "", Flash = "",
        Bluetooth = "", Pointer = "", Eye = "", Device = "", Video = "", Chevron = "",
        ChevronDown = "", ChevronUp = "", Rotate = "", Volume = "", Stick = "",
        Timer = "", Cube = "", Nfc = "", Gauge = "", Ring = "", Refresh = "",
        Folder = "", Check = "";

    private static readonly Dictionary<float, Font> Fonts = [];

    public static Font Font(float size)
    {
        if (!Fonts.TryGetValue(size, out var font))
            Fonts[size] = font = new Font("Segoe MDL2 Assets", size, GraphicsUnit.Point);
        return font;
    }
}

/// <summary>Schriften der Oberfläche: „Segoe UI Variable“ (Windows 11), sonst „Segoe UI“.</summary>
internal static class UiFonts
{
    private static readonly bool Variable = FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text");
    private static readonly Dictionary<(float, FontStyle, string), Font> Cache = [];

    private static Font Get(string family, string fallback, float size, FontStyle style)
    {
        string name = Variable ? family : fallback;
        if (!Cache.TryGetValue((size, style, name), out var font))
            Cache[(size, style, name)] = font = new Font(name, size, style, GraphicsUnit.Point);
        return font;
    }

    public static Font Body => Get("Segoe UI Variable Text", "Segoe UI", 9.75f, FontStyle.Regular);
    public static Font Small => Get("Segoe UI Variable Small", "Segoe UI", 8.75f, FontStyle.Regular);
    public static Font Strong => Get("Segoe UI Variable Text Semibold", "Segoe UI Semibold", 9.75f, FontStyle.Regular);
    public static Font Subtitle => Get("Segoe UI Variable Display Semibold", "Segoe UI Semibold", 12.5f, FontStyle.Regular);
    public static Font Title => Get("Segoe UI Variable Display Semibold", "Segoe UI Semibold", 20f, FontStyle.Regular);
}

/// <summary>Steuerelement, dessen Höhe von der verfügbaren Breite abhängt (umbrechender Text).</summary>
/// <summary>Zeichnet seinen Text selbst übersetzt (Tr.T beim Zeichnen) – <see cref="Tr.Apply(Control)"/> lässt ihn aus.</summary>
internal interface ISelfTranslating;

internal interface IHeightForWidth
{
    int HeightFor(int width);
}

/// <summary>Texte für die Übersetzungsprüfung (--dump-ui), die nicht in <see cref="Control.Text"/> stehen.</summary>
internal interface IExtraTexts
{
    IEnumerable<string> ExtraTexts { get; }
}

/// <summary>Ordnet Kinder untereinander in voller Breite an und passt die eigene Höhe an (schnell, ohne AutoSize-Ketten).</summary>
internal class StackPanel : Panel, IHeightForWidth
{
    private readonly HashSet<Control> _hidden = [];

    public int Spacing { get; set; }

    public StackPanel()
    {
        DoubleBuffered = true;
        BackColor = Theme.Backdrop;
    }

    /// <summary>Kind ein-/ausblenden (Visible allein ist bei unsichtbaren Eltern immer false).</summary>
    public void SetShown(Control child, bool shown)
    {
        bool changed = shown ? _hidden.Remove(child) : _hidden.Add(child);
        child.Visible = shown;
        if (changed)
            PerformLayout();
    }

    public bool IsShown(Control child) => !_hidden.Contains(child);

    private static int ChildHeight(Control c, int width) =>
        c is IHeightForWidth f ? f.HeightFor(width) : c.AutoSize ? c.GetPreferredSize(new Size(width, 0)).Height : c.Height;

    public virtual int HeightFor(int width)
    {
        int inner = width - Padding.Horizontal, h = Padding.Vertical, n = 0;
        foreach (Control c in Controls)
        {
            if (_hidden.Contains(c))
                continue;
            if (n++ > 0)
                h += Spacing;
            h += ChildHeight(c, inner - c.Margin.Horizontal) + c.Margin.Vertical;
        }
        return h;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        int inner = ClientSize.Width - Padding.Horizontal, y = Padding.Top, n = 0;
        foreach (Control c in Controls)
        {
            if (_hidden.Contains(c))
                continue;
            if (n++ > 0)
                y += Spacing;
            int w = inner - c.Margin.Horizontal, h = ChildHeight(c, w);
            c.SetBounds(Padding.Left + c.Margin.Left, y + c.Margin.Top, Math.Max(0, w), h);
            y += h + c.Margin.Vertical;
        }
        int total = y + Padding.Bottom;
        if (Height != total)
            Height = total;
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        e.Control!.Resize += ChildResized;
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        base.OnControlRemoved(e);
        e.Control!.Resize -= ChildResized;
        _hidden.Remove(e.Control);
    }

    // Ändert ein Kind seine Höhe selbst (z. B. aufgeklappte Karte), neu anordnen.
    private bool _inLayout;

    private void ChildResized(object? sender, EventArgs e)
    {
        if (_inLayout || sender is IHeightForWidth)
            return;
        _inLayout = true;
        try { PerformLayout(); }
        finally { _inLayout = false; }
    }
}

/// <summary>Abgerundete Gruppe von Einstellungszeilen (wie in den Windows-11-Einstellungen).</summary>
internal class SettingsGroup : StackPanel
{
    public SettingsGroup()
    {
        Padding = new Padding(1, 4, 1, 4);
        Margin = new Padding(0, 0, 0, 0);
        BackColor = Theme.Backdrop;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 7);
        using (var fill = new SolidBrush(Theme.Current.Surface))
            g.FillPath(fill, path);
        using var pen = new Pen(Theme.Current.Border);
        g.DrawPath(pen, path);
    }
}

/// <summary>Überschrift über einer Gruppe bzw. Seite.</summary>
internal sealed class Heading : Control, IHeightForWidth, ISelfTranslating
{
    private readonly Font _font;
    private readonly string? _subtitle;
    private readonly bool _hint;

    public Heading(string text, bool page = false, string? subtitle = null, bool hint = false)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        Text = text;
        _subtitle = subtitle;
        _font = page ? UiFonts.Title : hint ? UiFonts.Small : UiFonts.Strong;
        _hint = hint;
        BackColor = Theme.Backdrop;
        Margin = page ? new Padding(0, 0, 0, 8) : new Padding(2, 14, 0, 2);
        TabStop = false;
    }

    private string Sub => _subtitle is null ? "" : Tr.T(_subtitle);

    public int HeightFor(int width)
    {
        int h = TextRenderer.MeasureText(Tr.T(Text), _font, new Size(width, 0), TextFormatFlags.WordBreak).Height + 4;
        if (_subtitle is not null)
            h += TextRenderer.MeasureText(Sub, UiFonts.Body, new Size(width, 0), TextFormatFlags.WordBreak).Height + 4;
        return h;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var r = ClientRectangle;
        int h = TextRenderer.MeasureText(Tr.T(Text), _font, new Size(r.Width, 0), TextFormatFlags.WordBreak).Height;
        TextRenderer.DrawText(e.Graphics, Tr.T(Text), _font, new Rectangle(0, 0, r.Width, h + 4), _hint ? Theme.Current.TextMuted : Theme.Current.Text,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        if (_subtitle is not null)
            TextRenderer.DrawText(e.Graphics, Sub, UiFonts.Body, new Rectangle(0, h + 4, r.Width, r.Height - h - 4),
                Theme.Current.TextMuted, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Einstellungszeile: Symbol, Titel und Beschreibung links, Bedienelement rechts (wie in den Windows-11-Einstellungen).
/// Texte werden selbst gezeichnet – schnell und ohne verschachtelte Labels.
/// </summary>
internal sealed class SettingRow : Control, IHeightForWidth, IExtraTexts, ISelfTranslating
{
    private readonly string? _glyph;
    private string? _description;
    private readonly Control? _content;
    private bool _hover;

    /// <summary>Zeile, die wie ein Link zu einer anderen Seite führt (mit Pfeil rechts).</summary>
    public bool Navigates { get; init; }

    public SettingRow(string title, string? description = null, Control? content = null, string? glyph = null)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        Text = title;
        _description = description;
        _glyph = glyph;
        _content = content;
        BackColor = Theme.Current.Surface;
        TabStop = false;
        if (content is not null)
        {
            content.BackColor = Theme.Current.Surface;
            Controls.Add(content);
        }
    }

    public Control? Content => _content;

    public string? Description
    {
        get => _description;
        set
        {
            if (_description == value)
                return;
            _description = value;
            Invalidate();
        }
    }

    public IEnumerable<string> ExtraTexts => _description is null ? [] : [Tr.T(_description)];

    private const int Pad = 16;
    private int TextLeft => _glyph is null ? Pad : Pad + 36;
    private int ContentWidth => _content is null ? Navigates ? 24 : 0 : _content.Width;

    private int TextWidth(int width) => Math.Max(120, width - TextLeft - ContentWidth - Pad - (_content is null ? 0 : 20));

    public int HeightFor(int width)
    {
        int tw = TextWidth(width);
        int h = TextRenderer.MeasureText(Tr.T(Text), UiFonts.Body, new Size(tw, 0), TextFormatFlags.WordBreak).Height;
        if (!string.IsNullOrEmpty(_description))
            h += TextRenderer.MeasureText(Tr.T(_description), UiFonts.Small, new Size(tw, 0), TextFormatFlags.WordBreak).Height + 1;
        int contentH = _content?.Height ?? 0;
        return Math.Max(Math.Max(h, contentH) + 26, string.IsNullOrEmpty(_description) ? 50 : 62);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (_content is not null)
            _content.Location = new Point(Width - Pad - _content.Width, (Height - _content.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(_hover && Navigates ? p.SurfaceHover : p.Surface);
        // Trennlinie zur vorigen Zeile in derselben Gruppe.
        if (Parent is SettingsGroup group && group.Controls.GetChildIndex(this) > 0)
            using (var line = new Pen(Theme.Dark ? Color.FromArgb(0x1F, 0x1F, 0x1F) : Color.FromArgb(0xEA, 0xEA, 0xEA)))
                g.DrawLine(line, 0, 0, Width, 0);
        if (_glyph is not null)
            TextRenderer.DrawText(g, _glyph, Glyph.Font(13f), new Rectangle(Pad, 0, 24, Height), p.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        int tw = TextWidth(Width);
        string title = Tr.T(Text);
        int th = TextRenderer.MeasureText(title, UiFonts.Body, new Size(tw, 0), TextFormatFlags.WordBreak).Height;
        int dh = string.IsNullOrEmpty(_description) ? 0
            : TextRenderer.MeasureText(Tr.T(_description), UiFonts.Small, new Size(tw, 0), TextFormatFlags.WordBreak).Height + 1;
        int y = (Height - th - dh) / 2;
        TextRenderer.DrawText(g, title, UiFonts.Body, new Rectangle(TextLeft, y, tw, th), Enabled ? p.Text : p.TextMuted,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        if (dh > 0)
            TextRenderer.DrawText(g, Tr.T(_description), UiFonts.Small, new Rectangle(TextLeft, y + th + 1, tw, dh), p.TextMuted,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        if (Navigates)
            TextRenderer.DrawText(g, Glyph.Chevron, Glyph.Font(10f), new Rectangle(Width - Pad - 20, 0, 20, Height), p.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (Navigates) { _hover = true; Invalidate(); Cursor = Cursors.Hand; }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover) { _hover = false; Invalidate(); }
    }
}

/// <summary>Ein/Aus-Schalter im Windows-11-Stil (statt Kontrollkästchen).</summary>
internal sealed class ToggleSwitch : Control, ISelfTranslating
{
    private bool _checked;
    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        Size = new Size(96, 24);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Enabled)
        {
            Focus();
            Checked = !Checked;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space && Enabled)
            Checked = !Checked;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(Width - 42, (Height - 20) / 2f, 40, 20);
        TextRenderer.DrawText(g, Tr.T(_checked ? "Ein" : "Aus"), UiFonts.Body, new Rectangle(0, 0, (int)track.X - 10, Height),
            Enabled ? p.Text : p.TextMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        using var path = Theme.RoundedRect(track, 10);
        var on = Enabled ? Theme.Accent : p.TextMuted;
        if (_checked)
        {
            using var fill = new SolidBrush(on);
            g.FillPath(fill, path);
        }
        else
        {
            using var pen = new Pen(Enabled ? p.TextMuted : p.Border, 1.2f);
            g.DrawPath(pen, path);
        }
        float d = 12, x = _checked ? track.Right - d - 4 : track.X + 4;
        using (var knob = new SolidBrush(_checked ? Theme.OnAccent : Enabled ? p.TextMuted : p.Border))
            g.FillEllipse(knob, x, track.Y + (track.Height - d) / 2, d, d);
        if (Focused && ShowFocusCues)
        {
            using var focus = Theme.RoundedRect(RectangleF.Inflate(track, 3, 3), 13);
            using var pen = new Pen(p.Text, 1.5f);
            g.DrawPath(pen, focus);
        }
    }
}

/// <summary>Schieberegler im Windows-11-Stil mit Wertanzeige (statt TrackBar, die keine Werte zeigt).</summary>
internal sealed class Slider : Control, ISelfTranslating
{
    private int _value, _min, _max = 100;
    private bool _drag, _hover;
    public event EventHandler? ValueChanged;
    public Func<int, string> Format { get; set; } = v => v.ToString();
    public int SmallChange { get; set; } = 1;
    private const int ValueWidth = 76;

    public Slider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.Selectable, true);
        Size = new Size(300, 32);
        TabStop = true;
    }

    public int Minimum { get => _min; set { _min = value; Value = _value; Invalidate(); } }
    public int Maximum { get => _max; set { _max = value; Value = _value; Invalidate(); } }

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, _min, Math.Max(_min, _max));
            if (v == _value)
                return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private RectangleF Track => new(10, Height / 2f - 2, Width - ValueWidth - 20, 4);

    private void SetFromMouse(int x)
    {
        var t = Track;
        float f = Math.Clamp((x - t.X) / t.Width, 0f, 1f);
        int raw = _min + (int)MathF.Round(f * (_max - _min));
        Value = _min + (raw - _min) / SmallChange * SmallChange;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled)
            return;
        Focus();
        _drag = true;
        Capture = true;
        SetFromMouse(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag)
            SetFromMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _drag = false;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int big = Math.Max(SmallChange, (_max - _min) / 10);
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down: Value -= SmallChange; break;
            case Keys.Right or Keys.Up: Value += SmallChange; break;
            case Keys.PageDown: Value -= big; break;
            case Keys.PageUp: Value += big; break;
            case Keys.Home: Value = _min; break;
            case Keys.End: Value = _max; break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var t = Track;
        float f = _max > _min ? (float)(_value - _min) / (_max - _min) : 0;
        float x = t.X + f * t.Width;
        using (var rest = Theme.RoundedRect(t, 2))
        using (var restBrush = new SolidBrush(Theme.Dark ? Color.FromArgb(0x9A, 0x9A, 0x9A) : Color.FromArgb(0x86, 0x86, 0x86)))
            g.FillPath(restBrush, rest);
        if (x > t.X + 1)
            using (var done = Theme.RoundedRect(new RectangleF(t.X, t.Y, x - t.X, t.Height), 2))
            using (var accent = new SolidBrush(Enabled ? Theme.Accent : p.TextMuted))
                g.FillPath(accent, done);
        // Daumen: Ring in Flächenfarbe, innen Akzentpunkt (größer bei Hover/Ziehen).
        const float D = 20;
        var outer = new RectangleF(x - D / 2, Height / 2f - D / 2, D, D);
        using (var ring = new SolidBrush(Theme.Dark ? Color.FromArgb(0x45, 0x45, 0x45) : Color.White))
            g.FillEllipse(ring, outer);
        using (var border = new Pen(p.Border))
            g.DrawEllipse(border, outer);
        float inner = _drag ? 10 : _hover ? 14 : 12;
        using (var dot = new SolidBrush(Enabled ? Theme.Accent : p.TextMuted))
            g.FillEllipse(dot, x - inner / 2, Height / 2f - inner / 2, inner, inner);
        if (Focused && ShowFocusCues)
            using (var pen = new Pen(p.Text, 1.5f))
                g.DrawEllipse(pen, RectangleF.Inflate(outer, 2, 2));
        TextRenderer.DrawText(g, Tr.T(Format(_value)), UiFonts.Body, new Rectangle(Width - ValueWidth, 0, ValueWidth, Height),
            Enabled ? p.Text : p.TextMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Umschalter mit mehreren Feldern (z. B. „Xbox | Nintendo“), gewähltes Feld in Akzentfarbe.</summary>
internal sealed class Segmented : Control, IExtraTexts, ISelfTranslating
{
    private readonly string[] _options;
    private int _selected;
    private int _hover = -1;
    public event EventHandler? SelectedIndexChanged;

    public Segmented(params string[] options)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.Selectable, true);
        _options = options;
        TabStop = true;
        Cursor = Cursors.Hand;
        int w = options.Sum(o => TextRenderer.MeasureText(Tr.T(o), UiFonts.Body).Width + 28);
        Size = new Size(w + 4, 32);
    }

    public IEnumerable<string> ExtraTexts => _options.Select(Tr.T);

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (_selected == value || value < 0 || value >= _options.Length)
                return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private Rectangle Cell(int i)
    {
        int x = 2;
        for (int k = 0; k < i; k++)
            x += TextRenderer.MeasureText(Tr.T(_options[k]), UiFonts.Body).Width + 28;
        return new Rectangle(x, 2, TextRenderer.MeasureText(Tr.T(_options[i]), UiFonts.Body).Width + 28, Height - 4);
    }

    private int HitTest(Point pt)
    {
        for (int i = 0; i < _options.Length; i++)
            if (Cell(i).Contains(pt))
                return i;
        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        int i = HitTest(e.Location);
        if (i >= 0 && Enabled)
            SelectedIndex = i;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = HitTest(e.Location);
        if (i != _hover) { _hover = i; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, _selected - 1);
        if (e.KeyCode == Keys.Right) SelectedIndex = Math.Min(_options.Length - 1, _selected + 1);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var outer = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 6))
        {
            using var back = new SolidBrush(p.SurfaceHover);
            g.FillPath(back, outer);
            using var pen = new Pen(p.Border);
            g.DrawPath(pen, outer);
        }
        for (int i = 0; i < _options.Length; i++)
        {
            var r = Cell(i);
            bool sel = i == _selected;
            if (sel || i == _hover)
            {
                using var cell = Theme.RoundedRect(r, 4);
                using var brush = new SolidBrush(sel ? Theme.Accent : Theme.Blend(p.SurfaceHover, p.Text, 0.08f));
                g.FillPath(brush, cell);
            }
            TextRenderer.DrawText(g, Tr.T(_options[i]), UiFonts.Body, r, sel ? Theme.OnAccent : p.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        if (Focused && ShowFocusCues)
            using (var pen = new Pen(p.Text, 1.5f))
            using (var path = Theme.RoundedRect(new RectangleF(1, 1, Width - 3, Height - 3), 6))
                g.DrawPath(pen, path);
    }
}

/// <summary>Flacher Knopf mit Symbol und Text (Windows-11-Stil); optional Akzent oder umschaltbar.</summary>
internal sealed class GlyphButton : Control, ISelfTranslating
{
    private bool _hover, _down, _checked;
    private readonly string? _glyph;
    public bool Accent { get; set; }
    /// <summary>Umschaltknopf (z. B. „Einstellungen“ auf- und zuklappen): gedrückt in Akzentfarbe.</summary>
    public bool Toggle { get; set; }
    public string? TrailingGlyph { get; set; }

    public GlyphButton(string text, string? glyph = null, bool accent = false)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        Text = text;
        _glyph = glyph;
        Accent = accent;
        TabStop = true;
        Height = 32;
        Cursor = Cursors.Hand;
        Margin = new Padding(6, 0, 0, 0);
        FitWidth();
    }

    public bool Checked
    {
        get => _checked;
        set { _checked = value; Invalidate(); }
    }

    public void FitWidth()
    {
        string text = Tr.T(Text);
        int w = 24 + (text.Length > 0 ? TextRenderer.MeasureText(text, UiFonts.Body).Width : 0) + (_glyph is null ? 0 : 24)
                + (TrailingGlyph is null ? 0 : 20) - (text.Length == 0 ? 10 : 0);
        Width = Math.Max(text.Length == 0 ? 34 : 64, w);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        FitWidth();
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _down = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _down = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter && Enabled)
            OnClick(EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool accent = Accent || (Toggle && _checked);
        Color back = accent ? Theme.Accent : p.SurfaceHover;
        if (Enabled && _down) back = Theme.Blend(back, accent ? Color.Black : p.Text, 0.14f);
        else if (Enabled && _hover) back = Theme.Blend(back, accent ? Color.White : p.Text, 0.08f);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.RoundedRect(r, 5))
        {
            using var fill = new SolidBrush(back);
            g.FillPath(fill, path);
            using var pen = new Pen(accent ? Theme.Blend(back, Color.White, 0.15f) : p.Border);
            g.DrawPath(pen, path);
        }
        var fore = !Enabled ? p.TextMuted : accent ? Theme.OnAccent : p.Text;
        string text = Tr.T(Text);
        int textW = text.Length > 0 ? TextRenderer.MeasureText(text, UiFonts.Body).Width : 0;
        int total = (_glyph is null ? 0 : 16) + (_glyph is not null && textW > 0 ? 8 : 0) + textW + (TrailingGlyph is null ? 0 : 18);
        int x = (Width - total) / 2;
        if (_glyph is not null)
        {
            TextRenderer.DrawText(g, _glyph, Glyph.Font(10.5f), new Rectangle(x, 0, 16, Height), fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += textW > 0 ? 24 : 16;
        }
        if (textW > 0)
        {
            TextRenderer.DrawText(g, text, UiFonts.Body, new Rectangle(x, 0, textW + 2, Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            x += textW + 8;
        }
        if (TrailingGlyph is not null)
            TextRenderer.DrawText(g, TrailingGlyph, Glyph.Font(8f), new Rectangle(x, 0, 12, Height), fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
            using (var pen = new Pen(p.Text, 1.5f))
            using (var path = Theme.RoundedRect(RectangleF.Inflate(r, -2, -2), 4))
                g.DrawPath(pen, path);
    }
}

/// <summary>Reiterleiste (Text mit Akzentstrich unter dem gewählten Reiter), z. B. in der Controller-Karte.</summary>
internal sealed class PivotTabs : Control, IExtraTexts, ISelfTranslating
{
    private readonly List<(string Text, bool Shown)> _tabs = [];
    private int _selected, _hover = -1;
    public event EventHandler? SelectedIndexChanged;

    public PivotTabs()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = 40;
        TabStop = true;
    }

    public IEnumerable<string> ExtraTexts => _tabs.Select(t => Tr.T(t.Text));

    public int Add(string text)
    {
        _tabs.Add((text, true));
        Invalidate();
        return _tabs.Count - 1;
    }

    public bool IsShown(int index) => index >= 0 && index < _tabs.Count && _tabs[index].Shown;

    public void SetShown(int index, bool shown)
    {
        if (_tabs[index].Shown == shown)
            return;
        _tabs[index] = (_tabs[index].Text, shown);
        if (!shown && _selected == index)
            SelectedIndex = _tabs.FindIndex(t => t.Shown);
        Invalidate();
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value < 0 || value >= _tabs.Count || value == _selected)
                return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private IEnumerable<(int Index, Rectangle Bounds)> Cells()
    {
        int x = 0;
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (!_tabs[i].Shown)
                continue;
            int w = TextRenderer.MeasureText(Tr.T(_tabs[i].Text), UiFonts.Strong).Width + 24;
            yield return (i, new Rectangle(x, 0, w, Height));
            x += w + 4;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        foreach (var (i, r) in Cells())
            if (r.Contains(e.Location))
                SelectedIndex = i;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = Cells().Where(c => c.Bounds.Contains(e.Location)).Select(c => c.Index).DefaultIfEmpty(-1).First();
        if (hover != _hover) { _hover = hover; Invalidate(); Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default; }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var shown = Cells().Select(c => c.Index).ToList();
        int pos = shown.IndexOf(_selected);
        if (e.KeyCode == Keys.Left && pos > 0) SelectedIndex = shown[pos - 1];
        if (e.KeyCode == Keys.Right && pos < shown.Count - 1) SelectedIndex = shown[pos + 1];
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var (i, r) in Cells())
        {
            bool sel = i == _selected;
            if (i == _hover && !sel)
            {
                using var hover = Theme.RoundedRect(Rectangle.Inflate(r, 0, -5), 5);
                using var brush = new SolidBrush(p.SurfaceHover);
                g.FillPath(brush, hover);
            }
            TextRenderer.DrawText(g, Tr.T(_tabs[i].Text), sel ? UiFonts.Strong : UiFonts.Body, r, sel ? p.Text : p.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (sel)
            {
                using var bar = Theme.RoundedRect(new RectangleF(r.X + r.Width / 2f - 10, r.Bottom - 4, 20, 3), 1.5f);
                using var accent = new SolidBrush(Theme.Accent);
                g.FillPath(accent, bar);
                if (Focused && ShowFocusCues)
                    using (var pen = new Pen(p.Text))
                        g.DrawRectangle(pen, Rectangle.Inflate(r, -2, -6));
            }
        }
    }
}

/// <summary>Eintrag der linken Navigationsleiste (Symbol und Text, gewählt mit Akzentstrich links).</summary>
internal sealed class NavItem : Control, ISelfTranslating
{
    private readonly string _glyph;
    private bool _hover, _selected;

    public NavItem(string text, string glyph, Action select)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.ResizeRedraw, true);
        Text = text;
        _glyph = glyph;
        Height = 40;
        Cursor = Cursors.Hand;
        TabStop = true;
        Margin = new Padding(0, 0, 0, 2);
        Click += (_, _) => select();
        KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) select(); };
    }

    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        if (_selected || _hover)
        {
            using var path = Theme.RoundedRect(r, 5);
            using var brush = new SolidBrush(_selected ? p.SurfaceHover : Theme.Blend(BackColor, p.Text, 0.05f));
            g.FillPath(brush, path);
        }
        if (_selected)
        {
            using var pill = Theme.RoundedRect(new RectangleF(0, Height / 2f - 8, 3, 16), 1.5f);
            using var accent = new SolidBrush(Theme.Accent);
            g.FillPath(accent, pill);
        }
        TextRenderer.DrawText(g, _glyph, Glyph.Font(12f), new Rectangle(12, 0, 24, Height), p.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Tr.T(Text), UiFonts.Body, new Rectangle(46, 0, Width - 50, Height), p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
            using (var pen = new Pen(p.Text, 1.5f))
            using (var path = Theme.RoundedRect(RectangleF.Inflate(r, -1, -1), 5))
                g.DrawPath(pen, path);
    }
}

/// <summary>Seite mit senkrechtem Bildlauf; Inhalt (StackPanel) in voller Breite bis zu einer Höchstbreite.</summary>
internal sealed class ScrollPage : Panel
{
    public StackPanel Content { get; } = new() { Spacing = 0, Padding = new Padding(0, 0, 0, 24) };
    public int MaxContentWidth { get; set; } = 1000;

    public ScrollPage()
    {
        AutoScroll = true;
        BackColor = Theme.Backdrop;
        Padding = new Padding(28, 20, 28, 0);
        Controls.Add(Content);
        Content.Resize += (_, _) => AdjustScroll();
        Theme.DarkScroll(this);
    }

    private void AdjustScroll() => AutoScrollMinSize = new Size(0, Content.Bottom + 8 - AutoScrollPosition.Y);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int width = Math.Min(MaxContentWidth, ClientSize.Width - Padding.Horizontal);
        Content.SetBounds(Padding.Left + AutoScrollPosition.X, Padding.Top + AutoScrollPosition.Y, Math.Max(200, width), Content.Height);
        Content.PerformLayout();
        AdjustScroll();
    }

    /// <summary>Gruppe mit Überschrift hinzufügen; gibt die Gruppe zurück.</summary>
    public SettingsGroup AddGroup(string? title, params Control[] rows)
    {
        if (title is not null)
            Content.Controls.Add(new Heading(title));
        var group = new SettingsGroup();
        foreach (var row in rows)
            group.Controls.Add(row);
        Content.Controls.Add(group);
        return group;
    }
}
