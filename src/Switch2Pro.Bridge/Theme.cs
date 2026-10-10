using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Switch2Pro.Bridge;

/// <summary>
/// Darstellung im Stil von Windows 11: dunkel (Standard), hell oder wie Windows; Akzentfarbe von Windows;
/// Mica in der Titelleiste ab Windows 11. Farben aller Fenster, Karten und Grafiken kommen von hier.
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

    // Farben aus dem Logo: dunkles Navy als Grund, Neon-Blau → Violett als Akzent.
    public static readonly Palette DarkPalette = new(true,
        Color.FromArgb(0x10, 0x12, 0x1C), Color.FromArgb(0x19, 0x1C, 0x2A), Color.FromArgb(0x24, 0x28, 0x3B),
        Color.FromArgb(0x30, 0x35, 0x52), Color.FromArgb(0xF2, 0xF4, 0xFA), Color.FromArgb(0xA4, 0xA9, 0xC0),
        Color.FromArgb(0x15, 0x18, 0x26));

    public static readonly Palette LightPalette = new(false,
        Color.FromArgb(0xF1, 0xF2, 0xF8), Color.FromArgb(0xFC, 0xFC, 0xFF), Color.FromArgb(0xFF, 0xFF, 0xFF),
        Color.FromArgb(0xDA, 0xDC, 0xE8), Color.FromArgb(0x16, 0x18, 0x2A), Color.FromArgb(0x46, 0x49, 0x5E),
        Color.FromArgb(0xEB, 0xEC, 0xF5));


    public static Palette Current { get; private set; } = DarkPalette;
    public static bool Dark => Current.Dark;
    public static Color Accent { get; private set; } = Color.FromArgb(0x2F, 0x8B, 0xFF);
    /// <summary>Zweite Akzentfarbe (Violett) – Ende des Neon-Verlaufs <see cref="Accent"/> → <see cref="Accent2"/>.</summary>
    public static Color Accent2 { get; private set; } = Color.FromArgb(0x9B, 0x5C, 0xFF);
    /// <summary>Text auf Akzentflächen. Beide Verlaufsfarben sind dunkel genug für Weiß.</summary>
    public static Color OnAccent => Luminance(Accent) > 0.36f ? Color.Black : Color.White;

    /// <summary>Neon-Verlauf (Blau → Violett) über ein Rechteck; <paramref name="angle"/> 0 = waagerecht, 90 = senkrecht.</summary>
    public static LinearGradientBrush AccentBrush(RectangleF r, float angle = 0, bool enabled = true)
    {
        var bounds = new RectangleF(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height));
        return enabled
            ? new LinearGradientBrush(bounds, Accent, Accent2, angle)
            : new LinearGradientBrush(bounds, Current.TextMuted, Current.TextMuted, angle);
    }

    /// <summary>Weicher Lichtschein um eine Form (mehrere breiter werdende, blasser werdende Ränder).
    /// <paramref name="strength"/> 0–1 regelt die Deckkraft (für Ein-/Ausblenden).</summary>
    public static void Glow(Graphics g, GraphicsPath path, float strength, int size = 6)
    {
        if (strength <= 0.01f)
            return;
        var bounds = path.GetBounds();
        for (int i = size; i >= 1; i--)
        {
            int alpha = (int)(strength * (Dark ? 70 : 45) * (1f - (i - 1f) / size) / 2.2f);
            if (alpha <= 0)
                continue;
            using var brush = AccentBrush(RectangleF.Inflate(bounds, i, i));
            brush.LinearColors = [Color.FromArgb(alpha, Accent), Color.FromArgb(alpha, Accent2)];
            using var pen = new Pen(brush, i * 2f) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, path);
        }
    }

    /// <summary>Relative Helligkeit (WCAG) einer Farbe, 0 = schwarz, 1 = weiß.</summary>
    public static float Luminance(Color c)
    {
        static float L(int v) { float s = v / 255f; return s <= 0.03928f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f); }
        return 0.2126f * L(c.R) + 0.7152f * L(c.G) + 0.0722f * L(c.B);
    }
    /// <summary>Mica-Titelleiste aktiv (Einstellung an und Windows 11).</summary>
    public static bool Mica { get; private set; }

    /// <summary>Darstellung festlegen: "dark" (Standard), "light" oder "system"; Mica nur ab Windows 11.
    /// Ohne App-Einstellung (null) gilt die Themawahl des Installers aus der Registry, sonst Dunkel.</summary>
    public static void Init(string? mode, bool transparency)
    {
        mode ??= InstallerTheme();
        bool dark = mode switch
        {
            "light" => false,
            "system" => !WindowsUsesLightApps(),
            _ => true,
        };
        Current = dark ? DarkPalette : LightPalette;
        // Akzent = Neon-Blau → Violett wie im Logo; im hellen Modus etwas tiefer für den Kontrast auf weißen
        // Flächen. Nicht die Windows-Akzentfarbe – die sieht je nach Nutzerwahl beliebig aus (z. B. pink).
        Accent = dark ? Color.FromArgb(0x2F, 0x8B, 0xFF) : Color.FromArgb(0x1F, 0x6F, 0xE0);
        Accent2 = dark ? Color.FromArgb(0x9B, 0x5C, 0xFF) : Color.FromArgb(0x7B, 0x3F, 0xE4);
        Mica = transparency && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
    }

    /// <summary>Themawahl des Installers (HKCU\Software\N-Connect\Theme): "dark", "light" oder "system".</summary>
    private static string? InstallerTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\N-Connect");
            return key?.GetValue("Theme") is string v && v is "dark" or "light" or "system" ? v : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
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




    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>Fensterhintergrund (hinter Karten und Abschnitten).</summary>
    public static Color Backdrop => Current.Window;

    // ---------- Fensterrahmen ----------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subApp, string? idList);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34,
        DWMWA_CAPTION_COLOR = 35, DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    /// <summary>
    /// Fenster in die Windows-Darstellung einbinden: Titelleiste hell/dunkel, abgerundete Ecken, Mica-Hintergrund,
    /// Farben aller Steuerelemente. Der Inhalt bleibt deckend: ein durchsichtiger Fensterinhalt (Farbschlüssel/Layered
    /// Window) zwingt Windows zum Neuzeichnen per CPU und macht die Live-Anzeige sehr langsam.
    /// </summary>
    public static void Apply(Form form)
    {
        form.Icon = Branding.AppIcon;
        form.BackColor = Backdrop;
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
            else if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                // Ohne Mica: Titelleiste in der Fensterfarbe (Navy), sonst steht ein graues Band über dem Inhalt.
                int caption = ColorRef(Backdrop);
                DwmSetWindowAttribute(form.Handle, DWMWA_CAPTION_COLOR, ref caption, 4);
            }
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                int border = ColorRef(Current.Border);
                DwmSetWindowAttribute(form.Handle, DWMWA_BORDER_COLOR, ref border, 4);
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
            case TextBox t when t.Parent is TextField:
                t.BackColor = t.Enabled ? p.SurfaceHover : p.Surface;
                t.ForeColor = p.Text;
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
                c.BackColor = Parent(c); // deckend: durchsichtige Steuerelemente zeichnen bei jeder Änderung ihr Elternelement mit
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
        var fore = selected ? OnAccent : combo.Enabled ? Current.Text : Current.TextMuted;
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
            if (p.BackColor != Color.Transparent)
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
    /// <summary>Tooltip in den Farben der Darstellung (der Windows-Standard ist immer hell).</summary>
    public static ToolTip CreateToolTip()
    {
        var tip = new ToolTip { OwnerDraw = true };
        tip.Draw += (_, e) =>
        {
            var p = Current;
            using (var back = new SolidBrush(p.SurfaceHover))
                e.Graphics.FillRectangle(back, e.Bounds);
            using (var border = new Pen(p.Border))
                e.Graphics.DrawRectangle(border, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, e.Bounds, p.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
        };
        return tip;
    }

    /// <summary>Meldung im Stil der Darstellung (statt der immer hellen MessageBox von Windows).</summary>
    public static DialogResult Message(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        using var form = new UiForm
        {
            Text = caption, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
            ShowInTaskbar = owner is null, StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            Font = new Font("Segoe UI", 9.5f), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AutoScaleDimensions = new SizeF(96F, 96F), AutoScaleMode = AutoScaleMode.Dpi,
            Padding = new Padding(0),
        };
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 2, Padding = new Padding(20, 20, 20, 12) };
        Icon? symbol = icon switch
        {
            MessageBoxIcon.Error => SystemIcons.Error,
            MessageBoxIcon.Warning => SystemIcons.Warning,
            MessageBoxIcon.Question => SystemIcons.Question,
            MessageBoxIcon.Information => SystemIcons.Information,
            _ => null,
        };
        if (symbol is not null)
            layout.Controls.Add(new PictureBox { Image = symbol.ToBitmap(), Size = new Size(32, 32), Margin = new Padding(0, 0, 14, 0) }, 0, 0);
        layout.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(460, 0), Margin = new Padding(0, 6, 0, 16) }, 1, 0);

        var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        (string Text, DialogResult Result)[] choices = buttons switch
        {
            MessageBoxButtons.YesNo => [("Nein", DialogResult.No), ("Ja", DialogResult.Yes)],
            MessageBoxButtons.YesNoCancel => [("Abbrechen", DialogResult.Cancel), ("Nein", DialogResult.No), ("Ja", DialogResult.Yes)],
            MessageBoxButtons.OKCancel => [("Abbrechen", DialogResult.Cancel), ("OK", DialogResult.OK)],
            _ => [("OK", DialogResult.OK)],
        };
        foreach (var (label, result) in choices)
        {
            var button = new Button { Text = Tr.T(label), DialogResult = result, MinimumSize = new Size(88, 30), AutoSize = true };
            bar.Controls.Add(button);
            if (result is DialogResult.Yes or DialogResult.OK)
                form.AcceptButton = button;
            if (result is DialogResult.Cancel or DialogResult.No)
                form.CancelButton = button;
        }
        form.CancelButton ??= (IButtonControl)bar.Controls[0];
        layout.Controls.Add(bar, 0, 1);
        layout.SetColumnSpan(bar, 2);
        form.Controls.Add(layout);
        Apply(form);
        switch (icon)
        {
            case MessageBoxIcon.Error: System.Media.SystemSounds.Hand.Play(); break;
            case MessageBoxIcon.Warning: System.Media.SystemSounds.Exclamation.Play(); break;
            case MessageBoxIcon.Question: System.Media.SystemSounds.Question.Play(); break;
            case MessageBoxIcon.Information: System.Media.SystemSounds.Asterisk.Play(); break;
        }
        return form.ShowDialog(owner);
    }

    public static ToolStripRenderer MenuRenderer() => new ThemeMenuRenderer();

    /// <summary>
    /// Menü-Renderer für beide Farbmodi: Zeichnet neben dem Hintergrund auch Text, Tastaturkürzel,
    /// Pfeile, Häkchen und Trennlinien in den Theme-Farben – sonst bleibt der Text im dunklen Modus schwarz.
    /// Gilt über den Farbtabellen-Mechanismus auch für alle Untermenüs.
    /// </summary>
    private sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer
    {
        private static Palette P => Current;

        public ThemeMenuRenderer() : base(new MenuColors())
        {
            RoundedEdges = true;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? P.Text : P.TextMuted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item!.Enabled ? P.Text : P.TextMuted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            using var back = new SolidBrush(e.Item.Selected ? ColorTable.CheckSelectedBackground : ColorTable.CheckBackground);
            g.FillRectangle(back, new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2));
            using var pen = new Pen(P.Text, Math.Max(1.5f, r.Height / 8f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            g.DrawLines(pen,
            [
                new PointF(r.X + r.Width * 0.18f, r.Y + r.Height * 0.52f),
                new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.78f),
                new PointF(r.X + r.Width * 0.86f, r.Y + r.Height * 0.22f),
            ]);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (e.Vertical || e.Item is not ToolStripSeparator)
            {
                base.OnRenderSeparator(e);
                return;
            }
            int y = e.Item.Bounds.Y + e.Item.Bounds.Height / 2;
            using var pen = new Pen(P.Border);
            e.Graphics.DrawLine(pen, e.Item.Bounds.X + 28, y, e.Item.Bounds.Right - 6, y);
        }
    }

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
        g.SmoothingMode = SmoothingMode.AntiAlias;
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
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundedRect(r, 8);
        using (var fill = new SolidBrush(Theme.Current.Surface))
            g.FillPath(fill, path);
        using var pen = new Pen(Theme.Current.Border, 1f);
        g.DrawPath(pen, path);
    }
}
