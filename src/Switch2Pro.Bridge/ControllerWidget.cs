using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Switch2Pro.Bridge.Links;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Desktop-Widget (<see cref="Settings.DesktopWidget"/>, Standard aus): kleines Kärtchen im aktuellen Farbschema mit
/// allen verbundenen Controllern – Spieler, Name, Verbindung, Ausgabe und Akku. Mit der Maus verschiebbar (Position wird
/// gemerkt), Rechtsklick für Optionen, Doppelklick öffnet die Einstellungen. Nimmt keinen Fokus und erscheint nicht in
/// der Taskleiste; „Immer im Vordergrund“ hält es über allen Fenstern.
/// </summary>
internal sealed class ControllerWidget : Form
{
    private readonly ControllerManager? _manager;
    private readonly Func<Settings> _settings;
    private readonly Action _saved;
    private readonly Action _openSettings;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly ContextMenuStrip _menu = new();
    private List<Row> _rows = [];
    private string _signature = "";

    /// <summary>Eine Zeile im Widget: was zu einem Spieler angezeigt wird.</summary>
    private readonly record struct Row(int Player, string Name, string Detail, int? Battery, bool Charging);

    public ControllerWidget(ControllerManager? manager, Func<Settings> settings, Action saved, Action openSettings)
    {
        _manager = manager;
        _settings = settings;
        _saved = saved;
        _openSettings = openSettings;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        Text = "N-Connect Widget";
        _timer.Tick += (_, _) => UpdateContent(force: false);
        _menu.Opening += (_, _) => BuildMenu();
        ContextMenuStrip = _menu;
        UpdateContent(force: true);
        PlaceInitially();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE: keine Taskleiste, kein Alt+Tab, nimmt Spielen und Fenstern nicht den Fokus.
            cp.ExStyle |= 0x00000080 | 0x08000000;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyRoundCorners(this);
        _timer.Start();
    }

    private static int S(int v) => UiScale.Px(v);
    private static int RowH(bool compact) => compact ? S(34) : Math.Max(S(52), TextRenderer.MeasureText("Ag", UiFonts.Body).Height + TextRenderer.MeasureText("Ag", UiFonts.Small).Height + S(16));
    private static int HeaderH => Math.Max(S(44), TextRenderer.MeasureText("Ag", UiFonts.Strong).Height + S(20));

    /// <summary>Daten, Farbschema und Optionen übernehmen; zeichnet nur neu, wenn sich etwas geändert hat.</summary>
    public void UpdateContent(bool force)
    {
        var settings = _settings();
        var players = _manager?.Players ?? [];
        _rows = players.OrderBy(p => p.Index).Select(p => Describe(p, settings)).ToList();
        string profile = Profile(settings) ?? "";
        string signature = string.Join("|", _rows) + $"|{profile}|{Theme.ActiveScheme.Id}|{Theme.Dark}|{settings.WidgetCompact}|{Tr.Lang}";
        if (TopMost != settings.WidgetOnTop)
            TopMost = settings.WidgetOnTop;
        double opacity = settings.WidgetOpacity / 100.0;
        if (Math.Abs(Opacity - opacity) > 0.001)
            Opacity = opacity;
        if (!force && signature == _signature)
            return;
        _signature = signature;
        BackColor = Theme.Current.Surface;
        int width = S(settings.WidgetCompact ? 300 : 350);
        int rows = Math.Max(1, _rows.Count);
        int height = HeaderH + rows * RowH(settings.WidgetCompact && _rows.Count > 0) + S(_rows.Count == 0 ? 30 : 10);
        if (Size != new Size(width, height))
            Size = new Size(width, height);
        if (IsHandleCreated)
            Theme.ApplyRoundCorners(this);
        Invalidate();
    }

    private static string? Profile(Settings settings) => settings.ForcedProfile ?? settings.DetectedProfile;

    private static Row Describe(Player player, Settings settings)
    {
        var links = player.Links;
        // Leerster Akku des Spielers (Joy-Con-Paar: der schwächere); Controller ohne Akkuanzeige liefern null.
        var states = links.Select(l => l.LastState).Where(s => s is { BatteryPercent: >= 0 }).ToList();
        int? battery = states.Count > 0 ? Math.Min(100, states.Min(s => s!.BatteryPercent)) : null;
        bool charging = states.Any(s => s!.Charging);
        string via = links.FirstOrDefault()?.Transport switch
        {
            Transport.BluetoothLE => "Bluetooth LE",
            Transport.Bluetooth => "Bluetooth",
            Transport.Usb => "USB",
            Transport.XInput => "Xbox",
            _ => "",
        };
        string output = links.Any(l => l.Native)
            ? Tr.T("nativ")
            : player.Output == OutputMode.DualShock4 ? "DualShock 4" : "Xbox 360";
        return new Row(player.Index + 1, Tr.T(player.DisplayName(settings)), via.Length > 0 ? $"{via}  ·  {output}" : output, battery, charging);
    }

    /// <summary>Gemerkte Position, falls sie noch auf einem Bildschirm liegt – sonst oben rechts unter der Einblendung.</summary>
    private void PlaceInitially()
    {
        var settings = _settings();
        if (settings.WidgetX is int x && settings.WidgetY is int y)
        {
            var bounds = new Rectangle(x, y, Width, Height);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(Rectangle.Inflate(bounds, -S(20), -S(20)))))
            {
                Location = new Point(x, y);
                return;
            }
        }
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(area.Right - Width - S(16), area.Top + S(110));
    }

    private void BuildMenu()
    {
        var settings = _settings();
        _menu.Items.Clear();
        _menu.Renderer = Theme.MenuRenderer();
        _menu.ForeColor = Theme.Current.Text;
        _menu.Font = UiFonts.Body;
        ToolStripMenuItem Item(string text, bool check, Action action)
        {
            var item = new ToolStripMenuItem(Tr.T(text)) { Checked = check };
            item.Click += (_, _) => { action(); UpdateContent(force: true); };
            return item;
        }
        _menu.Items.Add(Item("N-Connect öffnen", false, _openSettings));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("Immer im Vordergrund", settings.WidgetOnTop, () => { settings.WidgetOnTop = !settings.WidgetOnTop; _saved(); }));
        _menu.Items.Add(Item("Kompakte Ansicht", settings.WidgetCompact, () => { settings.WidgetCompact = !settings.WidgetCompact; _saved(); }));
        var opacity = new ToolStripMenuItem(Tr.T("Deckkraft"));
        foreach (int value in new[] { 100, 90, 80, 70, 60 })
            opacity.DropDownItems.Add(Item($"{value} %", settings.WidgetOpacity == value, () => { settings.WidgetOpacity = value; _saved(); }));
        _menu.Items.Add(opacity);
        _menu.Items.Add(Item("Position zurücksetzen", false, () =>
        {
            settings.WidgetX = settings.WidgetY = null;
            PlaceInitially();
            _saved();
        }));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("Widget ausblenden", false, () => { settings.DesktopWidget = false; _saved(); }));
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        if (e.Clicks >= 2)
        {
            _openSettings();
            return;
        }
        // Verschieben wie an einer Titelleiste; kehrt zurück, sobald die Maus losgelassen wird.
        var before = Location;
        ReleaseCapture();
        SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, 2 /* HTCAPTION */, IntPtr.Zero);
        if (Location != before)
        {
            var settings = _settings();
            settings.WidgetX = Left;
            settings.WidgetY = Top;
            _saved();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        var settings = _settings();
        bool compact = settings.WidgetCompact;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(p.Surface);

        // Rahmen; in den Neon-Schemata ein Akzentverlauf oben wie bei den Karten.
        using (var pen = new Pen(Theme.Neon ? Theme.Blend(p.Surface, Theme.AccentLine, 0.55f) : p.ControlBorder))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        if (Theme.Neon)
            using (var line = Theme.LineBrush(new RectangleF(0, 0, Width, S(2))))
                g.FillRectangle(line, 0, 0, Width, S(2));

        // Kopf: Symbol im Akzentkreis, „N-Connect“, rechts Anzahl der Controller bzw. aktives Profil.
        int header = HeaderH;
        var icon = new RectangleF(S(14), (header - S(26)) / 2f, S(26), S(26));
        using (var disc = Theme.AccentBrush(icon, 45))
        {
            disc.LinearColors = [Theme.Blend(p.Surface, Theme.Accent, 0.30f), Theme.Blend(p.Surface, Theme.Accent2, 0.30f)];
            g.FillEllipse(disc, icon);
        }
        var glyphColor = Theme.Dark ? Theme.Blend(Theme.Accent, Color.White, 0.45f) : Theme.Accent;
        TextRenderer.DrawText(g, Glyph.Gamepad, Glyph.Font(9.5f), Rectangle.Round(icon), glyphColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        int titleX = (int)icon.Right + S(10);
        string status = Profile(settings) is { } profile
            ? $"{Tr.T("Profil")}: {profile}"
            : $"{_rows.Count} / {ControllerManager.MaxPlayers}";
        int statusW = Math.Min(Width / 2, TextRenderer.MeasureText(status, UiFonts.Small).Width + S(4));
        TextRenderer.DrawText(g, "N-Connect", UiFonts.Strong, new Rectangle(titleX, 0, Width - titleX - statusW - S(20), header), p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, status, UiFonts.Small, new Rectangle(Width - statusW - S(14), 0, statusW, header), p.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using (var divider = new Pen(Theme.Divider))
            g.DrawLine(divider, S(12), header - 1, Width - S(12), header - 1);

        if (_rows.Count == 0)
        {
            var box = new Rectangle(S(14), header + S(6), Width - S(28), RowH(false) + S(18));
            int lineH = TextRenderer.MeasureText("Ag", UiFonts.Body).Height;
            TextRenderer.DrawText(g, Tr.T("Kein Controller verbunden"), UiFonts.Body,
                new Rectangle(box.X, box.Y + box.Height / 2 - lineH, box.Width, lineH), p.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Tr.T("Controller einschalten oder SYNC drücken"), UiFonts.Small,
                new Rectangle(box.X, box.Y + box.Height / 2 + S(2), box.Width, lineH), p.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            return;
        }

        int y = header + S(4);
        int rowH = RowH(compact);
        foreach (var row in _rows)
        {
            // Spielernummer im Verlaufskreis
            int badgeD = compact ? S(22) : S(30);
            var badge = new RectangleF(S(14), y + (rowH - badgeD) / 2f, badgeD, badgeD);
            using (var fill = Theme.AccentBrush(badge, 45))
                g.FillEllipse(fill, badge);
            TextRenderer.DrawText(g, row.Player.ToString(), compact ? UiFonts.Small : UiFonts.Strong, Rectangle.Round(badge), Theme.OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // Akku rechts: Symbol, Prozent, Blitz beim Laden
            int batteryW = S(row.Battery is null ? 0 : 78);
            if (row.Battery is int percent)
                DrawBattery(g, new Rectangle(Width - batteryW - S(10), y, batteryW, rowH), percent, row.Charging);

            int textX = (int)badge.Right + S(12);
            int textW = Width - textX - batteryW - S(16);
            if (compact)
            {
                TextRenderer.DrawText(g, row.Name, UiFonts.Body, new Rectangle(textX, y, textW, rowH), p.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            else
            {
                int nameH = TextRenderer.MeasureText("Ag", UiFonts.Body).Height;
                int detailH = TextRenderer.MeasureText("Ag", UiFonts.Small).Height;
                int top = y + (rowH - nameH - detailH) / 2;
                TextRenderer.DrawText(g, row.Name, UiFonts.Body, new Rectangle(textX, top, textW, nameH), p.Text,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, row.Detail, UiFonts.Small, new Rectangle(textX, top + nameH, textW, detailH), p.TextMuted,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            y += rowH;
        }
    }

    private static void DrawBattery(Graphics g, Rectangle area, int percent, bool charging)
    {
        var p = Theme.Current;
        var frame = new RectangleF(area.X, area.Y + area.Height / 2f - S(7), S(26), S(14));
        using (var path = Theme.RoundedRect(frame, S(3)))
        using (var pen = new Pen(p.TextMuted, Math.Max(1.2f, UiScale.Px(1.3f))))
            g.DrawPath(pen, path);
        using (var tip = new SolidBrush(p.TextMuted))
            g.FillRectangle(tip, frame.Right + S(1), frame.Y + S(4), S(2), frame.Height - S(8));
        using (var fill = new SolidBrush(StatusBand.BatteryColor(percent)))
            g.FillRectangle(fill, frame.X + S(2), frame.Y + S(2), Math.Max(S(2), (frame.Width - S(4)) * percent / 100f), frame.Height - S(4));
        if (charging)
            TextRenderer.DrawText(g, Glyph.Flash, Glyph.Font(7f), Rectangle.Round(frame), p.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, $"{percent} %", UiFonts.Small, new Rectangle((int)frame.Right + S(6), area.Y, area.Right - (int)frame.Right - S(6), area.Height),
            p.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
