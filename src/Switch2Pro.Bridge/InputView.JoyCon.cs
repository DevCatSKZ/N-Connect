using System.Drawing;
using System.Drawing.Drawing2D;
using Switch2Pro.Protocol;
using Switch2Pro.Bridge.Links;

namespace Switch2Pro.Bridge;

/// <summary>
/// Joy-Con nach Produktfotos: Umriss und Tastenlage 1:1 vom Foto (Joy-Con 1), Joy-Con 2 auf sein schlankeres
/// Seitenverhältnis gestreckt. Als Paar nebeneinander, im Charging Grip (Umriss des Griffs vom Foto, Joy-Con links
/// und rechts vom Mittelstück), einzeln hochkant oder quer.
/// </summary>
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

    /// <summary>Lokales Raster eines Joy-Con: 120 Einheiten breit, Höhe nach Seitenverhältnis (Schultertasten darüber).</summary>
    private const float LW = 120;

    /// <summary>Breite : Höhe – Joy-Con 1 vom Foto (392 × 1094 px), Joy-Con 2 schlanker und länger.</summary>
    private static float Aspect(ControllerKind kind) =>
        kind is ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right ? 0.30f : 392f / 1094f;

    private static float LocalHeight(ControllerKind kind) => LW / Aspect(kind);

    private void PaintJoyCons(Graphics g, IReadOnlyList<JoyConPart> parts)
    {
        const float shoulder = 0.09f; // Anteil der Höhe für die Schultertasten über dem Gehäuse
        if (parts.Count == 2)
        {
            var left = parts.First(p => p.Kind.IsLeftJoyCon());
            var right = parts.First(p => !p.Kind.IsLeftJoyCon());
            if (left.InGrip && right.InGrip)
            {
                PaintGrip(g, left, right);
            }
            else
            {
                // Paar ohne Griff: aufrecht nebeneinander, schmale Fuge
                float h = Stage.Height / (1 + shoulder), w = h * Aspect(left.Kind), gap = 10;
                float x = Stage.X + (Stage.Width - 2 * w - gap) / 2, y = Stage.Y + h * shoulder;
                DrawJoyConIn(g, left, new RectangleF(x, y, w, h));
                DrawJoyConIn(g, right, new RectangleF(x + w + gap, y, w, h));
            }
        }
        else if (parts[0].Upright)
        {
            float h = Stage.Height / (1 + shoulder), w = h * Aspect(parts[0].Kind);
            DrawJoyConIn(g, parts[0], new RectangleF(Stage.X + (Stage.Width - w) / 2, Stage.Y + h * shoulder, w, h));
        }
        else
        {
            // Einzeln quer gehalten: Schiene (SL/SR) oben – links gegen den Uhrzeigersinn gedreht, rechts mit
            var part = parts[0];
            float length = Math.Min(Stage.Width * 0.82f, 440), w = length * Aspect(part.Kind);
            var state = g.Save();
            g.TranslateTransform(Stage.X + Stage.Width / 2, Stage.Y + Stage.Height / 2);
            float angle = part.Kind.IsLeftJoyCon() ? -90 : 90;
            g.RotateTransform(angle);
            _captionRotation = angle;
            try { DrawJoyConIn(g, part, new RectangleF(-w / 2, -length / 2, w, length), badge: false); }
            finally { _captionRotation = 0; }
            g.Restore(state);
            if (part.MouseActive)
                MouseBadge(g, new PointF(W / 2, Stage.Bottom - 6));
        }
        Gyro(g, _input?.Motion);
    }

    /// <summary>
    /// Joy-Con-Paar im Charging Grip: Umriss des Griffs vom Foto (Joy-Con 2 Grip), darauf die Joy-Con an ihrer
    /// Foto-Position links und rechts vom Mittelstück. GL/GR sitzen hinten an den Griffen.
    /// </summary>
    private void PaintGrip(Graphics g, JoyConPart left, JoyConPart right)
    {
        var f = Frame(83, 1197, 92, 990);
        var color = Color.FromArgb(0x34, 0x35, 0x3A);
        using (var path = f.Outline(PhotoOutlines.JoyConGrip, symmetric: true))
            BodyShape(g, path, color);
        // Mittelstück zwischen den Joy-Con (leicht abgesetzt)
        using (var middle = Rounded(f.R(470, 100, 340, 870), f.S(26)))
        using (var brush = new LinearGradientBrush(f.R(470, 100, 340, 870), Mix(color, Color.White, 0.08f), Mix(color, Color.Black, 0.12f), LinearGradientMode.Vertical))
            g.FillPath(brush, middle);
        // Joy-Con: oben bündig mit dem Griff, Breite wie auf dem Foto
        float w = f.S(270), h = w / Aspect(left.Kind);
        float top = f.P(0, 104).Y;
        DrawJoyConIn(g, left, new RectangleF(f.P(198, 0).X, top, w, h));
        DrawJoyConIn(g, right, new RectangleF(f.P(1082, 0).X - w, top, w, h));
        // Rücktasten GL/GR (hinten an den Griffen) als Tasten auf den Griffen, wie beim Pro Controller 2
        GripButton(g, f.R(96, 846, 130, 58), _input?.Has(ProButtons.GL) == true, "GL");
        GripButton(g, f.R(1054, 846, 130, 58), _input?.Has(ProButtons.GR) == true, "GR");
    }

    /// <summary>Joy-Con in ein Zielrechteck (Gehäuse ohne Schultertasten) zeichnen.</summary>
    private void DrawJoyConIn(Graphics g, JoyConPart part, RectangleF body, bool badge = true)
    {
        float lh = LocalHeight(part.Kind);
        var state = g.Save();
        g.TranslateTransform(body.X, body.Y);
        g.ScaleTransform(body.Width / LW, body.Height / lh);
        DrawJoyCon(g, part, lh);
        g.Restore(state);
        if (badge && part.MouseActive)
            MouseBadge(g, new PointF(body.X + body.Width / 2, Math.Min(body.Bottom + 14, H - 30)));
    }

    /// <summary>
    /// Einen Joy-Con im lokalen Raster zeichnen (Breite 120, Höhe <paramref name="lh"/>; Schultertasten bei y &lt; 0).
    /// Umriss und Tastenpositionen kommen vom Foto: links Stick oben, Richtungstasten, „−“, Aufnahme; rechts A/B/X/Y,
    /// Stick, „+“, HOME und beim Joy-Con 2 die C-Taste.
    /// </summary>
    private void DrawJoyCon(Graphics g, JoyConPart part, float lh)
    {
        var savedBody = _body;
        var savedButtons = _buttons;
        if (part.Info.BodyColor is { } body) _body = Rgb(body);
        if (part.Info.ButtonColor is { } btn) _buttons = Rgb(btn);
        // Joy-Con: schwarze Tasten mit hellen Beschriftungen, unabhängig von der Gehäusefarbe (wie das Original).
        _darkButtons = true;
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
            // Foto-Pixel → lokales Raster (Joy-Con 2: gleiche Anordnung, auf seine Länge gestreckt)
            var photo = left ? new RectangleF(89, 66, 392, 1094) : new RectangleF(803, 71, 390, 1092);
            float sx = LW / photo.Width, sy = lh / photo.Height;
            PointF P(float x, float y) => new((x - photo.X) * sx, (y - photo.Y) * sy);
            float S(float v) => v * sx;

            // Trigger und Schultertaste oben außen (über dem Gehäuse)
            var trigger = new RectangleF(left ? 10 : LW - 82, -lh * 0.075f, 72, 30);
            using (var tPath = Rounded(trigger, 12))
                Fill(g, tPath, On(left ? ProButtons.ZL : ProButtons.ZR), Mix(_body, Color.Black, 0.25f));
            var bumper = new RectangleF(left ? 2 : LW - 90, -lh * 0.035f, 88, 30);
            using (var bPath = Rounded(bumper, 14))
                Fill(g, bPath, On(left ? ProButtons.L : ProButtons.R), Mix(_body, Color.White, 0.15f));
            // Beschriftung jeweils im sichtbaren Streifen (der Rest liegt hinter der Taste davor bzw. dem Gehäuse)
            Caption(g, new RectangleF(trigger.X, trigger.Y, trigger.Width, bumper.Y - trigger.Y), left ? "ZL" : "ZR", 7f,
                On(left ? ProButtons.ZL : ProButtons.ZR) ? Color.White : FaceText);
            var bumperText = Theme.Luminance(Mix(_body, Color.White, 0.15f)) > 0.4f ? Color.FromArgb(0x20, 0x20, 0x24) : Color.White;
            Caption(g, new RectangleF(bumper.X, bumper.Y, bumper.Width, -bumper.Y), left ? "L" : "R", 7f,
                On(left ? ProButtons.L : ProButtons.R) ? Color.White : bumperText);

            // Gehäuse: Umriss vom Foto
            using var shell = new GraphicsPath();
            shell.AddClosedCurve(ParsePoints(left ? PhotoOutlines.JoyCon1Left : PhotoOutlines.JoyCon1Right)
                .Select(q => P(q.X, q.Y)).ToArray(), 0.3f);
            using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            {
                var st = g.Save();
                g.TranslateTransform(0, 5);
                g.FillPath(shadow, shell);
                g.Restore(st);
            }
            var bounds = shell.GetBounds();
            using (var fill = new LinearGradientBrush(bounds, Mix(_body, Color.White, 0.14f), Mix(_body, Color.Black, 0.18f), LinearGradientMode.Horizontal))
                g.FillPath(fill, shell);
            using (var pen = new Pen(Mix(_body, Color.White, 0.3f), 1.4f))
                g.DrawPath(pen, shell);

            // Schiene mit SL/SR (innen, Foto: linker Joy-Con x 445–481, rechter x 803–835)
            var railTop = P(0, 130).Y;
            var railBottom = P(0, 1050).Y;
            float railX = left ? P(447, 0).X : P(805, 0).X, railW = S(32);
            var rail = new RectangleF(railX, railTop, railW, railBottom - railTop);
            using (var railBrush = new SolidBrush(Mix(_body, Color.Black, 0.45f)))
                g.FillRectangle(railBrush, rail);
            using (var edgeBrush = new SolidBrush(accent))
                g.FillRectangle(edgeBrush, left ? rail.Right - 3 : rail.X, rail.Y - 6, 3, rail.Height + 12);
            var sl = left ? ProButtons.SLLeft : ProButtons.SLRight;
            var sr = left ? ProButtons.SRLeft : ProButtons.SRRight;
            // Quer gehalten liegt SL jeweils links: beim linken Joy-Con oben, beim rechten unten.
            float slotH = lh * 0.16f;
            RailButton(g, new RectangleF(rail.X, left ? lh * 0.24f : lh * 0.62f, rail.Width, slotH), On(sl), "SL");
            RailButton(g, new RectangleF(rail.X, left ? lh * 0.62f : lh * 0.24f, rail.Width, slotH), On(sr), "SR");

            if (left)
            {
                Small(g, P(393, 173), On(ProButtons.Minus), "−");
                var ls = P(282, 333);
                AccentRing(g, ls, S(96), accent);
                Stick(g, ls, StickValue(s.LeftX, part.Calibration.Left.X),
                    StickValue(s.LeftY, part.Calibration.Left.Y), On(ProButtons.LeftStick), S(85), S(58),
                    capColor: Color.FromArgb(0x2A, 0x2A, 0x2E), wellColor: Color.FromArgb(0x16, 0x16, 0x1A));
                float r = S(40);
                Face(g, P(282, 560), On(ProButtons.Up), "▲", r);
                Face(g, P(280, 723), On(ProButtons.Down), "▼", r);
                Face(g, P(195, 642), On(ProButtons.Left), "◀", r);
                Face(g, P(368, 642), On(ProButtons.Right), "▶", r);
                CaptureKey(g, P(340, 848), On(ProButtons.Capture));
            }
            else
            {
                Small(g, P(891, 175), On(ProButtons.Plus), "+");
                float r = S(40);
                Face(g, P(1000, 258), On(ProButtons.X), "X", r);
                Face(g, P(1087, 340), On(ProButtons.A), "A", r);
                Face(g, P(1000, 422), On(ProButtons.B), "B", r);
                Face(g, P(915, 340), On(ProButtons.Y), "Y", r);
                var rs = P(1003, 640);
                AccentRing(g, rs, S(96), accent);
                Stick(g, rs, StickValue(s.RightX, part.Calibration.Right.X),
                    StickValue(s.RightY, part.Calibration.Right.Y), On(ProButtons.RightStick), S(85), S(58),
                    capColor: Color.FromArgb(0x2A, 0x2A, 0x2E), wellColor: Color.FromArgb(0x16, 0x16, 0x1A));
                Home(g, P(941, 855), On(ProButtons.Home));
                if (switch2)
                    SquareKey(g, P(941, 960), On(ProButtons.C), "C");
            }
        }
        finally
        {
            _body = savedBody;
            _buttons = savedButtons;
            _darkButtons = false;
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
        if (_captionRotation != 0)
        {
            // Quer gehalten liegt die Schiene waagerecht: Beschriftung waagerecht (Caption dreht zurück, Maße getauscht).
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            Caption(g, new RectangleF(cx - r.Height / 2, cy - r.Width / 2, r.Height, r.Width), text, 6.5f, on ? Color.White : FaceText);
            return;
        }
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
