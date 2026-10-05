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
    /// <summary>Tasten dunkel mit hellen Beschriftungen zeichnen (Joy-Con: schwarze Tasten auf neonfarbenem Gehäuse).</summary>
    private bool _darkButtons;

    private Color KeyFill => _darkButtons ? Color.FromArgb(0x20, 0x20, 0x24) : Mix(_body, Color.Black, 0.35f);
    private Color KeyEdge => _darkButtons ? Color.FromArgb(0x3A, 0x3A, 0x40) : Mix(_body, Color.White, 0.25f);
    private Color FaceFill => _darkButtons ? Color.FromArgb(0x24, 0x24, 0x28) : Mix(_body, Color.Black, 0.25f);
    private Color FaceText => _darkButtons ? Color.FromArgb(0xDE, 0xDE, 0xE4) : _buttons;

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
        var p = _input;
        bool On(ProButtons b) => p is not null && p.Has(b);
        if (_input is { Kind: var kind } && IsRetro(kind))
        {
            PaintRetro(g, kind);
            return;
        }
        // Sony-Pads haben ein eigenes Layout (Sticks symmetrisch unten, Touchpad mittig) – eigene Zeichnung.
        if (p?.Kind is ControllerKind.DualShock4 or ControllerKind.DualSense)
        {
            PaintPlayStation(g, p.Kind, On);
            return;
        }

        // Umriss und alle Tastenpositionen 1:1 vom offiziellen Produktfoto (Frontansicht), wie bei den anderen Controllern.
        var f = Frame(307, 1600, 92, 1012);
        // Schultertasten aus der Gehäusefarbe abgeleitet: Trigger ZL/ZR dezent, Schultertasten L/R als hellere Taste.
        var triggerColor = Mix(_body, Color.White, 0.13f);
        var bumperColor = Mix(_body, Color.White, 0.30f);

        // Hinten die Trigger ZL/ZR (unterer Rand vom Gehäuse verdeckt), dann das Gehäuse, dann die Schultertasten L/R.
        var (lt, rt, lb, rb) = p?.Kind switch
        {
            ControllerKind.XboxController => ("LT", "RT", "LB", "RB"),
            ControllerKind.DualShock4 or ControllerKind.DualSense => ("L2", "R2", "L1", "R1"),
            _ => ("ZL", "ZR", "L", "R"),
        };
        Shoulder(g, f.R(448, 44, 246, 70), On(ProButtons.ZL), lt, triggerColor, _gamepad.LeftTrigger / 255f, tucked: true);
        Shoulder(g, f.R(1213, 44, 246, 70), On(ProButtons.ZR), rt, triggerColor, _gamepad.RightTrigger / 255f, tucked: true);
        DrawPro2Body(g, f);
        Shoulder(g, f.R(430, 102, 268, 52), On(ProButtons.L), lb, bumperColor);
        Shoulder(g, f.R(1202, 102, 268, 52), On(ProButtons.R), rb, bumperColor);

        // Links: Stick oben außen, Steuerkreuz darunter weiter innen
        Stick(g, f.P(575, 372), _gamepad.LeftX, _gamepad.LeftY, On(ProButtons.LeftStick), f.S(92), f.S(62));
        PhotoDPad(g, f, 765, 558, 96, On);

        // Rechts: Tasten oben außen (X oben, A rechts, B unten, Y links), Stick darunter weiter innen.
        // Beschriftung je nach Controller-Art: Xbox = A/B/X/Y in den Originalfarben, Nintendo = A/B/X/Y.
        bool xbox = p?.Kind == ControllerKind.XboxController;
        var (top, right, bottom, left) = xbox ? ("Y", "B", "A", "X") : ("X", "A", "B", "Y");
        Face(g, f.P(1292, 278), On(ProButtons.X), top, f.S(50), xbox ? XboxYellow : null);
        Face(g, f.P(1388, 372), On(ProButtons.A), right, f.S(50), xbox ? XboxRed : null);
        Face(g, f.P(1290, 462), On(ProButtons.B), bottom, f.S(50), xbox ? XboxGreen : null);
        Face(g, f.P(1190, 372), On(ProButtons.Y), left, f.S(50), xbox ? XboxBlue : null);
        Stick(g, f.P(1095, 558), _gamepad.RightX, _gamepad.RightY, On(ProButtons.RightStick), f.S(92), f.S(62));

        // Mitte: − / + oben, Aufnahme (eckig) / HOME (rund) darunter; C nur beim Switch-2-Pro.
        // Xbox: Ansicht/Menü und Xbox-Taste.
        Face(g, f.P(795, 272), On(ProButtons.Minus), xbox ? "▤" : "−", f.S(28));
        Face(g, f.P(1110, 272), On(ProButtons.Plus), xbox ? "☰" : "+", f.S(28));
        CaptureKey(g, f.P(873, 372), On(ProButtons.Capture));
        if (xbox)
            XboxHome(g, f.P(1035, 372), On(ProButtons.Home));
        else
            Home(g, f.P(1035, 372), On(ProButtons.Home));
        // C-Taste und GL/GR gibt es nur am Switch-2-Pro-Controller, nicht am Switch-1-Pro.
        bool switch2 = p?.Kind == ControllerKind.Pro2;
        if (switch2)
            SquareKey(g, f.P(955, 650), On(ProButtons.C), "C");
        PlayerLeds(g, f.P(830, 120));

        Gyro(g, p?.Motion);

        // Rücktasten GL/GR (auf der Rückseite der Griffe) – als Tasten auf den Griffen angedeutet, nicht lose daneben.
        if (switch2)
        {
            GripButton(g, f.R(340, 646, 124, 54), On(ProButtons.GL), "GL");
            GripButton(g, f.R(1443, 646, 124, 54), On(ProButtons.GR), "GR");
        }
    }

    /// <summary>Rücktaste GL/GR als flache Taste auf dem Griff (leuchtet beim Drücken).</summary>
    private void GripButton(Graphics g, RectangleF r, bool on, string text)
    {
        using var path = Rounded(r, r.Height / 2);
        if (on)
        {
            using var glow = new Pen(AccentGlow, 6f);
            g.DrawPath(glow, path);
        }
        using (var fill = new SolidBrush(on ? Accent : Mix(_body, Color.Black, 0.28f)))
            g.FillPath(fill, path);
        using (var edge = new Pen(on ? Color.FromArgb(160, 235, 255) : Mix(_body, Color.White, 0.18f), 1.2f))
            g.DrawPath(edge, path);
        Caption(g, r, text, Math.Clamp(r.Height * 0.42f, 7f, 9f), on ? Color.White : Mix(_body, Color.White, 0.7f));
    }

    /// <summary>Gehäuse des Pro Controller 2: Umriss vom Foto, matte Fläche mit leichtem Verlauf und feiner Glanzkante.</summary>
    private void DrawPro2Body(Graphics g, PhotoFrame f)
    {
        using var path = f.Outline(PhotoOutlines.Pro2, symmetric: true);
        using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
        {
            var st = g.Save();
            g.TranslateTransform(0, 6);
            g.FillPath(shadow, path);
            g.Restore(st);
        }
        var b = path.GetBounds();
        using (var fill = new LinearGradientBrush(b, Mix(_body, Color.White, 0.12f), Mix(_body, Color.Black, 0.2f), LinearGradientMode.Vertical))
            g.FillPath(fill, path);
        var clip = g.Save();
        g.SetClip(path);
        using (var shade = new LinearGradientBrush(new RectangleF(b.X, b.Y + b.Height * 0.55f, b.Width, b.Height * 0.5f),
                   Color.FromArgb(0, 0, 0, 0), Color.FromArgb(70, 0, 0, 0), LinearGradientMode.Vertical))
            g.FillRectangle(shade, b.X, b.Y + b.Height * 0.55f, b.Width, b.Height * 0.5f);
        using (var shine = new Pen(Color.FromArgb(40, 255, 255, 255), 2f))
            g.DrawBezier(shine, b.X + b.Width * 0.28f, b.Y + 10, b.X + b.Width * 0.42f, b.Y + 5,
                b.X + b.Width * 0.58f, b.Y + 5, b.X + b.Width * 0.72f, b.Y + 10);
        g.Restore(clip);
        using (var pen = new Pen(Mix(_body, Color.White, 0.3f), 1.5f))
            g.DrawPath(pen, path);
    }

    /// <summary>Spielernummer für die LED-Anzeige (0–7), −1 = aus.</summary>
    public int PlayerIndex { get; set; } = -1;

    /// <summary>Farbe der Lichtleiste, die das Spiel dem Controller gegeben hat (Sony-Pads), null = Standard.</summary>
    public Color? LightbarTint { get; set; }

    // Tastenfarben des Xbox-Controllers (Buchstaben auf schwarzen Tasten).
    private static readonly Color XboxGreen = Color.FromArgb(0x44, 0xB2, 0x3F);
    private static readonly Color XboxRed = Color.FromArgb(0xE0, 0x46, 0x3E);
    private static readonly Color XboxBlue = Color.FromArgb(0x4E, 0x8F, 0xD8);
    private static readonly Color XboxYellow = Color.FromArgb(0xF0, 0xB6, 0x10);

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

    // ---------- Bedienelemente ----------

    private void Face(Graphics g, PointF c, bool on, string text, float r = 20.5f, Color? glyph = null)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
        Fill(g, path, on, FaceFill);
        Caption(g, new RectangleF(c.X - r, c.Y - r, r * 2, r * 2), text, r * 0.63f, on ? Color.White : glyph ?? FaceText);
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

    /// <summary>Xbox-Taste: runde Taste mit dem Xbox-X (Ring und Kreuz) statt des Hauses.</summary>
    private void XboxHome(Graphics g, PointF c, bool on)
    {
        const float r = 13;
        using var path = new GraphicsPath();
        path.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
        Fill(g, path, on, KeyFill);
        var col = on ? Color.White : FaceText;
        using (var ring = new Pen(col, 1.4f))
            g.DrawEllipse(ring, c.X - r + 3.5f, c.Y - r + 3.5f, (r - 3.5f) * 2, (r - 3.5f) * 2);
        float d = r * 0.38f;
        using var x = new Pen(col, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(x, c.X - d, c.Y - d, c.X + d, c.Y + d);
        g.DrawLine(x, c.X - d, c.Y + d, c.X + d, c.Y - d);
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
                    Mix(light, Color.White, light.GetBrightness() > 0.5f ? 0.6f : 0.12f), Mix(light, Color.Black, 0.08f), LinearGradientMode.ForwardDiagonal);
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

    /// <summary>
    /// Stick mit Mulde und Kappe. Farben wahlweise eigene (z. B. graue Kappe beim GameCube), sonst nach dem Gehäuse;
    /// <paramref name="octagon"/>: achteckige Führung wie bei GameCube, N64 und Classic Controller.
    /// </summary>
    private void Stick(Graphics g, PointF c, short x, short y, bool pressed, float well = 41, float cap = 27,
        Color? capColor = null, Color? wellColor = null, bool octagon = false)
    {
        (x, y) = Smooth(c, x, y);
        var wellFill = wellColor ?? Mix(_body, Color.Black, 0.55f);
        using (var wellPath = new GraphicsPath())
        {
            if (octagon)
                wellPath.AddPolygon(Enumerable.Range(0, 8).Select(i =>
                    new PointF(c.X + well * MathF.Cos((i * 45 + 22.5f) * MathF.PI / 180), c.Y + well * MathF.Sin((i * 45 + 22.5f) * MathF.PI / 180))).ToArray());
            else
                wellPath.AddEllipse(c.X - well, c.Y - well, well * 2, well * 2);
            // Erhabener Rand um die Mulde (wie beim Original)
            using (var outer = new Pen(Mix(wellFill, Color.White, 0.18f), 3f))
                g.DrawPath(outer, wellPath);
            using (var wellBrush = new SolidBrush(wellFill))
                g.FillPath(wellBrush, wellPath);
            using var ring = new Pen(Mix(wellFill, Color.Black, 0.3f), 1.2f);
            g.DrawPath(ring, wellPath);
        }

        float travel = well - cap + 2;
        float px = c.X + x / 32767f * travel, py = c.Y - y / 32767f * travel;
        bool moved = Math.Abs(x) > 1500 || Math.Abs(y) > 1500;
        if (pressed)
        {
            using var glow = new SolidBrush(AccentGlow);
            g.FillEllipse(glow, px - cap - 5, py - cap - 5, (cap + 5) * 2, (cap + 5) * 2);
        }
        var capBase = capColor ?? _body;
        using (var capBrush = new LinearGradientBrush(new RectangleF(px - cap, py - cap, cap * 2, cap * 2),
                   Mix(capBase, Color.White, 0.25f), Mix(capBase, Color.Black, 0.2f), LinearGradientMode.ForwardDiagonal))
            g.FillEllipse(capBrush, px - cap, py - cap, cap * 2, cap * 2);
        var edgeColor = capColor is { } cc ? Mix(cc, Color.Black, 0.3f) : KeyEdge;
        using (var capEdge = new Pen(pressed || moved ? Accent : edgeColor, pressed ? 2.5f : 1.5f))
            g.DrawEllipse(capEdge, px - cap, py - cap, cap * 2, cap * 2);
        float dotR = Math.Max(3f, cap * 0.18f);
        using var dot = new SolidBrush(moved || pressed ? Accent : edgeColor);
        g.FillEllipse(dot, px - dotR, py - dotR, dotR * 2, dotR * 2);
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
        // Pfeile als gleich große Dreiecke statt Schriftzeichen (◀ ▶ sind in Segoe UI kleiner als ▲ ▼). Sie drehen mit dem
        // Controller mit: am quer gehaltenen Joy-Con zeigt die Taste oben dann nach oben, wie auf dem echten Gerät.
        if (text is "▲" or "▼" or "◀" or "▶")
        {
            Arrow(g, new PointF(r.X + r.Width / 2, r.Y + r.Height / 2), text, size, color);
            return;
        }
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

    /// <summary>Gleichseitiges Dreieck um den Mittelpunkt (Schwerpunkt), Größe wie ein Schriftzeichen der Größe <paramref name="size"/>.</summary>
    private static void Arrow(Graphics g, PointF c, string direction, float size, Color color)
    {
        float h = size * 0.95f, half = h / MathF.Sqrt(3f); // Höhe, halbe Grundseite
        // Nach oben zeigend; Schwerpunkt bei 2/3 der Höhe von der Spitze.
        PointF[] up = [new(0, -h * 2 / 3), new(half, h / 3), new(-half, h / 3)];
        float angle = direction switch { "▶" => 90, "▼" => 180, "◀" => 270, _ => 0 } * MathF.PI / 180;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        var points = up.Select(p => new PointF(c.X + p.X * cos - p.Y * sin, c.Y + p.X * sin + p.Y * cos)).ToArray();
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(color))
            g.FillPolygon(brush, points);
        g.SmoothingMode = smoothing;
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
