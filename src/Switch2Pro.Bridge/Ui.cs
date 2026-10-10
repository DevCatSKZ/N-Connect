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
            Fonts[size] = font = new Font("Segoe MDL2 Assets", size * UiScale.Test, GraphicsUnit.Point);
        return font;
    }

    /// <summary>Symbole mit eigener Vektorzeichnung (schärfer und besser lesbar als die Fontschrift).
    /// <paramref name="em"/> ist die Schriftgröße, die das Fontsymbol an dieser Stelle hätte – so bleibt
    /// die Vektorzeichnung genauso groß wie die anderen Symbole. true = gezeichnet.</summary>
    public static bool Paint(Graphics g, string glyph, Rectangle bounds, Color color, float em = 0)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (glyph == Bluetooth)
        {
            BluetoothMark(g, bounds, color, em);
            return true;
        }
        return false;
    }

    /// <summary>Bluetooth-Rune (ᚼ+ᛒ): Mittelsteg, zwei Dreiecke nach rechts, zwei Striche von links zur Mitte.</summary>
    private static void BluetoothMark(Graphics g, Rectangle bounds, Color color, float em)
    {
        // em in Punkt → Pixel bei der aktuellen Skalierung (wie die Fontsymbole daneben).
        float h = em > 0 ? em * 96f / 72f * UiScale.Factor : Math.Min(bounds.Height * 0.72f, bounds.Width * 1.1f);
        h = Math.Min(h, bounds.Height * 0.8f);
        float w = h * 0.82f;
        float x = bounds.X + (bounds.Width - w) / 2f, y = bounds.Y + (bounds.Height - h) / 2f;
        PointF P(float nx, float ny) => new(x + nx * w, y + ny * h);
        var top = P(0.5f, 0f); var mid = P(0.5f, 0.5f); var bot = P(0.5f, 1f);
        var r1 = P(0.98f, 0.26f); var r2 = P(0.98f, 0.74f);
        var l1 = P(0.02f, 0.26f); var l2 = P(0.02f, 0.74f);
        using var pen = new Pen(color, Math.Max(1.6f, h * 0.11f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, top, bot);
        g.DrawLine(pen, top, r1); g.DrawLine(pen, r1, mid);
        g.DrawLine(pen, mid, r2); g.DrawLine(pen, r2, bot);
        g.DrawLine(pen, l1, mid); g.DrawLine(pen, l2, mid);
    }
}

/// <summary>
/// Skalierung der selbst gezeichneten Oberfläche. Schriften in Punkt wachsen mit der Windows-Skalierung (125 %, 150 %,
/// 200 % …); Abstände, Zeilenhöhen und Kacheln, die zur Laufzeit berechnet werden, müssen mitwachsen – sonst ist die
/// Schrift größer als ihr Feld. <see cref="Px(int)"/> rechnet Werte für 100 % auf die aktuelle Skalierung um.
/// Die App skaliert vollständig selbst (alle Fenster: AutoScaleMode.None) – feste Größen immer über Px angeben, dann
/// sehen Fensterteile gleich aus, egal ob sie beim Öffnen oder erst später entstehen.
/// Prüfhilfe: <c>--scale=1.5</c> bildet 150 % nach (Schriften, Abstände und WinForms-Skalierung), ohne die Anzeige
/// umzustellen.
/// </summary>
internal static class UiScale
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    /// <summary>Testfaktor aus <c>--scale=x</c> (1 = keine Simulation).</summary>
    public static readonly float Test = ParseTest();

    /// <summary>Faktor gegenüber 100 %: System-DPI ÷ 96 × Testfaktor (so wachsen Punkt-Schriften tatsächlich).</summary>
    public static readonly float Factor = SystemDpi() / 96f * Test;

    public static int Px(int value) => (int)MathF.Round(value * Factor);
    public static float Px(float value) => value * Factor;

    /// <summary>
    /// Zur Laufzeit erzeugten Bereich (Controller-Karte, aufgeklappte Reiterseite) einmal so skalieren, wie WinForms es
    /// beim Öffnen des Fensters mit allen vorhandenen Steuerelementen tut – sonst blieben später erzeugte Teile bei 100 %.
    /// Vor dem Einfügen in das Fenster aufrufen.
    /// </summary>
    public static void ScaleNew(Control control)
    {
        if (MathF.Abs(Factor - 1f) > 0.01f)
            control.Scale(new SizeF(Factor, Factor));
    }

    /// <summary>Basis für AutoScaleDimensions: 96 DPI, bei der Simulation entsprechend kleiner (WinForms skaliert dann mit).</summary>
    public static SizeF Dimensions => new(96f / Test, 96f / Test);

    private static float SystemDpi()
    {
        try { return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393) ? Math.Max(96u, GetDpiForSystem()) : 96f; }
        catch (EntryPointNotFoundException) { return 96f; }
    }

    private static float ParseTest()
    {
        var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--scale=", StringComparison.Ordinal));
        return arg is not null && float.TryParse(arg["--scale=".Length..], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float f) && f is >= 1f and <= 3f ? f : 1f;
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
            Cache[(size, style, name)] = font = new Font(name, size * UiScale.Test, style, GraphicsUnit.Point);
        return font;
    }

    // Typografie wie Windows 11 (Fluent): Caption 12 px, Body 14 px, Body Strong 14 px, Body Large 18 px, Title 28 px.
    public static Font Body => Get("Segoe UI Variable Text", "Segoe UI", 10.5f, FontStyle.Regular);
    public static Font Small => Get("Segoe UI Variable Small", "Segoe UI", 9f, FontStyle.Regular);
    public static Font Strong => Get("Segoe UI Variable Text Semibold", "Segoe UI Semibold", 10.5f, FontStyle.Regular);
    public static Font Subtitle => Get("Segoe UI Variable Display Semibold", "Segoe UI Semibold", 13.5f, FontStyle.Regular);
    public static Font Title => Get("Segoe UI Variable Display Semibold", "Segoe UI Semibold", 21f, FontStyle.Regular);
}

/// <summary>Schriftzug „N-Connect“ im Navigationskopf, gefüllt mit dem Neon-Verlauf des Logos.</summary>
internal sealed class Wordmark : Control, ISelfTranslating
{
    /// <summary>Größe (skaliert über UiScale) setzt das Steuerelement selbst – WinForms nur Lage überlassen.</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) =>
        base.ScaleControl(factor, specified & ~BoundsSpecified.Size);

    public Wordmark()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        Text = "N-Connect";
        Size = new Size(UiScale.Px(200), UiScale.Px(34));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var font = UiFonts.Subtitle;
        if (!Theme.Neon)
        {
            // Windows-Schema: App-Name schlicht und scharf (ClearType) in Textfarbe, wie Windows-Apps.
            TextRenderer.DrawText(g, Text, font, new Rectangle(0, 0, Width, Height), Theme.Current.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }
        using var path = new GraphicsPath();
        // Schriftgröße in Pixel (Punkt × DPI / 72), damit der Pfad gleich groß wie gezeichneter Text wird.
        float em = font.SizeInPoints * DeviceDpi / 72f * 1.12f;
        path.AddString(Text, font.FontFamily, (int)FontStyle.Bold, em, new PointF(UiScale.Px(4f), UiScale.Px(4f)), StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        Theme.Glow(g, path, 0.6f, 3);
        using var brush = Theme.LineBrush(bounds);
        g.FillPath(brush, path);
    }
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

/// <summary>
/// WinRT-/COM-Aufrufe (Bluetooth, Gerätesuche) können auf dem UI-Thread einen DPI-unaware-Kontext
/// hinterlassen. Ein Fenster, das dann entsteht, meldet 96 DPI: das Layout bleibt unskaliert, Windows
/// skaliert aber die GDI-Schrift auf die Monitor-DPI – Texte werden überall abgeschnitten. Darum
/// erzeugt jedes Fenster sein Handle bewusst im PerMonitorV2-Kontext des Prozesses.
/// </summary>
internal static class Dpi
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
    private static readonly IntPtr PerMonitorV2 = new(-4);

    public static IntPtr BeginPerMonitorV2() => OperatingSystem.IsWindows() ? SetThreadDpiAwarenessContext(PerMonitorV2) : IntPtr.Zero;

    public static void End(IntPtr previous)
    {
        if (OperatingSystem.IsWindows() && previous != IntPtr.Zero)
            SetThreadDpiAwarenessContext(previous);
    }
}

/// <summary>Basis aller Fenster: erzeugt das Handle im PerMonitorV2-Kontext (siehe <see cref="Dpi"/>).</summary>
internal class UiForm : Form
{
    /// <summary>
    /// Prüfhilfen (<c>--render…</c>, <c>--dump-ui</c>): Fenster nie aktivieren, nicht in Taskleiste/Alt-Tab und immer
    /// außerhalb des Bildschirms – sonst springen Fokus und Maus des Benutzers, während Bilder entstehen.
    /// </summary>
    internal static readonly bool Offscreen = Environment.GetCommandLineArgs()
        .Any(a => a.StartsWith("--render", StringComparison.Ordinal) || a == "--dump-ui");

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool LockSetForegroundWindow(uint code);

    /// <summary>Prüfhilfe läuft auf dem eigenen, unsichtbaren Desktop (<see cref="HiddenDesktop"/>).</summary>
    internal static bool Isolated => HiddenDesktop.Active;

    /// <summary>Prüfhilfe außerhalb des unsichtbaren Desktops (Notfall): Fokuswechsel für diesen Prozess sperren.</summary>
    internal static void LockForeground()
    {
        if (Offscreen && !Isolated)
            LockSetForegroundWindow(1); // LSFW_LOCK
    }

    protected override bool ShowWithoutActivation => Offscreen || base.ShowWithoutActivation;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (Offscreen)
                cp.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        if (Offscreen)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-6000, -6000);
            ShowInTaskbar = false;
        }
        base.OnLoad(e);
    }

    /// <summary>
    /// Fenster auf einen Monitor mit anderer Skalierung gezogen: nicht von WinForms umskalieren lassen. Schriften und
    /// alle Maße (UiScale) richten sich einheitlich nach der System-Skalierung; ein nachträgliches Umskalieren einzelner
    /// Teile würde Text und Felder auseinanderbringen.
    /// </summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        e.Cancel = true;
        base.OnDpiChanged(e);
    }

    protected override void CreateHandle()
    {
        var previous = Dpi.BeginPerMonitorV2();
        try { base.CreateHandle(); }
        finally { Dpi.End(previous); }
    }
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

    /// <summary>Sichtbarkeit aller Kinder auf einmal neu setzen – nur ein Layout-Durchlauf statt je Kind einer.</summary>
    public void SetShownAll(Func<Control, bool> shown)
    {
        SuspendLayout();
        _hidden.Clear();
        foreach (Control c in Controls)
        {
            bool s = shown(c);
            if (!s)
                _hidden.Add(c);
            c.Visible = s;
        }
        ResumeLayout(true);
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
        Padding = new Padding(UiScale.Px(1), UiScale.Px(4), UiScale.Px(1), UiScale.Px(4));
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
        IsPage = page;
        _subtitle = subtitle;
        _font = page ? UiFonts.Title : hint ? UiFonts.Small : UiFonts.Strong;
        _hint = hint;
        BackColor = Theme.Backdrop;
        Margin = page ? new Padding(0, 0, 0, UiScale.Px(8)) : new Padding(UiScale.Px(2), UiScale.Px(14), 0, UiScale.Px(2));
        TabStop = false;
    }

    /// <summary>Seitenüberschrift (groß) – im Gegensatz zu den Gruppenüberschriften darunter.</summary>
    internal bool IsPage { get; }

    private string Sub => _subtitle is null ? "" : Tr.T(_subtitle);

    public int HeightFor(int width)
    {
        int h = TextRenderer.MeasureText(Tr.T(Text), _font, new Size(width, 0), TextFormatFlags.WordBreak).Height + 4;
        if (_subtitle is not null)
            h += TextRenderer.MeasureText(Sub, UiFonts.Body, new Size(width, 0), TextFormatFlags.WordBreak).Height + 4;
        return h;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        // Oben angedockt (Kopf der Controller-Seite): Höhe aus dem Text – feste Höhen schneiden bei 150 % die Unterzeile ab.
        if (Dock == DockStyle.Top && Width > 0)
        {
            int h = HeightFor(Width) + UiScale.Px(14);
            if (Height != h)
                Height = h;
        }
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
            // Wächst das Bedienelement (z. B. Auswahlfeld passt sich der Übersetzung an), Zeile und Gruppe neu anordnen.
            content.Resize += (_, _) =>
            {
                PerformLayout();
                Parent?.PerformLayout();
                Invalidate();
            };
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

    private static int Pad => UiScale.Px(16);
    private int TextLeft => _glyph is null ? Pad : Pad + UiScale.Px(36);
    private int ContentWidth => _content is null ? Navigates ? UiScale.Px(24) : 0 : _content.Width;

    /// <summary>Breites Bedienelement unter den Text statt daneben, wenn daneben zu wenig Platz für den Text bliebe
    /// (schmales Fenster, hohe Skalierung, lange Übersetzungen) – wie in den Windows-11-Einstellungen.</summary>
    private bool Stacked(int width) =>
        _content is not null && width - TextLeft - _content.Width - Pad - UiScale.Px(20) < UiScale.Px(220);

    private int TextWidth(int width) => Stacked(width)
        ? Math.Max(UiScale.Px(120), width - TextLeft - Pad)
        : Math.Max(UiScale.Px(120), width - TextLeft - ContentWidth - Pad - (_content is null ? 0 : UiScale.Px(20)));

    private int TextHeight(int width)
    {
        int tw = TextWidth(width);
        int h = TextRenderer.MeasureText(Tr.T(Text), UiFonts.Body, new Size(tw, 0), TextFormatFlags.WordBreak).Height;
        if (!string.IsNullOrEmpty(_description))
            h += TextRenderer.MeasureText(Tr.T(_description), UiFonts.Small, new Size(tw, 0), TextFormatFlags.WordBreak).Height + 1;
        return h;
    }

    public int HeightFor(int width)
    {
        int h = TextHeight(width);
        int contentH = _content?.Height ?? 0;
        if (Stacked(width))
            return h + UiScale.Px(10) + contentH + UiScale.Px(26);
        return Math.Max(Math.Max(h, contentH) + UiScale.Px(26), UiScale.Px(string.IsNullOrEmpty(_description) ? 50 : 62));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (_content is null)
            return;
        _content.Location = Stacked(Width)
            ? new Point(TextLeft, Height - UiScale.Px(13) - _content.Height)
            : new Point(Width - Pad - _content.Width, (Height - _content.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(_hover && Navigates ? p.SurfaceHover : p.Surface);
        // Trennlinie zur vorigen Zeile in derselben Gruppe.
        if (Parent is SettingsGroup group && group.Controls.GetChildIndex(this) > 0)
            using (var line = new Pen(Theme.Divider))
                g.DrawLine(line, 0, 0, Width, 0);
        // Symbol leicht in der Akzentfarbe getönt – verbindet die Zeilen optisch mit dem Farbschema.
        var glyphColor = Theme.Neon ? Theme.Blend(p.Text, Theme.AccentLine, 0.35f) : p.Text;
        int tw = TextWidth(Width);
        string title = Tr.T(Text);
        int th = TextRenderer.MeasureText(title, UiFonts.Body, new Size(tw, 0), TextFormatFlags.WordBreak).Height;
        int dh = string.IsNullOrEmpty(_description) ? 0
            : TextRenderer.MeasureText(Tr.T(_description), UiFonts.Small, new Size(tw, 0), TextFormatFlags.WordBreak).Height + 1;
        // Untereinander: Text oben, Bedienelement darunter; sonst Text senkrecht mittig.
        bool stacked = Stacked(Width);
        int y = stacked ? UiScale.Px(13) : (Height - th - dh) / 2;
        // Symbol: mittig in der Zeile, untereinander auf Höhe des Titels.
        int glyphBox = UiScale.Px(24);
        var glyphRect = stacked ? new Rectangle(Pad, y, glyphBox, th) : new Rectangle(Pad, 0, glyphBox, Height);
        if (_glyph is not null && !Glyph.Paint(g, _glyph, glyphRect, glyphColor, 13f))
            TextRenderer.DrawText(g, _glyph, Glyph.Font(13f), glyphRect, glyphColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, title, UiFonts.Body, new Rectangle(TextLeft, y, tw, th), Enabled ? p.Text : p.TextMuted,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        if (dh > 0)
            TextRenderer.DrawText(g, Tr.T(_description), UiFonts.Small, new Rectangle(TextLeft, y + th + 1, tw, dh), p.TextMuted,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        if (Navigates)
            TextRenderer.DrawText(g, Glyph.Chevron, Glyph.Font(10f), new Rectangle(Width - Pad - UiScale.Px(20), 0, UiScale.Px(20), Height), p.Text,
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
    private bool _checked, _hover;
    /// <summary>0 = aus, 1 = ein (gleitet beim Umschalten).</summary>
    private readonly Anim _knob;
    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        // Breite nach dem längsten übersetzten Zustand („Ein“/„Aus“) plus Schalter; alles in der aktuellen Skalierung.
        int label = Math.Max(TextRenderer.MeasureText(Tr.T("Ein"), UiFonts.Body).Width, TextRenderer.MeasureText(Tr.T("Aus"), UiFonts.Body).Width);
        Size = new Size(Math.Max(UiScale.Px(96), label + UiScale.Px(56)), UiScale.Px(24));
        Cursor = Cursors.Hand;
        TabStop = true;
        _knob = new Anim(this, 0, speed: 18f);
    }

    /// <summary>Größe setzt der Schalter selbst (skaliert) – WinForms verschiebt ihn nur, sonst wäre sie doppelt skaliert.</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) =>
        base.ScaleControl(factor, specified & ~BoundsSpecified.Size);

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;
            _checked = value;
            _knob.Target = value ? 1 : 0;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

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
        float k = UiScale.Factor;
        var track = new RectangleF(Width - 42 * k, (Height - 20 * k) / 2f, 40 * k, 20 * k);
        TextRenderer.DrawText(g, Tr.T(_checked ? "Ein" : "Aus"), UiFonts.Body, new Rectangle(0, 0, (int)(track.X - 10 * k), Height),
            Enabled ? p.Text : p.TextMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        using var path = Theme.RoundedRect(track, 10 * k);
        float t = _knob.Value;
        // Aus: Umriss; ein: Neon-Verlauf, der beim Umschalten ein- bzw. ausblendet.
        if (t < 1)
        {
            using var pen = new Pen(Enabled ? p.TextMuted : p.Border, 1.2f);
            g.DrawPath(pen, path);
        }
        if (t > 0)
        {
            using var fill = Theme.AccentBrush(track, 0, Enabled);
            fill.LinearColors = [Color.FromArgb((int)(255 * t), fill.LinearColors[0]), Color.FromArgb((int)(255 * t), fill.LinearColors[1])];
            g.FillPath(fill, path);
        }
        float d = (12 + (_hover && Enabled ? 2 : 0)) * k;
        float x = track.X + 4 * k + (track.Width - 20 * k) * t - (d - 12 * k) / 2;
        var knobOff = Enabled ? p.TextMuted : p.Border;
        using (var knob = new SolidBrush(Theme.Blend(knobOff, Theme.OnAccent, t)))
            g.FillEllipse(knob, x, track.Y + (track.Height - d) / 2, d, d);
        if (Focused && ShowFocusCues)
        {
            using var focus = Theme.RoundedRect(RectangleF.Inflate(track, 3 * k, 3 * k), 13 * k);
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
    private static int ValueWidth => UiScale.Px(76);

    public Slider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.Selectable, true);
        Size = new Size(300, UiScale.Px(32));
        TabStop = true;
    }

    /// <summary>Höhe setzt der Regler selbst (skaliert); Breite und Lage skaliert WinForms.</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) =>
        base.ScaleControl(factor, specified & ~BoundsSpecified.Height);

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

    private RectangleF Track => new(UiScale.Px(10f), Height / 2f - UiScale.Px(2f), Width - ValueWidth - UiScale.Px(20), UiScale.Px(4f));

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
        using (var rest = Theme.RoundedRect(t, t.Height / 2))
        using (var restBrush = new SolidBrush(Theme.Blend(p.SurfaceHover, p.TextMuted, 0.55f)))
            g.FillPath(restBrush, rest);
        if (x > t.X + 1)
            using (var done = Theme.RoundedRect(new RectangleF(t.X, t.Y, x - t.X, t.Height), t.Height / 2))
            using (var accent = Theme.AccentBrush(t, 0, Enabled)) // Verlauf über die ganze Strecke: rechts wird es violetter
                g.FillPath(accent, done);
        // Daumen: Ring in Flächenfarbe, innen Akzentpunkt (größer bei Hover/Ziehen) in der Verlaufsfarbe an dieser Stelle.
        float D = UiScale.Px(20f);
        var outer = new RectangleF(x - D / 2, Height / 2f - D / 2, D, D);
        using (var ring = new SolidBrush(Theme.Dark ? p.SurfaceHover : Color.White))
            g.FillEllipse(ring, outer);
        using (var border = new Pen(p.ControlBorder))
            g.DrawEllipse(border, outer);
        float inner = UiScale.Px(_drag ? 10f : _hover ? 14f : 12f);
        using (var dot = new SolidBrush(Enabled ? Theme.Blend(Theme.AccentLine, Theme.Accent2Line, f) : p.TextMuted))
            g.FillEllipse(dot, x - inner / 2, Height / 2f - inner / 2, inner, inner);
        if (Focused && ShowFocusCues)
            using (var pen = new Pen(p.Text, 1.5f))
                g.DrawEllipse(pen, RectangleF.Inflate(outer, UiScale.Px(2f), UiScale.Px(2f)));
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
    /// <summary>Position der Auswahlmarke als (Bruchteil-)Index – gleitet beim Wechsel zum neuen Feld.</summary>
    private readonly Anim _slide;
    public event EventHandler? SelectedIndexChanged;

    public Segmented(params string[] options)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.Selectable, true);
        _slide = new Anim(this, 0, speed: 16f);
        _options = options;
        TabStop = true;
        Cursor = Cursors.Hand;
        int w = options.Sum(o => TextRenderer.MeasureText(Tr.T(o), UiFonts.Body).Width + CellPad);
        Size = new Size(w + UiScale.Px(4), UiScale.Px(32));
    }

    /// <summary>Innenabstand je Feld (links + rechts), skaliert.</summary>
    private static int CellPad => UiScale.Px(28);

    /// <summary>Größe ergibt sich aus gemessenem Text und skalierten Abständen – WinForms nur die Lage überlassen.</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) =>
        base.ScaleControl(factor, specified & ~BoundsSpecified.Size);

    public IEnumerable<string> ExtraTexts => _options.Select(Tr.T);

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (_selected == value || value < 0 || value >= _options.Length)
                return;
            _selected = value;
            _slide.Target = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private Rectangle Cell(int i)
    {
        int inset = UiScale.Px(2), x = inset;
        for (int k = 0; k < i; k++)
            x += TextRenderer.MeasureText(Tr.T(_options[k]), UiFonts.Body).Width + CellPad;
        return new Rectangle(x, inset, TextRenderer.MeasureText(Tr.T(_options[i]), UiFonts.Body).Width + CellPad, Height - 2 * inset);
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
        using (var outer = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), UiScale.Px(6f)))
        {
            using var back = new SolidBrush(p.SurfaceHover);
            g.FillPath(back, outer);
            using var pen = new Pen(p.ControlBorder);
            g.DrawPath(pen, outer);
        }
        if (_hover >= 0 && _hover != _selected)
        {
            using var cell = Theme.RoundedRect(Cell(_hover), UiScale.Px(4f));
            using var brush = new SolidBrush(Theme.Blend(p.SurfaceHover, p.Text, 0.08f));
            g.FillPath(brush, cell);
        }
        // Auswahlmarke zwischen zwei Feldern einblenden (gleitet beim Wechsel).
        float pos = Math.Clamp(_slide.Value, 0, _options.Length - 1);
        int a = (int)MathF.Floor(pos), b = Math.Min(_options.Length - 1, a + 1);
        float f = pos - a;
        Rectangle ra = Cell(a), rb = Cell(b);
        var mark = new RectangleF(ra.X + (rb.X - ra.X) * f, ra.Y, ra.Width + (rb.Width - ra.Width) * f, ra.Height);
        using (var cell = Theme.RoundedRect(mark, UiScale.Px(4f)))
        using (var brush = Theme.AccentBrush(mark, 0, Enabled))
            g.FillPath(brush, cell);
        for (int i = 0; i < _options.Length; i++)
        {
            var r = Cell(i);
            // Text wird weiß, sobald die Marke das Feld überwiegend bedeckt.
            bool sel = MathF.Abs(pos - i) < 0.5f;
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
    private bool _down, _checked;
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
        Height = UiScale.Px(32);
        Cursor = Cursors.Hand;
        Margin = new Padding(UiScale.Px(6), 0, 0, 0);
        _hoverAnim = new Anim(this);
        FitWidth();
    }

    /// <summary>Hover-Hervorhebung 0–1 (blendet weich ein und aus).</summary>
    private readonly Anim _hoverAnim;

    public bool Checked
    {
        get => _checked;
        set { _checked = value; Invalidate(); }
    }

    public void FitWidth()
    {
        string text = Tr.T(Text);
        static int S(int v) => UiScale.Px(v);
        int w = S(24) + (text.Length > 0 ? TextRenderer.MeasureText(text, UiFonts.Body).Width : 0) + (_glyph is null ? 0 : S(24))
                + (TrailingGlyph is null ? 0 : S(20)) - (text.Length == 0 ? S(10) : 0);
        Width = Math.Max(S(text.Length == 0 ? 34 : 64), w);
    }

    /// <summary>Größe ergibt sich aus gemessenem Text und skalierten Abständen – WinForms nur die Lage überlassen
    /// (sonst wären Knöpfe, die beim Öffnen des Fensters schon existieren, doppelt so breit skaliert).</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) =>
        base.ScaleControl(factor, specified & ~BoundsSpecified.Size);

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        FitWidth();
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hoverAnim.Target = 1; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _down = false; _hoverAnim.Target = 0; Invalidate(); }
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
        float hover = Enabled ? _hoverAnim.Value : 0;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        static int S(int v) => UiScale.Px(v);
        using (var path = Theme.RoundedRect(r, S(5)))
        {
            if (accent)
            {
                // Neon-Verlauf; Hover hellt auf, Drücken dunkelt ab.
                using var fill = Theme.AccentBrush(r, 0, Enabled);
                var shade = Enabled && _down ? Color.Black : Color.White;
                float amount = Enabled && _down ? 0.14f : 0.12f * hover;
                fill.LinearColors = [Theme.Blend(fill.LinearColors[0], shade, amount), Theme.Blend(fill.LinearColors[1], shade, amount)];
                g.FillPath(fill, path);
                using var pen = new Pen(Color.FromArgb(Theme.Dark ? 60 : 40, Color.White));
                g.DrawPath(pen, path);
            }
            else
            {
                Color back = Theme.Blend(p.SurfaceHover, p.Text, Enabled && _down ? 0.14f : 0.07f * hover);
                using var fill = new SolidBrush(back);
                g.FillPath(fill, path);
                // Lichtkante oben (im dunklen Modus) gibt dem Knopf etwas Tiefe.
                if (Theme.Dark && !_down)
                    using (var shine = new Pen(Color.FromArgb(22, Color.White)))
                        g.DrawLine(shine, r.X + S(5), r.Y + 1, r.Right - S(5), r.Y + 1);
                // Rand: kräftiger als Kartenränder, färbt sich beim Hover zum Akzent.
                using var pen = new Pen(Enabled ? Theme.Blend(p.ControlBorder, Theme.AccentLine, 0.6f * hover) : p.Border);
                g.DrawPath(pen, path);
            }
        }
        var fore = !Enabled ? p.TextMuted : accent ? Theme.OnAccent : p.Text;
        string text = Tr.T(Text);
        int textW = text.Length > 0 ? TextRenderer.MeasureText(text, UiFonts.Body).Width : 0;
        int total = (_glyph is null ? 0 : S(16)) + (_glyph is not null && textW > 0 ? S(8) : 0) + textW + (TrailingGlyph is null ? 0 : S(18));
        int x = (Width - total) / 2;
        if (_glyph is not null)
        {
            if (!Glyph.Paint(g, _glyph, new Rectangle(x, 0, S(18), Height), fore, 10.5f))
                TextRenderer.DrawText(g, _glyph, Glyph.Font(10.5f), new Rectangle(x, 0, S(16), Height), fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += textW > 0 ? S(24) : S(16);
        }
        if (textW > 0)
        {
            TextRenderer.DrawText(g, text, UiFonts.Body, new Rectangle(x, 0, textW + S(2), Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            x += textW + S(8);
        }
        if (TrailingGlyph is not null)
            TextRenderer.DrawText(g, TrailingGlyph, Glyph.Font(8f), new Rectangle(x, 0, S(12), Height), fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
            using (var pen = new Pen(p.Text, 1.5f))
            using (var path = Theme.RoundedRect(RectangleF.Inflate(r, -S(2), -S(2)), S(4)))
                g.DrawPath(pen, path);
    }
}

/// <summary>Reiterleiste (Text mit Akzentstrich unter dem gewählten Reiter), z. B. in der Controller-Karte.</summary>
internal sealed class PivotTabs : Control, IExtraTexts, ISelfTranslating
{
    /// <summary>Größe (skaliert über UiScale) setzt das Steuerelement selbst – WinForms nur Lage überlassen.</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) =>
        base.ScaleControl(factor, specified & ~BoundsSpecified.Height);

    private readonly List<(string Text, bool Shown)> _tabs = [];
    private int _selected, _hover = -1;
    public event EventHandler? SelectedIndexChanged;

    public PivotTabs()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = UiScale.Px(40);
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
            int w = TextRenderer.MeasureText(Tr.T(_tabs[i].Text), UiFonts.Strong).Width + UiScale.Px(24);
            yield return (i, new Rectangle(x, 0, w, Height));
            x += w + UiScale.Px(4);
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
                using var hover = Theme.RoundedRect(Rectangle.Inflate(r, 0, -UiScale.Px(5)), UiScale.Px(5));
                using var brush = new SolidBrush(p.SurfaceHover);
                g.FillPath(brush, hover);
            }
            TextRenderer.DrawText(g, Tr.T(_tabs[i].Text), sel ? UiFonts.Strong : UiFonts.Body, r, sel ? p.Text : p.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (sel)
            {
                var barRect = new RectangleF(r.X + r.Width / 2f - UiScale.Px(14f), r.Bottom - UiScale.Px(4f), UiScale.Px(28f), UiScale.Px(3f));
                using var bar = Theme.RoundedRect(barRect, 1.5f);
                using var accent = Theme.LineBrush(barRect);
                g.FillPath(accent, bar);
                if (Focused && ShowFocusCues)
                    using (var pen = new Pen(p.Text))
                        g.DrawRectangle(pen, Rectangle.Inflate(r, -UiScale.Px(2), -UiScale.Px(6)));
            }
        }
    }
}

/// <summary>Eintrag der linken Navigationsleiste (Symbol und Text, gewählt mit Akzentstrich links).
/// Mit <see cref="Expandable"/> zeigt er rechts einen Pfeil und klappt Unterpunkte auf bzw. zu.</summary>
internal sealed class NavItem : Control, ISelfTranslating
{
    /// <summary>Größe (skaliert über UiScale) setzt das Steuerelement selbst – WinForms nur Lage überlassen.</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) =>
        base.ScaleControl(factor, specified & ~BoundsSpecified.Size);

    private readonly string _glyph;
    private bool _selected, _expanded;

    public NavItem(string text, string glyph, Action select)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.ResizeRedraw, true);
        Text = text;
        _glyph = glyph;
        Height = UiScale.Px(44);
        Cursor = Cursors.Hand;
        TabStop = true;
        Margin = new Padding(0, 0, 0, UiScale.Px(4));
        _hoverAnim = new Anim(this);
        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && Expandable && e.X >= Width - UiScale.Px(34))
                ExpandToggled?.Invoke();
            else
                select();
        };
        KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) select(); };
    }

    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    /// <summary>Hat Unterpunkte: Pfeil rechts, Klick darauf meldet <see cref="ExpandToggled"/>.</summary>
    public bool Expandable { get; set; }

    public bool Expanded
    {
        get => _expanded;
        set { _expanded = value; Invalidate(); }
    }

    /// <summary>Klick auf den Pfeil rechts (Auf-/Zuklappen ohne die Seite zu wechseln).</summary>
    public event Action? ExpandToggled;

    private readonly Anim _hoverAnim;

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hoverAnim.Target = 1; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hoverAnim.Target = 0; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        if (_selected || _hoverAnim.Value > 0)
        {
            using var path = Theme.RoundedRect(r, UiScale.Px(6));
            // Gewählt: Fläche mit einem Hauch Neon-Verlauf; Hover: leicht aufgehellt (blendet weich ein).
            if (_selected && Theme.Neon)
            {
                using var brush = Theme.AccentBrush(r);
                brush.LinearColors = [Theme.Blend(p.SurfaceHover, Theme.Accent, 0.16f), Theme.Blend(p.SurfaceHover, Theme.Accent2, 0.06f)];
                g.FillPath(brush, path);
            }
            else if (_selected)
            {
                // Wie die Navigation in Windows 11: dezente helle Fläche, Akzent nur in der Marke links.
                using var brush = new SolidBrush(Theme.Blend(BackColor, p.Text, Theme.Dark ? 0.06f : 0.045f));
                g.FillPath(brush, path);
            }
            else
            {
                using var brush = new SolidBrush(Theme.Blend(BackColor, p.Text, 0.05f * _hoverAnim.Value));
                g.FillPath(brush, path);
            }
        }
        if (_selected)
        {
            var pillRect = new RectangleF(0, Height / 2f - UiScale.Px(10f), UiScale.Px(3f), UiScale.Px(20f));
            using var pill = Theme.RoundedRect(pillRect, 1.5f);
            using var accent = Theme.LineBrush(pillRect, 90);
            g.FillPath(accent, pill);
        }
        var glyphColor = _selected && Theme.Neon ? Theme.Blend(Theme.AccentLine, p.Text, Theme.Dark ? 0.2f : 0.1f) : p.Text;
        if (!Glyph.Paint(g, _glyph, new Rectangle(UiScale.Px(12), 0, UiScale.Px(24), Height), glyphColor, 12f))
            TextRenderer.DrawText(g, _glyph, Glyph.Font(12f), new Rectangle(UiScale.Px(12), 0, UiScale.Px(24), Height), glyphColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Tr.T(Text), UiFonts.Body, new Rectangle(UiScale.Px(44), 0, Width - UiScale.Px(56), Height), p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (Expandable)
            TextRenderer.DrawText(g, _expanded ? Glyph.ChevronUp : Glyph.ChevronDown, Glyph.Font(9f),
                new Rectangle(Width - UiScale.Px(32), 0, UiScale.Px(24), Height), p.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
            using (var pen = new Pen(p.Text, 1.5f))
            using (var path = Theme.RoundedRect(RectangleF.Inflate(r, -1, -1), 5))
                g.DrawPath(pen, path);
    }
}

/// <summary>Eingerückter Unterpunkt in der linken Navigation (verbundene Controller, Abschnitte einer Seite).
/// Ohne Aktion ist er deaktiviert gezeichnet (z. B. „Kein Controller verbunden“).</summary>
internal sealed class NavSubItem : Control, ISelfTranslating
{
    /// <summary>Größe (skaliert über UiScale) setzt das Steuerelement selbst – WinForms nur Lage überlassen.</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) =>
        base.ScaleControl(factor, specified & ~BoundsSpecified.Size);

    private bool _hover, _selected;

    public NavSubItem(string text, Action? select)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        Text = text;
        Height = UiScale.Px(32);
        Enabled = select is not null;
        Cursor = select is null ? Cursors.Default : Cursors.Hand;
        TabStop = select is not null;
        Margin = new Padding(0, 0, 0, UiScale.Px(2));
        if (select is not null)
        {
            Click += (_, _) => select();
            KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) select(); };
        }
    }

    /// <summary>Gewählter Unterpunkt: Akzentmarke links, gleiche Füllung wie <see cref="NavItem.Selected"/>.</summary>
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_selected || (_hover && Enabled))
        {
            using var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 5);
            using var brush = new SolidBrush(Theme.Blend(BackColor, p.Text, _selected ? (Theme.Dark ? 0.06f : 0.045f) : 0.035f));
            g.FillPath(brush, path);
        }
        if (_selected)
        {
            var pillRect = new RectangleF(UiScale.Px(24f), Height / 2f - UiScale.Px(8f), UiScale.Px(3f), UiScale.Px(16f));
            using var pill = Theme.RoundedRect(pillRect, 1.5f);
            using var accent = Theme.LineBrush(pillRect, 90);
            g.FillPath(accent, pill);
        }
        TextRenderer.DrawText(g, Tr.T(Text), UiFonts.Small, new Rectangle(UiScale.Px(48), 0, Width - UiScale.Px(62), Height),
            Enabled ? p.Text : p.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>
/// Eingabefeld im Windows-11-Stil: abgerundeter Rahmen, Text senkrecht mittig mit Innenabstand, Akzentlinie unten bei
/// Fokus. Umhüllt ein randloses <see cref="TextBox"/> (Text, Platzhalter, Ereignisse bleiben dort).
/// </summary>
internal sealed class TextField : Panel
{
    /// <summary>Größe (skaliert über UiScale) setzt das Steuerelement selbst – WinForms nur Lage überlassen.</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) =>
        base.ScaleControl(factor, specified & ~BoundsSpecified.Height);

    public TextBox Box { get; }

    public TextField(TextBox box, int? width = null)
    {
        Box = box;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        Size = new Size(width ?? box.Width, UiScale.Px(32));
        Location = box.Location;
        box.BorderStyle = BorderStyle.None;
        box.Font = UiFonts.Body;
        Controls.Add(box);
        box.GotFocus += (_, _) => Invalidate();
        box.LostFocus += (_, _) => Invalidate();
        box.EnabledChanged += (_, _) => { Enabled = box.Enabled; Invalidate(); };
        Visible = box.Visible; // danach das Feld ein-/ausblenden, nicht das TextBox
        box.Visible = true;
        Cursor = Cursors.IBeam;
        Click += (_, _) => box.Focus();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        Box.SetBounds(UiScale.Px(10), (Height - Box.PreferredHeight) / 2 + 1, Math.Max(10, Width - UiScale.Px(20)), Box.PreferredHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(Parent?.BackColor ?? Theme.Backdrop);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Box.BackColor = Enabled ? p.SurfaceHover : p.Surface;
        using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 5))
        {
            using var fill = new SolidBrush(Box.BackColor);
            g.FillPath(fill, path);
            using var pen = new Pen(Box.Focused ? Theme.Blend(p.ControlBorder, Theme.AccentLine, 0.85f) : p.ControlBorder);
            g.DrawPath(pen, path);
        }
        if (Box.Focused)
        {
            using var brush = Theme.LineBrush(new RectangleF(4, Height - 3, Width - 9, 3));
            using var accent = new Pen(brush, 2f);
            g.DrawLine(accent, 4, Height - 1.5f, Width - 5, Height - 1.5f);
        }
    }
}

/// <summary>Seite mit senkrechtem Bildlauf; Inhalt (StackPanel) in voller Breite bis zu einer Höchstbreite.</summary>
internal sealed class ScrollPage : Panel
{
    public StackPanel Content { get; } = new() { Spacing = 0, Padding = new Padding(0, 0, 0, UiScale.Px(24)) };
    public int MaxContentWidth { get; set; } = 1240;

    public ScrollPage()
    {
        AutoScroll = true;
        BackColor = Theme.Backdrop;
        Padding = new Padding(UiScale.Px(28), UiScale.Px(20), UiScale.Px(28), 0);
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

    /// <summary>Nur die Gruppe mit dieser Überschrift zeigen (Unterpunkt in der Navigation); null = ganze Seite.</summary>
    public void ShowOnly(string? section)
    {
        // Zugehörigkeit vorberechnen – eine Überschrift startet eine Gruppe, alles danach gehört dazu.
        var show = new Dictionary<Control, bool>();
        bool inSection = section is null, found = section is null;
        foreach (Control c in Content.Controls)
        {
            if (c is Heading { IsPage: true })
            {
                show[c] = true; // Seitentitel bleibt immer sichtbar
                continue;
            }
            if (c is Heading h)
            {
                inSection = section is null || h.Text == section;
                found |= h.Text == section;
            }
            show[c] = inSection;
        }
        if (!found)
        {
            ShowOnly(null);
            return;
        }
        Content.SetShownAll(c => !show.TryGetValue(c, out bool s) || s);
        if (section is not null)
            AutoScrollPosition = Point.Empty;
    }
}
