using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Switch2Pro.Bridge;

/// <summary>
/// Darstellung im Stil von Windows 11: dunkel (Standard), hell oder wie Windows; Akzentfarbe von Windows;
/// Mica-Hintergrund (durchscheinend) ab Windows 11. Farben aller Fenster, Karten und Grafiken kommen von hier.
/// </summary>
internal static class Theme
{
    public sealed record Palette(
        bool Dark,
        Color Window,       // Fensterhintergrund (ohne Mica)
        Color Surface,      // Karten/Abschnitte
        Color SurfaceHover, // Eingabefelder, Knöpfe
        Color Border,
        Color Text,
        Color TextMuted,
        Color Canvas);      // Hintergrund der Controller-Grafik

    public static readonly Palette DarkPalette = new(true,
        Color.FromArgb(0x20, 0x20, 0x20), Color.FromArgb(0x2B, 0x2B, 0x2B), Color.FromArgb(0x37, 0x37, 0x37),
        Color.FromArgb(0x45, 0x45, 0x45), Color.FromArgb(0xF3, 0xF3, 0xF3), Color.FromArgb(0xA8, 0xA8, 0xAE),
        Color.FromArgb(0x24, 0x24, 0x28));

    public static readonly Palette LightPalette = new(false,
        Color.FromArgb(0xF3, 0xF3, 0xF3), Color.FromArgb(0xFB, 0xFB, 0xFB), Color.FromArgb(0xFF, 0xFF, 0xFF),
        Color.FromArgb(0xE0, 0xE0, 0xE0), Color.FromArgb(0x1B, 0x1B, 0x1B), Color.FromArgb(0x60, 0x60, 0x66),
        Color.FromArgb(0xEC, 0xEC, 0xF0));

    /// <summary>Farbe, die durch Mica ersetzt wird (Farbschlüssel) – kommt sonst nirgends vor.</summary>
    public static readonly Color MicaKey = Color.FromArgb(1, 2, 3);

    public static Palette Current { get; private set; } = DarkPalette;
    public static bool Dark => Current.Dark;
    public static Color Accent { get; private set; } = Color.FromArgb(0, 120, 212);
    /// <summary>Mica aktiv (Einstellung an und Windows 11).</summary>
    public static bool Mica { get; private set; }

    /// <summary>Darstellung festlegen: "dark" (Standard), "light" oder "system"; Mica nur ab Windows 11.</summary>
    public static void Init(string? mode, bool transparency)
    {
        bool dark = mode switch
        {
            "light" => false,
            "system" => !WindowsUsesLightApps(),
            _ => true,
        };
        Current = dark ? DarkPalette : LightPalette;
        Accent = WindowsAccent() ?? Color.FromArgb(0, 120, 212);
        Mica = transparency && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
    }

    private static bool WindowsUsesLightApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Akzentfarbe von Windows (DWM, ABGR); im dunklen Modus etwas aufgehellt wie in Windows.</summary>
    private static Color? WindowsAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is not int abgr)
                return null;
            var c = Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
            return Current.Dark ? Blend(c, Color.White, 0.25f) : c;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>
    /// Kantenglättung für Formen am Mica-Hintergrund: Mit Farbschlüssel entstünden sonst dunkle Säume an den Rändern
    /// (Mischfarben aus Fläche und Schlüsselfarbe).
    /// </summary>
    public static SmoothingMode EdgeSmoothing => Mica ? SmoothingMode.None : SmoothingMode.AntiAlias;

    /// <summary>Hintergrund von Flächen, durch die Mica scheinen soll (sonst die normale Fensterfarbe).</summary>
    public static Color Backdrop => Mica ? MicaKey : Current.Window;

    // ---------- Fensterrahmen ----------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subApp, string? idList);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_SYSTEMBACKDROP_TYPE = 38;

    /// <summary>
    /// Fenster in die Windows-Darstellung einbinden: Titelleiste hell/dunkel, abgerundete Ecken, Mica-Hintergrund,
    /// Farben aller Steuerelemente. Mica scheint durch Flächen in <see cref="MicaKey"/> (Farbschlüssel).
    /// </summary>
    public static void Apply(Form form)
    {
        form.BackColor = Backdrop;
        if (Mica)
            form.TransparencyKey = MicaKey;
        form.ForeColor = Current.Text;
        void Frame()
        {
            int dark = Dark ? 1 : 0;
            DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
            int round = 2; // abgerundet
            DwmSetWindowAttribute(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
            if (Mica)
            {
                int backdrop = 2; // Mica
                DwmSetWindowAttribute(form.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4);
            }
        }
        if (form.IsHandleCreated) Frame(); else form.HandleCreated += (_, _) => Frame();
        ApplyControls(form);
    }

    /// <summary>Farben auf alle Steuerelemente (rekursiv); auch für später erzeugte aufrufen.</summary>
    public static void ApplyControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            Style(c);
            ApplyControls(c);
        }
    }

    private static void Style(Control c)
    {
        var p = Current;
        switch (c)
        {
            case Button b:
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = p.SurfaceHover;
                b.ForeColor = p.Text;
                b.FlatAppearance.BorderColor = p.Border;
                b.FlatAppearance.MouseOverBackColor = Blend(p.SurfaceHover, p.Text, 0.08f);
                b.FlatAppearance.MouseDownBackColor = Blend(p.SurfaceHover, p.Text, 0.14f);
                break;
            case TextBox t:
                t.BackColor = p.SurfaceHover;
                t.ForeColor = p.Text;
                t.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox combo:
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = p.SurfaceHover;
                combo.ForeColor = p.Text;
                // Selbst zeichnen: das Auswahlfeld einer DropDownList bleibt sonst weiß.
                combo.DrawMode = DrawMode.OwnerDrawFixed;
                combo.DrawItem -= DrawComboItem;
                combo.DrawItem += DrawComboItem;
                if (Dark)
                {
                    // Natives dunkles Klappfeld von Windows (auch der Pfeil); die Liste nutzt die dunkle Bildlaufleiste.
                    combo.FlatStyle = FlatStyle.Standard;
                    void SetCfd() => SetWindowTheme(combo.Handle, "DarkMode_CFD", null);
                    if (combo.IsHandleCreated) SetCfd(); else combo.HandleCreated += (_, _) => SetCfd();
                    break;
                }
                DarkScroll(combo);
                break;
            case TrackBar track:
                track.BackColor = Parent(track);
                break;
            case CheckBox or RadioButton:
                c.ForeColor = p.Text;
                c.BackColor = Color.Transparent;
                break;
            case Label label when label.ForeColor == SystemColors.GrayText || Equals(label.Tag, MutedTag):
                label.ForeColor = p.TextMuted;
                label.Tag = MutedTag;
                break;
            case Label label:
                if (label.ForeColor == SystemColors.ControlText || label.ForeColor == Color.Empty || label.ForeColor == DarkPalette.Text
                    || label.ForeColor == LightPalette.Text)
                    label.ForeColor = p.Text;
                break;
            case ScrollableControl scroll when scroll.AutoScroll:
                DarkScroll(scroll);
                break;
        }
    }

    private static void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo)
            return;
        bool editField = (e.State & DrawItemState.ComboBoxEdit) != 0;
        bool selected = !editField && (e.State & DrawItemState.Selected) != 0;
        var back = selected ? Accent : combo.Enabled ? Current.SurfaceHover : Current.Surface;
        var fore = selected ? Color.White : combo.Enabled ? Current.Text : Current.TextMuted;
        using (var brush = new SolidBrush(back))
            e.Graphics.FillRectangle(brush, e.Bounds);
        if (e.Index >= 0)
            TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), combo.Font, e.Bounds, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    public const string MutedTag = "muted";

    /// <summary>Hintergrund des nächsten Elternelements mit eigener Farbe (für TrackBar, die keine Transparenz kann).</summary>
    private static Color Parent(Control c)
    {
        for (var p = c.Parent; p is not null; p = p.Parent)
            if (p.BackColor != Color.Transparent && p.BackColor != MicaKey)
                return p.BackColor;
        return Current.Window;
    }

    /// <summary>Dunkle Bildlaufleisten (Windows-eigenes Design „DarkMode_Explorer“).</summary>
    public static void DarkScroll(Control c)
    {
        void Set() => SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null);
        if (c.IsHandleCreated) Set(); else c.HandleCreated += (_, _) => Set();
    }

    // ---------- Zeichenhilfen ----------

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Menü im Infobereich in den Farben der Darstellung.</summary>
    public static ToolStripRenderer MenuRenderer() => new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = true };

    private sealed class MenuColors : ProfessionalColorTable
    {
        private static Palette P => Current;
        public override Color ToolStripDropDownBackground => P.Surface;
        public override Color ImageMarginGradientBegin => P.Surface;
        public override Color ImageMarginGradientMiddle => P.Surface;
        public override Color ImageMarginGradientEnd => P.Surface;
        public override Color MenuBorder => P.Border;
        public override Color MenuItemBorder => P.Border;
        public override Color MenuItemSelected => P.SurfaceHover;
        public override Color MenuItemSelectedGradientBegin => P.SurfaceHover;
        public override Color MenuItemSelectedGradientEnd => P.SurfaceHover;
        public override Color MenuItemPressedGradientBegin => P.SurfaceHover;
        public override Color MenuItemPressedGradientEnd => P.SurfaceHover;
        public override Color SeparatorDark => P.Border;
        public override Color SeparatorLight => P.Border;
        public override Color CheckBackground => Blend(P.SurfaceHover, Accent, 0.3f);
        public override Color CheckSelectedBackground => Blend(P.SurfaceHover, Accent, 0.4f);
        public override Color CheckPressedBackground => Blend(P.SurfaceHover, Accent, 0.4f);
    }
}

/// <summary>Eintrag der Navigation oben (wie in Windows 11): Text, die aktive Seite mit Akzent-Unterstrich.</summary>
internal sealed class NavButton : Control
{
    private bool _selected, _hover;

    public NavButton(string text, Action click)
    {
        Text = Tr.T(text);
        Font = new Font("Segoe UI Semibold", 10.5f);
        Size = new Size(TextRenderer.MeasureText(Text, Font).Width + 36, 38);
        Margin = new Padding(0, 0, 6, 0);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        BackColor = Theme.Backdrop;
        Click += (_, _) => click();
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
    }

    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = Theme.EdgeSmoothing;
        var p = Theme.Current;
        if (_selected || _hover)
        {
            using var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 6);
            using var fill = new SolidBrush(_selected ? p.Surface : Theme.Blend(p.Window, p.Surface, 0.6f));
            g.FillPath(fill, path);
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height - 4), _selected ? p.Text : p.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (_selected)
        {
            using var accent = new SolidBrush(Theme.Accent);
            using var bar = Theme.RoundedRect(new RectangleF(Width / 2f - 12, Height - 5, 24, 3), 1.5f);
            g.FillPath(accent, bar);
        }
    }
}

/// <summary>
/// Abschnitt im Stil von Windows 11 (wie die Kacheln in den Windows-Einstellungen): abgerundete Fläche mit dünnem
/// Rand und Überschrift. Ersetzt die altmodische GroupBox.
/// </summary>
internal sealed class Section : Panel
{
    private readonly Label _title;

    public Section(string title, Control content)
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16, 40, 16, 14);
        Margin = new Padding(0, 0, 0, 10);
        BackColor = Theme.Backdrop;
        DoubleBuffered = true;
        _title = new Label
        {
            Text = title, AutoSize = true, Location = new Point(16, 12), Font = new Font("Segoe UI Semibold", 10.5f),
            BackColor = Theme.Current.Surface, ForeColor = Theme.Current.Text,
        };
        content.BackColor = Theme.Current.Surface;
        content.Location = new Point(Padding.Left, Padding.Top);
        Controls.Add(content);
        Controls.Add(_title);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = Theme.EdgeSmoothing;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundedRect(r, 8);
        using (var fill = new SolidBrush(Theme.Current.Surface))
            g.FillPath(fill, path);
        using var pen = new Pen(Theme.Current.Border, 1f);
        g.DrawPath(pen, path);
    }
}
