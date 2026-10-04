using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Gezeichneter Controller im Stil des Pro Controller 2 zum Prüfen der Eingaben: gedrückte Tasten leuchten
/// auf, Sticks bewegen ihre Kappe, Trigger füllen sich, der Bewegungssensor erscheint als Balken.
/// Farben kommen vom Controller selbst (Gehäuse, Tasten, Griffe), sonst die des Pro Controller 2.
/// Zeichnet in einem festen Raster (580 × 430) und skaliert auf die Größe des Steuerelements.
/// </summary>
internal sealed partial class InputView : Control
{
    private const float W = 580, H = 430;

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

    /// <summary>Hintergrund der Grafik = Kartenfarbe der Darstellung (dunkel/hell).</summary>
    private static Color Back => Theme.Current.Surface;
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
        if (_input is { Kind: var kind } && IsRetro(kind))
        {
            PaintRetro(g, kind);
            return;
        }
        var controller = g.Save();
        g.TranslateTransform(X0, 0);

        var p = _input;
        bool On(ProButtons b) => p is not null && p.Has(b);
        bool joyCon = p?.Kind.IsJoyCon() == true;

        // Alle Positionen vom offiziellen Produktfoto (Frontansicht) übertragen: 1 Foto-Pixel = 0,397 Einheiten,
        // Gesamtmaß 148 × 105 mm. Hinten die Trigger ZL/ZR, davor die hellen Schultertasten L/R an den Ecken.
        Pro2Trigger(g, left: true, _gamepad.LeftTrigger / 255f);
        Pro2Trigger(g, left: false, _gamepad.RightTrigger / 255f);
        Pro2Bumper(g, left: true, On(ProButtons.L));
        Pro2Bumper(g, left: false, On(ProButtons.R));

        DrawBody(g);

        // Links: Stick oben außen, Steuerkreuz darunter weiter innen
        Stick(g, new PointF(117, 143), _gamepad.LeftX, _gamepad.LeftY, On(ProButtons.LeftStick));
        DPad(g, new PointF(183, 217), On(ProButtons.Up), On(ProButtons.Down), On(ProButtons.Left), On(ProButtons.Right));

        // Rechts: Tasten oben außen (X oben, A rechts, B unten, Y links), Stick darunter weiter innen
        Face(g, new PointF(390, 107), On(ProButtons.X), "X");
        Face(g, new PointF(432, 143), On(ProButtons.A), "A");
        Face(g, new PointF(390, 179), On(ProButtons.B), "B");
        Face(g, new PointF(348, 143), On(ProButtons.Y), "Y");
        Stick(g, new PointF(320, 217), _gamepad.RightX, _gamepad.RightY, On(ProButtons.RightStick));

        // Mitte: − / + oben, Aufnahme (eckig) / HOME (rund) darunter, C unten mittig
        Small(g, new PointF(195, 104), On(ProButtons.Minus), "−");
        Small(g, new PointF(319, 104), On(ProButtons.Plus), "+");
        CaptureKey(g, new PointF(226, 144), On(ProButtons.Capture));
        Home(g, new PointF(289, 144), On(ProButtons.Home));
        SquareKey(g, new PointF(257, 256), On(ProButtons.C), "C");
        PlayerLeds(g, new PointF(CW / 2, 50));

        Gyro(g, p?.Motion);

        // Rücktasten GL/GR (bzw. SL/SR beim einzelnen Joy-Con) seitlich neben dem schmalen Oberteil, symmetrisch
        g.Restore(controller);
        bool gl = On(ProButtons.GL) || On(ProButtons.SLLeft) || On(ProButtons.SLRight);
        bool gr = On(ProButtons.GR) || On(ProButtons.SRLeft) || On(ProButtons.SRRight);
        BackButton(g, new RectangleF(10, 196, 40, 26), gl, joyCon ? "SL" : "GL");
        BackButton(g, new RectangleF(W - 50, 196, 40, 26), gr, joyCon ? "SR" : "GR");
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

    /// <summary>
    /// Linke Hälfte des Umrisses, vom offiziellen Frontfoto übertragen (oben Mitte → über den linken Griff → Bogen
    /// unten Mitte). Typisch für das Original: Oberteil schmaler als die Griffe, die Griffe laufen nach unten schräg
    /// nach außen, dazwischen ein breiter, flacher Bogen. Die rechte Hälfte ist gespiegelt.
    /// </summary>
    private static readonly PointF[] BodyLeft =
    [
        new(CW / 2, 40.7f),                                                       // oben Mitte
        new(230, 40.7f), new(200, 40.8f), new(181, 40.9f),                        // Oberkante (gerade)
        new(120, 41.5f), new(68, 50), new(57.6f, 75.7f),                          // obere Ecke (unter der Schultertaste)
        new(50, 95), new(48.5f, 150), new(49, 194.8f),                            // schmales Oberteil, fast senkrecht
        new(49, 225), new(32, 262), new(21.8f, 294),                              // Übergang in den Griff, nach außen
        new(10, 325), new(1, 350), new(0, 373.4f),                                // Griff außen
        new(-1, 392), new(15, 398), new(29.8f, 397.3f),                           // rundes Griffende
        new(48, 397), new(62, 393), new(69.5f, 385),
        new(78, 365), new(90, 340), new(105.2f, 320),                             // Griff innen, schräg nach oben
        new(118, 303), new(132, 296), new(148.9f, 294),
        new(180, 293), new(230, 293.3f), new(CW / 2, 293.3f),                     // breiter, flacher Bogen
    ];

    private static GraphicsPath BodyPath()
    {
        // Linke Hälfte rückwärts (unten Mitte → über den linken Griff → oben Mitte), dann rechte Hälfte gespiegelt.
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddBeziers(BodyLeft.Reverse().ToArray());
        path.AddBeziers(BodyLeft.Select(p => new PointF(CW - p.X, p.Y)).ToArray());
        path.CloseFigure();
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

        // Gehäuse, matt mit leichtem Verlauf über die ganze Höhe
        using (var fill = new LinearGradientBrush(new RectangleF(0, 36, CW, 370),
                   Mix(_body, Color.White, 0.12f), Mix(_body, Color.Black, 0.2f), LinearGradientMode.Vertical))
            g.FillPath(fill, path);

        var clip = g.Save();
        g.SetClip(path);
        // Griffe zum Ende hin etwas dunkler (gerundete Form)
        using (var shade = new LinearGradientBrush(new RectangleF(0, 290, CW, 116),
                   Color.FromArgb(0, 0, 0, 0), Color.FromArgb(70, 0, 0, 0), LinearGradientMode.Vertical))
            g.FillRectangle(shade, 0, 291, CW, 115);
        // Abgesetzte Frontplatte (beim Original als feine Kante sichtbar)
        using (var plate = Rounded(new RectangleF(53, 8, CW - 106, 268), 44)) // obere Ecken liegen außerhalb (abgeschnitten)
        using (var line = new Pen(Color.FromArgb(40, 255, 255, 255), 1.2f))
            g.DrawPath(line, plate);
        // Glanzkante oben
        using (var shine = new Pen(Color.FromArgb(45, 255, 255, 255), 2f))
            g.DrawBezier(shine, 120, 46, 200, 43, 320, 43, 400, 46);
        g.Restore(clip);

        using (var pen = new Pen(Mix(_body, Color.White, 0.3f), 1.5f))
            g.DrawPath(pen, path);
    }

    // ---------- Schulter- und Triggertasten (Blick von vorn) ----------

    /// <summary>Spiegelt einen Punkt der linken Seite für die rechte.</summary>
    private static PointF M(bool left, float x, float y) => new(left ? x : CW - x, y);

    /// <summary>
    /// Schultertaste L/R: helle Sichel, die die obere Gehäuseecke umfasst (innen = Gehäusekante, außen etwas größer).
    /// </summary>
    private static GraphicsPath BumperPath(bool left)
    {
        var path = new GraphicsPath();
        path.AddBeziers(
        [
            M(left, 184, 41),
            M(left, 172, 32), M(left, 130, 29), M(left, 105, 30),                      // Oberkante der Sichel
            M(left, 78, 31), M(left, 56, 40), M(left, 48, 58),                         // um die Ecke
            M(left, 44, 68), M(left, 46, 78), M(left, 50, 86),                         // ausläufig zur Seite
            // zurück entlang der Gehäusekante (wird vom Gehäuse verdeckt)
            M(left, 51, 80), M(left, 53, 78), M(left, 57.6f, 75.7f),
            M(left, 68, 50), M(left, 120, 41.5f), M(left, 181, 40.9f),
            M(left, 182, 41), M(left, 183, 41), M(left, 184, 41),
        ]);
        path.CloseFigure();
        return path;
    }

    private void Pro2Bumper(Graphics g, bool left, bool on)
    {
        using var path = BumperPath(left);
        if (on)
        {
            using var glow = new Pen(AccentGlow, 7f);
            g.DrawPath(glow, path);
        }
        var bounds = path.GetBounds();
        using (var fill = new LinearGradientBrush(bounds, on ? Accent : Mix(_grip, Color.White, 0.3f),
                   on ? Mix(Accent, Color.Black, 0.2f) : Mix(_grip, Color.Black, 0.15f), LinearGradientMode.Vertical))
            g.FillPath(fill, path);
        using (var edge = new Pen(on ? Color.FromArgb(160, 235, 255) : Mix(_grip, Color.Black, 0.3f), 1.2f))
            g.DrawPath(edge, path);
        Caption(g, new RectangleF(left ? 92 : CW - 132, 30, 40, 12), left ? "L" : "R", 8.5f,
            on ? Color.White : Mix(_grip, Color.Black, 0.65f));
    }

    /// <summary>Trigger ZL/ZR: hinter der Schultertaste, nur der obere Rand ist von vorn sichtbar.</summary>
    private void Pro2Trigger(Graphics g, bool left, float value)
    {
        var r = new RectangleF(left ? 62 : CW - 176, 8, 114, 40);
        using var path = Rounded(r, 12);
        using (var back = new LinearGradientBrush(r, Mix(_body, Color.White, 0.14f), Mix(_body, Color.Black, 0.25f), LinearGradientMode.Vertical))
            g.FillPath(back, path);
        value = Math.Clamp(value, 0f, 1f);
        if (value > 0.02f)
        {
            // Analog: füllt sich von außen nach innen, je weiter der Trigger gedrückt ist.
            var state = g.Save();
            g.SetClip(path);
            float w = r.Width * value;
            using var fill = new SolidBrush(Accent);
            g.FillRectangle(fill, left ? r.X : r.Right - w, r.Y, w, r.Height);
            g.Restore(state);
        }
        using (var edge = new Pen(value > 0.02f ? Accent : KeyEdge, 1.3f))
            g.DrawPath(edge, path);
        Caption(g, new RectangleF(r.X + 30, 10, 54, 16), left ? "ZL" : "ZR", 8.5f, value > 0.5f ? Color.White : FaceText);
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

    /// <summary>Steuerkreuz; Farben wahlweise eigene (z. B. weiß bei der Wii-Fernbedienung), sonst nach dem Gehäuse.</summary>
    private void DPad(Graphics g, PointF c, bool up, bool down, bool left, bool right, Color? fill = null, Color? border = null, Color? glyphs = null)
    {
        const float arm = 28, len = 43;
        using (var cross = new GraphicsPath { FillMode = FillMode.Winding })
        {
            using (var vertical = Rounded(new RectangleF(c.X - arm / 2, c.Y - len, arm, len * 2), 6))
                cross.AddPath(vertical, false);
            using (var horizontal = Rounded(new RectangleF(c.X - len, c.Y - arm / 2, len * 2, arm), 6))
                cross.AddPath(horizontal, false);
            if (fill is { } light)
            {
                // Helles Kreuz (Wii): leichter Schatten darunter, Fläche mit sanftem Verlauf.
                var shadowState = g.Save();
                g.TranslateTransform(0, 2.5f);
                using (var shadow = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
                    g.FillPath(shadow, cross);
                g.Restore(shadowState);
                using var gradient = new LinearGradientBrush(new RectangleF(c.X - len, c.Y - len, len * 2, len * 2),
                    Mix(light, Color.White, 0.6f), Mix(light, Color.Black, 0.08f), LinearGradientMode.ForwardDiagonal);
                g.FillPath(gradient, cross);
            }
            else
            {
                using var well = new SolidBrush(KeyFill);
                g.FillPath(well, cross);
            }
            using var edge = new Pen(border ?? KeyEdge, 1.2f);
            g.DrawPath(edge, cross);
        }
        void Arm(bool on, RectangleF r, string glyph)
        {
            if (on)
            {
                using var lit = new SolidBrush(Accent);
                g.FillRectangle(lit, r);
            }
            Caption(g, r, glyph, 8f, on ? Color.White : glyphs ?? FaceText);
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
        var area = new RectangleF(160, 412, 200, 14);
        using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var label = new SolidBrush(Color.FromArgb(170, 175, 185));
        if (motion is not { } m)
        {
            using var format = new StringFormat { Alignment = StringAlignment.Center };
            g.DrawString(Tr.T("Bewegungssensor: keine Daten"), font, label, area, format);
            return;
        }
        (string Axis, short Value)[] axes = [("Gyro X", m.GyroX), ("Y", m.GyroY), ("Z", m.GyroZ)];
        float w = (area.Width - 20) / 3;
        for (int i = 0; i < 3; i++)
        {
            var r = new RectangleF(area.X + i * (w + 10), area.Y + 8, w, 8);
            using (var back = new SolidBrush(Theme.Current.Border))
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
