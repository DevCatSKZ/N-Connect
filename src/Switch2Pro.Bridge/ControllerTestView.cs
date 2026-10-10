using System.Drawing;
using System.Drawing.Drawing2D;
using Switch2Pro.Bridge.Links;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Testseite einer Controller-Karte: beide Sticks als Kreis mit Live-Punkt und Rundheitslinie (Stick einmal ganz im
/// Kreis drehen), Trigger als Balken, Gyro und Beschleunigung, gedrückte Tasten und Berichtsrate. Hilft bei Drift,
/// Gebrauchtkauf und Fehlersuche – misst nur, ändert nichts.
/// </summary>
internal sealed class ControllerTestView : Control, ISelfTranslating, IExtraTexts
{
    private const int Buckets = 48;
    private readonly float[] _leftMax = new float[Buckets], _rightMax = new float[Buckets];
    private PadInput? _input;
    private IReadOnlyList<IControllerLink> _links = [];
    private long _lastPaint;

    public ControllerTestView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        Height = UiScale.Px(300);
    }

    public IEnumerable<string> ExtraTexts => new[] { "Linker Stick", "Rechter Stick", "Trigger", "Bewegung", "Rundheit", "Gedrückt" }.Select(Tr.T);

    /// <summary>Neue Eingaben (ca. 60-mal pro Sekunde aus der Karte).</summary>
    public void Show(PadInput? input, IReadOnlyList<IControllerLink> links)
    {
        _input = input;
        _links = links;
        if (input is not null)
        {
            Track(_leftMax, input.LeftX, input.LeftY);
            Track(_rightMax, input.RightX, input.RightY);
        }
        long now = Environment.TickCount64;
        if (now - _lastPaint >= 16)
        {
            _lastPaint = now;
            Invalidate();
        }
    }

    /// <summary>Rundheitsmessung neu beginnen.</summary>
    public void ResetCircles()
    {
        Array.Clear(_leftMax);
        Array.Clear(_rightMax);
        Invalidate();
    }

    private static void Track(float[] max, float x, float y)
    {
        float r = MathF.Sqrt(x * x + y * y);
        if (r < 0.5f)
            return; // nur am Rand messen
        int bucket = (int)((MathF.Atan2(y, x) + MathF.PI) / (2 * MathF.PI) * Buckets) % Buckets;
        if (r > max[bucket])
            max[bucket] = Math.Min(r, 1.5f);
    }

    /// <summary>Abweichung vom Kreis in Prozent (erst, wenn der Stick einmal fast ganz herumgedreht wurde).</summary>
    private static float? Roundness(float[] max)
    {
        var filled = max.Where(v => v > 0).ToArray();
        if (filled.Length < Buckets * 3 / 4)
            return null;
        float mean = filled.Average();
        return (filled.Max() - filled.Min()) / mean * 50f; // ± Hälfte der Spanne
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        static int S(int v) => UiScale.Px(v);
        int pad = S(16), stick = Math.Min(S(150), (Width - 2 * pad) / 5);
        int top = S(34);
        DrawStick(g, new Rectangle(pad, top, stick, stick), "Linker Stick", _input?.LeftX ?? 0, _input?.LeftY ?? 0, _leftMax);
        int x = pad + stick + S(24);
        DrawStick(g, new Rectangle(x, top, stick, stick), "Rechter Stick", _input?.RightX ?? 0, _input?.RightY ?? 0, _rightMax);
        x += stick + S(28);

        // Trigger
        Label(g, "Trigger", new Point(x, S(8)));
        float lt = _input?.LeftTrigger ?? (_input?.Buttons.HasFlag(ProButtons.ZL) == true ? 1 : 0);
        float rt = _input?.RightTrigger ?? (_input?.Buttons.HasFlag(ProButtons.ZR) == true ? 1 : 0);
        DrawBar(g, new Rectangle(x, top, S(26), stick), lt, "L");
        DrawBar(g, new Rectangle(x + S(40), top, S(26), stick), rt, "R");
        x += S(96);

        // Bewegung: Gyro und Beschleunigung als Balken um die Mitte
        Label(g, "Bewegung", new Point(x, S(8)));
        int barW = Math.Max(S(80), Width - x - pad);
        if (_input?.Motion is { } m)
        {
            (string Name, short Value, float Range)[] axes =
            [
                ("Gyro X", m.GyroX, 8000), ("Gyro Y", m.GyroY, 8000), ("Gyro Z", m.GyroZ, 8000),
                ("Accel X", m.AccelX, 8000), ("Accel Y", m.AccelY, 8000), ("Accel Z", m.AccelZ, 8000),
            ];
            int rowH = stick / axes.Length;
            for (int i = 0; i < axes.Length; i++)
                DrawCentered(g, new Rectangle(x, top + i * rowH, barW, rowH), axes[i].Name, axes[i].Value, axes[i].Range);
        }
        else
        {
            TextRenderer.DrawText(g, Tr.T("Bewegungssensor: keine Daten"), UiFonts.Small, new Point(x, top), p.TextMuted, TextFormatFlags.NoPrefix);
        }

        // Unten: gedrückte Tasten und Berichtsrate
        int y = top + stick + S(20);
        var pressed = _input is null ? "–" : string.Join("  ", Enum.GetValues<ProButtons>()
            .Where(b => b != ProButtons.None && _input.Buttons.HasFlag(b)).Select(b => b.ToString()));
        TextRenderer.DrawText(g, $"{Tr.T("Gedrückt")}: {(pressed.Length == 0 ? "–" : pressed)}", UiFonts.Body,
            new Rectangle(pad, y, Width - 2 * pad, S(24)), p.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        string rates = string.Join("   ", _links.Select(l => $"{l.Kind.DisplayName()}: {l.ReportRate:F0} {Tr.T("Berichte/s")}"));
        TextRenderer.DrawText(g, rates, UiFonts.Small, new Rectangle(pad, y + S(26), Width - 2 * pad, S(22)), p.TextMuted,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void Label(Graphics g, string text, Point at) =>
        TextRenderer.DrawText(g, Tr.T(text), UiFonts.Strong, at, Theme.Current.Text, TextFormatFlags.NoPrefix);

    private void DrawStick(Graphics g, Rectangle r, string title, float x, float y, float[] max)
    {
        var p = Theme.Current;
        Label(g, title, new Point(r.X, UiScale.Px(8)));
        using (var back = new SolidBrush(p.SurfaceHover))
            g.FillEllipse(back, r);
        using (var ring = new Pen(p.ControlBorder, 1.2f))
        {
            g.DrawEllipse(ring, r);
            g.DrawLine(ring, r.X, r.Y + r.Height / 2f, r.Right, r.Y + r.Height / 2f);
            g.DrawLine(ring, r.X + r.Width / 2f, r.Y, r.X + r.Width / 2f, r.Bottom);
        }
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, rad = r.Width / 2f - UiScale.Px(4);
        // Rundheitslinie: größter gemessener Ausschlag je Richtung
        var points = new List<PointF>();
        for (int i = 0; i < Buckets; i++)
            if (max[i] > 0)
            {
                float a = (i + 0.5f) / Buckets * 2 * MathF.PI - MathF.PI;
                points.Add(new PointF(cx + MathF.Cos(a) * max[i] * rad, cy - MathF.Sin(a) * max[i] * rad));
            }
        if (points.Count >= 3)
            using (var pen = new Pen(Color.FromArgb(200, Theme.AccentLine), 1.6f))
                g.DrawPolygon(pen, points.ToArray());
        // Live-Punkt
        float px = cx + Math.Clamp(x, -1.2f, 1.2f) * rad, py = cy - Math.Clamp(y, -1.2f, 1.2f) * rad;
        float d = UiScale.Px(12f);
        using (var dot = new SolidBrush(Theme.Pressed))
            g.FillEllipse(dot, px - d / 2, py - d / 2, d, d);
        string values = $"X {x:+0.00;-0.00} · Y {y:+0.00;-0.00}";
        if (Roundness(max) is { } dev)
            values += $"  ·  {Tr.T("Rundheit")} ±{dev:0} %";
        TextRenderer.DrawText(g, values, UiFonts.Small, new Rectangle(r.X - UiScale.Px(20), r.Bottom + UiScale.Px(2), r.Width + UiScale.Px(60), UiScale.Px(18)),
            p.TextMuted, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    private static void DrawBar(Graphics g, Rectangle r, float value, string label)
    {
        var p = Theme.Current;
        using (var path = Theme.RoundedRect(r, UiScale.Px(6)))
        using (var back = new SolidBrush(p.SurfaceHover))
            g.FillPath(back, path);
        float h = r.Height * Math.Clamp(value, 0f, 1f);
        if (h >= 1)
            using (var fill = Theme.AccentBrush(new RectangleF(r.X, r.Bottom - h, r.Width, h), 90))
            using (var path = Theme.RoundedRect(new RectangleF(r.X, r.Bottom - h, r.Width, h), UiScale.Px(6)))
                g.FillPath(fill, path);
        TextRenderer.DrawText(g, $"{label} {value * 100:0}%", UiFonts.Small, new Rectangle(r.X - UiScale.Px(10), r.Bottom + UiScale.Px(2), r.Width + UiScale.Px(20), UiScale.Px(18)),
            p.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
    }

    private static void DrawCentered(Graphics g, Rectangle row, string name, short value, float range)
    {
        var p = Theme.Current;
        int labelW = UiScale.Px(62), valueW = UiScale.Px(56);
        TextRenderer.DrawText(g, name, UiFonts.Small, new Rectangle(row.X, row.Y, labelW, row.Height), p.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        var bar = new RectangleF(row.X + labelW, row.Y + row.Height / 2f - UiScale.Px(4), Math.Max(UiScale.Px(20), row.Width - labelW - valueW), UiScale.Px(8));
        using (var back = new SolidBrush(p.SurfaceHover))
            g.FillRectangle(back, bar);
        float v = Math.Clamp(value / range, -1f, 1f) * bar.Width / 2;
        using (var fill = new SolidBrush(Theme.AccentLine))
            g.FillRectangle(fill, v >= 0 ? bar.X + bar.Width / 2 : bar.X + bar.Width / 2 + v, bar.Y, MathF.Abs(v), bar.Height);
        using (var tick = new Pen(p.TextMuted))
            g.DrawLine(tick, bar.X + bar.Width / 2, bar.Y - 2, bar.X + bar.Width / 2, bar.Bottom + 2);
        TextRenderer.DrawText(g, value.ToString(), UiFonts.Small, new Rectangle((int)bar.Right + UiScale.Px(6), row.Y, valueW, row.Height), p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
