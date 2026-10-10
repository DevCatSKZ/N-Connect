using System.Drawing;
using System.Drawing.Drawing2D;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Statusleiste oben auf der Controller-Seite: auf einen Blick, wie viele Controller verbunden sind, ob Bluetooth bereit
/// ist, welcher Akku am leersten ist, wie Spiele die Controller sehen und ob die Originale versteckt sind. Rechts der
/// Umschalter „Groß | Kompakt“ für die Karten.
/// </summary>
internal sealed class StatusBand : Control, ISelfTranslating, IExtraTexts
{
    /// <summary>Eine Kachel: Symbol im Neon-Kreis, Titel (klein, gedämpft), Wert; <see cref="Level"/> färbt den Wert
    /// (grün/gelb/rot), <see cref="Battery"/> zeichnet statt des Symbols einen Akku mit diesem Füllstand.</summary>
    public readonly record struct Tile(string Glyph, string Title, string Value, Color? Level = null, int? Battery = null);

    // Maße in der aktuellen Skalierung; die Kachelhöhe richtet sich nach der tatsächlichen Schrifthöhe.
    private static int S(int v) => UiScale.Px(v);
    private static int TitleH => TextRenderer.MeasureText("Ag", UiFonts.Small).Height;
    private static int ValueH => TextRenderer.MeasureText("Ag", UiFonts.Strong).Height;
    private static int TileH => Math.Max(S(56), TitleH + ValueH + S(14));
    private static int Gap => S(10);
    private static int Circle => S(32);
    private static int TopPad => S(6);
    private IReadOnlyList<Tile> _tiles = [];
    private string _signature = "";
    public Segmented View { get; } = new("Groß", "Kompakt");

    public StatusBand()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        Height = TileH + S(12);
        BackColor = Theme.Backdrop;
        View.BackColor = Theme.Backdrop;
        Controls.Add(View);
    }

    /// <summary>Höhe setzt die Leiste selbst (skaliert, nach Schrifthöhe) – WinForms nur Lage und Breite überlassen.</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) =>
        base.ScaleControl(factor, specified & ~BoundsSpecified.Height);

    public IEnumerable<string> ExtraTexts => _tiles.SelectMany(t => new[] { Tr.T(t.Title), Tr.T(t.Value) });

    /// <summary>Kacheln setzen; zeichnet nur neu, wenn sich etwas geändert hat.</summary>
    public void SetTiles(IReadOnlyList<Tile> tiles)
    {
        string signature = string.Join("|", tiles);
        if (signature == _signature)
            return;
        _signature = signature;
        _tiles = tiles;
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        View.Location = new Point(Width - S(12) - View.Width, TopPad + (TileH - View.Height) / 2);
    }

    private int TileWidth(Tile t)
    {
        int text = Math.Max(TextRenderer.MeasureText(Tr.T(t.Title), UiFonts.Small).Width,
            TextRenderer.MeasureText(Tr.T(t.Value), UiFonts.Strong).Width);
        return S(14) + Circle + S(12) + Math.Min(S(260), text) + S(16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int x = S(12), right = View.Left - Gap;
        int textTop = TopPad + (TileH - TitleH - ValueH) / 2;
        foreach (var tile in _tiles)
        {
            int w = TileWidth(tile);
            if (x + w > right)
                break; // schmales Fenster: hintere Kacheln weglassen statt zu quetschen
            var r = new RectangleF(x + 0.5f, TopPad + 0.5f, w - 1, TileH - 1);
            using (var path = Theme.RoundedRect(r, S(8)))
            {
                using var fill = new SolidBrush(p.Surface);
                g.FillPath(fill, path);
                using var pen = new Pen(p.Border);
                g.DrawPath(pen, path);
            }
            var circle = new RectangleF(x + S(14), TopPad + (TileH - Circle) / 2f, Circle, Circle);
            using (var disc = Theme.AccentBrush(circle, 45))
            {
                // Dezenter Kreis: Verlauf stark mit der Fläche gemischt, Symbol in kräftiger Akzentfarbe.
                disc.LinearColors = [Theme.Blend(p.Surface, Theme.Accent, 0.28f), Theme.Blend(p.Surface, Theme.Accent2, 0.28f)];
                g.FillEllipse(disc, circle);
            }
            var glyphColor = Theme.Dark ? Theme.Blend(Theme.Accent, Color.White, 0.45f) : Theme.Accent;
            if (tile.Battery is { } level)
                DrawBattery(g, circle, level, tile.Level ?? glyphColor);
            else if (!Glyph.Paint(g, tile.Glyph, Rectangle.Round(circle), glyphColor, 11f))
                TextRenderer.DrawText(g, tile.Glyph, Glyph.Font(11f), Rectangle.Round(circle), glyphColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int textX = (int)circle.Right + S(12), textW = x + w - S(12) - textX;
            TextRenderer.DrawText(g, Tr.T(tile.Title), UiFonts.Small, new Rectangle(textX, textTop, textW, TitleH), p.TextMuted,
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            var valueColor = tile.Level is { } c && tile.Battery is null ? c : p.Text;
            TextRenderer.DrawText(g, Tr.T(tile.Value), UiFonts.Strong, new Rectangle(textX, textTop + TitleH, textW, ValueH), valueColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            x += w + Gap;
        }
    }

    /// <summary>Kleiner Akku mitten im Kreis, gefüllt nach Ladestand.</summary>
    private static void DrawBattery(Graphics g, RectangleF circle, int percent, Color color)
    {
        float k = circle.Width / 32f; // Maße relativ zum (skalierten) Kreis
        var frame = new RectangleF(circle.X + 7 * k, circle.Y + circle.Height / 2 - 5.5f * k, 16 * k, 11 * k);
        using (var path = Theme.RoundedRect(frame, 2.5f * k))
        using (var pen = new Pen(Theme.Current.Text, 1.2f * k))
            g.DrawPath(pen, path);
        using (var tip = new SolidBrush(Theme.Current.Text))
            g.FillRectangle(tip, frame.Right + 0.8f * k, frame.Y + 3.5f * k, 1.8f * k, 4 * k);
        using var fill = new SolidBrush(color);
        g.FillRectangle(fill, frame.X + 2 * k, frame.Y + 2 * k, Math.Max(1.5f * k, (frame.Width - 4 * k) * Math.Clamp(percent, 0, 100) / 100f), frame.Height - 4 * k);
    }

    /// <summary>Farbe eines Akkustands (wie in den Karten): rot unter 15 %, gelb unter 35 %, sonst grün.</summary>
    public static Color BatteryColor(int percent) =>
        percent < 15 ? Color.FromArgb(235, 80, 70) : percent < 35 ? Color.FromArgb(240, 180, 40) : Color.FromArgb(70, 200, 110);

    /// <summary>Kacheln aus dem aktuellen Zustand.</summary>
    public static List<Tile> Build(ControllerManager? manager, IReadOnlyList<Player> players, Settings settings)
    {
        var good = Color.FromArgb(70, 200, 110);
        var bad = Color.FromArgb(235, 80, 70);
        var tiles = new List<Tile>
        {
            new(Glyph.Gamepad, "Verbunden", $"{players.Count} / {ControllerManager.MaxPlayers}"),
            manager is null
                ? new(Glyph.Bluetooth, "Bluetooth", "–")
                : manager.AdapterProblem is null
                    ? new(Glyph.Bluetooth, "Bluetooth", "bereit", good)
                    : new(Glyph.Bluetooth, "Bluetooth", "nicht verfügbar", bad),
        };
        // Leerster Akku (Controller ohne Akkuanzeige zählen nicht).
        var lowest = players
            .SelectMany(p => p.Links.Select(l => (Player: p, State: l.LastState)))
            .Where(x => x.State is { BatteryPercent: >= 0 })
            .OrderBy(x => x.State!.BatteryPercent)
            .FirstOrDefault();
        if (lowest.State is { } st)
        {
            int percent = Math.Min(100, st.BatteryPercent);
            string value = $"{percent} %  ·  {Tr.T($"Spieler {lowest.Player.Index + 1}")}" + (st.Charging ? $"  ·  {Tr.T("lädt")}" : "");
            tiles.Add(new(Glyph.Gauge, "Niedrigster Akku", value, BatteryColor(percent), percent));
        }
        else
        {
            tiles.Add(new(Glyph.Gauge, "Niedrigster Akku", "–"));
        }
        var outputs = players.Where(p => !p.Links.Any(l => l.Native)).Select(p => p.Output).Distinct().ToList();
        var output = outputs.Count switch
        {
            0 => settings.OutputMode,
            1 => outputs[0],
            _ => (OutputMode?)null,
        };
        tiles.Add(new(Glyph.Layers, "Ausgabe", output switch
        {
            OutputMode.DualShock4 => "DualShock 4",
            OutputMode.Xbox360 => "Xbox 360",
            _ => "gemischt",
        }));
        tiles.Add(!HidHideInstalled
            ? new(Glyph.Eye, "Original-Controller", "HidHide fehlt", Color.FromArgb(240, 180, 40))
            : new(Glyph.Eye, "Original-Controller", settings.HideFromGames ? "versteckt" : "sichtbar"));
        return tiles;
    }

    private static bool _hidHide;
    private static long _hidHideChecked = -1;

    /// <summary>HidHide installiert? Liest Registry und Dateisystem – darum nur alle 5 s, die Leiste fragt ~60-mal pro Sekunde.</summary>
    private static bool HidHideInstalled
    {
        get
        {
            long now = Environment.TickCount64;
            if (_hidHideChecked < 0 || now - _hidHideChecked > 5000)
            {
                _hidHide = HidHide.IsInstalled;
                _hidHideChecked = now;
            }
            return _hidHide;
        }
    }
}
