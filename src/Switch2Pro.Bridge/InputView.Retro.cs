using System.Drawing;
using System.Drawing.Drawing2D;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Grafiken der klassischen Controller: NSO-Controller (NES, SNES, N64, Mega Drive) sowie Wii-Fernbedienung
/// (mit Nunchuk oder Classic Controller) und Wii U Pro Controller. Tasten in ihren Originalfarben; gedrückte
/// Tasten leuchten wie beim Pro Controller. Eingaben sind bereits vereinheitlicht (siehe Mapping.Normalize).
/// </summary>
internal sealed partial class InputView
{
    /// <summary>Was an der Wii-Fernbedienung steckt (für die richtige Zeichnung).</summary>
    public WiiExtension WiiExtension { get; set; }

    private static bool IsRetro(ControllerKind k) =>
        k.IsClassic() || k is ControllerKind.WiiRemote or ControllerKind.WiiUPro or ControllerKind.GameCube2;

    private void PaintRetro(Graphics g, ControllerKind kind)
    {
        var p = _input;
        bool On(ProButtons b) => p is not null && p.Has(b);
        switch (kind)
        {
            case ControllerKind.SnesController: PaintSnes(g, On); break;
            case ControllerKind.NesController: PaintNes(g, On); break;
            case ControllerKind.N64Controller: PaintN64(g, On); break;
            case ControllerKind.MegaDrive: PaintMegaDrive(g, On); break;
            case ControllerKind.WiiUPro: PaintWiiUPro(g, On); break;
            case ControllerKind.GameCube2: PaintGameCube(g, On); break;
            default: PaintWiiRemote(g, On); break;
        }
    }

    // ---------- Bausteine ----------


    private static void BodyShape(Graphics g, GraphicsPath path, Color body)
    {
        using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
        {
            var state = g.Save();
            g.TranslateTransform(0, 6);
            g.FillPath(shadow, path);
            g.Restore(state);
        }
        var bounds = path.GetBounds();
        using (var fill = new LinearGradientBrush(bounds, Mix(body, Color.White, 0.12f), Mix(body, Color.Black, 0.15f), LinearGradientMode.Vertical))
            g.FillPath(fill, path);
        using var edge = new Pen(Mix(body, Color.Black, 0.35f), 1.5f);
        g.DrawPath(edge, path);
    }

    /// <summary>Runde Taste in eigener Farbe (z. B. SNES-Farben), gedrückt leuchtend.</summary>
    private void ColorKey(Graphics g, PointF c, bool on, string text, Color color, float r = 19, Color? textColor = null)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
        if (on)
        {
            using var glow = new Pen(AccentGlow, 7f);
            g.DrawPath(glow, path);
        }
        using (var brush = new SolidBrush(on ? Accent : color))
            g.FillPath(brush, path);
        using (var edge = new Pen(on ? Color.FromArgb(160, 235, 255) : Mix(color, Color.Black, 0.35f), 1.3f))
            g.DrawPath(edge, path);
        Caption(g, new RectangleF(c.X - r, c.Y - r, r * 2, r * 2), text, r * 0.6f, on ? Color.White : textColor ?? Color.White);
    }

    /// <summary>Längliche Taste (SELECT/START), schräg wie beim SNES möglich.</summary>
    private void PillKey(Graphics g, PointF c, bool on, string text, Color color, float angle = 0, float w = 34, float h = 12, Color? labelColor = null)
    {
        var state = g.Save();
        g.TranslateTransform(c.X, c.Y);
        g.RotateTransform(angle);
        using var path = Rounded(new RectangleF(-w / 2, -h / 2, w, h), h / 2);
        if (on)
        {
            using var glow = new Pen(AccentGlow, 6f);
            g.DrawPath(glow, path);
        }
        using (var brush = new SolidBrush(on ? Accent : color))
            g.FillPath(brush, path);
        g.Restore(state);
        using var font = new Font("Segoe UI", 6.5f, FontStyle.Bold);
        using var label = new SolidBrush(labelColor ?? Color.FromArgb(170, 175, 185));
        using var format = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(text, font, label, new RectangleF(c.X - 40, c.Y + h / 2 + 4, 80, 14), format);
    }


    // ---------- Wii-Fernbedienung (+ Nunchuk / Classic Controller) ----------

    private void PaintWiiRemote(Graphics g, Func<ProButtons, bool> on)
    {
        var white = Color.FromArgb(0xF0, 0xF0, 0xF2);
        var key = Color.FromArgb(0xE2, 0xE2, 0xE6);
        var dark = Color.FromArgb(0x50, 0x50, 0x58);
        if (WiiExtension.HasClassic())
        {
            PaintWiiClassic(g, on, white, key, dark);
            return;
        }
        WiiOverlay(g); // Zeiger-Anzeige (nur bei der Fernbedienung selbst)
        bool nunchuk = WiiExtension.HasNunchuk();
        bool motionPlus = WiiExtension.HasMotionPlus();
        float cx = nunchuk ? W / 2 + 80 : W / 2;
        // Maße wie das Original (14,8 × 3,6 cm, Seitenverhältnis 0,245); Tastenlage entlang der Länge vom Foto vermessen.
        const float Top = 24;
        float bottom = motionPlus ? 352 : 382;
        float length = bottom - Top, remoteW = 356 * 0.245f;
        float Y(float t) => Top + t * length;
        float edge = cx - remoteW / 2;
        if (nunchuk)
            PaintNunchuk(g, on, cx, motionPlus ? 392 : bottom, white, key, dark);

        // B-Abzug auf der Rückseite: als seitlich vorstehender Abzug angedeutet (hinter dem Gehäuse, Höhe von A).
        float bY = Y(0.29f);
        using (var trigger = new GraphicsPath())
        {
            float bx = edge + remoteW - 22;
            trigger.AddBeziers(
            [
                new PointF(bx, bY - 46), new PointF(bx + 36, bY - 42), new PointF(bx + 42, bY - 8), new PointF(bx + 32, bY + 24),
                new PointF(bx + 26, bY + 40), new PointF(bx + 10, bY + 42), new PointF(bx, bY + 38),
            ]);
            trigger.CloseFigure();
            bool b = on(ProButtons.ZR);
            if (b)
            {
                using var glow = new Pen(AccentGlow, 7f);
                g.DrawPath(glow, trigger);
            }
            using (var fill = new SolidBrush(b ? Accent : Color.FromArgb(0xC8, 0xC8, 0xCE)))
                g.FillPath(fill, trigger);
            Caption(g, new RectangleF(bx + 18, bY - 9, 20, 18), "B", 9f, b ? Color.White : dark);
        }

        using (var body = Rounded(new RectangleF(edge, Top, remoteW, length), 32))
            BodyShape(g, body, white);
        // IR-Fenster an der Spitze (dunkel, durchscheinend)
        var clip = g.Save();
        using (var bodyClip = Rounded(new RectangleF(edge, Top, remoteW, length), 32))
            g.SetClip(bodyClip);
        using (var dark1 = new LinearGradientBrush(new RectangleF(edge, Top, remoteW, 20), Color.FromArgb(70, 72, 82), Color.FromArgb(30, 32, 38), LinearGradientMode.Vertical))
            g.FillRectangle(dark1, edge, Top, remoteW, 16);
        g.Restore(clip);

        // Ein/Aus-Taste (klein, oben links)
        float py = Y(0.045f) + 4;
        using (var power = new SolidBrush(Color.FromArgb(0xD6, 0xD6, 0xDC)))
            g.FillEllipse(power, edge + 14, py - 7, 14, 14);
        using (var red = new Pen(Color.FromArgb(200, 70, 70), 1.6f))
            g.DrawArc(red, edge + 17.5f, py - 3.5f, 7, 7, -60, 300);

        // Steuerkreuz: Eingaben sind beim Querhalten schon gedreht – für die Anzeige zurückdrehen.
        bool up = on(ProButtons.Up), down = on(ProButtons.Down), left = on(ProButtons.Left), right = on(ProButtons.Right);
        if (!nunchuk)
            (up, down, left, right) = (on(ProButtons.Left), on(ProButtons.Right), on(ProButtons.Down), on(ProButtons.Up));
        var state = g.Save();
        g.TranslateTransform(cx, Y(0.155f));
        float k = remoteW * 0.27f / 43f; // Kreuz ≈ 54 % der Breite
        g.ScaleTransform(k, k);
        DPad(g, new PointF(0, 0), up, down, left, right,
            fill: Color.FromArgb(0xEE, 0xEE, 0xF1), border: Color.FromArgb(0xB0, 0xB0, 0xBA), glyphs: Color.FromArgb(0x90, 0x90, 0x9A));
        g.Restore(state);

        // A: rund, leicht vertieft mit Ring (≈ 35 % der Breite)
        float aY = Y(0.285f), aR = remoteW * 0.175f;
        using (var ring = new Pen(Color.FromArgb(0xC6, 0xC6, 0xCC), 2f))
            g.DrawEllipse(ring, cx - aR - 4, aY - aR - 4, (aR + 4) * 2, (aR + 4) * 2);
        ColorKey(g, new PointF(cx, aY), on(ProButtons.A), "A", key, aR, dark);

        // − HOME + (HOME mit blauem Ring)
        float hY = Y(0.42f), small = remoteW * 0.085f, dx = remoteW * 0.28f;
        ColorKey(g, new PointF(cx - dx, hY), on(ProButtons.Minus), "−", key, small, dark);
        ColorKey(g, new PointF(cx + dx, hY), on(ProButtons.Plus), "+", key, small, dark);
        using (var blue = new Pen(Color.FromArgb(70, 140, 230), 1.6f))
            g.DrawEllipse(blue, cx - small - 2, hY - small - 2, (small + 2) * 2, (small + 2) * 2);
        ColorKey(g, new PointF(cx, hY), on(ProButtons.Home), "⌂", key, small, Color.FromArgb(40, 120, 220));
        Caption(g, new RectangleF(cx - 30, hY + small + 2, 60, 12), "HOME", 5.5f, Color.FromArgb(0xA8, 0xA8, 0xB2));

        // Lautsprecher (Lochraster)
        float sY = Y(0.53f);
        using (var holes = new SolidBrush(Color.FromArgb(0xB8, 0xB8, 0xC0)))
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 6; col++)
                    g.FillEllipse(holes, cx - 15 + col * 6, sY - 6 + row * 6, 2.6f, 2.6f);

        // 1 und 2
        float oneR = remoteW * 0.12f;
        ColorKey(g, new PointF(cx, Y(0.65f)), on(ProButtons.Y), "1", key, oneR, dark);
        ColorKey(g, new PointF(cx, Y(0.73f)), on(ProButtons.B), "2", key, oneR, dark);

        // Vier Spieler-LEDs (blau, wie das Original), darunter der „Wii“-Schriftzug
        byte mask = PlayerIndex >= 0 ? Commands.PlayerLedMask(PlayerIndex) : (byte)0;
        float ledY = Y(0.80f);
        for (int i = 0; i < 4; i++)
        {
            bool lit = (mask & (1 << i)) != 0;
            using var led = new SolidBrush(lit ? Color.FromArgb(70, 160, 255) : Color.FromArgb(0xC0, 0xC0, 0xC8));
            g.FillRectangle(led, cx - 20 + i * 12, ledY - 1.5f, 6, 3);
        }
        Caption(g, new RectangleF(edge, Y(0.865f) - 9, remoteW, 18), "Wii", 10f, Color.FromArgb(0xA8, 0xA8, 0xB2));

        // MotionPlus-Aufsatz unten
        if (motionPlus)
        {
            using (var mp = Rounded(new RectangleF(edge + 2, bottom - 4, remoteW - 4, 40), 10))
                BodyShape(g, mp, Color.FromArgb(0xEC, 0xEC, 0xF0));
            Caption(g, new RectangleF(edge, bottom + 6, remoteW, 16), "MotionPlus", 7f, Color.FromArgb(90, 120, 200));
        }
        if (!nunchuk)
        {
            using var font = new Font("Segoe UI", 8f);
            using var label = new SolidBrush(Color.FromArgb(150, 155, 166));
            g.DrawString(Tr.T("quer halten: Steuerkreuz links,\n1 und 2 rechts"), font, label, cx + 70, Y(0.66f));
        }
    }

    /// <summary>
    /// Nunchuk von oben: runder Kopf mit Stick, nach unten schmaler Griff; vorn C (klein, rund) und Z (breit);
    /// Kabel zur Unterseite der Fernbedienung.
    /// </summary>
    private void PaintNunchuk(Graphics g, Func<ProButtons, bool> on, float remoteX, float remoteBottom, Color white, Color key, Color dark)
    {
        const float nx = 150;
        // Kabel zuerst (liegt hinter beiden Geräten)
        using (var cable = new Pen(Color.FromArgb(190, 190, 198), 3.2f))
            g.DrawBezier(cable, nx, 318, nx, 404, remoteX - 10, 404, remoteX, remoteBottom);
        // Maße wie das Original (rund 11 × 4 cm neben der 14,8 × 3,6 cm großen Fernbedienung)
        var size = g.Save();
        g.TranslateTransform(nx, 54);
        g.ScaleTransform(0.70f, 0.84f);
        g.TranslateTransform(-nx, -54);
        // Z (breit) und C (klein) an der Vorderkante, teilweise hinter dem Kopf
        bool z = on(ProButtons.ZL), c = on(ProButtons.L);
        using (var zPath = Rounded(new RectangleF(nx - 38, 40, 76, 30), 12))
        {
            if (z)
            {
                using var glow = new Pen(AccentGlow, 6f);
                g.DrawPath(glow, zPath);
            }
            using var fill = new SolidBrush(z ? Accent : key);
            g.FillPath(fill, zPath);
            using var edgePen = new Pen(Color.FromArgb(0xA8, 0xA8, 0xB0), 1.2f);
            g.DrawPath(edgePen, zPath);
        }
        Caption(g, new RectangleF(nx + 14, 44, 22, 16), "Z", 8.5f, z ? Color.White : dark);
        // Körper: ein Umriss – breiter, runder Kopf, schmaler werdender Griff
        using (var body = new GraphicsPath())
        {
            body.AddBeziers(
            [
                new PointF(nx, 54),
                new PointF(nx + 58, 54), new PointF(nx + 74, 98), new PointF(nx + 66, 140),   // Kopf rechts
                new PointF(nx + 60, 172), new PointF(nx + 38, 190), new PointF(nx + 36, 232), // Übergang
                new PointF(nx + 34, 290), new PointF(nx + 34, 350), new PointF(nx, 368),      // Griff rechts
                new PointF(nx - 34, 350), new PointF(nx - 34, 290), new PointF(nx - 36, 232), // Griff links
                new PointF(nx - 38, 190), new PointF(nx - 60, 172), new PointF(nx - 66, 140), // Übergang
                new PointF(nx - 74, 98), new PointF(nx - 58, 54), new PointF(nx, 54),         // Kopf links
            ]);
            body.CloseFigure();
            BodyShape(g, body, white);
        }
        ColorKey(g, new PointF(nx - 30, 50), c, "C", key, 10, dark);
        Stick(g, new PointF(nx, 112), _gamepad.LeftX, _gamepad.LeftY, false, 34, 21);
        g.Restore(size);
    }


    /// <summary>
    /// Wii-Zusätze oben rechts: „MotionPlus“-Abzeichen und – wenn die Sensorleiste im Blick ist – ein kleiner
    /// Bildschirm mit dem Zeigerpunkt.
    /// </summary>
    private void WiiOverlay(Graphics g)
    {
        if (_input?.Pointer is not { } p)
            return;
        var screen = new RectangleF(W - 128, 36, 116, 70);
        using (var back = new SolidBrush(Color.FromArgb(40, 42, 50)))
            g.FillRectangle(back, screen);
        using (var edge = new Pen(Color.FromArgb(120, 125, 140), 1.2f))
            g.DrawRectangle(edge, screen.X, screen.Y, screen.Width, screen.Height);
        using (var dot = new SolidBrush(Accent))
            g.FillEllipse(dot, screen.X + p.X * screen.Width - 4, screen.Y + p.Y * screen.Height - 4, 8, 8);
        Caption(g, new RectangleF(screen.X, screen.Bottom + 2, screen.Width, 14), Tr.T("Zeiger"), 7.5f, MutedText);
    }

    private static readonly Color MutedText = Color.FromArgb(150, 155, 166);
}
