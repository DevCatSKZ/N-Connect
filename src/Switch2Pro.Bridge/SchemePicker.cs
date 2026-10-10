using System.Drawing;
using System.Drawing.Drawing2D;

namespace Switch2Pro.Bridge;

/// <summary>
/// Auswahl des Farbschemas als Vorschau-Kacheln: je Schema ein Mini-Fenster (Fläche, Karte, Knopf im Verlauf) im
/// aktuellen Farbmodus, darunter der Name. Gewählt: Rand im Verlauf des Schemas und Häkchen. Pfeiltasten wechseln.
/// </summary>
internal sealed class SchemePicker : Control, ISelfTranslating
{
    // Maße in der aktuellen Skalierung (die Vorschau wächst mit, Schrift ebenso).
    private static int S(int v) => UiScale.Px(v);
    private static float S(float v) => UiScale.Px(v);
    private static int TileW => S(100);
    private static int TileH => Math.Max(S(74), TextRenderer.MeasureText("Ag", UiFonts.Small).Height + S(56));
    private static int Gap => S(8);
    private int _selected, _hover = -1;
    private readonly Anim[] _hoverAnim;
    public event EventHandler? SelectedIndexChanged;

    public SchemePicker()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Size = new Size(Theme.Schemes.Length * (TileW + Gap) - Gap + S(4), TileH + S(4));
        _hoverAnim = Theme.Schemes.Select(_ => new Anim(this)).ToArray();
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value == _selected || value < 0 || value >= Theme.Schemes.Length)
                return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Kennung des gewählten Schemas ("neon", "aurora" …).</summary>
    public string SelectedId => Theme.Schemes[_selected].Id;

    /// <summary>Größe setzt die Auswahl selbst (skaliert) – WinForms nur die Lage überlassen.</summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) =>
        base.ScaleControl(factor, specified & ~BoundsSpecified.Size);

    private Rectangle Tile(int i) => new(S(2) + i * (TileW + Gap), S(2), TileW, TileH);

    private int HitTest(Point pt)
    {
        for (int i = 0; i < Theme.Schemes.Length; i++)
            if (Tile(i).Contains(pt))
                return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = HitTest(e.Location);
        if (hit == _hover)
            return;
        if (_hover >= 0)
            _hoverAnim[_hover].Target = 0;
        _hover = hit;
        if (hit >= 0)
            _hoverAnim[hit].Target = 1;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover >= 0)
            _hoverAnim[_hover].Target = 0;
        _hover = -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (HitTest(e.Location) is var hit and >= 0)
            SelectedIndex = hit;
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, _selected - 1);
        if (e.KeyCode == Keys.Right) SelectedIndex = Math.Min(Theme.Schemes.Length - 1, _selected + 1);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var current = Theme.Current;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 0; i < Theme.Schemes.Length; i++)
        {
            var scheme = Theme.Schemes[i];
            var p = Theme.Dark ? scheme.DarkPalette : scheme.LightPalette;
            var r = Tile(i);
            bool selected = i == _selected;
            float lift = _hoverAnim[i].Value;
            var outer = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            using var path = Theme.RoundedRect(outer, S(8f));
            // Vorschau: Fenstergrund, darin eine Karte mit Textzeilen und ein Knopf im Verlauf des Schemas.
            using (var back = new SolidBrush(p.Window))
                g.FillPath(back, path);
            var card = new RectangleF(r.X + S(8f), r.Y + S(8f), r.Width - S(16f), S(34f));
            using (var cardPath = Theme.RoundedRect(card, S(5f)))
            {
                using var fill = new SolidBrush(p.Surface);
                g.FillPath(fill, cardPath);
                using var pen = new Pen(p.Border);
                g.DrawPath(pen, cardPath);
            }
            using (var line = new SolidBrush(p.Text))
                g.FillRectangle(line, card.X + S(8f), card.Y + S(9f), S(42f), S(4f));
            using (var line = new SolidBrush(p.TextMuted))
                g.FillRectangle(line, card.X + S(8f), card.Y + S(19f), S(60f), S(3f));
            var button = new RectangleF(card.Right - S(30f), card.Y + S(9f), S(22f), S(12f));
            using (var buttonPath = Theme.RoundedRect(button, S(6f)))
            using (var gradient = new LinearGradientBrush(button, p.Accent, p.Accent2, 0f))
                g.FillPath(gradient, buttonPath);
            // Name unten auf dem Fenstergrund.
            TextRenderer.DrawText(g, scheme.Name, UiFonts.Small, new Rectangle(r.X + S(8), (int)card.Bottom, r.Width - S(30), r.Bottom - (int)card.Bottom), p.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            // Farbpunkt „gedrückt“ rechts neben dem Namen – zeigt, wie die Controller-Grafik leuchtet.
            using (var dot = new SolidBrush(p.Pressed))
                g.FillEllipse(dot, r.Right - S(20f), card.Bottom + (r.Bottom - card.Bottom - S(9f)) / 2, S(9f), S(9f));
            // Rand: gewählt im Verlauf (2 px), sonst dezent; Hover hebt ihn an.
            if (selected)
            {
                var accent1 = Theme.Dark ? Theme.Blend(p.Accent, Color.White, 0.28f) : p.Accent;
                var accent2 = Theme.Dark ? Theme.Blend(p.Accent2, Color.White, 0.28f) : p.Accent2;
                using var brush = new LinearGradientBrush(outer, accent1, accent2, 30f);
                using var pen = new Pen(brush, S(2f));
                g.DrawPath(pen, path);
                DrawCheck(g, new RectangleF(r.Right - S(21f), r.Y + S(3f), S(16f), S(16f)), accent1);
            }
            else
            {
                using var pen = new Pen(Theme.Blend(current.ControlBorder, current.Text, 0.35f * lift), 1f + lift * 0.5f);
                g.DrawPath(pen, path);
            }
            if (selected && Focused && ShowFocusCues)
                using (var focus = new Pen(current.Text, 1f) { DashStyle = DashStyle.Dot })
                using (var focusPath = Theme.RoundedRect(RectangleF.Inflate(outer, -S(3f), -S(3f)), S(6f)))
                    g.DrawPath(focus, focusPath);
        }
    }

    /// <summary>Häkchen im Kreis (gewähltes Schema).</summary>
    private static void DrawCheck(Graphics g, RectangleF r, Color color)
    {
        using (var disc = new SolidBrush(color))
            g.FillEllipse(disc, r);
        using var pen = new Pen(Color.White, r.Width / 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen,
        [
            new PointF(r.X + r.Width * 0.27f, r.Y + r.Height * 0.52f),
            new PointF(r.X + r.Width * 0.44f, r.Y + r.Height * 0.68f),
            new PointF(r.X + r.Width * 0.74f, r.Y + r.Height * 0.34f),
        ]);
    }
}
