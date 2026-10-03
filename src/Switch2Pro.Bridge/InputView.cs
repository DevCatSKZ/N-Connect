using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Gezeichneter Controller im Stil des Pro Controller 2 zum Prüfen der Eingaben: gedrückte Tasten leuchten
/// auf, Sticks bewegen ihre Kappe, Trigger füllen sich, der Bewegungssensor erscheint als Balken.
/// Farben kommen vom Controller selbst (Gehäuse, Tasten, Griffe), sonst die des Pro Controller 2.
/// Zeichnet in einem festen Raster (580 × 410) und skaliert auf die Größe des Steuerelements.
/// </summary>
internal sealed partial class InputView : Control
{
    private const float W = 580, H = 410;

    /// <summary>Breite der Controller-Zeichnung; sie steht um <see cref="X0"/> eingerückt, daneben GL/GR.</summary>
    private const float CW = 520, X0 = 30;

    /// <summary>Höhe stauchen: Positionen und Umriss werden ab der Oberkante (y = 44) zusammengeschoben.</summary>
    private const float Vs = 1f;

    private static float Y(float y) => 44 + (y - 44) * Vs;
    private static PointF P(float x, float y) => new(x, Y(y));

    private PadInput? _input;
    private GamepadState _gamepad;

    /// <summary>Farben des Pro Controller 2 (so meldet ihn der Controller selbst, Speicher 0x13000).</summary>
    private Color _body = Color.FromArgb(0x23, 0x23, 0x23);
    private Color _buttons = Color.FromArgb(0xA0, 0xA0, 0xA0);
    private Color _grip = Color.FromArgb(0xE6, 0xE6, 0xE6);

    private static readonly Color Back = Color.FromArgb(30, 31, 36);
    private static readonly Color Accent = Color.FromArgb(0, 190, 255);
    private static readonly Color AccentGlow = Color.FromArgb(110, 0, 190, 255);

    public InputView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        Size = new Size(580, 410);
    }

    /// <summary>Farben aus den Gerätedaten übernehmen (RGB, null = Standard).</summary>
    public void SetColors(int? body, int? buttons, int? grip)
    {
        static Color C(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        if (body is { } b) _body = C(b);
        if (buttons is { } k) _buttons = C(k);
        if (grip is { } gr) _grip = C(gr);
    }

    public void Show(PadInput? input, GamepadState gamepad)
    {
        _joyCons = null;
        _input = input;
        _gamepad = gamepad;
        Invalidate();
    }

    // Abgeleitete Farben: Tasten auf dem Gehäuse etwas dunkler, Beschriftung je nach Helligkeit.
    private Color KeyFill => Mix(_body, Color.Black, 0.35f);
    private Color KeyEdge => Mix(_body, Color.White, 0.25f);
    private Color FaceFill => Mix(_body, Color.Black, 0.25f);
    private Color FaceText => _buttons;

    private static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float s = Math.Min(Width / W, Height / H);
        g.TranslateTransform((Width - W * s) / 2, (Height - H * s) / 2);
        g.ScaleTransform(s, s);
        if (_joyCons is { Count: > 0 } parts)
        {
            PaintJoyCons(g, parts);
            return;
        }
        var controller = g.Save();
        g.TranslateTransform(X0, 0);

        var p = _input;
        bool On(ProButtons b) => p is not null && p.Has(b);
        bool joyCon = p?.Kind.IsJoyCon() == true;

        // Positionen maßstabsgetreu nach einem Produktfoto des Pro Controller 2 (Frontansicht).
        // Hinten: Trigger ZL/ZR, davor die hell abgesetzten Schultertasten L/R
        CornerTrigger(g, left: true, _gamepad.LeftTrigger / 255f);
        CornerTrigger(g, left: false, _gamepad.RightTrigger / 255f);
        CornerBumper(g, left: true, On(ProButtons.L));
        CornerBumper(g, left: false, On(ProButtons.R));

        DrawBody(g);
        ShoulderLabels(g, _gamepad.LeftTrigger / 255f, _gamepad.RightTrigger / 255f, On(ProButtons.L), On(ProButtons.R));

        // Rücktasten (Pro Controller 2: GL/GR, einzelner Joy-Con: SL/SR) – unter den Griffen angedeutet

        // Links: Stick oben außen, großes Steuerkreuz darunter weiter innen
        Stick(g, P(119, 153), _gamepad.LeftX, _gamepad.LeftY, On(ProButtons.LeftStick));
        DPad(g, P(192, 233), On(ProButtons.Up), On(ProButtons.Down), On(ProButtons.Left), On(ProButtons.Right));

        // Rechts: Tasten oben außen (X oben, A rechts, B unten, Y links), Stick darunter weiter innen
        Face(g, P(412, 116), On(ProButtons.X), "X");
        Face(g, P(456, 153), On(ProButtons.A), "A");
        Face(g, P(412, 190), On(ProButtons.B), "B");
        Face(g, P(368, 154), On(ProButtons.Y), "Y");
        Stick(g, P(339, 231), _gamepad.RightX, _gamepad.RightY, On(ProButtons.RightStick));

        // Mitte: − / + oben, Aufnahme (eckig) / HOME (rund) darunter, C unten mittig
        Small(g, P(205, 112), On(ProButtons.Minus), "−");
        Small(g, P(336, 112), On(ProButtons.Plus), "+");
        CaptureKey(g, P(238, 155), On(ProButtons.Capture));
        Home(g, P(304, 154), On(ProButtons.Home));
        SquareKey(g, P(272, 273), On(ProButtons.C), "C");
        PlayerLeds(g, new PointF(CW / 2, 47));

        Gyro(g, p?.Motion);

        // Rücktasten GL/GR (bzw. SL/SR beim einzelnen Joy-Con) seitlich neben den Griffen, symmetrisch
        g.Restore(controller);
        bool gl = On(ProButtons.GL) || On(ProButtons.SLLeft) || On(ProButtons.SLRight);
        bool gr = On(ProButtons.GR) || On(ProButtons.SRLeft) || On(ProButtons.SRRight);
        BackButton(g, new RectangleF(2, 318, 40, 26), gl, joyCon ? "SL" : "GL");
        BackButton(g, new RectangleF(W - 42, 318, 40, 26), gr, joyCon ? "SR" : "GR");
    }

    /// <summary>Spielernummer für die LED-Anzeige (0–7), −1 = aus.</summary>
    public int PlayerIndex { get; set; } = -1;

    /// <summary>Vier Spieler-LEDs oben an der Gehäusekante, Muster wie an der Konsole.</summary>
    private void PlayerLeds(Graphics g, PointF c)
    {
        byte mask = PlayerIndex >= 0 ? Commands.PlayerLedMask(PlayerIndex) : (byte)0;
        for (int i = 0; i < 4; i++)
        {
            float x = c.X - 18 + i * 12;
            bool on = (mask & (1 << i)) != 0;
            if (on)
            {
                using var glow = new SolidBrush(Color.FromArgb(90, 120, 255, 120));
                g.FillEllipse(glow, x - 4, c.Y - 4, 8, 8);
            }
            using var led = new SolidBrush(on ? Color.FromArgb(140, 255, 140) : Mix(_body, Color.Black, 0.4f));
            g.FillEllipse(led, x - 2, c.Y - 2, 4, 4);
        }
    }

    // ---------- Gehäuse ----------

    private static GraphicsPath BodyPath()
    {
        var path = new GraphicsPath { FillMode = FillMode.Winding };
        // Umriss des Pro Controller 2, vermessen nach dem Produktfoto (Frontansicht, Maßstab ≈ 0,49):
        // Oberkante fast gerade mit großen runden Ecken, senkrechte Seiten, darunter breite, runde Griffe,
        // die nach unten leicht nach innen laufen; zwischen den Griffen ein flacher Bogen.
        // Linke Hälfte als Punktliste, die rechte entsteht durch Spiegeln an der Mittelachse – exakt symmetrisch.
        PointF[] left =
        [
            new(CW / 2, 42),                                                   // Mitte oben
            new(170, 42), new(80, 43), new(46, 46),                            // Oberkante
            new(16, 50), new(4, 80), new(4, 118),                              // Ecke oben links
            new(4, 160), new(5, 200), new(7, 228),                             // Seite
            new(10, 290), new(36, 372), new(76, 392),                          // Griff außen, nach innen laufend
            new(104, 405), new(134, 398), new(140, 372),                       // rundes Griffende
            new(146, 344), new(146, 318), new(158, 302),                       // Griff innen
            new(200, 290), new(240, 288), new(CW / 2, 288),                    // Bogen unten bis zur Mitte
        ];
        path.StartFigure();
        // Linke Hälfte rückwärts (von unten Mitte über den linken Griff nach oben Mitte) …
        var leftReversed = left.Reverse().ToArray();
        // … dann rechte Hälfte gespiegelt (von oben Mitte über den rechten Griff nach unten Mitte).
        var right = left.Select(p => new PointF(CW - p.X, p.Y)).ToArray();
        path.AddBeziers(leftReversed);
        path.AddBeziers(right);
        path.CloseFigure();
        using var squash = new Matrix();
        squash.Translate(0, 44 * (1 - Vs));
        squash.Scale(1, Vs);
        path.Transform(squash);
        return path;
    }

    private void DrawBody(Graphics g)
    {
        using var path = BodyPath();

        using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
        {
            var state = g.Save();
            g.TranslateTransform(0, 6);
            g.FillPath(shadow, path);
            g.Restore(state);
        }

        // Gehäuse, matt mit leichtem Verlauf
        using (var fill = new LinearGradientBrush(new RectangleF(0, 40, CW, 340),
                   Mix(_body, Color.White, 0.12f), Mix(_body, Color.Black, 0.2f), LinearGradientMode.Vertical))
            g.FillPath(fill, path);

        var clip = g.Save();
        g.SetClip(path);
        // Griffe: zum Ende hin etwas dunkler (gerundete Form, wie auf dem Foto)
        using (var shade = new LinearGradientBrush(new RectangleF(0, 240, CW, 170),
                   Color.FromArgb(0, 0, 0, 0), Color.FromArgb(75, 0, 0, 0), LinearGradientMode.Vertical) { WrapMode = WrapMode.TileFlipXY })
            g.FillRectangle(shade, 0, 241, CW, 169);
        // Glanzkante oben
        using (var shine = new Pen(Color.FromArgb(45, 255, 255, 255), 2f))
            g.DrawBezier(shine, 120, 50, 200, 46, 320, 46, 400, 50);
        g.Restore(clip);

        using (var pen = new Pen(Mix(_body, Color.White, 0.3f), 1.5f))
            g.DrawPath(pen, path);
    }

    // ---------- Schulter- und Triggertasten (Blick von vorn: L/R auf den oberen Ecken, ZL/ZR dahinter) ----------

    /// <summary>Linke Seite zeichnen; rechts wird an der Mittelachse gespiegelt.</summary>
    private static PointF M(bool left, float x, float y) => new(left ? x : CW - x, y);

    /// <summary>Form einer Schultertaste: oben gerade, innen klein abgerundet, außen der Gehäuseecke folgend.</summary>
    private static GraphicsPath ShoulderPath(bool left, float top, float inner, float outerX, float outerY, float bottom)
    {
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddLine(M(left, outerX + 34, top), M(left, inner - 10, top));
        path.AddBezier(M(left, inner - 10, top), M(left, inner - 3, top), M(left, inner, top + 3), M(left, inner, top + 10));
        path.AddLine(M(left, inner, top + 10), M(left, inner, bottom));
        path.AddLine(M(left, inner, bottom), M(left, outerX + 10, bottom + 30));
        path.AddLine(M(left, outerX + 10, bottom + 30), M(left, outerX, outerY));
        path.AddBezier(M(left, outerX, outerY), M(left, outerX, top + 22), M(left, outerX + 12, top), M(left, outerX + 34, top));
        path.CloseFigure();
        return path;
    }

    private void CornerTrigger(Graphics g, bool left, float value)
    {
        using var path = ShoulderPath(left, top: 4, inner: 172, outerX: 34, outerY: 70, bottom: 44);
        var bounds = path.GetBounds();
        using (var back = new LinearGradientBrush(bounds, Mix(_body, Color.White, 0.12f), Mix(_body, Color.Black, 0.25f), LinearGradientMode.Vertical))
            g.FillPath(back, path);
        value = Math.Clamp(value, 0f, 1f);
        if (value > 0.02f)
        {
            // Analog: füllt sich von außen nach innen, je weiter der Trigger gedrückt ist.
            var state = g.Save();
            g.SetClip(path);
            float w = bounds.Width * value;
            using var fill = new SolidBrush(Accent);
            g.FillRectangle(fill, left ? bounds.X : bounds.Right - w, bounds.Y, w, bounds.Height);
            g.Restore(state);
        }
        using var edge = new Pen(value > 0.02f ? Accent : KeyEdge, 1.3f);
        g.DrawPath(edge, path);
    }

    private void CornerBumper(Graphics g, bool left, bool on)
    {
        using var path = ShoulderPath(left, top: 24, inner: 178, outerX: 24, outerY: 92, bottom: 60);
        if (on)
        {
            using var glow = new Pen(AccentGlow, 7f);
            g.DrawPath(glow, path);
        }
        var bounds = path.GetBounds();
        using (var fill = new LinearGradientBrush(bounds, on ? Accent : Mix(_grip, Color.White, 0.25f),
                   on ? Mix(Accent, Color.Black, 0.2f) : Mix(_grip, Color.Black, 0.2f), LinearGradientMode.Vertical))
            g.FillPath(fill, path);
        using var edge = new Pen(on ? Color.FromArgb(160, 235, 255) : Mix(_grip, Color.Black, 0.3f), 1.2f);
        g.DrawPath(edge, path);
    }

    /// <summary>Beschriftungen nach dem Gehäuse, damit sie im sichtbaren Teil der Tasten stehen.</summary>
    private void ShoulderLabels(Graphics g, float zl, float zr, bool l, bool r)
    {
        var onBumper = Mix(_grip, Color.Black, 0.65f);
        foreach (bool left in new[] { true, false })
        {
            var trigger = new RectangleF(left ? 90 : CW - 160, 5, 70, 18);
            var bumper = new RectangleF(left ? 90 : CW - 160, 25, 70, 18);
            float value = left ? zl : zr;
            bool pressed = left ? l : r;
            Caption(g, trigger, left ? "ZL" : "ZR", 9f, value > 0.5f ? Color.White : FaceText);
            Caption(g, bumper, left ? "L" : "R", 9.5f, pressed ? Color.White : onBumper);
        }
    }

    /// <summary>Rücktaste (GL/GR) bzw. SL/SR, seitlich neben dem Griff dargestellt.</summary>
    private void BackButton(Graphics g, RectangleF r, bool on, string text)
    {
        using var path = Rounded(r, 9);
        Fill(g, path, on, Mix(_body, Color.Black, 0.15f));
        Caption(g, r, text, 8.5f, on ? Color.White : FaceText);
    }

    // ---------- Bedienelemente ----------

    private void Trigger(Graphics g, RectangleF r, string text, float value)
    {
        using var path = Rounded(r, 9);
        using (var back = new SolidBrush(Mix(_body, Color.Black, 0.2f)))
            g.FillPath(back, path);
        if (value > 0)
        {
            var clip = g.Save();
            g.SetClip(path);
            using var fill = new SolidBrush(Accent);
            g.FillRectangle(fill, r.X, r.Y, r.Width * Math.Clamp(value, 0f, 1f), r.Height);
            g.Restore(clip);
        }
        using (var edge = new Pen(value > 0 ? Accent : KeyEdge, 1.2f))
            g.DrawPath(edge, path);
        Caption(g, r, text, 8.5f, value > 0.5f ? Color.White : FaceText);
    }

    private void Shoulder(Graphics g, RectangleF r, bool on, string text)
    {
        // Beim Pro Controller 2 hell abgesetzt (zweite Gerätefarbe).
        using var path = Rounded(r, 13);
        Fill(g, path, on, _grip);
        Caption(g, new RectangleF(r.X, r.Y, r.Width, 18), text, 9f, on ? Color.White : Mix(_grip, Color.Black, 0.6f));
    }

    private void Pill(Graphics g, RectangleF r, bool on, string text)
    {
        using var path = Rounded(r, r.Height / 2);
        Fill(g, path, on, KeyFill);
        Caption(g, r, text, 7.5f, on ? Color.White : FaceText);
    }

    private void Face(Graphics g, PointF c, bool on, string text, float r = 20.5f)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
        Fill(g, path, on, FaceFill);
        Caption(g, new RectangleF(c.X - r, c.Y - r, r * 2, r * 2), text, r * 0.63f, on ? Color.White : FaceText);
    }

    private void Small(Graphics g, PointF c, bool on, string text)
    {
        const float r = 13;
        using var path = new GraphicsPath();
        path.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
        Fill(g, path, on, KeyFill);
        Caption(g, new RectangleF(c.X - r, c.Y - r - 1, r * 2, r * 2), text, 13f, on ? Color.White : FaceText);
    }

    private void CaptureKey(Graphics g, PointF c, bool on)
    {
        var r = new RectangleF(c.X - 12, c.Y - 12, 24, 24);
        using var path = Rounded(r, 5);
        Fill(g, path, on, KeyFill);
        using var ring = new Pen(on ? Color.White : FaceText, 1.6f);
        g.DrawEllipse(ring, c.X - 6, c.Y - 6, 12, 12);
    }

    private void SquareKey(Graphics g, PointF c, bool on, string text)
    {
        // Wie die C-Taste des Pro Controller 2: kleines, abgerundetes Quadrat mit feinem hellem Rand.
        var r = new RectangleF(c.X - 11, c.Y - 11, 22, 22);
        using var path = Rounded(r, 6);
        Fill(g, path, on, KeyFill);
        using (var ring = new Pen(on ? Color.White : Mix(_buttons, _body, 0.35f), 1.2f))
        {
            using var inner = Rounded(RectangleF.Inflate(r, -2.5f, -2.5f), 4);
            g.DrawPath(ring, inner);
        }
        Caption(g, r, text, 10f, on ? Color.White : FaceText);
    }

    private void Home(Graphics g, PointF c, bool on)
    {
        const float r = 13;
        using var path = new GraphicsPath();
        path.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
        Fill(g, path, on, KeyFill);
        // Leuchtring um HOME
        using (var ring = new Pen(on ? Color.White : Mix(_body, Color.White, 0.3f), 1.2f))
            g.DrawEllipse(ring, c.X - r + 3, c.Y - r + 3, (r - 3) * 2, (r - 3) * 2);
        using var house = new GraphicsPath();
        house.AddPolygon([new PointF(c.X, c.Y - 5.5f), new PointF(c.X + 5.5f, c.Y), new PointF(c.X + 3.5f, c.Y),
            new PointF(c.X + 3.5f, c.Y + 5), new PointF(c.X - 3.5f, c.Y + 5), new PointF(c.X - 3.5f, c.Y), new PointF(c.X - 5.5f, c.Y)]);
        using var brush = new SolidBrush(on ? Color.White : FaceText);
        g.FillPath(brush, house);
    }

    private void DPad(Graphics g, PointF c, bool up, bool down, bool left, bool right)
    {
        const float arm = 28, len = 43;
        using (var cross = new GraphicsPath { FillMode = FillMode.Winding })
        {
            using (var vertical = Rounded(new RectangleF(c.X - arm / 2, c.Y - len, arm, len * 2), 6))
                cross.AddPath(vertical, false);
            using (var horizontal = Rounded(new RectangleF(c.X - len, c.Y - arm / 2, len * 2, arm), 6))
                cross.AddPath(horizontal, false);
            using var well = new SolidBrush(KeyFill);
            g.FillPath(well, cross);
            using var edge = new Pen(KeyEdge, 1.2f);
            g.DrawPath(edge, cross);
        }
        void Arm(bool on, RectangleF r, string glyph)
        {
            if (on)
            {
                using var lit = new SolidBrush(Accent);
                g.FillRectangle(lit, r);
            }
            Caption(g, r, glyph, 8f, on ? Color.White : FaceText);
        }
        Arm(up, new RectangleF(c.X - arm / 2, c.Y - len, arm, len - arm / 2), "▲");
        Arm(down, new RectangleF(c.X - arm / 2, c.Y + arm / 2, arm, len - arm / 2), "▼");
        Arm(left, new RectangleF(c.X - len, c.Y - arm / 2, len - arm / 2, arm), "◀");
        Arm(right, new RectangleF(c.X + arm / 2, c.Y - arm / 2, len - arm / 2, arm), "▶");
    }

    private void Stick(Graphics g, PointF c, short x, short y, bool pressed, float well = 41, float cap = 27)
    {
        (x, y) = Smooth(c, x, y);
        // Erhabener Ring um die Stick-Mulde (wie beim Original)
        using (var outer = new Pen(Mix(_body, Color.White, 0.1f), 3f))
            g.DrawEllipse(outer, c.X - well - 3, c.Y - well - 3, (well + 3) * 2, (well + 3) * 2);
        using (var wellBrush = new SolidBrush(Mix(_body, Color.Black, 0.55f)))
            g.FillEllipse(wellBrush, c.X - well, c.Y - well, well * 2, well * 2);
        using (var ring = new Pen(KeyEdge, 1.2f))
            g.DrawEllipse(ring, c.X - well, c.Y - well, well * 2, well * 2);

        float travel = well - cap + 2;
        float px = c.X + x / 32767f * travel, py = c.Y - y / 32767f * travel;
        bool moved = Math.Abs(x) > 1500 || Math.Abs(y) > 1500;
        if (pressed)
        {
            using var glow = new SolidBrush(AccentGlow);
            g.FillEllipse(glow, px - cap - 5, py - cap - 5, (cap + 5) * 2, (cap + 5) * 2);
        }
        using (var capBrush = new LinearGradientBrush(new RectangleF(px - cap, py - cap, cap * 2, cap * 2),
                   Mix(_body, Color.White, 0.25f), Mix(_body, Color.Black, 0.2f), LinearGradientMode.ForwardDiagonal))
            g.FillEllipse(capBrush, px - cap, py - cap, cap * 2, cap * 2);
        using (var capEdge = new Pen(pressed || moved ? Accent : KeyEdge, pressed ? 2.5f : 1.5f))
            g.DrawEllipse(capEdge, px - cap, py - cap, cap * 2, cap * 2);
        using var dot = new SolidBrush(moved || pressed ? Accent : KeyEdge);
        g.FillEllipse(dot, px - 5, py - 5, 10, 10);
    }

    private void Gyro(Graphics g, Motion? motion)
    {
        var area = new RectangleF(160, 394, 200, 14);
        using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var label = new SolidBrush(Color.FromArgb(170, 175, 185));
        if (motion is not { } m)
        {
            using var format = new StringFormat { Alignment = StringAlignment.Center };
            g.DrawString("Bewegungssensor: keine Daten", font, label, area, format);
            return;
        }
        (string Axis, short Value)[] axes = [("Gyro X", m.GyroX), ("Y", m.GyroY), ("Z", m.GyroZ)];
        float w = (area.Width - 20) / 3;
        for (int i = 0; i < 3; i++)
        {
            var r = new RectangleF(area.X + i * (w + 10), area.Y + 8, w, 8);
            using (var back = new SolidBrush(Color.FromArgb(55, 57, 64)))
                g.FillRectangle(back, r);
            float v = Math.Clamp(axes[i].Value / 6000f, -1f, 1f) * r.Width / 2;
            using (var fill = new SolidBrush(Accent))
                g.FillRectangle(fill, v >= 0 ? r.X + r.Width / 2 : r.X + r.Width / 2 + v, r.Y, Math.Abs(v), r.Height);
            g.DrawString(axes[i].Axis, font, label, r.X - 1, r.Y - 13);
        }
    }

    // ---------- Hilfen ----------

    /// <summary>Taste füllen: in Grundfarbe, oder bei gedrückter Taste leuchtend mit Glührand.</summary>
    private void Fill(Graphics g, GraphicsPath path, bool on, Color idle)
    {
        if (on)
        {
            using var glow = new Pen(AccentGlow, 7f);
            g.DrawPath(glow, path);
        }
        using (var brush = new SolidBrush(on ? Accent : idle))
            g.FillPath(brush, path);
        using var edge = new Pen(on ? Color.FromArgb(160, 235, 255) : KeyEdge, 1.2f);
        g.DrawPath(edge, path);
    }

    private static readonly Dictionary<float, Font> Fonts = [];
    private static readonly StringFormat CenterFormat = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    /// <summary>Schriften wiederverwenden statt bei jedem Bild neu anzulegen (60 Bilder/s).</summary>
    private static Font CachedFont(float size)
    {
        if (!Fonts.TryGetValue(size, out var font))
            Fonts[size] = font = new Font("Segoe UI", size, FontStyle.Bold);
        return font;
    }

    /// <summary>
    /// Angezeigte Stick-Positionen, weich zur gemeldeten Position nachgeführt. Die Controller melden sich per
    /// Bluetooth nur alle 30 ms; ohne Nachführen springt die Anzeige sichtbar. Nur optisch – ans Spiel geht
    /// immer der echte Wert.
    /// </summary>
    private readonly Dictionary<(float, float), (float X, float Y)> _smoothSticks = [];

    private (short X, short Y) Smooth(PointF c, short x, short y)
    {
        var key = (MathF.Round(c.X), MathF.Round(c.Y));
        var (sx, sy) = _smoothSticks.TryGetValue(key, out var prev) ? prev : (x, y);
        sx += (x - sx) * 0.5f;
        sy += (y - sy) * 0.5f;
        _smoothSticks[key] = (sx, sy);
        return ((short)sx, (short)sy);
    }

    /// <summary>Gegendrehung für Beschriftungen, wenn der Controller gedreht gezeichnet wird (quer gehaltener Joy-Con).</summary>
    private static float _captionRotation;

    private static void Caption(Graphics g, RectangleF r, string text, float size, Color color)
    {
        if (_captionRotation != 0)
        {
            var state = g.Save();
            g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
            g.RotateTransform(-_captionRotation);
            float rotation = _captionRotation;
            _captionRotation = 0;
            Caption(g, new RectangleF(-r.Width / 2, -r.Height / 2, r.Width, r.Height), text, size, color);
            _captionRotation = rotation;
            g.Restore(state);
            return;
        }
        var font = CachedFont(size);
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, r, CenterFormat);
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
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
}
