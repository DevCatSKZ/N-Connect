using System.Drawing;
using System.Drawing.Drawing2D;
using Switch2Pro.Protocol;
using Switch2Pro.Bridge.Links;

namespace Switch2Pro.Bridge;

/// <summary>Joy-Con-Darstellung: als Paar aufrecht nebeneinander, einzeln quer gehalten.</summary>
internal sealed partial class InputView
{
    /// <summary>Ein Joy-Con für die Anzeige: roher Zustand (Tasten an ihrer echten Position), Farben, Mausmodus.</summary>
    public sealed record JoyConPart(ControllerKind Kind, ControllerState State, DeviceCalibration Calibration,
        ControllerInfo Info, bool MouseActive, bool InGrip = false, bool Upright = false);

    private IReadOnlyList<JoyConPart>? _joyCons;

    public void ShowJoyCons(IReadOnlyList<JoyConPart> parts, PadInput? input)
    {
        _joyCons = parts;
        _input = input;
        Invalidate();
    }

    private const float JW = 120, JH = 330;

    private void PaintJoyCons(Graphics g, IReadOnlyList<JoyConPart> parts)
    {
        if (parts.Count == 2)
        {
            // Paar: links und rechts aufrecht, wie im Halter
            var left = parts.First(p => p.Kind.IsLeftJoyCon());
            var right = parts.First(p => !p.Kind.IsLeftJoyCon());
            float gap = 16, x = (W - 2 * JW - gap) / 2, y = 30;
            if (left.InGrip && right.InGrip)
                DrawGrip(g, x, y, gap);
            DrawJoyConAt(g, left, x, y);
            DrawJoyConAt(g, right, x + JW + gap, y);
        }
        else if (parts[0].Upright)
        {
            // Einzeln hochkant gehalten: aufrecht in der Mitte
            DrawJoyConAt(g, parts[0], (W - JW) / 2, 30);
            if (parts[0].MouseActive)
                MouseBadge(g, new PointF(W / 2, 300));
        }
        else
        {
            // Einzeln: quer gehalten, Schiene (SL/SR) oben – links gegen den Uhrzeigersinn gedreht, rechts mit
            var part = parts[0];
            var state = g.Save();
            g.TranslateTransform(W / 2, 190);
            float angle = part.Kind.IsLeftJoyCon() ? -90 : 90;
            g.RotateTransform(angle);
            g.TranslateTransform(-JW / 2, -JH / 2);
            _captionRotation = angle;
            try { DrawJoyCon(g, part); } finally { _captionRotation = 0; }
            g.Restore(state);
            if (part.MouseActive)
                MouseBadge(g, new PointF(W / 2, 300));
        }
        Gyro(g, _input?.Motion);
    }

    /// <summary>Charging Grip hinter einem Joy-Con-Paar: Gehäuse mit Handgriffen, GL/GR seitlich.</summary>
    private void DrawGrip(Graphics g, float x, float y, float gap)
    {
        float left = x - 26, right = x + 2 * JW + gap + 26, top = y + 40, bottom = y + JH - 20;
        using var grip = new GraphicsPath { FillMode = FillMode.Winding };
        using (var outline = Rounded(new RectangleF(left, top, right - left, bottom - top), 44))
            grip.AddPath(outline, false);
        grip.AddEllipse(left - 18, bottom - 110, 96, 170);
        grip.AddEllipse(right - 78, bottom - 110, 96, 170);
        var color = Color.FromArgb(40, 41, 46);
        using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
        {
            var st = g.Save();
            g.TranslateTransform(0, 6);
            g.FillPath(shadow, grip);
            g.Restore(st);
        }
        using (var fill = new LinearGradientBrush(new RectangleF(left - 20, top, right - left + 40, bottom - top + 70),
                   Mix(color, Color.White, 0.1f), Mix(color, Color.Black, 0.2f), LinearGradientMode.Vertical))
            g.FillPath(fill, grip);
        using (var pen = new Pen(Mix(color, Color.White, 0.25f), 1.4f))
            g.DrawPath(pen, grip);
        bool gl = _input?.Has(ProButtons.GL) == true, gr = _input?.Has(ProButtons.GR) == true;
        BackButton(g, new RectangleF(left - 66, bottom - 10, 40, 26), gl, "GL");
        BackButton(g, new RectangleF(right + 26, bottom - 10, 40, 26), gr, "GR");
    }

    private void DrawJoyConAt(Graphics g, JoyConPart part, float x, float y)
    {
        var state = g.Save();
        g.TranslateTransform(x, y);
        DrawJoyCon(g, part);
        g.Restore(state);
        if (part.MouseActive)
            MouseBadge(g, new PointF(x + JW / 2, y + JH + 14));
    }

    /// <summary>
    /// Einen Joy-Con aufrecht zeichnen (lokal 120 × 330). Linker Joy-Con: Schiene rechts, Stick oben,
    /// Richtungstasten darunter, „−“ oben innen, Aufnahme unten innen. Rechter Joy-Con gespiegelt mit
    /// A/B/X/Y oben, Stick darunter, „+“, HOME und (Joy-Con 2) C.
    /// </summary>
    private void DrawJoyCon(Graphics g, JoyConPart part)
    {
        var savedBody = _body;
        var savedButtons = _buttons;
        if (part.Info.BodyColor is { } body) _body = Rgb(body);
        if (part.Info.ButtonColor is { } btn) _buttons = Rgb(btn);
        try
        {
            bool left = part.Kind.IsLeftJoyCon();
            bool switch2 = part.Kind is ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right;
            // Joy-Con 2: dunkles Gehäuse mit farbigem Akzent (links blau, rechts rot) an Stickring und Schiene.
            var accent = part.Info.GripColor is { } a ? Rgb(a)
                : left ? Color.FromArgb(28, 180, 220) : Color.FromArgb(242, 89, 60);
            if (!switch2)
                accent = Mix(_body, Color.Black, 0.45f);
            var s = part.State;
            bool On(ProButtons b) => s.Has(b);
            float X(float x) => left ? x : JW - x; // rechter Joy-Con gespiegelt

            // Trigger- und Schultertaste oben außen
            var trigger = new RectangleF(left ? 14 : JW - 86, -4, 72, 30);
            using (var tPath = Rounded(trigger, 12))
                Fill(g, tPath, On(left ? ProButtons.ZL : ProButtons.ZR), Mix(_body, Color.Black, 0.25f));
            var bumper = new RectangleF(left ? 4 : JW - 92, 10, 88, 30);
            using (var bPath = Rounded(bumper, 14))
                Fill(g, bPath, On(left ? ProButtons.L : ProButtons.R), Mix(_body, Color.White, 0.15f));

            // Gehäuse: außen stark gerundet, an der Schiene (innen) gerade
            using var shell = new GraphicsPath();
            const float r = 52, ri = 8;
            var rect = new RectangleF(0, 24, JW, JH - 24);
            if (left)
            {
                shell.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
                shell.AddArc(rect.Right - ri * 2, rect.Y, ri * 2, ri * 2, 270, 90);
                shell.AddArc(rect.Right - ri * 2, rect.Bottom - ri * 2, ri * 2, ri * 2, 0, 90);
                shell.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
            }
            else
            {
                shell.AddArc(rect.X, rect.Y, ri * 2, ri * 2, 180, 90);
                shell.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
                shell.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
                shell.AddArc(rect.X, rect.Bottom - ri * 2, ri * 2, ri * 2, 90, 90);
            }
            shell.CloseFigure();
            using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            {
                var st = g.Save();
                g.TranslateTransform(0, 5);
                g.FillPath(shadow, shell);
                g.Restore(st);
            }
            using (var fill = new LinearGradientBrush(rect, Mix(_body, Color.White, 0.14f), Mix(_body, Color.Black, 0.18f),
                       left ? LinearGradientMode.Horizontal : LinearGradientMode.Horizontal))
                g.FillPath(fill, shell);
            using (var pen = new Pen(Mix(_body, Color.White, 0.3f), 1.4f))
                g.DrawPath(pen, shell);

            // Schiene mit SL/SR (innen)
            var rail = new RectangleF(left ? JW - 12 : 0, 40, 12, JH - 70);
            using (var railBrush = new SolidBrush(Mix(_body, Color.Black, 0.45f)))
                g.FillRectangle(railBrush, rail);
            using (var edgeBrush = new SolidBrush(accent))
                g.FillRectangle(edgeBrush, left ? rail.Right - 3 : rail.X, rail.Y - 6, 3, rail.Height + 12);
            var sl = left ? ProButtons.SLLeft : ProButtons.SLRight;
            var sr = left ? ProButtons.SRLeft : ProButtons.SRRight;
            // Quer gehalten liegt SL jeweils links: beim linken Joy-Con oben, beim rechten unten.
            RailButton(g, new RectangleF(rail.X, left ? 70 : JH - 120, 12, 50), On(sl), "SL");
            RailButton(g, new RectangleF(rail.X, left ? JH - 120 : 70, 12, 50), On(sr), "SR");

            if (left)
            {
                Small(g, new PointF(X(92), 50), On(ProButtons.Minus), "−");
                var ls = new PointF(X(56), 104);
                AccentRing(g, ls, 34, accent);
                Stick(g, ls, StickValue(s.LeftX, part.Calibration.Left.X),
                    StickValue(s.LeftY, part.Calibration.Left.Y), On(ProButtons.LeftStick), 28, 19);
                var d = new PointF(X(56), 196);
                Face(g, new PointF(d.X, d.Y - 25), On(ProButtons.Up), "▲", 12);
                Face(g, new PointF(d.X, d.Y + 25), On(ProButtons.Down), "▼", 12);
                Face(g, new PointF(d.X - 25, d.Y), On(ProButtons.Left), "◀", 12);
                Face(g, new PointF(d.X + 25, d.Y), On(ProButtons.Right), "▶", 12);
                CaptureKey(g, new PointF(X(76), 268), On(ProButtons.Capture));
            }
            else
            {
                Small(g, new PointF(X(92), 50), On(ProButtons.Plus), "+");
                var f = new PointF(X(60), 104);
                Face(g, new PointF(f.X, f.Y - 25), On(ProButtons.X), "X", 12);
                Face(g, new PointF(f.X + 25, f.Y), On(ProButtons.A), "A", 12);
                Face(g, new PointF(f.X, f.Y + 25), On(ProButtons.B), "B", 12);
                Face(g, new PointF(f.X - 25, f.Y), On(ProButtons.Y), "Y", 12);
                var rs = new PointF(X(56), 196);
                AccentRing(g, rs, 34, accent);
                Stick(g, rs, StickValue(s.RightX, part.Calibration.Right.X),
                    StickValue(s.RightY, part.Calibration.Right.Y), On(ProButtons.RightStick), 28, 19);
                Home(g, new PointF(X(78), 262), On(ProButtons.Home));
                if (switch2)
                    SquareKey(g, new PointF(X(78), 292), On(ProButtons.C), "C");
            }
            Caption(g, new RectangleF(left ? 16 : JW - 84, -3, 68, 14), left ? "ZL" : "ZR", 7.5f, FaceText);
            Caption(g, new RectangleF(left ? 10 : JW - 86, 12, 76, 14), left ? "L" : "R", 7.5f, Mix(_body, Color.White, 0.8f));
        }
        finally
        {
            _body = savedBody;
            _buttons = savedButtons;
        }
    }

    /// <summary>Farbiger Ring um den Stick (Joy-Con 2).</summary>
    private static void AccentRing(Graphics g, PointF c, float r, Color color)
    {
        using var pen = new Pen(color, 3f);
        g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
    }

    private static Color Rgb(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    private static short StickValue(int raw, AxisCalibration axis) =>
        (short)Math.Clamp(MathF.Round(axis.Normalize(raw) * 32767f), -32767f, 32767f);

    private void RailButton(Graphics g, RectangleF r, bool on, string text)
    {
        using var path = Rounded(r, 5);
        Fill(g, path, on, Mix(_body, Color.Black, 0.2f));
        var state = g.Save();
        g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
        g.RotateTransform(-90);
        Caption(g, new RectangleF(-r.Height / 2, -r.Width / 2, r.Height, r.Width), text, 6.5f, on ? Color.White : FaceText);
        g.Restore(state);
    }

    /// <summary>Markierung „Maus“, wenn ein Joy-Con 2 gerade als Maus arbeitet.</summary>
    private static void MouseBadge(Graphics g, PointF c)
    {
        var r = new RectangleF(c.X - 30, c.Y - 10, 60, 20);
        using var path = Rounded(r, 10);
        using (var fill = new SolidBrush(Accent))
            g.FillPath(fill, path);
        Caption(g, r, "Maus", 8.5f, Color.Black);
    }
}
