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

    /// <summary>„Knochenform“ (SNES, Classic Controller) als ein einziger Umriss: zwei Halbkreise, oben/unten verbunden.</summary>
    private static GraphicsPath DogBone()
    {
        var path = new GraphicsPath();
        path.AddArc(40, 110, 200, 200, 90, 180);              // linker Halbkreis (unten → oben über links)
        path.AddBezier(140, 110, 220, 120, W - 220, 120, W - 140, 110); // Oberkante leicht eingezogen
        path.AddArc(W - 240, 110, 200, 200, 270, 180);        // rechter Halbkreis
        path.AddBezier(W - 140, 310, W - 220, 298, 220, 298, 140, 310); // Unterkante
        path.CloseFigure();
        return path;
    }

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

    /// <summary>Schultertaste als Lasche oben am Gehäuse.</summary>
    private void Tab(Graphics g, RectangleF r, bool on, string text, Color color)
    {
        using var path = Rounded(r, 8);
        if (on)
        {
            using var glow = new Pen(AccentGlow, 6f);
            g.DrawPath(glow, path);
        }
        using (var brush = new SolidBrush(on ? Accent : color))
            g.FillPath(brush, path);
        using (var edge = new Pen(Mix(color, Color.Black, 0.35f), 1.2f))
            g.DrawPath(edge, path);
        Caption(g, new RectangleF(r.X, r.Y + 1, r.Width, 16), text, 8.5f, on ? Color.White : Mix(color, Color.Black, 0.65f));
    }

    private void Batt(Graphics g)
    {
        // Platzhalter: Akku steht in der Infospalte; hier nur der Name der Art unten.
        if (_input is null)
            return;
        using var font = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var label = new SolidBrush(Color.FromArgb(150, 155, 166));
        using var format = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(Tr.T(_input.Kind.DisplayName()), font, label, new RectangleF(0, 392, W, 16), format);
    }

    // ---------- SNES ----------

    private void PaintSnes(Graphics g, Func<ProButtons, bool> on)
    {
        var body = Color.FromArgb(0xC9, 0xC9, 0xCE);
        Tab(g, new RectangleF(70, 92, 130, 30), on(ProButtons.L), "L", Color.FromArgb(0xA8, 0xA8, 0xAE));
        Tab(g, new RectangleF(W - 200, 92, 130, 30), on(ProButtons.R), "R", Color.FromArgb(0xA8, 0xA8, 0xAE));
        Tab(g, new RectangleF(110, 66, 70, 22), on(ProButtons.ZL), "ZL", Color.FromArgb(0x90, 0x90, 0x96));
        Tab(g, new RectangleF(W - 180, 66, 70, 22), on(ProButtons.ZR), "ZR", Color.FromArgb(0x90, 0x90, 0x96));
        using (var path = DogBone())
            BodyShape(g, path, body);
        using (var panel = new SolidBrush(Color.FromArgb(0x58, 0x4E, 0x8A)))
            g.FillEllipse(panel, W - 218, 132, 156, 156);
        DPad(g, new PointF(140, 210), on(ProButtons.Up), on(ProButtons.Down), on(ProButtons.Left), on(ProButtons.Right));
        var c = new PointF(W - 140, 210);
        ColorKey(g, new PointF(c.X, c.Y - 42), on(ProButtons.X), "X", Color.FromArgb(0x2C, 0x5F, 0xC7));
        ColorKey(g, new PointF(c.X + 42, c.Y), on(ProButtons.A), "A", Color.FromArgb(0xD5, 0x25, 0x2C));
        ColorKey(g, new PointF(c.X, c.Y + 42), on(ProButtons.B), "B", Color.FromArgb(0xE8, 0xB7, 0x1C));
        ColorKey(g, new PointF(c.X - 42, c.Y), on(ProButtons.Y), "Y", Color.FromArgb(0x2E, 0x9B, 0x4A));
        var grey = Color.FromArgb(0x6E, 0x6E, 0x74);
        PillKey(g, new PointF(W / 2 - 30, 228), on(ProButtons.Minus), "SELECT", grey, -35, labelColor: Color.FromArgb(80, 80, 88));
        PillKey(g, new PointF(W / 2 + 30, 228), on(ProButtons.Plus), "START", grey, -35, labelColor: Color.FromArgb(80, 80, 88));
        Batt(g);
    }

    // ---------- NES ----------

    private void PaintNes(Graphics g, Func<ProButtons, bool> on)
    {
        var body = Color.FromArgb(0xD8, 0xD4, 0xCC);
        Tab(g, new RectangleF(70, 112, 110, 26), on(ProButtons.L), "L", Color.FromArgb(0xB0, 0xAC, 0xA4));
        Tab(g, new RectangleF(W - 180, 112, 110, 26), on(ProButtons.R), "R", Color.FromArgb(0xB0, 0xAC, 0xA4));
        using (var path = Rounded(new RectangleF(50, 128, W - 100, 170), 10))
            BodyShape(g, path, body);
        using (var panel = Rounded(new RectangleF(70, 150, W - 140, 126), 6))
        using (var black = new SolidBrush(Color.FromArgb(0x22, 0x22, 0x24)))
            g.FillPath(black, panel);
        DPad(g, new PointF(140, 214), on(ProButtons.Up), on(ProButtons.Down), on(ProButtons.Left), on(ProButtons.Right));
        var grey = Color.FromArgb(0x5A, 0x5A, 0x5E);
        PillKey(g, new PointF(W / 2 - 34, 232), on(ProButtons.Minus), "SELECT", grey);
        PillKey(g, new PointF(W / 2 + 34, 232), on(ProButtons.Plus), "START", grey);
        var red = Color.FromArgb(0xC8, 0x22, 0x28);
        ColorKey(g, new PointF(W - 190, 226), on(ProButtons.B), "B", red, 21);
        ColorKey(g, new PointF(W - 128, 226), on(ProButtons.A), "A", red, 21);
        Batt(g);
    }

    // ---------- Nintendo 64 ----------

    private void PaintN64(Graphics g, Func<ProButtons, bool> on)
    {
        var body = Color.FromArgb(0x9C, 0x9C, 0xA2);
        Tab(g, new RectangleF(40, 72, 120, 26), on(ProButtons.L), "L", Color.FromArgb(0x80, 0x80, 0x86));
        Tab(g, new RectangleF(W - 160, 72, 120, 26), on(ProButtons.R), "R", Color.FromArgb(0x80, 0x80, 0x86));
        Tab(g, new RectangleF(W - 150, 46, 100, 20), on(ProButtons.ZR), "ZR", Color.FromArgb(0x70, 0x70, 0x76));
        using (var path = new GraphicsPath { FillMode = FillMode.Winding })
        {
            path.AddBeziers(
            [
                new PointF(60, 90), new PointF(200, 82), new PointF(380, 82), new PointF(W - 60, 90),          // oben
                new PointF(W - 20, 100), new PointF(W - 10, 150), new PointF(W - 30, 190),                      // rechte Ecke
                new PointF(W - 60, 260), new PointF(W - 70, 330), new PointF(W - 100, 360),                     // rechter Griff
                new PointF(W - 130, 380), new PointF(W - 160, 360), new PointF(W - 170, 300),
                new PointF(W - 180, 250), new PointF(W / 2 + 60, 240), new PointF(W / 2 + 40, 300),            // zur Mitte
                new PointF(W / 2 + 30, 370), new PointF(W / 2 - 30, 370), new PointF(W / 2 - 40, 300),         // Mittelgriff
                new PointF(W / 2 - 60, 240), new PointF(180, 250), new PointF(170, 300),
                new PointF(160, 360), new PointF(130, 380), new PointF(100, 360),                               // linker Griff
                new PointF(70, 330), new PointF(60, 260), new PointF(30, 190),
                new PointF(10, 150), new PointF(20, 100), new PointF(60, 90),
            ]);
            BodyShape(g, path, body);
        }
        DPad(g, new PointF(110, 170), on(ProButtons.Up), on(ProButtons.Down), on(ProButtons.Left), on(ProButtons.Right));
        ColorKey(g, new PointF(W / 2, 150), on(ProButtons.Plus), "", Color.FromArgb(0xC8, 0x22, 0x28), 14);
        Caption(g, new RectangleF(W / 2 - 40, 166, 80, 14), "START", 7f, Color.FromArgb(70, 70, 76));
        var stickX = _gamepad.LeftX;
        var stickY = _gamepad.LeftY;
        Stick(g, new PointF(W / 2, 268), stickX, stickY, false, 30, 18);
        // Z unten am Mittelgriff (als Lasche angedeutet)
        Tab(g, new RectangleF(W / 2 - 30, 318, 60, 22), on(ProButtons.ZL), "Z", Color.FromArgb(0x60, 0x60, 0x66));
        // A (blau) und B (grün), C-Tasten (gelb) = rechter Stick
        ColorKey(g, new PointF(W - 168, 206), on(ProButtons.B), "A", Color.FromArgb(0x2A, 0x4F, 0xC0), 20);
        ColorKey(g, new PointF(W - 206, 172), on(ProButtons.Y), "B", Color.FromArgb(0x2E, 0x8B, 0x45), 17);
        var cy = Color.FromArgb(0xE8, 0xC2, 0x1C);
        float rx = p_RightX(), ry = p_RightY();
        var cc = new PointF(W - 104, 158);
        ColorKey(g, new PointF(cc.X, cc.Y - 28), ry > 0.5f, "▲", cy, 12, Color.FromArgb(60, 50, 0));
        ColorKey(g, new PointF(cc.X, cc.Y + 28), ry < -0.5f, "▼", cy, 12, Color.FromArgb(60, 50, 0));
        ColorKey(g, new PointF(cc.X - 28, cc.Y), rx < -0.5f, "◀", cy, 12, Color.FromArgb(60, 50, 0));
        ColorKey(g, new PointF(cc.X + 28, cc.Y), rx > 0.5f, "▶", cy, 12, Color.FromArgb(60, 50, 0));
        Batt(g);
    }

    private float p_RightX() => _input?.RightX ?? 0;
    private float p_RightY() => _input?.RightY ?? 0;

    // ---------- Mega Drive ----------

    private void PaintMegaDrive(Graphics g, Func<ProButtons, bool> on)
    {
        var body = Color.FromArgb(0x24, 0x24, 0x27);
        Tab(g, new RectangleF(W - 250, 96, 70, 22), on(ProButtons.Minus), "MODE", Color.FromArgb(0x50, 0x50, 0x55));
        using (var path = new GraphicsPath { FillMode = FillMode.Winding })
        {
            // Nierenform: oben flacher Bogen, unten mittig eingezogen
            path.AddBeziers(
            [
                new PointF(70, 120), new PointF(200, 104), new PointF(380, 104), new PointF(W - 70, 120),
                new PointF(W - 10, 130), new PointF(W - 10, 300), new PointF(W - 90, 320),
                new PointF(W - 170, 336), new PointF(W / 2 + 40, 280), new PointF(W / 2, 282),
                new PointF(W / 2 - 40, 280), new PointF(170, 336), new PointF(90, 320),
                new PointF(10, 300), new PointF(10, 130), new PointF(70, 120),
            ]);
            BodyShape(g, path, body);
        }
        DPad(g, new PointF(130, 210), on(ProButtons.Up), on(ProButtons.Down), on(ProButtons.Left), on(ProButtons.Right));
        PillKey(g, new PointF(W / 2, 168), on(ProButtons.Plus), "START", Color.FromArgb(0x60, 0x60, 0x66), 0, 38, 13);
        var key = Color.FromArgb(0x3A, 0x3A, 0x3E);
        var text = Color.FromArgb(0xB8, 0xB8, 0xC0);
        float x0 = W - 236, dx = 52;
        ColorKey(g, new PointF(x0, 168), on(ProButtons.L), "X", key, 15, text);
        ColorKey(g, new PointF(x0 + dx, 160), on(ProButtons.X), "Y", key, 15, text);
        ColorKey(g, new PointF(x0 + 2 * dx, 152), on(ProButtons.R), "Z", key, 15, text);
        ColorKey(g, new PointF(x0, 226), on(ProButtons.Y), "A", key, 21, text);
        ColorKey(g, new PointF(x0 + dx, 216), on(ProButtons.B), "B", key, 21, text);
        ColorKey(g, new PointF(x0 + 2 * dx, 206), on(ProButtons.A), "C", key, 21, text);
        Batt(g);
    }

    // ---------- Wii-Fernbedienung (+ Nunchuk / Classic Controller) ----------

    private void PaintWiiRemote(Graphics g, Func<ProButtons, bool> on)
    {
        var white = Color.FromArgb(0xF0, 0xF0, 0xF2);
        var key = Color.FromArgb(0xE2, 0xE2, 0xE6);
        var dark = Color.FromArgb(0x50, 0x50, 0x58);
        if (WiiExtension == WiiExtension.Classic)
        {
            PaintWiiClassic(g, on, white, key, dark);
            return;
        }
        bool nunchuk = WiiExtension == WiiExtension.Nunchuk;
        // Fernbedienung senkrecht; ohne Nunchuk wird sie quer gehalten (Steuerkreuz links) – Hinweis darunter.
        float cx = nunchuk ? W / 2 + 90 : W / 2;
        using (var path = Rounded(new RectangleF(cx - 42, 30, 84, 350), 30))
            BodyShape(g, path, white);
        // Steuerkreuz: Eingaben sind beim Querhalten schon gedreht – für die Anzeige zurückdrehen.
        bool up = on(ProButtons.Up), down = on(ProButtons.Down), left = on(ProButtons.Left), right = on(ProButtons.Right);
        if (!nunchuk)
            (up, down, left, right) = (on(ProButtons.Left), on(ProButtons.Right), on(ProButtons.Down), on(ProButtons.Up));
        var state = g.Save();
        g.TranslateTransform(cx, 92);
        g.ScaleTransform(0.62f, 0.62f);
        DPad(g, new PointF(0, 0), up, down, left, right);
        g.Restore(state);
        ColorKey(g, new PointF(cx, 160), on(ProButtons.A), "A", key, 20, dark);
        ColorKey(g, new PointF(cx - 28, 212), on(ProButtons.Minus), "−", key, 10, dark);
        ColorKey(g, new PointF(cx, 212), on(ProButtons.Home), "⌂", key, 10, Color.FromArgb(40, 120, 220));
        ColorKey(g, new PointF(cx + 28, 212), on(ProButtons.Plus), "+", key, 10, dark);
        ColorKey(g, new PointF(cx, 288), on(ProButtons.Y), "1", key, 15, dark);
        ColorKey(g, new PointF(cx, 330), on(ProButtons.B), "2", key, 15, dark);
        // B-Abzug auf der Rückseite: seitlich angedeutet
        Tab(g, new RectangleF(cx + 48, 140, 46, 40), on(ProButtons.ZR), "B", key);
        if (nunchuk)
        {
            using (var path = new GraphicsPath { FillMode = FillMode.Winding })
            {
                path.AddEllipse(60, 70, 150, 130);
                path.AddEllipse(95, 160, 80, 210);
                BodyShape(g, path, white);
            }
            Stick(g, new PointF(135, 130), _gamepad.LeftX, _gamepad.LeftY, false, 34, 20);
            Tab(g, new RectangleF(196, 92, 44, 28), on(ProButtons.L), "C", key);
            Tab(g, new RectangleF(196, 126, 44, 40), on(ProButtons.ZL), "Z", key);
            using var cable = new Pen(Color.FromArgb(200, 200, 205), 3f);
            g.DrawBezier(cable, 135, 370, 160, 400, cx - 40, 400, cx, 380);
        }
        else
        {
            using var font = new Font("Segoe UI", 8f);
            using var label = new SolidBrush(Color.FromArgb(150, 155, 166));
            g.DrawString(Tr.T("quer halten: Steuerkreuz links,\n1 und 2 rechts"), font, label, cx + 60, 270);
        }
    }

    private void PaintWiiClassic(Graphics g, Func<ProButtons, bool> on, Color white, Color key, Color dark)
    {
        Tab(g, new RectangleF(70, 92, 130, 30), on(ProButtons.L) || (_input?.LeftTrigger ?? 0) > 0.1f, "L", key);
        Tab(g, new RectangleF(W - 200, 92, 130, 30), on(ProButtons.R) || (_input?.RightTrigger ?? 0) > 0.1f, "R", key);
        Tab(g, new RectangleF(150, 66, 60, 22), on(ProButtons.ZL), "ZL", key);
        Tab(g, new RectangleF(W - 210, 66, 60, 22), on(ProButtons.ZR), "ZR", key);
        using (var path = DogBone())
            BodyShape(g, path, white);
        DPad(g, new PointF(120, 180), on(ProButtons.Up), on(ProButtons.Down), on(ProButtons.Left), on(ProButtons.Right));
        var c = new PointF(W - 120, 180);
        ColorKey(g, new PointF(c.X, c.Y - 38), on(ProButtons.X), "x", key, 17, dark);
        ColorKey(g, new PointF(c.X + 38, c.Y), on(ProButtons.A), "a", key, 17, dark);
        ColorKey(g, new PointF(c.X, c.Y + 38), on(ProButtons.B), "b", key, 17, dark);
        ColorKey(g, new PointF(c.X - 38, c.Y), on(ProButtons.Y), "y", key, 17, dark);
        ColorKey(g, new PointF(W / 2 - 32, 170), on(ProButtons.Minus), "−", key, 10, dark);
        ColorKey(g, new PointF(W / 2, 170), on(ProButtons.Home), "⌂", key, 10, Color.FromArgb(40, 120, 220));
        ColorKey(g, new PointF(W / 2 + 32, 170), on(ProButtons.Plus), "+", key, 10, dark);
        Stick(g, new PointF(W / 2 - 64, 252), _gamepad.LeftX, _gamepad.LeftY, false, 28, 17);
        Stick(g, new PointF(W / 2 + 64, 252), _gamepad.RightX, _gamepad.RightY, false, 28, 17);
        Batt(g);
    }

    // ---------- GameCube-Controller (Switch 2) ----------

    /// <summary>
    /// GameCube-Controller: großes grünes A, rotes B, nierenförmige X/Y, gelber C-Stick, analoge L/R (füllen sich),
    /// Z rechts oben. Tastennamen nach Position (siehe ControllerButtons.Label): B-Bit = A, Y-Bit = B, A-Bit = X, X-Bit = Y.
    /// </summary>
    private void PaintGameCube(Graphics g, Func<ProButtons, bool> on)
    {
        var body = Color.FromArgb(0x4A, 0x4B, 0x8C);
        var grey = Color.FromArgb(0xC8, 0xC8, 0xD2);
        // Analoge Trigger L/R als Laschen, die sich mit dem Druck füllen.
        AnalogTab(g, new RectangleF(60, 78, 150, 30), _input?.LeftTrigger ?? 0, on(ProButtons.L), "L", grey);
        AnalogTab(g, new RectangleF(W - 210, 78, 150, 30), _input?.RightTrigger ?? 0, on(ProButtons.R), "R", grey);
        Tab(g, new RectangleF(W - 190, 60, 100, 22), on(ProButtons.ZR), "Z", Color.FromArgb(0x6E, 0x5C, 0xC8));
        Tab(g, new RectangleF(90, 60, 100, 22), on(ProButtons.ZL), "ZL", Color.FromArgb(0x6E, 0x5C, 0xC8));
        using (var path = new GraphicsPath { FillMode = FillMode.Winding })
        {
            path.AddBeziers(
            [
                new PointF(110, 100), new PointF(220, 92), new PointF(360, 92), new PointF(W - 110, 100),
                new PointF(W - 40, 104), new PointF(W - 15, 190), new PointF(W - 45, 300),
                new PointF(W - 65, 380), new PointF(W - 150, 375), new PointF(W - 165, 300),
                new PointF(W - 185, 270), new PointF(185, 270), new PointF(165, 300),
                new PointF(150, 375), new PointF(65, 380), new PointF(45, 300),
                new PointF(15, 190), new PointF(40, 104), new PointF(110, 100),
            ]);
            BodyShape(g, path, body);
        }
        Stick(g, new PointF(130, 170), _gamepad.LeftX, _gamepad.LeftY, false, 36, 22);
        var state = g.Save();
        g.TranslateTransform(205, 262);
        g.ScaleTransform(0.55f, 0.55f);
        DPad(g, new PointF(0, 0), on(ProButtons.Up), on(ProButtons.Down), on(ProButtons.Left), on(ProButtons.Right));
        g.Restore(state);
        // C-Stick gelb
        var cs = new PointF(W - 205, 262);
        using (var yellow = new SolidBrush(Color.FromArgb(0xE8, 0xC2, 0x1C)))
            g.FillEllipse(yellow, cs.X - 24, cs.Y - 24, 48, 48);
        Stick(g, cs, _gamepad.RightX, _gamepad.RightY, false, 20, 12);
        // Tasten: A groß grün, B rot links unten, X rechts, Y oben (nierenförmig angedeutet)
        var a = new PointF(W - 150, 175);
        ColorKey(g, a, on(ProButtons.B), "A", Color.FromArgb(0x1E, 0xA0, 0x5A), 30);
        ColorKey(g, new PointF(a.X - 50, a.Y + 38), on(ProButtons.Y), "B", Color.FromArgb(0xD0, 0x2A, 0x2A), 16);
        PillKey(g, new PointF(a.X + 50, a.Y - 8), on(ProButtons.A), "X", grey, 75, 44, 20, Color.FromArgb(220, 220, 230));
        PillKey(g, new PointF(a.X - 6, a.Y - 50), on(ProButtons.X), "Y", grey, -15, 44, 20, Color.FromArgb(220, 220, 230));
        // Mitte: START, darüber HOME, Aufnahme, C (Switch-2-Version)
        ColorKey(g, new PointF(W / 2, 182), on(ProButtons.Plus), "", Color.FromArgb(0x30, 0x30, 0x40), 11);
        Caption(g, new RectangleF(W / 2 - 40, 194, 80, 14), "START/PAUSE", 6.5f, Color.FromArgb(200, 200, 215));
        ColorKey(g, new PointF(W / 2 - 34, 132), on(ProButtons.Capture), "●", Color.FromArgb(0x30, 0x30, 0x40), 9);
        ColorKey(g, new PointF(W / 2, 132), on(ProButtons.Home), "⌂", Color.FromArgb(0x30, 0x30, 0x40), 9);
        ColorKey(g, new PointF(W / 2 + 34, 132), on(ProButtons.C), "C", Color.FromArgb(0x30, 0x30, 0x40), 9);
        Batt(g);
    }

    /// <summary>Lasche mit Füllstand (analoger Trigger), leuchtet beim ganz durchgedrückten Klick.</summary>
    private void AnalogTab(Graphics g, RectangleF r, float value, bool click, string text, Color color)
    {
        Tab(g, r, click, text, color);
        if (value <= 0.02f || click)
            return;
        using var path = Rounded(r, 8);
        var state = g.Save();
        g.SetClip(path);
        using (var fill = new SolidBrush(Color.FromArgb(170, Accent)))
            g.FillRectangle(fill, r.X, r.Y, r.Width * Math.Clamp(value, 0f, 1f), r.Height);
        g.Restore(state);
        Caption(g, new RectangleF(r.X, r.Y + 1, r.Width, 16), text, 8.5f, Color.White);
    }

    // ---------- Wii U Pro Controller ----------

    private void PaintWiiUPro(Graphics g, Func<ProButtons, bool> on)
    {
        var body = Color.FromArgb(0x26, 0x26, 0x2A);
        var key = Color.FromArgb(0x3A, 0x3A, 0x40);
        var text = Color.FromArgb(0xC8, 0xC8, 0xD0);
        Tab(g, new RectangleF(70, 60, 120, 26), on(ProButtons.ZL), "ZL", Color.FromArgb(0x50, 0x50, 0x56));
        Tab(g, new RectangleF(W - 190, 60, 120, 26), on(ProButtons.ZR), "ZR", Color.FromArgb(0x50, 0x50, 0x56));
        Tab(g, new RectangleF(60, 90, 140, 28), on(ProButtons.L), "L", Color.FromArgb(0x60, 0x60, 0x66));
        Tab(g, new RectangleF(W - 200, 90, 140, 28), on(ProButtons.R), "R", Color.FromArgb(0x60, 0x60, 0x66));
        using (var path = new GraphicsPath { FillMode = FillMode.Winding })
        {
            path.AddBeziers(
            [
                new PointF(110, 108), new PointF(220, 102), new PointF(360, 102), new PointF(W - 110, 108),
                new PointF(W - 40, 112), new PointF(W - 20, 200), new PointF(W - 50, 330),
                new PointF(W - 70, 390), new PointF(W - 150, 380), new PointF(W - 170, 310),
                new PointF(W - 200, 290), new PointF(200, 290), new PointF(170, 310),
                new PointF(150, 380), new PointF(70, 390), new PointF(50, 330),
                new PointF(20, 200), new PointF(40, 112), new PointF(110, 108),
            ]);
            BodyShape(g, path, body);
        }
        // Wii U Pro: beide Sticks oben, Steuerkreuz und Tasten darunter
        Stick(g, new PointF(130, 160), _gamepad.LeftX, _gamepad.LeftY, on(ProButtons.LeftStick), 34, 22);
        Stick(g, new PointF(W - 130, 160), _gamepad.RightX, _gamepad.RightY, on(ProButtons.RightStick), 34, 22);
        var state = g.Save();
        g.TranslateTransform(195, 240);
        g.ScaleTransform(0.8f, 0.8f);
        DPad(g, new PointF(0, 0), on(ProButtons.Up), on(ProButtons.Down), on(ProButtons.Left), on(ProButtons.Right));
        g.Restore(state);
        var c = new PointF(W - 195, 240);
        ColorKey(g, new PointF(c.X, c.Y - 32), on(ProButtons.X), "X", key, 15, text);
        ColorKey(g, new PointF(c.X + 32, c.Y), on(ProButtons.A), "A", key, 15, text);
        ColorKey(g, new PointF(c.X, c.Y + 32), on(ProButtons.B), "B", key, 15, text);
        ColorKey(g, new PointF(c.X - 32, c.Y), on(ProButtons.Y), "Y", key, 15, text);
        ColorKey(g, new PointF(W / 2 - 30, 200), on(ProButtons.Minus), "−", key, 10, text);
        ColorKey(g, new PointF(W / 2, 228), on(ProButtons.Home), "⌂", key, 11, Color.FromArgb(80, 160, 255));
        ColorKey(g, new PointF(W / 2 + 30, 200), on(ProButtons.Plus), "+", key, 10, text);
        Batt(g);
    }
}
