using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Controller nach Produktfotos: Umriss 1:1 aus dem Foto (siehe <see cref="PhotoOutlines"/>), alle Tasten an ihrer
/// Foto-Position und in Foto-Größe. Alle Controller teilen dieselbe Bühne und denselben Zeichenstil (Schatten,
/// Verlauf, Kanten, Tasten, Leuchten beim Drücken) – so wirken sie einheitlich.
/// </summary>
internal sealed partial class InputView
{
    /// <summary>Gemeinsame Bühne: jeder Controller wird so groß wie möglich hineingesetzt (gleiche Wirkung für alle).</summary>
    private static readonly RectangleF Stage = new(26, 34, W - 52, H - 80);

    /// <summary>Rechnet Foto-Pixel in die Zeichenfläche um (gleicher Maßstab in x und y, zentriert auf der Bühne).</summary>
    private readonly struct PhotoFrame
    {
        public readonly float Scale, X0, Y0;

        public PhotoFrame(RectangleF photo, RectangleF stage)
        {
            Scale = Math.Min(stage.Width / photo.Width, stage.Height / photo.Height);
            X0 = stage.X + (stage.Width - photo.Width * Scale) / 2 - photo.X * Scale;
            Y0 = stage.Y + (stage.Height - photo.Height * Scale) / 2 - photo.Y * Scale;
        }

        public PointF P(float x, float y) => new(X0 + x * Scale, Y0 + y * Scale);
        public float S(float v) => v * Scale;
        public RectangleF R(float x, float y, float w, float h) => new(X0 + x * Scale, Y0 + y * Scale, w * Scale, h * Scale);

        /// <summary>
        /// Umriss aus den Foto-Punkten als glatte geschlossene Kurve. <paramref name="symmetric"/>: linke Hälfte
        /// gespiegelt (gleicht leichte Schräglage und Schatten des Fotos aus, für spiegelgleiche Controller).
        /// </summary>
        public GraphicsPath Outline(string points, bool symmetric = false, float tension = 0.3f, bool mirrorRight = false)
        {
            var path = new GraphicsPath();
            var p = this;
            var source = ParsePoints(points);
            if (symmetric && mirrorRight)
                source = Symmetric(Mirror(source)); // rechte Hälfte gespiegelt (wenn sie auf dem Foto sauberer ist)
            else if (symmetric)
                source = Symmetric(source);
            path.AddClosedCurve(source.Select(q => p.P(q.X, q.Y)).ToArray(), tension);
            return path;
        }
    }

    /// <summary>
    /// Spiegelgleicher Umriss: von der oberen Mitte die linke Hälfte entlang bis zur Mitte unten, dann gespiegelt zurück.
    /// </summary>
    /// <summary>
    /// Umriss aus dem linken oberen Viertel, in beide Richtungen gespiegelt (für Controller, die oben wie unten
    /// gleich geformt sind, wenn der untere Fotorand im Schatten liegt). <paramref name="centerY"/>: Mitte in Foto-Pixeln.
    /// </summary>
    private static PointF[] QuadrantSymmetric(PointF[] pts, float centerY)
    {
        var sym = Symmetric(pts);
        float cx = sym[0].X;
        var quarter = new List<PointF>();
        foreach (var p in sym)
        {
            if (p.Y >= centerY)
            {
                var last = quarter[^1];
                float t = (centerY - last.Y) / (p.Y - last.Y);
                quarter.Add(new PointF(last.X + (p.X - last.X) * t, centerY));
                break;
            }
            quarter.Add(p);
        }
        var left = quarter.Concat(quarter.Take(quarter.Count - 1).Reverse().Select(p => new PointF(p.X, 2 * centerY - p.Y))).ToList();
        var right = left.Skip(1).Take(left.Count - 2).Reverse().Select(p => new PointF(2 * cx - p.X, p.Y));
        return [.. left, .. right];
    }

    private static PointF[] Mirror(PointF[] pts)
    {
        float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
        return pts.Select(p => new PointF(minX + maxX - p.X, p.Y)).ToArray();
    }

    private static PointF[] Symmetric(PointF[] pts)
    {
        float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), cx = (minX + maxX) / 2;
        int n = pts.Length;
        // Start: höchster Punkt nahe der Mitte
        int start = Enumerable.Range(0, n).Where(i => Math.Abs(pts[i].X - cx) < (maxX - minX) * 0.15f)
            .OrderBy(i => pts[i].Y).First();
        // Richtung, in der es nach links geht
        int dir = pts[(start + 1) % n].X < pts[(start - 1 + n) % n].X ? 1 : -1;
        var left = new List<PointF> { new(cx, pts[start].Y) };
        var previous = pts[start];
        for (int k = 1; k < n; k++)
        {
            var q = pts[((start + dir * k) % n + n) % n];
            if (q.X > cx && left.Count > 3)
            {
                // Mitte unten erreicht: Schnittpunkt mit der Mittelachse
                float t = (cx - previous.X) / (q.X - previous.X);
                left.Add(new PointF(cx, previous.Y + (q.Y - previous.Y) * t));
                break;
            }
            if (q.X <= cx)
                left.Add(q);
            previous = q;
        }
        var right = left.Skip(1).Take(left.Count - 2).Reverse().Select(q => new PointF(2 * cx - q.X, q.Y));
        return [.. left, .. right];
    }

    private static readonly Dictionary<string, PointF[]> ParsedOutlines = [];

    private static PointF[] ParsePoints(string points)
    {
        if (ParsedOutlines.TryGetValue(points, out var cached))
            return cached;
        var numbers = points.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        var result = new PointF[numbers.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = new PointF(numbers[2 * i], numbers[2 * i + 1]);
        return ParsedOutlines[points] = result;
    }

    /// <summary>Rahmen eines Fotos (Foto-Pixel des Controllers) auf der gemeinsamen Bühne.</summary>
    private static PhotoFrame Frame(float x0, float x1, float y0, float y1) => new(new RectangleF(x0, y0, x1 - x0, y1 - y0), Stage);

    /// <summary>Steuerkreuz an Foto-Position und in Foto-Größe (halbe Länge eines Arms in Foto-Pixeln).</summary>
    private void PhotoDPad(Graphics g, PhotoFrame f, float x, float y, float halfLength, Func<ProButtons, bool> on,
        Color? fill = null, Color? border = null, Color? glyphs = null)
    {
        var state = g.Save();
        var c = f.P(x, y);
        g.TranslateTransform(c.X, c.Y);
        float k = f.S(halfLength) / 43f;
        g.ScaleTransform(k, k);
        DPad(g, PointF.Empty, on(ProButtons.Up), on(ProButtons.Down), on(ProButtons.Left), on(ProButtons.Right), fill, border, glyphs);
        g.Restore(state);
    }

    /// <summary>
    /// Schultertaste als abgerundete Fläche an ihrer Foto-Position (leuchtet beim Drücken). <paramref name="tucked"/>:
    /// liegt hinter der Gehäuse-Oberkante (vor dem Gehäuse gezeichnet) – die Beschriftung sitzt dann im sichtbaren oberen Teil.
    /// </summary>
    private void Shoulder(Graphics g, RectangleF r, bool on, string text, Color color, float value = 0, bool tucked = false)
    {
        using var path = Rounded(r, Math.Min(r.Height / 2, 10));
        if (on)
        {
            using var glow = new Pen(AccentGlow, 6f);
            g.DrawPath(glow, path);
        }
        using (var brush = new LinearGradientBrush(r, on ? Accent : Mix(color, Color.White, 0.15f),
                   on ? Mix(Accent, Color.Black, 0.15f) : Mix(color, Color.Black, 0.12f), LinearGradientMode.Vertical))
            g.FillPath(brush, path);
        if (!on && value > 0.02f)
        {
            // Analoger Trigger: füllt sich mit dem Druck.
            var state = g.Save();
            g.SetClip(path);
            using var fill = new SolidBrush(Color.FromArgb(170, Accent));
            g.FillRectangle(fill, r.X, r.Y, r.Width * Math.Clamp(value, 0f, 1f), r.Height);
            g.Restore(state);
        }
        using (var edge = new Pen(on ? Color.FromArgb(160, 235, 255) : Mix(color, Color.Black, 0.35f), 1.2f))
            g.DrawPath(edge, path);
        var label = on || value > 0.5f ? Color.White : color.GetBrightness() > 0.55f ? Mix(color, Color.Black, 0.6f) : Mix(color, Color.White, 0.7f);
        var captionRect = tucked ? new RectangleF(r.X, r.Y, r.Width, r.Height * 0.5f) : r;
        Caption(g, captionRect, text, Math.Clamp(captionRect.Height * (tucked ? 0.62f : 0.42f), 6.5f, 10f), label);
    }

    /// <summary>Beschriftung unter einer Taste (z. B. SELECT, START) in Foto-Position.</summary>
    private static void Label(Graphics g, PhotoFrame f, float x, float y, string text, Color color, float size = 6.8f) =>
        Caption(g, new RectangleF(f.P(x, y).X - 50, f.P(x, y).Y - 8, 100, 16), text, size, color);

    /// <summary>
    /// Schultertaste, die sich als Kappe um die Gehäuse-Oberkante legt: Keil von <paramref name="x0"/> bis
    /// <paramref name="x1"/> mit schräger, gewölbter Oberkante (yT0 → yT1 folgt dem Gehäuserand) und leicht
    /// mitwölbender Unterkante (yB0 → yB1). Wird nach dem Gehäuse gezeichnet; Trigger-Kappen höher und vor den
    /// Schultertasten-Kappen zeichnen.
    /// <paramref name="labelDy"/>: Abstand der Beschriftung unter der Oberkanten-Mitte.
    /// </summary>
    private void EdgeShoulder(Graphics g, PhotoFrame f, float x0, float x1, float yT0, float yT1, float yB0, float yB1,
        bool on, string text, Color color, float value = 0, float labelDy = 16)
    {
        var p0 = f.P(x0, yT0);
        var p1 = f.P(x1, yT1);
        var p2 = f.P(x1, yB1);
        var p3 = f.P(x0, yB0);
        var pm = f.P((x0 + x1) / 2, Math.Min(yT0, yT1) - Math.Abs(yT1 - yT0) * 0.12f - 6);
        var pb = f.P((x0 + x1) / 2, Math.Max(yB0, yB1) + 10);
        using var path = new GraphicsPath();
        path.AddCurve([p0, pm, p1], 0.5f);
        path.AddLine(p1, p2);
        path.AddCurve([p2, pb, p3], 0.5f);   // Unterkante wölbt sich leicht mit dem Gehäuse
        path.CloseFigure();
        if (on)
        {
            using var glow = new Pen(AccentGlow, 6f);
            g.DrawPath(glow, path);
        }
        var bounds = path.GetBounds();
        using (var brush = new LinearGradientBrush(bounds, on ? Accent : Mix(color, Color.White, 0.15f),
                   on ? Mix(Accent, Color.Black, 0.15f) : Mix(color, Color.Black, 0.12f), LinearGradientMode.Vertical))
            g.FillPath(brush, path);
        if (!on && value > 0.02f)
        {
            // Analoger Trigger: füllt sich mit dem Druck.
            var clip = g.Save();
            g.SetClip(path);
            using var fill = new SolidBrush(Color.FromArgb(170, Accent));
            float xL = Math.Min(p0.X, p1.X), xR = Math.Max(p0.X, p1.X);
            g.FillRectangle(fill, xL, bounds.Y, (xR - xL) * Math.Clamp(value, 0f, 1f), bounds.Height);
            g.Restore(clip);
        }
        using (var edge = new Pen(on ? Color.FromArgb(160, 235, 255) : Mix(color, Color.Black, 0.35f), 1.2f))
            g.DrawPath(edge, path);
        // Beschriftung auf dem sichtbaren Band direkt unter der gewölbten Oberkante (Bézier-Mitte bei t = 0,5).
        var mid = new PointF((p0.X + 2 * pm.X + p1.X) / 4, (p0.Y + 2 * pm.Y + p1.Y) / 4 + labelDy * f.Scale);
        var label = on || value > 0.5f ? Color.White
            : color.GetBrightness() > 0.55f ? Mix(color, Color.Black, 0.6f) : Mix(color, Color.White, 0.7f);
        Caption(g, new RectangleF(mid.X - 40, mid.Y - 8, 80, 16), text, 8.5f, label);
    }

    // ---------- GameCube-Controller (Switch 2) ----------

    /// <summary>
    /// GameCube-Controller (Foto: Indigo-Original): großes A, rotes B, nierenförmige X/Y, gelber C-Stick,
    /// analoge L/R, Z oben rechts; die Switch-2-Version hat zusätzlich HOME, Aufnahme und C oben in der Mitte.
    /// Tastennamen nach Position (siehe ControllerButtons.Label): B-Bit = A, Y-Bit = B, A-Bit = X, X-Bit = Y.
    /// </summary>
    private void PaintGameCube(Graphics g, Func<ProButtons, bool> on)
    {
        var f = Frame(108, 1184, 92, 914);
        var body = Color.FromArgb(0x4B, 0x4E, 0xA6);
        var grey = Color.FromArgb(0xD4, 0xD4, 0xDC);
        // Schulterkante in Segmente: ZL (Switch-2-Version) außen links, L breit daneben; rechts R + Z an der Ecke.
        var zcol = Color.FromArgb(0x6B, 0x6E, 0xC8);
        using (var path = f.Outline(PhotoOutlines.GameCube))
            BodyShape(g, path, body);
        EdgeShoulder(g, f, 228, 324, 150, 102, 225, 178, on(ProButtons.ZL), "ZL", zcol, labelDy: 30);
        EdgeShoulder(g, f, 336, 560, 100, 58, 176, 132, on(ProButtons.L), "L", grey, _input?.LeftTrigger ?? 0, 36);
        EdgeShoulder(g, f, 720, 954, 58, 98, 132, 166, on(ProButtons.R), "R", grey, _input?.RightTrigger ?? 0, 36);
        EdgeShoulder(g, f, 966, 1082, 100, 150, 172, 222, on(ProButtons.ZR), "Z", zcol, labelDy: 30);

        Stick(g, f.P(318, 348), _gamepad.LeftX, _gamepad.LeftY, on(ProButtons.LeftStick), f.S(98), f.S(68),
            capColor: grey, wellColor: Mix(body, Color.Black, 0.3f), octagon: true);
        PhotoDPad(g, f, 465, 575, 72, on, fill: grey, border: Mix(grey, Color.Black, 0.3f), glyphs: Color.FromArgb(0x90, 0x90, 0x9A));
        Stick(g, f.P(815, 580), _gamepad.RightX, _gamepad.RightY, on(ProButtons.RightStick), f.S(86), f.S(56),
            capColor: Color.FromArgb(0xF2, 0xB8, 0x1A), wellColor: Mix(body, Color.Black, 0.3f), octagon: true);

        ColorKey(g, f.P(965, 345), on(ProButtons.B), "A", Color.FromArgb(0x2F, 0xC4, 0xA8), f.S(63));
        ColorKey(g, f.P(845, 405), on(ProButtons.Y), "B", Color.FromArgb(0xD2, 0x1E, 0x2A), f.S(41));
        PillKey(g, f.P(1080, 322), on(ProButtons.A), "", grey, 80, f.S(112), f.S(56));
        PillKey(g, f.P(933, 235), on(ProButtons.X), "", grey, -8, f.S(112), f.S(56));
        Caption(g, new RectangleF(f.P(1080, 322).X - 10, f.P(1080, 322).Y - 9, 20, 18), "X", 8f, Color.FromArgb(0x9A, 0x9A, 0xA6));
        Caption(g, new RectangleF(f.P(933, 235).X - 10, f.P(933, 235).Y - 9, 20, 18), "Y", 8f, Color.FromArgb(0x9A, 0x9A, 0xA6));

        ColorKey(g, f.P(642, 358), on(ProButtons.Plus), "", grey, f.S(26));
        Label(g, f, 642, 305, "START/PAUSE", Mix(body, Color.Black, 0.45f));
        var dark = Mix(body, Color.Black, 0.45f);
        ColorKey(g, f.P(572, 248), on(ProButtons.Capture), "●", dark, f.S(17), grey);
        ColorKey(g, f.P(642, 248), on(ProButtons.Home), "⌂", dark, f.S(17), grey);
        ColorKey(g, f.P(712, 248), on(ProButtons.C), "C", dark, f.S(17), grey);
    }

    // ---------- Wii U Pro Controller ----------

    private void PaintWiiUPro(Graphics g, Func<ProButtons, bool> on)
    {
        var f = Frame(92, 1184, 102, 803);
        var body = Color.FromArgb(0x2A, 0x2A, 0x2E);
        var key = Color.FromArgb(0x30, 0x30, 0x35);
        var text = Color.FromArgb(0xE0, 0xE0, 0xE6);
        // Schultern an der Oberkante: ZL/ZR als höhere Kappen, L/R davor auf der Kante.
        using (var path = f.Outline(PhotoOutlines.WiiUPro, symmetric: true))
            BodyShape(g, path, body);
        EdgeShoulder(g, f, 255, 424, 82, 66, 155, 138, on(ProButtons.L), "L", Color.FromArgb(0x5E, 0x5E, 0x66), labelDy: 32);
        EdgeShoulder(g, f, 436, 565, 66, 100, 138, 173, on(ProButtons.ZL), "ZL", Color.FromArgb(0x48, 0x48, 0x4E), labelDy: 32);
        EdgeShoulder(g, f, 715, 844, 100, 66, 173, 138, on(ProButtons.ZR), "ZR", Color.FromArgb(0x48, 0x48, 0x4E), labelDy: 32);
        EdgeShoulder(g, f, 856, 1025, 66, 92, 138, 165, on(ProButtons.R), "R", Color.FromArgb(0x5E, 0x5E, 0x66), labelDy: 32);

        Stick(g, f.P(335, 215), _gamepad.LeftX, _gamepad.LeftY, on(ProButtons.LeftStick), f.S(92), f.S(60), key, Color.FromArgb(0x1A, 0x1A, 0x1E));
        Stick(g, f.P(945, 215), _gamepad.RightX, _gamepad.RightY, on(ProButtons.RightStick), f.S(92), f.S(60), key, Color.FromArgb(0x1A, 0x1A, 0x1E));
        PhotoDPad(g, f, 440, 380, 85, on, fill: Color.FromArgb(0x24, 0x24, 0x28), border: Color.FromArgb(0x55, 0x55, 0x5C), glyphs: text);

        ColorKey(g, f.P(557, 265), on(ProButtons.Minus), "−", key, f.S(22), text);
        ColorKey(g, f.P(637, 268), on(ProButtons.Home), "⌂", key, f.S(24), text);
        ColorKey(g, f.P(727, 265), on(ProButtons.Plus), "+", key, f.S(22), text);
        var muted = Color.FromArgb(0xB0, 0xB0, 0xB8);
        Label(g, f, 550, 304, "SELECT", muted, 6f);
        Label(g, f, 637, 304, "HOME", muted, 6f);
        Label(g, f, 727, 304, "START", muted, 6f);
        ColorKey(g, f.P(637, 378), false, "⏻", key, f.S(22), Color.FromArgb(0xE0, 0x50, 0x48));

        ColorKey(g, f.P(837, 310), on(ProButtons.X), "X", key, f.S(34), text);
        ColorKey(g, f.P(760, 370), on(ProButtons.Y), "Y", key, f.S(34), text);
        ColorKey(g, f.P(910, 370), on(ProButtons.A), "A", key, f.S(34), text);
        ColorKey(g, f.P(837, 433), on(ProButtons.B), "B", key, f.S(34), text);
        PhotoLeds(g, f, 565, 705, 503, Color.FromArgb(70, 160, 255));
    }

    /// <summary>Vier Spieler-LEDs nebeneinander (Foto-Position), leuchtend nach Spielernummer.</summary>
    private void PhotoLeds(Graphics g, PhotoFrame f, float x0, float x1, float y, Color lit)
    {
        byte mask = PlayerIndex >= 0 ? Commands.PlayerLedMask(PlayerIndex) : (byte)0;
        for (int i = 0; i < 4; i++)
        {
            var c = f.P(x0 + (x1 - x0) * i / 3f, y);
            bool on = (mask & (1 << i)) != 0;
            using var led = new SolidBrush(on ? lit : Color.FromArgb(0x55, 0x55, 0x5C));
            g.FillRectangle(led, c.X - 3, c.Y - 1.5f, 6, 3);
        }
    }

    // ---------- Nintendo 64 ----------

    private void PaintN64(Graphics g, Func<ProButtons, bool> on)
    {
        var f = Frame(28, 1252, 53, 1235);
        var body = Color.FromArgb(0xB4, 0xB4, 0xB8);
        var dark = Color.FromArgb(0x4E, 0x4E, 0x54);
        // Schultern: ZR (Switch-Online-Zusatz) als schmale Kappe innen rechts, L/R als Kappen auf den Flügeln.
        using (var path = f.Outline(PhotoOutlines.N64, symmetric: true))
            BodyShape(g, path, body);
        EdgeShoulder(g, f, 152, 400, 160, 104, 232, 176, on(ProButtons.L), "L", Color.FromArgb(0x6A, 0x6A, 0x70), labelDy: 34);
        EdgeShoulder(g, f, 768, 868, 52, 96, 108, 148, on(ProButtons.ZR), "ZR", Color.FromArgb(0x6A, 0x6A, 0x70), labelDy: 26);
        EdgeShoulder(g, f, 896, 1130, 104, 165, 180, 238, on(ProButtons.R), "R", Color.FromArgb(0x6A, 0x6A, 0x70), labelDy: 34);

        using (var dish = new SolidBrush(Mix(body, Color.White, 0.2f)))
        {
            g.FillEllipse(dish, f.R(132, 288, 260, 260));
            g.FillEllipse(dish, f.R(590, 393, 112, 112));
        }
        PhotoDPad(g, f, 262, 418, 105, on, fill: dark, border: Mix(dark, Color.Black, 0.4f), glyphs: Color.FromArgb(0x9A, 0x9A, 0xA0));
        ColorKey(g, f.P(645, 449), on(ProButtons.Plus), "", Color.FromArgb(0xC8, 0x40, 0x2A), f.S(42));
        Caption(g, new RectangleF(f.P(645, 449).X - 20, f.P(645, 449).Y - 7, 40, 14), "START", 5.5f, Color.FromArgb(0x80, 0x20, 0x14));

        // A (blau) und B (grün), C-Tasten (gelb) = rechter Stick
        ColorKey(g, f.P(963, 530), on(ProButtons.B), "A", Color.FromArgb(0x2A, 0x3F, 0xB0), f.S(47));
        ColorKey(g, f.P(877, 440), on(ProButtons.Y), "B", Color.FromArgb(0x2E, 0x8B, 0x45), f.S(47));
        using (var ring = new Pen(Mix(body, Color.Black, 0.18f), 1.4f))
            g.DrawEllipse(ring, f.R(970, 285, 170, 170));
        var yellow = Color.FromArgb(0xEC, 0xC8, 0x22);
        var arrow = Color.FromArgb(0x8A, 0x70, 0x10);
        float rx = p_RightX(), ry = p_RightY();
        ColorKey(g, f.P(1053, 300), ry > 0.5f, "▲", yellow, f.S(36), arrow);
        ColorKey(g, f.P(1056, 438), ry < -0.5f, "▼", yellow, f.S(36), arrow);
        ColorKey(g, f.P(983, 368), rx < -0.5f, "◀", yellow, f.S(36), arrow);
        ColorKey(g, f.P(1128, 371), rx > 0.5f, "▶", yellow, f.S(36), arrow);

        // Stick in der Mitte: dunkler Sockel, achteckige Führung, helle Kappe
        using (var socket = new SolidBrush(dark))
            g.FillEllipse(socket, f.R(520, 565, 260, 260));
        Stick(g, f.P(650, 695), _gamepad.LeftX, _gamepad.LeftY, false, f.S(82), f.S(56),
            capColor: Color.FromArgb(0xE2, 0xE2, 0xE6), wellColor: Color.FromArgb(0x38, 0x38, 0x3E), octagon: true);
        // Z sitzt auf der Rückseite des Mittelgriffs unter dem Stick – als Kapsel auf dem Griff angedeutet.
        GripButton(g, f.R(612, 875, 76, 150), on(ProButtons.ZL), "Z");
    }

    private float p_RightX() => _input?.RightX ?? 0;
    private float p_RightY() => _input?.RightY ?? 0;

    // ---------- SNES ----------

    private void PaintSnes(Graphics g, Func<ProButtons, bool> on)
    {
        var f = Frame(31, 1238, 70, 596);
        var body = Color.FromArgb(0xCD, 0xCD, 0xD2);
        var shoulder = Color.FromArgb(0xB0, 0xB0, 0xB6);
        // Schultern: ZL/ZR (Switch-Online-Zusatztasten) als schmale Kappen in der Mulde, L/R über den Ecken.
        using (var path = f.Outline(PhotoOutlines.Snes, symmetric: true))
            BodyShape(g, path, body);
        EdgeShoulder(g, f, 195, 424, 38, 45, 110, 118, on(ProButtons.L), "L", shoulder, labelDy: 34);
        EdgeShoulder(g, f, 436, 575, 45, 45, 118, 118, on(ProButtons.ZL), "ZL", shoulder, labelDy: 34);
        EdgeShoulder(g, f, 705, 849, 44, 38, 117, 110, on(ProButtons.ZR), "ZR", shoulder, labelDy: 34);
        EdgeShoulder(g, f, 861, 1005, 38, 44, 110, 116, on(ProButtons.R), "R", shoulder, labelDy: 34);

        using (var dish = new SolidBrush(Mix(body, Color.White, 0.25f)))
            g.FillEllipse(dish, f.R(140, 196, 290, 290));
        using (var panel = new SolidBrush(Mix(body, Color.Black, 0.12f)))
            g.FillEllipse(panel, f.R(752, 112, 456, 456));
        PhotoDPad(g, f, 284, 340, 107, on, fill: Color.FromArgb(0x4A, 0x4A, 0x50), border: Color.FromArgb(0x30, 0x30, 0x34),
            glyphs: Color.FromArgb(0x8A, 0x8A, 0x92));
        var pill = Color.FromArgb(0x5A, 0x5A, 0x60);
        var labels = Color.FromArgb(0x8E, 0x8E, 0x98);
        PillKey(g, f.P(521, 378), on(ProButtons.Minus), "", pill, -38, f.S(110), f.S(36));
        PillKey(g, f.P(652, 378), on(ProButtons.Plus), "", pill, -38, f.S(110), f.S(36));
        Label(g, f, 515, 465, "SELECT", labels);
        Label(g, f, 647, 465, "START", labels);
        // Europäische Farben (wie der Switch-Online-Controller in Europa)
        ColorKey(g, f.P(982, 250), on(ProButtons.X), "X", Color.FromArgb(0x2C, 0x5F, 0xC7), f.S(45));
        ColorKey(g, f.P(1098, 340), on(ProButtons.A), "A", Color.FromArgb(0xD5, 0x25, 0x2C), f.S(45));
        ColorKey(g, f.P(982, 430), on(ProButtons.B), "B", Color.FromArgb(0xE8, 0xB7, 0x1C), f.S(45));
        ColorKey(g, f.P(866, 340), on(ProButtons.Y), "Y", Color.FromArgb(0x2E, 0x9B, 0x4A), f.S(45));
    }

    // ---------- NES ----------

    private void PaintNes(Graphics g, Func<ProButtons, bool> on)
    {
        var f = Frame(35, 1243, 70, 605);
        var body = Color.FromArgb(0xD6, 0xD4, 0xD0);
        var face = Color.FromArgb(0x26, 0x26, 0x28);
        var stripe = Color.FromArgb(0xB4, 0xB2, 0xAE);
        var red = Color.FromArgb(0xC8, 0x22, 0x28);
        using (var path = Rounded(f.R(35, 83, 1208, 522), f.S(22)))
            BodyShape(g, path, body);
        EdgeShoulder(g, f, 95, 380, 60, 60, 102, 102, on(ProButtons.L), "L", Color.FromArgb(0xA8, 0xA6, 0xA2), labelDy: 22);
        EdgeShoulder(g, f, 900, 1185, 60, 60, 102, 102, on(ProButtons.R), "R", Color.FromArgb(0xA8, 0xA6, 0xA2), labelDy: 22);
        using (var plate = Rounded(f.R(70, 168, 1138, 400), f.S(8)))
        using (var black = new SolidBrush(face))
            g.FillPath(black, plate);
        using (var stripes = new SolidBrush(stripe))
        {
            foreach (var (y, h) in new[] { (170f, 55f), (245f, 55f), (320f, 55f), (537f, 31f) })
                using (var s = Rounded(f.R(425, y, 338, h), f.S(10)))
                    g.FillPath(stripes, s);
            using (var panel = Rounded(f.R(425, 390, 338, 122), f.S(10)))
                g.FillPath(stripes, panel);
        }
        Label(g, f, 512, 347, "SELECT", red, 7f);
        Label(g, f, 685, 347, "START", red, 7f);
        // Steuerkreuz auf hellem Rand
        using (var rim = Rounded(f.R(128, 272, 240, 240), f.S(14)))
        using (var rimBrush = new SolidBrush(Color.FromArgb(0xE6, 0xE4, 0xE0)))
        {
            var plus = new GraphicsPath { FillMode = FillMode.Winding };
            plus.AddRectangle(f.R(198, 272, 100, 240));
            plus.AddRectangle(f.R(128, 342, 240, 100));
            g.FillPath(rimBrush, plus);
            plus.Dispose();
        }
        PhotoDPad(g, f, 248, 392, 108, on, fill: Color.FromArgb(0x1C, 0x1C, 0x1E), border: Color.FromArgb(0x55, 0x55, 0x58),
            glyphs: Color.FromArgb(0x80, 0x80, 0x84));
        var pill = Color.FromArgb(0x30, 0x30, 0x32);
        PillKey(g, f.P(516, 452), on(ProButtons.Minus), "", pill, 0, f.S(98), f.S(36));
        PillKey(g, f.P(672, 452), on(ProButtons.Plus), "", pill, 0, f.S(98), f.S(36));
        using (var square = new SolidBrush(Color.FromArgb(0xD8, 0xD6, 0xD2)))
        {
            using (var b = Rounded(f.R(818, 385, 132, 133), f.S(8))) g.FillPath(square, b);
            using (var a = Rounded(f.R(975, 385, 133, 133), f.S(8))) g.FillPath(square, a);
        }
        ColorKey(g, f.P(883, 450), on(ProButtons.B), "", red, f.S(52));
        ColorKey(g, f.P(1040, 450), on(ProButtons.A), "", red, f.S(52));
        Label(g, f, 938, 548, "B", red, 9f);
        Label(g, f, 1095, 548, "A", red, 9f);
    }

    // ---------- Mega Drive (6 Tasten) ----------

    /// <summary>
    /// Mega-Drive-Controller mit 6 Tasten (Foto gerade gedreht): runde Steuerkreuz-Scheibe links, START in der Mitte,
    /// oben X/Y/Z (grau, klein), unten A/B/C (schwarz, groß) – beide Reihen schräg wie beim Original.
    /// </summary>
    private void PaintMegaDrive(Graphics g, Func<ProButtons, bool> on)
    {
        var f = Frame(465, 1200, 387, 739);
        var body = Color.FromArgb(0x2C, 0x2C, 0x30);
        Shoulder(g, f.R(1030, 384, 110, 50), on(ProButtons.Minus), "MODE", Color.FromArgb(0x50, 0x50, 0x56), tucked: true);
        using (var path = f.Outline(PhotoOutlines.MegaDrive, symmetric: true, mirrorRight: true))
            BodyShape(g, path, body);
        var dish = Mix(body, Color.Black, 0.25f);
        using (var d = new SolidBrush(dish))
        {
            g.FillEllipse(d, f.R(649 - 118, 517 - 92, 236, 184));
            g.FillEllipse(d, f.R(1016 - 142, 516 - 112, 284, 224));
        }
        using (var disc = new SolidBrush(Mix(body, Color.White, 0.08f)))
            g.FillEllipse(disc, f.R(647 - 78, 500 - 78, 156, 156));
        PhotoDPad(g, f, 647, 500, 66, on, fill: Color.FromArgb(0x3A, 0x3A, 0x40), border: Color.FromArgb(0x18, 0x18, 0x1A),
            glyphs: Color.FromArgb(0x90, 0x90, 0x98));
        PillKey(g, f.P(827, 487), on(ProButtons.Plus), "", Color.FromArgb(0xD8, 0x2C, 0x2C), 0, f.S(58), f.S(24));
        Label(g, f, 827, 515, "START", Color.FromArgb(0xC8, 0xC8, 0xD0), 6.5f);
        var grey = Color.FromArgb(0x7A, 0x7A, 0x82);
        var black = Color.FromArgb(0x1E, 0x1E, 0x22);
        var text = Color.FromArgb(0xB8, 0xB8, 0xC0);
        ColorKey(g, f.P(932, 484), on(ProButtons.L), "X", grey, f.S(23), Color.FromArgb(0x30, 0x30, 0x34));
        ColorKey(g, f.P(989, 471), on(ProButtons.X), "Y", grey, f.S(23), Color.FromArgb(0x30, 0x30, 0x34));
        ColorKey(g, f.P(1048, 463), on(ProButtons.R), "Z", grey, f.S(23), Color.FromArgb(0x30, 0x30, 0x34));
        ColorKey(g, f.P(962, 564), on(ProButtons.Y), "A", black, f.S(31), text);
        ColorKey(g, f.P(1028, 535), on(ProButtons.B), "B", black, f.S(31), text);
        ColorKey(g, f.P(1094, 518), on(ProButtons.A), "C", black, f.S(31), text);
    }

    // ---------- PlayStation (DualShock 4 / DualSense) ----------

    /// <summary>
    /// DualShock 4 und DualSense im Sony-Layout: Steuerkreuz und Symboltasten oben, beide Sticks symmetrisch
    /// unten, Touchpad in der Mitte (klickbar = Aufnahme), SHARE/OPTIONS daneben, PS-Taste darunter.
    /// DualSense: weiß, Leuchtstreifen neben dem Touchpad, Mute-Taste. DualShock 4: schwarz, die Lichtleiste
    /// auf der Rückseite ist von vorn nur als schmaler Streifen an der Oberkante sichtbar.
    /// </summary>
    private void PaintPlayStation(Graphics g, ControllerKind kind, Func<ProButtons, bool> on)
    {
        bool ds5 = kind == ControllerKind.DualSense;
        var body = ds5 ? Color.FromArgb(0xF4, 0xF4, 0xF7) : Color.FromArgb(0x27, 0x27, 0x2C);
        // DualSense: Tasten weiß mit grauen Symbolen; DualShock 4: dunkle Tasten mit Farbsymbolen.
        var key = ds5 ? Color.FromArgb(0xEC, 0xEC, 0xF0) : Color.FromArgb(0x33, 0x33, 0x39);
        var text = ds5 ? Color.FromArgb(0x8E, 0x8E, 0x98) : Color.FromArgb(0xD8, 0xD8, 0xDE);
        var trigger = ds5 ? Color.FromArgb(0xDC, 0xDC, 0xE2) : Color.FromArgb(0x3A, 0x3A, 0x40);
        var bumper = ds5 ? Color.FromArgb(0xF4, 0xF4, 0xF6) : Color.FromArgb(0x4A, 0x4A, 0x52);
        // Lichtleiste: vom Spiel gesetzte Farbe, sonst das übliche PlayStation-Blau.
        var lightbar = LightbarTint ?? Color.FromArgb(0x1E, 0x6F, 0xE0);
        var f = Frame(40, 1240, 40, 760);
        // Tastenpalette auf das Original stellen: DualSense weiß, DualShock 4 dunkel.
        var savedBody = _body;
        var savedButtons = _buttons;
        var savedDark = _darkButtons;
        _body = body;
        _buttons = key;
        _darkButtons = !ds5;
        try
        {

        using (var path = f.Outline(ds5 ? PhotoOutlines.DualSense : PhotoOutlines.DualShock))
            BodyShape(g, path, body);
        // DS4: die Lichtleiste sitzt auf der Rückseite – von vorn als schmaler Leuchtstreifen in der Mulde
        // der Oberkante (über dem Touchpad, ohne es zu berühren).
        if (!ds5)
            LightbarStrip(g, f.R(552, 82, 176, 14), lightbar);
        // Schulterkante in Segmente ohne Überlappung: L1 außen breit, L2 innen auf dem Höcker – rechts gespiegelt.
        EdgeShoulder(g, f, 208, 359, 80, 36, 154, 110, on(ProButtons.L), "L1", bumper, labelDy: 34);
        EdgeShoulder(g, f, 371, 505, 36, 28, 110, 98, on(ProButtons.ZL), "L2", trigger, _input?.LeftTrigger ?? 0, 32);
        EdgeShoulder(g, f, 775, 909, 28, 36, 98, 110, on(ProButtons.ZR), "R2", trigger, _input?.RightTrigger ?? 0, 32);
        EdgeShoulder(g, f, 921, 1072, 36, 80, 100, 130, on(ProButtons.R), "R1", bumper, labelDy: 34);

        // Steuerkreuz oben links, Symboltasten oben rechts (DS5: graue Symbole, DS4: Originalfarben).
        PhotoDPad(g, f, 322, 255, 96, on, fill: key, border: Mix(key, ds5 ? Color.Black : Color.White, 0.18f), glyphs: text);
        var symX = ds5 ? text : Color.FromArgb(0x2F, 0xB5, 0x8C);
        var symA = ds5 ? text : Color.FromArgb(0xE0, 0x4B, 0x5A);
        var symB = ds5 ? text : Color.FromArgb(0x4C, 0x7F, 0xD9);
        var symY = ds5 ? text : Color.FromArgb(0xD9, 0x7F, 0xB4);
        Face(g, f.P(958, 192), on(ProButtons.X), "△", f.S(40), symX);
        Face(g, f.P(1033, 267), on(ProButtons.A), "○", f.S(40), symA);
        Face(g, f.P(958, 342), on(ProButtons.B), "✕", f.S(40), symB);
        Face(g, f.P(883, 267), on(ProButtons.Y), "□", f.S(40), symY);

        // Beide Sticks symmetrisch unten (Kappe und Mulde schwarz, auch beim weißen DualSense).
        var cap = Color.FromArgb(0x24, 0x24, 0x28);
        var well = Color.FromArgb(0x18, 0x18, 0x1C);
        Stick(g, f.P(415, 455), _gamepad.LeftX, _gamepad.LeftY, on(ProButtons.LeftStick), f.S(88), f.S(58), cap, well);
        Stick(g, f.P(865, 455), _gamepad.RightX, _gamepad.RightY, on(ProButtons.RightStick), f.S(88), f.S(58), cap, well);

        // Touchpad (Trapez, oben breiter), klickbar = Aufnahme-Taste. DualSense: die Lichtleiste
        // läuft als U entlang der Touchpad-Seiten, wie am Original.
        PointF[] padPts = [f.P(465, 102), f.P(815, 102), f.P(800, 296), f.P(480, 296)];
        using (var pad = new GraphicsPath())
        {
            pad.AddPolygon(padPts);
            if (on(ProButtons.Capture))
            {
                using var glow = new Pen(AccentGlow, 7f);
                g.DrawPath(glow, pad);
            }
            var pb = pad.GetBounds();
            var padTop = on(ProButtons.Capture) ? Accent : ds5 ? Color.FromArgb(0xF0, 0xF0, 0xF3) : Color.FromArgb(0x34, 0x34, 0x3A);
            var padBottom = on(ProButtons.Capture) ? Mix(Accent, Color.Black, 0.2f) : ds5 ? Color.FromArgb(0xD8, 0xD8, 0xDE) : Color.FromArgb(0x12, 0x12, 0x16);
            using (var fill = new LinearGradientBrush(pb, padTop, padBottom, LinearGradientMode.Vertical))
                g.FillPath(fill, pad);
            using var edge = new Pen(ds5 ? Color.FromArgb(0xA8, 0xA8, 0xB2) : Color.FromArgb(0x4A, 0x4A, 0x52), 1.4f);
            g.DrawPath(edge, pad);
        }
        if (ds5)
        {
            // Leuchtstreifen säumen die Seiten und die Unterkante des Touchpads.
            LightbarEdge(g, f.P(467, 106), f.P(481, 290), lightbar);
            LightbarEdge(g, f.P(813, 106), f.P(799, 290), lightbar);
            LightbarEdge(g, f.P(488, 292), f.P(792, 292), lightbar);
            // Mute-Taste (Mikro) unter der PS-Taste.
            using (var mute = Rounded(f.R(615, 458, 50, 20), 10))
            {
                if (on(ProButtons.Headset))
                {
                    using var glow = new Pen(AccentGlow, 5f);
                    g.DrawPath(glow, mute);
                }
                using var fill = new SolidBrush(on(ProButtons.Headset) ? Accent : key);
                g.FillPath(fill, mute);
            }
            // Kleines Mikro-Piktogramm in der Taste.
            var mc = f.P(640, 468);
            var mic = on(ProButtons.Headset) ? Color.White : text;
            using (var p1 = new Pen(mic, 1.6f))
            {
                g.DrawLine(p1, mc.X, mc.Y - 5, mc.X, mc.Y + 2);
                g.DrawArc(p1, mc.X - 4, mc.Y - 2, 8, 6, 0, 180);
            }
        }

        // SHARE/CREATE und OPTIONS: kleine Tasten oberhalb der Touchpad-Ecken, Beschriftung mittig darunter.
        // Weit genug außen, damit der Text nicht an der Touchpad-Lichtleiste klebt.
        var menuKey = ds5 ? Color.FromArgb(0xDD, 0xDD, 0xE2) : key;
        PillKey(g, f.P(388, 136), on(ProButtons.Minus), "", menuKey, 0, f.S(56), f.S(20));
        PillKey(g, f.P(877, 136), on(ProButtons.Plus), "", menuKey, 0, f.S(56), f.S(20));
        var muted = Color.FromArgb(0x98, 0x98, 0xA2);
        Label(g, f, 388, 163, ds5 ? "CREATE" : "SHARE", muted, 5f);
        Label(g, f, 877, 163, "OPTIONS", muted, 5f);

        // PS-Taste mittig unter dem Touchpad (Kreis mit „PS“).
        var pc = f.P(640, 415);
        float pr = f.S(24);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(pc.X - pr, pc.Y - pr, pr * 2, pr * 2);
            Fill(g, path, on(ProButtons.Home), key);
        }
        using (var ring = new Pen(on(ProButtons.Home) ? Color.White : Mix(key, Color.White, 0.35f), 1.3f))
            g.DrawEllipse(ring, pc.X - pr + 3, pc.Y - pr + 3, (pr - 3) * 2, (pr - 3) * 2);
        Caption(g, new RectangleF(pc.X - pr, pc.Y - pr, pr * 2, pr * 2), "⌂", pr * 0.55f, on(ProButtons.Home) ? Color.White : text);

        if (!ds5)
        {
            // Lautsprecher-Lochreihe unter dem Touchpad.
            var dots = f.P(640, 340);
            using var holes = new SolidBrush(Mix(body, Color.White, 0.25f));
            for (int i = -2; i <= 2; i++)
                g.FillEllipse(holes, dots.X + i * f.S(16) - 2, dots.Y - 2, 4, 4);
        }
        else
        {
            // DualSense: fünf kleine Spieler-LEDs unter dem Touchpad (mittig gefüllt nach Platz).
            var ly = f.P(640, 335);
            int n = PlayerIndex >= 0 ? Math.Min(5, PlayerIndex + 1) : 0;
            int start = (5 - n) / 2;
            for (int i = 0; i < 5; i++)
            {
                bool lit = i >= start && i < start + n;
                using var led = new SolidBrush(lit ? lightbar : Mix(body, Color.Black, 0.2f));
                g.FillRectangle(led, ly.X - f.S(50) + i * f.S(22), ly.Y, f.S(14), f.S(5));
            }
        }
        Gyro(g, _input?.Motion);
        }
        finally
        {
            _body = savedBody;
            _buttons = savedButtons;
            _darkButtons = savedDark;
        }
    }

    /// <summary>Leuchtstreifen der Lichtleiste in der vom Spiel gesetzten Farbe (mit Glühschein).</summary>
    private static void LightbarStrip(Graphics g, RectangleF r, Color c)
    {
        using var path = Rounded(r, r.Height / 2);
        using (var glow = new Pen(Color.FromArgb(110, c), 6f))
            g.DrawPath(glow, path);
        using var brush = new SolidBrush(c);
        g.FillPath(brush, path);
    }

    /// <summary>Leuchtstreifen entlang einer Linie (DualSense: rahmt das Touchpad), mit Glühschein.</summary>
    private static void LightbarEdge(Graphics g, PointF p0, PointF p1, Color c)
    {
        using var glow = new Pen(Color.FromArgb(110, c), 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(glow, p0, p1);
        using var core = new Pen(c, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(core, p0, p1);
    }

    // ---------- Classic Controller (an der Wii-Fernbedienung) ----------

    private void PaintWiiClassic(Graphics g, Func<ProButtons, bool> on, Color white, Color key, Color dark)
    {
        var f = Frame(58, 1217, 61, 610);
        var shoulder = Color.FromArgb(0xDA, 0xDA, 0xE0);
        // Unterer Fotorand liegt im Schatten: oberes linkes Viertel in beide Richtungen gespiegelt.
        using (var path = new GraphicsPath())
        {
            path.AddClosedCurve(QuadrantSymmetric(ParsePoints(PhotoOutlines.Classic), 344.5f).Select(q => f.P(q.X, q.Y)).ToArray(), 0.3f);
            BodyShape(g, path, white);
        }
        // Schulterkante in Segmente ohne Überlappung: L breit außen, ZL innen (analoge Trigger), rechts gespiegelt.
        EdgeShoulder(g, f, 190, 474, 68, 32, 140, 102, on(ProButtons.L) || (_input?.LeftTrigger ?? 0) > 0.1f, "L", shoulder, _input?.LeftTrigger ?? 0, 32);
        EdgeShoulder(g, f, 486, 575, 32, 40, 102, 110, on(ProButtons.ZL), "ZL", shoulder, labelDy: 32);
        EdgeShoulder(g, f, 705, 794, 38, 32, 108, 102, on(ProButtons.ZR), "ZR", shoulder, labelDy: 32);
        EdgeShoulder(g, f, 806, 1090, 32, 68, 102, 140, on(ProButtons.R) || (_input?.RightTrigger ?? 0) > 0.1f, "R", shoulder, _input?.RightTrigger ?? 0, 32);
        PhotoDPad(g, f, 285, 270, 102, on, fill: Color.FromArgb(0xEE, 0xEE, 0xF1), border: Color.FromArgb(0xB0, 0xB0, 0xBA),
            glyphs: Color.FromArgb(0x90, 0x90, 0x9A));
        var labels = Color.FromArgb(0x9A, 0x9A, 0xA4);
        ColorKey(g, f.P(552, 270), on(ProButtons.Minus), "−", key, f.S(24), dark);
        ColorKey(g, f.P(637, 270), on(ProButtons.Home), "⌂", key, f.S(26), Color.FromArgb(40, 120, 220));
        ColorKey(g, f.P(722, 270), on(ProButtons.Plus), "+", key, f.S(24), dark);
        Label(g, f, 552, 315, "SELECT", labels, 6f);
        Label(g, f, 637, 315, "HOME", labels, 6f);
        Label(g, f, 722, 315, "START", labels, 6f);
        ColorKey(g, f.P(990, 186), on(ProButtons.X), "x", key, f.S(43), dark);
        ColorKey(g, f.P(1102, 272), on(ProButtons.A), "a", key, f.S(43), dark);
        ColorKey(g, f.P(990, 357), on(ProButtons.B), "b", key, f.S(43), dark);
        ColorKey(g, f.P(878, 270), on(ProButtons.Y), "y", key, f.S(43), dark);
        var gate = Color.FromArgb(0xD8, 0xD8, 0xDE);
        Stick(g, f.P(482, 452), _gamepad.LeftX, _gamepad.LeftY, false, f.S(92), f.S(56), white, gate);
        Stick(g, f.P(797, 452), _gamepad.RightX, _gamepad.RightY, false, f.S(92), f.S(56), white, gate);
    }
}
