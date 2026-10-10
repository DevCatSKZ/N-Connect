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
        Color Window,        // Fensterhintergrund (ohne Mica)
        Color Surface,       // Karten/Abschnitte
        Color SurfaceHover,  // Eingabefelder, Knöpfe
        Color Border,        // dezenter Rand von Karten und Abschnitten
        Color Text,
        Color TextMuted,
        Color Canvas,        // Hintergrund der Controller-Grafik
        Color ControlBorder, // kräftigerer Rand von Knöpfen, Eingabe- und Auswahlfeldern
        Color Accent,        // Verlaufsanfang – Flächen mit weißer Schrift (Kontrast ≥ 4,5 : 1)
        Color Accent2,       // Verlaufsende
        Color Pressed);      // gedrückte Tasten in der Controller-Grafik (leuchtend)

    /// <summary>Farbschema: Name und je eine Palette für dunkel und hell. <paramref name="Neon"/>: Verläufe, Leuchten und
    /// aufgehellte Linien; ohne (Windows) einfarbiger Akzent und ruhige Flächen wie in Windows 11.</summary>
    public sealed record Scheme(string Id, string Name, Palette DarkPalette, Palette LightPalette, bool Neon = true);

    private static Color C(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    private static Palette P(bool dark, int window, int surface, int control, int border, int text, int muted, int canvas,
        int controlBorder, int accent, int accent2, int pressed) =>
        new(dark, C(window), C(surface), C(control), C(border), C(text), C(muted), C(canvas), C(controlBorder),
            C(accent), C(accent2), C(pressed));

    /// <summary>
    /// Alle Farbschemata (das erste ist der Standard). Neon-Schemata: Akzente tief genug für weiße Schrift, Linien und
    /// Leuchten im dunklen Modus aufgehellt (<see cref="AccentLine"/>).
    /// </summary>
    public static readonly Scheme[] Schemes =
    [
        // Windows (Standard): Farben von Windows 11 (Fluent) – Hintergrund #202020, Karten #2B2B2B, Steuerelemente #373737,
        // Akzent im Windows-Standardblau (dunkel #4CC2FF mit schwarzer Schrift wie in Windows, hell #005FB8 mit weißer).
        new("windows", "Windows",
            P(true, 0x202020, 0x2B2B2B, 0x373737, 0x1D1D1D, 0xFFFFFF, 0xC5C5C5, 0x272727, 0x4A4A4A, 0x4CC2FF, 0x4CC2FF, 0x4CC2FF),
            P(false, 0xF3F3F3, 0xFBFBFB, 0xFFFFFF, 0xE5E5E5, 0x1B1B1B, 0x5C5C5C, 0xEEEEEE, 0xC8C8C8, 0x005FB8, 0x005FB8, 0x0078D4),
            Neon: false),
        // Neon: Farben aus dem Logo – Navy, Neon-Blau → Violett.
        new("neon", "Neon",
            P(true, 0x0E1019, 0x171A27, 0x222639, 0x2A2F45, 0xF2F4FA, 0xA0A6BE, 0x131623, 0x4A5378, 0x2B6FF2, 0x7C3AED, 0x00BEFF),
            P(false, 0xF0F2F8, 0xFCFCFF, 0xFFFFFF, 0xDCDFEA, 0x15172A, 0x4A4E64, 0xEAECF5, 0xA3AAC2, 0x1F5FD6, 0x7444E0, 0x0094E0)),
        // Aurora: Polarlicht – tiefes Blaugrün, Türkis → Grün.
        new("aurora", "Aurora",
            P(true, 0x0A1314, 0x111C1D, 0x1A292A, 0x223435, 0xEEF6F5, 0x9CB3B1, 0x0E1919, 0x416363, 0x0E7490, 0x15803D, 0x2EE6A8),
            P(false, 0xEEF5F4, 0xFBFEFD, 0xFFFFFF, 0xD4E3E1, 0x122322, 0x455A58, 0xE6EFED, 0x99B7B3, 0x0E7490, 0x15803D, 0x0BA67A)),
        // Sunset: Abendrot – warmes Pflaume, Orange → Pink.
        new("sunset", "Sunset",
            P(true, 0x140E13, 0x1E151B, 0x2A1E26, 0x352731, 0xFAF1F4, 0xBBA5AF, 0x1A1217, 0x5F4658, 0xC2410C, 0xBE185D, 0xFF8A3D),
            P(false, 0xF8F2F2, 0xFFFCFB, 0xFFFFFF, 0xEADBDC, 0x2A1519, 0x5E494E, 0xF2E8E8, 0xC2A6AA, 0xC2410C, 0xBE185D, 0xF26B1D)),
        // Joy-Con: Neonrot → Neonblau wie die Joy-Con, neutrales Anthrazit.
        new("joycon", "Joy-Con",
            P(true, 0x111113, 0x1A1A1E, 0x25252A, 0x2F2F35, 0xF4F4F6, 0xA8A8B2, 0x161619, 0x585862, 0xD7263D, 0x0A6FC2, 0xFF4B5C),
            P(false, 0xF2F2F4, 0xFDFDFE, 0xFFFFFF, 0xDEDEE3, 0x18181C, 0x4C4C55, 0xECECF0, 0xA8A8B3, 0xD7263D, 0x0A6FC2, 0xE8364A)),
    ];

    /// <summary>Paletten des Standardschemas (z. B. zum Erkennen unveränderter Label-Farben).</summary>
    public static Palette DarkPalette => Schemes[0].DarkPalette;
    public static Palette LightPalette => Schemes[0].LightPalette;

    public static Palette Current { get; private set; } = Schemes[0].DarkPalette;
    public static Scheme ActiveScheme { get; private set; } = Schemes[0];
    public static bool Dark => Current.Dark;
    public static Color Accent => Current.Accent;
    /// <summary>Zweite Akzentfarbe – Ende des Verlaufs <see cref="Accent"/> → <see cref="Accent2"/>.</summary>
    public static Color Accent2 => Current.Accent2;
    /// <summary>Text auf Akzentflächen: schwarz auf hellem Akzent (Windows dunkel), sonst weiß.</summary>
    public static Color OnAccent => Luminance(Accent) > 0.36f ? Color.Black : Color.White;
    /// <summary>Gedrückte Tasten in der Controller-Grafik.</summary>
    public static Color Pressed => Current.Pressed;
    /// <summary>Neon-Effekte (Verläufe, Leuchten) aktiv? Im Windows-Schema aus.</summary>
    public static bool Neon => ActiveScheme.Neon;

    /// <summary>Akzent für Linien, Symbole und Leuchten: bei Neon im dunklen Modus aufgehellt (sonst zu dunkel auf dem
    /// Grund); das Windows-Blau ist dunkel schon hell genug.</summary>
    public static Color AccentLine => Dark && Neon ? Blend(Accent, Color.White, 0.28f) : Accent;
    public static Color Accent2Line => Dark && Neon ? Blend(Accent2, Color.White, 0.28f) : Accent2;

    /// <summary>Trennlinie zwischen Zeilen in einer Karte (wie DividerStroke in Windows: leicht heller als die Fläche).</summary>
    public static Color Divider => Blend(Current.Surface, Current.Text, Dark ? 0.08f : 0.07f);

    /// <summary>Verlauf für Linien (Reiterstrich, Navigationsmarke, Fokuslinie, leuchtende Ränder).</summary>
    public static LinearGradientBrush LineBrush(RectangleF r, float angle = 0)
    {
        var brush = AccentBrush(r, angle);
        brush.LinearColors = [AccentLine, Accent2Line];
        return brush;
    }

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
        if (strength <= 0.01f || !Neon) // Windows-Schema: kein Leuchten
            return;
        var bounds = path.GetBounds();
        for (int i = size; i >= 1; i--)
        {
            int alpha = (int)(strength * (Dark ? 70 : 45) * (1f - (i - 1f) / size) / 2.2f);
            if (alpha <= 0)
                continue;
            using var brush = AccentBrush(RectangleF.Inflate(bounds, i, i));
            brush.LinearColors = [Color.FromArgb(alpha, AccentLine), Color.FromArgb(alpha, Accent2Line)];
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

    /// <summary>Schema zu einer Kennung ("neon", "aurora" …); unbekannt oder null = Neon.</summary>
    public static Scheme SchemeById(string? id) => Schemes.FirstOrDefault(s => s.Id == id) ?? Schemes[0];

    /// <summary>Darstellung festlegen: "dark" (Standard), "light" oder "system"; Farbschema (null = Neon); Mica nur ab
    /// Windows 11. Ohne App-Einstellung (null) gilt die Themawahl des Installers aus der Registry, sonst Dunkel.
    /// Nicht die Windows-Akzentfarbe – die sieht je nach Nutzerwahl beliebig aus (z. B. pink).</summary>
    public static void Init(string? mode, bool transparency, string? scheme = null)
    {
        mode ??= InstallerTheme();
        bool dark = mode switch
        {
            "light" => false,
            "system" => !WindowsUsesLightApps(),
            _ => true,
        };
        ActiveScheme = SchemeById(scheme);
        Current = dark ? ActiveScheme.DarkPalette : ActiveScheme.LightPalette;
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

    /// <summary>Rahmenloses Fenster (z. B. Einblendung) mit abgerundeten Ecken und passender Titelleistenfarbe (Windows 11).</summary>
    public static void ApplyRoundCorners(Form form)
    {
        if (!form.IsHandleCreated)
            return;
        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
        int dark = Dark ? 1 : 0;
        DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
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
                b.FlatAppearance.BorderColor = p.ControlBorder;
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
                    // Natives dunkles Klappfeld als Grundlage (dunkle Liste und Bildlaufleiste) …
                    combo.FlatStyle = FlatStyle.Standard;
                    void SetCfd() => SetWindowTheme(combo.Handle, "DarkMode_CFD", null);
                    if (combo.IsHandleCreated) SetCfd(); else combo.HandleCreated += (_, _) => SetCfd();
                }
                else
                {
                    DarkScroll(combo);
                }
                // … darüber zeichnet ComboSkin das geschlossene Feld in den Theme-Farben (sonst grau von Windows).
                if (combo.DropDownStyle == ComboBoxStyle.DropDownList && !Skins.TryGetValue(combo, out _))
                    Skins.Add(combo, new ComboSkin(combo));
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
                if (label.ForeColor == SystemColors.ControlText || label.ForeColor == Color.Empty
                    || Schemes.Any(s => label.ForeColor == s.DarkPalette.Text || label.ForeColor == s.LightPalette.Text))
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

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox, ComboSkin> Skins = new();

    /// <summary>
    /// Zeichnet das geschlossene Auswahlfeld (DropDownList) selbst – wie ein Eingabefeld: abgerundet, Theme-Fläche,
    /// kräftiger Rand, Chevron rechts, Akzentrand bei Fokus oder Hover. Windows zeichnet zuerst (Liste, Tastatur, Fokus
    /// bleiben nativ), danach wird nach jedem WM_PAINT übermalt.
    /// </summary>
    private sealed class ComboSkin : NativeWindow
    {
        private const int WM_PAINT = 0x000F, WM_NCPAINT = 0x0085, WM_PRINT = 0x0317, WM_PRINTCLIENT = 0x0318;

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        private readonly ComboBox _combo;
        private bool _hover;

        public ComboSkin(ComboBox combo)
        {
            _combo = combo;
            if (combo.IsHandleCreated)
                AssignHandle(combo.Handle);
            combo.HandleCreated += (_, _) =>
            {
                AssignHandle(combo.Handle);
                FitWidth();
            };
            combo.HandleDestroyed += (_, _) => ReleaseHandle();
            combo.MouseEnter += (_, _) => { _hover = true; combo.Invalidate(); };
            combo.MouseLeave += (_, _) => { _hover = false; combo.Invalidate(); };
            combo.GotFocus += (_, _) => combo.Invalidate();
            combo.LostFocus += (_, _) => combo.Invalidate();
            combo.DropDownClosed += (_, _) => combo.Invalidate();
            combo.EnabledChanged += (_, _) => combo.Invalidate();
        }

        /// <summary>
        /// Breite an den längsten (übersetzten) Eintrag anpassen, damit z. B. „Automatisch (wie Windows)“ oder lange
        /// Übersetzungen nicht abgekürzt werden; nie schmaler als vorgesehen, höchstens 420 px (skaliert).
        /// Die aufgeklappte Liste wird mindestens so breit wie ihr längster Eintrag.
        /// </summary>
        private void FitWidth()
        {
            if (_combo.Items.Count == 0)
                return;
            int text = _combo.Items.Cast<object>().Max(i => TextRenderer.MeasureText(_combo.GetItemText(i), _combo.Font).Width);
            // Das Feld selbst nur verbreitern, wenn es allein rechts in einer Einstellungszeile steht – neben Knöpfen
            // (Profilzeile) oder in festen Layouts (Tastenbelegung, Dialoge) würde es sonst Nachbarn überdecken.
            if (_combo.Parent is SettingRow row && row.Content == _combo && !Equals(_combo.Tag, Tr.UserData))
            {
                int wanted = Math.Min(UiScale.Px(420), text + UiScale.Px(44));
                if (wanted > _combo.Width)
                    _combo.Width = wanted;
            }
            // Die aufgeklappte Liste darf immer so breit wie ihr längster Eintrag werden (überdeckt nichts dauerhaft).
            _combo.DropDownWidth = Math.Max(_combo.Width, Math.Min(UiScale.Px(520), text + UiScale.Px(24)));
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (_combo.IsDisposed)
                return;
            if (m.Msg is WM_PAINT or WM_NCPAINT)
            {
                // Über das ganze Fenster zeichnen (auch den Rahmen außerhalb des Innenbereichs, den Windows hell malt).
                IntPtr hdc = GetWindowDC(_combo.Handle);
                if (hdc == IntPtr.Zero)
                    return;
                try
                {
                    using var g = Graphics.FromHdc(hdc);
                    Paint(g);
                }
                finally
                {
                    ReleaseDC(_combo.Handle, hdc);
                }
            }
            else if (m.Msg is WM_PRINT or WM_PRINTCLIENT && m.WParam != IntPtr.Zero)
            {
                // DrawToBitmap (Prüfhilfe) zeichnet über WM_PRINT in einen fremden Gerätekontext.
                using var g = Graphics.FromHdc(m.WParam);
                Paint(g);
            }
        }

        private void Paint(Graphics g)
        {
            var p = Current;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var client = new Rectangle(Point.Empty, _combo.Size); // ganzes Fenster inkl. Rahmen
            // Hintergrund an den runden Ecken: Farbe des Elternelements – zeichnet es sich selbst (SettingRow) und meldet
            // nur die Windows-Standardfarbe, ist es die Kartenfläche.
            var parent = Parent(_combo);
            if (parent == SystemColors.Control || parent.IsEmpty)
                parent = p.Surface;
            using (var back = new SolidBrush(parent))
                g.FillRectangle(back, client);
            var r = new RectangleF(0.5f, 0.5f, client.Width - 1.5f, client.Height - 1.5f);
            bool active = _combo.Enabled && (_combo.Focused || _combo.DroppedDown || _hover);
            using (var path = RoundedRect(r, 5))
            {
                using var fill = new SolidBrush(_combo.Enabled ? Blend(p.SurfaceHover, p.Text, _hover ? 0.05f : 0f) : p.Surface);
                g.FillPath(fill, path);
                using var pen = new Pen(active ? Blend(p.ControlBorder, AccentLine, _combo.Focused || _combo.DroppedDown ? 0.85f : 0.45f)
                    : p.ControlBorder);
                g.DrawPath(pen, path);
            }
            var fore = _combo.Enabled ? p.Text : p.TextMuted;
            string text = (_combo.SelectedIndex >= 0 ? _combo.GetItemText(_combo.SelectedItem) : _combo.Text) ?? "";
            TextRenderer.DrawText(g, text, _combo.Font, new Rectangle(8, 0, client.Width - 36, client.Height), fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Glyph.ChevronDown, Glyph.Font(8f), new Rectangle(client.Width - 28, 0, 22, client.Height),
                _combo.Enabled ? p.TextMuted : p.ControlBorder,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

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
            Font = UiFonts.Body, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AutoScaleDimensions = UiScale.Dimensions, AutoScaleMode = AutoScaleMode.None,
            Padding = new Padding(0),
        };
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 2, Padding = new Padding(UiScale.Px(20), UiScale.Px(20), UiScale.Px(20), UiScale.Px(12)) };
        Icon? symbol = icon switch
        {
            MessageBoxIcon.Error => SystemIcons.Error,
            MessageBoxIcon.Warning => SystemIcons.Warning,
            MessageBoxIcon.Question => SystemIcons.Question,
            MessageBoxIcon.Information => SystemIcons.Information,
            _ => null,
        };
        if (symbol is not null)
            layout.Controls.Add(new PictureBox { Image = symbol.ToBitmap(), Size = new Size(UiScale.Px(32), UiScale.Px(32)), Margin = new Padding(0, 0, UiScale.Px(14), 0) }, 0, 0);
        layout.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(UiScale.Px(460), 0), Margin = new Padding(0, UiScale.Px(6), 0, UiScale.Px(16)) }, 1, 0);

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
            var button = new Button { Text = Tr.T(label), DialogResult = result, MinimumSize = new Size(UiScale.Px(88), UiScale.Px(30)), AutoSize = true };
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
        if (!UiForm.Offscreen) // Prüfhilfe: keine Töne
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
        Margin = new Padding(0, 0, UiScale.Px(6), 0);
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
        Padding = new Padding(UiScale.Px(16), UiScale.Px(40), UiScale.Px(16), UiScale.Px(14));
        Margin = new Padding(0, 0, 0, UiScale.Px(10));
        BackColor = Theme.Backdrop;
        DoubleBuffered = true;
        _title = new Label
        {
            Text = title, AutoSize = true, Location = new Point(UiScale.Px(16), UiScale.Px(12)), Font = UiFonts.Strong,
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
