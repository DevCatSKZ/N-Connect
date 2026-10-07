using System.Drawing;
using System.Drawing.Drawing2D;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Leiste „Spieler-Reihenfolge“ über den Controller-Karten: je verbundenem Spieler ein Chip (Nummer, Name, ‹ ›).
/// Spieler 1 ist für Windows, Steam und Spiele der erste Controller – mit den Pfeilen lässt sich festlegen, welcher
/// Controller das ist. Umsortieren übernimmt <see cref="ControllerManager.MovePlayer"/> (legt die virtuellen
/// Controller in neuer Reihenfolge an und merkt sich die Plätze).
/// </summary>
internal sealed class PlayerOrderBar : Control, ISelfTranslating, IExtraTexts
{
    private const int Pad = 12, ChipH = 40, Gap = 8, Arrow = 26, CaptionGap = 8;
    private const string Caption = "Spieler-Reihenfolge – Spieler 1 ist für Windows, Steam und Spiele der erste Controller. Mit ‹ › umsortieren.";

    private readonly Action<Player, int> _move;
    private List<(Player Player, string Name)> _players = [];
    private readonly List<(Rectangle Rect, Player Player, int Target)> _hits = [];
    private Rectangle? _hover;
    private string _signature = "";

    public PlayerOrderBar(Action<Player, int> move)
    {
        _move = move;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        BackColor = Theme.Backdrop;
        TabStop = false;
    }

    public IEnumerable<string> ExtraTexts => [Caption];

    /// <summary>Spieler in Reihenfolge setzen; zeichnet nur bei Änderungen neu.</summary>
    public void SetPlayers(IReadOnlyList<Player> players, Settings settings)
    {
        var list = players.OrderBy(p => p.Index).Select(p => (p, Tr.T(p.DisplayName(settings)))).ToList();
        string signature = string.Join("|", list.Select(x => $"{x.Item1.Index}:{x.Item2}"));
        if (signature == _signature)
            return;
        _signature = signature;
        _players = list;
        UpdateHeight();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateHeight();
    }

    private int ChipWidth(string name) =>
        12 + 26 + 8 + Math.Min(430, TextRenderer.MeasureText(name, UiFonts.Body).Width) + 10 + 2 * Arrow + 8;

    /// <summary>Chips zeilenweise anordnen (umbrechen, wenn die Breite nicht reicht). Alle gleich breit:
    /// die breiteste nötige Breite gilt für alle – sieht ruhiger aus und lange Namen passen überall.</summary>
    private List<Rectangle> Arrange(int width, out int height)
    {
        var rects = new List<Rectangle>();
        int top = 12 + TextRenderer.MeasureText(Tr.T(Caption), UiFonts.Small, new Size(Math.Max(100, width - 2 * Pad), 0),
            TextFormatFlags.WordBreak).Height + CaptionGap;
        int x = Pad, y = top;
        int w = _players.Count > 0 ? _players.Max(p => ChipWidth(p.Name)) : 0;
        foreach (var _ in _players)
        {
            if (x > Pad && x + w > width - Pad)
            {
                x = Pad;
                y += ChipH + Gap;
            }
            rects.Add(new Rectangle(x, y, w, ChipH));
            x += w + Gap;
        }
        height = y + ChipH + 6;
        return rects;
    }

    private void UpdateHeight()
    {
        if (_players.Count < 2)
        {
            Visible = false;
            return;
        }
        Arrange(Width, out int h);
        Visible = true;
        if (Height != h)
            Height = h;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(Theme.Backdrop);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _hits.Clear();
        var rects = Arrange(Width, out _);
        TextRenderer.DrawText(g, Tr.T(Caption), UiFonts.Small, new Rectangle(Pad, 12, Width - 2 * Pad, rects.FirstOrDefault().Y - 12),
            p.TextMuted, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        for (int i = 0; i < _players.Count; i++)
        {
            var (player, name) = _players[i];
            var r = rects[i];
            using (var path = Theme.RoundedRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 7))
            {
                using var fill = new SolidBrush(p.Surface);
                g.FillPath(fill, path);
                using var pen = new Pen(i == 0 ? Theme.Accent : p.Border, i == 0 ? 1.5f : 1f);
                g.DrawPath(pen, path);
            }
            // Spielernummer im Kreis (Spieler 1 in Akzentfarbe)
            var circle = new Rectangle(r.X + 12, r.Y + (ChipH - 26) / 2, 26, 26);
            using (var back = new SolidBrush(i == 0 ? Theme.Accent : p.SurfaceHover))
                g.FillEllipse(back, circle);
            TextRenderer.DrawText(g, (player.Index + 1).ToString(), UiFonts.Strong, circle, i == 0 ? Theme.OnAccent : p.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int textX = circle.Right + 8, textW = r.Right - 8 - 2 * Arrow - 10 - textX;
            TextRenderer.DrawText(g, name, UiFonts.Body, new Rectangle(textX, r.Y, textW, ChipH), p.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            // ‹ › – mit dem Nachbarn tauschen
            var left = new Rectangle(r.Right - 8 - 2 * Arrow, r.Y + (ChipH - Arrow) / 2, Arrow, Arrow);
            var right = new Rectangle(r.Right - 8 - Arrow, left.Y, Arrow, Arrow);
            DrawArrow(g, left, "‹", i > 0);
            DrawArrow(g, right, "›", i < _players.Count - 1);
            if (i > 0)
                _hits.Add((left, player, _players[i - 1].Player.Index));
            if (i < _players.Count - 1)
                _hits.Add((right, player, _players[i + 1].Player.Index));
        }
    }

    private void DrawArrow(Graphics g, Rectangle r, string glyph, bool enabled)
    {
        var p = Theme.Current;
        if (enabled && _hover == r)
            using (var path = Theme.RoundedRect(r, 5))
            using (var back = new SolidBrush(p.SurfaceHover))
                g.FillPath(back, path);
        TextRenderer.DrawText(g, glyph, UiFonts.Subtitle, r, enabled ? p.Text : Theme.Blend(p.Surface, p.TextMuted, 0.4f),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Rectangle? hit = _hits.FirstOrDefault(h => h.Rect.Contains(e.Location)).Rect is { IsEmpty: false } r ? r : null;
        if (hit == _hover)
            return;
        _hover = hit;
        Cursor = hit is null ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        foreach (var (rect, player, target) in _hits)
            if (rect.Contains(e.Location))
            {
                _move(player, target);
                return;
            }
    }
}
