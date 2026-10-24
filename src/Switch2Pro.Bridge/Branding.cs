using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Switch2Pro.Bridge;

/// <summary>
/// Logo von SKZ Connect in der DevCat-Bildsprache (wie SKZ Monitor und SKZ Remote): Gamepad mit Katzenohren und
/// leuchtenden Cyan-Augen auf dunkler Navy-Kachel, Iris-Verlauf (Teal-Blau-Violett) am Rahmen, amber Nase und
/// Wi-Fi-Bögen als Connect-Signatur. Wird als Vektor gezeichnet (Fenster, Infobereich) und per
/// <c>--render-brand</c> als Programm-Icon und Installer-Grafiken gespeichert (ohne Fenster).
/// </summary>
internal static class Branding
{

    private static Icon? _appIcon;

    /// <summary>Fenstersymbol mit allen Größen (eingebettete ICO-Datei, sonst gezeichnet).</summary>
    public static Icon AppIcon => _appIcon ??= LoadAppIcon();

    private static Icon LoadAppIcon()
    {
        try
        {
            // Nicht ExtractAssociatedIcon: das liefert nur 32 px, verkleinert auf die Titelleiste wirkt der Rand pixelig.
            using var stream = typeof(Branding).Assembly.GetManifestResourceStream("SKZ-Connect.ico");
            if (stream is not null)
                return new Icon(stream);
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            // Fällt auf das gezeichnete Symbol zurück.
        }
        using var bmp = Render(32, tile: false);
        IntPtr handle = bmp.GetHicon();
        using var temp = Icon.FromHandle(handle);
        return (Icon)temp.Clone();
    }

    // Farben des App-Icons: Navy-Kachel wie bei SKZ Monitor/Remote, Iris-Verlauf und DevCat-Akzente.
    // Navy-Verlauf identisch zu den SKZ-Icons (oben dunkler, unten minimal heller).
    private static readonly Color TileTop = Color.FromArgb(0x06, 0x0D, 0x14);
    private static readonly Color TileBottom = Color.FromArgb(0x0A, 0x12, 0x1B);
    private static readonly Color IrisTeal = Color.FromArgb(45, 225, 194);
    private static readonly Color IrisBlue = Color.FromArgb(86, 138, 255);
    private static readonly Color IrisViolet = Color.FromArgb(124, 92, 255);
    private static readonly Color EyeCyan = Color.FromArgb(72, 240, 210);
    private static readonly Color Amber = Color.FromArgb(255, 194, 75);
    private static readonly Color DimTeal = Color.FromArgb(110, 190, 200);
    private static readonly Color Face = Color.FromArgb(10, 16, 24);

    /// <summary>Iris-Verlauf Teal-Blau-Violett über dem Logo-Rechteck (wie im Python-Generator).</summary>
    private static LinearGradientBrush Iris(RectangleF r) => new(r, IrisTeal, IrisViolet, 25f)
    {
        InterpolationColors = new ColorBlend
        {
            Colors = [IrisTeal, IrisBlue, IrisViolet],
            Positions = [0f, 0.5f, 1f],
        },
    };

    /// <summary>Gamepad-Silhouette: Körper mit runden Ecken und zwei leicht auswärts gekippte Griffe als eine Fläche.</summary>
    private static GraphicsPath PadPath(RectangleF r, float s)
    {
        var pad = new GraphicsPath(FillMode.Winding);
        RectangleF R(float x, float y, float w, float h) => new(r.X + x * s, r.Y + y * s, w * s, h * s);
        using (var body = Theme.RoundedRect(R(0.21f, 0.35f, 0.58f, 0.33f), s * 0.12f))
            pad.AddPath(body, false); // AddPath kopiert – den Hilfspfad gleich freigeben
        // Griffe: gedrehte Ellipsen unter den Ecken – liest sich wie die Handauflagen eines Pro-Controllers.
        void Grip(float cx, float angle)
        {
            using var grip = new GraphicsPath();
            grip.AddEllipse(new RectangleF(-0.072f * s, -0.125f * s, 0.144f * s, 0.25f * s));
            using var m = new System.Drawing.Drawing2D.Matrix();
            m.RotateAt(angle, PointF.Empty);
            m.Translate(r.X + cx * s, r.Y + 0.70f * s, MatrixOrder.Append);
            grip.Transform(m);
            pad.AddPath(grip, false);
        }
        Grip(0.5f - 0.245f, 20f);
        Grip(0.5f + 0.245f, -20f);
        return pad;
    }

    /// <summary>Katzenauge (Mandelform): zwei Bezier-Bögen, oben und unten gespiegelt.</summary>
    private static GraphicsPath LeafEye(float ecx, float ecy, float rx, float ry, float s, RectangleF r)
    {
        float X(float x) => r.X + x * s;
        float Y(float y) => r.Y + y * s;
        var eye = new GraphicsPath();
        float cx = rx * 0.62f;
        eye.AddBezier(X(ecx - rx), Y(ecy), X(ecx - cx), Y(ecy - ry), X(ecx + cx), Y(ecy - ry), X(ecx + rx), Y(ecy));
        eye.AddBezier(X(ecx + rx), Y(ecy), X(ecx + cx), Y(ecy + ry), X(ecx - cx), Y(ecy + ry), X(ecx - rx), Y(ecy));
        eye.CloseFigure();
        return eye;
    }

    /// <summary>
    /// App-Icon: Gamepad-Katze im DevCat-Stil wie SKZ Monitor (Monitor-Katze) und SKZ Remote (Handy-Katze): dünne
    /// Iris-Kontur, offene Ohren, dunkles Gesicht mit kleinen leuchtenden Augen. Vektor – in jeder Größe scharf; kleine
    /// Größen lassen Details weg (ab 24 px Steuerkreuz und Tasten, ab 32 px Augen-Leuchten, ab 40 px Wi-Fi-Signal).
    /// <paramref name="tile"/> false = nur das Glyph, etwas größer, damit es im Infobereich die Fläche füllt.
    /// </summary>
    public static void DrawLogo(Graphics g, RectangleF r, bool tile = true)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        if (tile)
        {
            float t = r.Width;
            using var tilePath = Theme.RoundedRect(r, t * 0.23f);
            using (var back = new LinearGradientBrush(r, TileTop, TileBottom, LinearGradientMode.Vertical))
                g.FillPath(back, tilePath);
        }
        else
        {
            // Glyph reicht im Einheitsquadrat von 0.15 bis 0.85 – ohne Kachel um diesen Bereich vergrößern.
            const float zoom = 1.2f;
            r = new RectangleF(r.X + r.Width * (1 - zoom) / 2, r.Y + r.Height * (1 - zoom) / 2 + r.Height * 0.01f,
                r.Width * zoom, r.Height * zoom);
        }

        float s = r.Width;
        PointF P(float x, float y) => new(r.X + x * s, r.Y + y * s);
        RectangleF R(float x, float y, float w, float h) => new(r.X + x * s, r.Y + y * s, w * s, h * s);
        float rim = Math.Max(1.2f, s * 0.03f);

        using (var iris = Iris(R(0.15f, 0.2f, 0.7f, 0.65f)))
        {
            // Katzenohren als offene Iris-Konturen – die Basis verschwindet unter der Gesichtsfläche.
            using (var earPen = new Pen(iris, rim) { LineJoin = LineJoin.Miter, MiterLimit = 6f })
            {
                foreach (int sgn in new[] { -1, 1 })
                {
                    float bcx = 0.5f + sgn * 0.175f;
                    g.DrawLines(earPen, new[]
                    {
                        P(bcx - 0.075f, 0.38f), P(bcx + sgn * 0.012f, 0.245f), P(bcx + 0.075f, 0.38f),
                    });
                }
            }

            // Pad-Kontur: doppelt so breit zeichnen und danach die Fläche deckend füllen – übrig bleibt nur der
            // äußere Rand der Gesamtform, ohne Linien, wo sich Körper und Griffe überlappen.
            using var pad = PadPath(r, s);
            using (var rimPen = new Pen(iris, rim * 2) { LineJoin = LineJoin.Round })
                g.DrawPath(rimPen, pad);
            using var face = new SolidBrush(Face);
            g.FillPath(face, pad);
        }

        // Katzenaugen: kleine Cyan-Mandeln mit Leuchten und dunkler Schlitzpupille.
        float er = 0.068f, ey = 0.475f;
        foreach (int sgn in new[] { -1, 1 })
        {
            float ecx = 0.5f + sgn * 0.12f;
            if (s >= 32)
            {
                var c = P(ecx, ey);
                float gr = er * 1.5f * s;
                using var glowPath = new GraphicsPath();
                glowPath.AddEllipse(c.X - gr, c.Y - gr, gr * 2, gr * 2);
                using var glow = new PathGradientBrush(glowPath)
                {
                    CenterColor = Color.FromArgb(110, EyeCyan),
                    SurroundColors = [Color.FromArgb(0, EyeCyan)],
                };
                g.FillPath(glow, glowPath);
            }
            using var eyeBrush = new SolidBrush(EyeCyan);
            using var eye = LeafEye(ecx, ey, er, er * 0.5f, s, r);
            g.FillPath(eyeBrush, eye);
            float pw = er * 0.3f, ph = er * 0.78f;
            using var pupilBrush = new SolidBrush(Face);
            using var pupil = Theme.RoundedRect(R(ecx - pw / 2, ey - ph / 2, pw, ph), pw / 2);
            g.FillPath(pupilBrush, pupil);
        }

        // Nase (kleines Amber-Dreieck) unter den Augen.
        using (var nose = new SolidBrush(Amber))
            g.FillPolygon(nose, new[] { P(0.474f, 0.535f), P(0.526f, 0.535f), P(0.5f, 0.562f) });

        if (s >= 24)
        {
            // Steuerkreuz links, vier Tasten als Raute rechts – dezent in Dim-Teal.
            using var dim = new SolidBrush(DimTeal);
            using (var dpad = new GraphicsPath(FillMode.Winding))
            {
                using (var across = Theme.RoundedRect(R(0.263f, 0.597f, 0.09f, 0.026f), s * 0.013f))
                    dpad.AddPath(across, false);
                using (var down = Theme.RoundedRect(R(0.295f, 0.565f, 0.026f, 0.09f), s * 0.013f))
                    dpad.AddPath(down, false);
                g.FillPath(dim, dpad);
            }
            float b = 0.026f;
            foreach (var (x, y) in new[] { (0.692f, 0.574f), (0.730f, 0.610f), (0.692f, 0.646f), (0.654f, 0.610f) })
                g.FillEllipse(dim, R(x - b / 2, y - b / 2, b, b));
        }

        if (s >= 44)
        {
            // Wi-Fi-Bögen oben mittig zwischen den Ohren (Connect-Signatur), Amber wie die Nase.
            float w = Math.Max(1.5f, s * 0.02f);
            using var pen = new Pen(Amber, w) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var center = P(0.5f, 0.275f);
            foreach (float radius in new[] { 0.04f, 0.075f })
            {
                float rr = radius * s;
                g.DrawArc(pen, center.X - rr, center.Y - rr, rr * 2, rr * 2, 225, 90);
            }
            float d = s * 0.026f;
            using var dot = new SolidBrush(Amber);
            g.FillEllipse(dot, center.X - d / 2, center.Y - d / 2, d, d);
        }
    }

    /// <summary>Logo als quadratisches Bild. <paramref name="tile"/> false = nur das Glyph (Tray, ICO).</summary>
    /// <remarks>In der Oberfläche direkt mit <see cref="DrawLogo"/> in Zielgröße zeichnen – ein verkleinertes Bild wird an den Kanten pixelig.</remarks>
    public static Bitmap Render(int size, bool tile = true)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        float margin = size >= 48 ? size / 32f : 0;
        DrawLogo(g, new RectangleF(margin, margin, size - 2 * margin, size - 2 * margin), tile);
        return bmp;
    }

    /// <summary>Programm-Icon und Installer-Grafiken in <paramref name="folder"/> speichern.</summary>
    public static void RenderAll(string folder)
    {
        Directory.CreateDirectory(folder);
        // ICO: nur das Glyph mit transparentem Grund – wie bei SKZ, im Tray soll kein Kasten kleben.
        WriteIco(Path.Combine(folder, "SKZ-Connect.ico"), [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]);
        // PNG-Master (Kodi, Website, Android-Altform): volle Navy-Fläche wie bei SKZ Monitor/Remote, das Glyph mit
        // demselben Abstand zum Rand (~65 % Breite).
        using (var png = new Bitmap(512, 512, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(png))
            {
                var all = new Rectangle(0, 0, 512, 512);
                using (var back = new LinearGradientBrush(all, TileTop, TileBottom, LinearGradientMode.Vertical))
                    g.FillRectangle(back, all);
                float m = 512 * 0.115f;
                DrawLogo(g, new RectangleF(m, m, 512 - 2 * m, 512 - 2 * m), tile: false);
            }
            png.Save(Path.Combine(folder, "SKZ-Connect.png"), ImageFormat.Png);
        }
        // Glyph ohne Grund für den Vordergrund des adaptiven Android-Icons (android/make_icons.py).
        using (var glyph = Render(1024, tile: false))
            glyph.Save(Path.Combine(folder, "SKZ-Connect-glyph.png"), ImageFormat.Png);
        // Inno Setup wählt je nach Bildschirmskalierung die passende Größe (100 %, 150 %, 200 %).
        foreach (var (w, h, tag) in new[] { (164, 314, "100"), (246, 471, "150"), (328, 628, "200") })
            SaveBmp(WizardImage(w, h), Path.Combine(folder, $"wizard-{tag}.bmp"));
        foreach (var (size, tag) in new[] { (55, "100"), (83, "150"), (110, "200") })
            SaveBmp(WizardSmall(size), Path.Combine(folder, $"wizard-small-{tag}.bmp"));
    }

    /// <summary>Großes Bild links auf der Begrüßungs- und Abschlussseite des Installers.</summary>
    private static Bitmap WizardImage(int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        float k = w / 164f;
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var all = new Rectangle(0, 0, w, h);
        using (var back = new LinearGradientBrush(all, TileTop, TileBottom, LinearGradientMode.Vertical))
            g.FillRectangle(back, all);
        Glow(g, new PointF(w * 0.5f, h * 0.30f), w * 0.85f, IrisTeal, 45); // dezenter Schein hinter dem Icon

        float logo = 84 * k;
        DrawLogo(g, new RectangleF((w - logo) / 2, h * 0.20f, logo, logo));

        using var title = new Font("Segoe UI Semibold", 19 * k, GraphicsUnit.Pixel);
        using var sub = new Font("Segoe UI", 10.5f * k, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center };
        float y = h * 0.20f + logo + 18 * k;
        g.DrawString("SKZ Connect", title, Brushes.White, new RectangleF(0, y, w, 30 * k), center);
        using (var muted = new SolidBrush(Color.FromArgb(0xC5, 0xC5, 0xC5)))
            g.DrawString("Switch · Switch 2 · Wii · Wii U\nController für Windows", sub, muted,
                new RectangleF(6 * k, y + 30 * k, w - 12 * k, 40 * k), center);

        // Feine Akzentlinie im Iris-Verlauf am unteren Rand.
        float barY = h - 10 * k;
        using (var bar = Iris(new RectangleF(0, barY, w, 3 * k)))
            g.FillRectangle(bar, 24 * k, barY, w - 48 * k, 3 * k);
        return bmp;
    }

    private static void Glow(Graphics g, PointF center, float radius, Color color, int alpha)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(alpha, color),
            SurroundColors = [Color.FromArgb(0, color)],
        };
        g.FillEllipse(brush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
    }

    /// <summary>Kleines Bild oben rechts auf den übrigen Installer-Seiten (heller Seitenhintergrund).</summary>
    private static Bitmap WizardSmall(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        float m = size * 0.04f;
        DrawLogo(g, new RectangleF(m, m, size - 2 * m, size - 2 * m));
        return bmp;
    }

    private static void SaveBmp(Bitmap bmp, string path)
    {
        using (bmp)
            bmp.Save(path, ImageFormat.Bmp);
    }

    /// <summary>
    /// ICO-Datei in allen angegebenen Größen: 256 px als PNG, kleinere als 32-Bit-Bitmap mit Alphakanal – PNG-Einträge
    /// in kleinen Größen zeigen manche Lader (z. B. System.Drawing unter .NET Framework) als Pixelrauschen.
    /// </summary>
    private static void WriteIco(string path, int[] sizes)
    {
        var images = sizes.Select(size =>
        {
            using var bmp = Render(size, tile: false);
            using var ms = new MemoryStream();
            if (size >= 256)
                bmp.Save(ms, ImageFormat.Png);
            else
                WriteDib(bmp, ms);
            return ms.ToArray();
        }).ToList();
        using var file = File.Create(path);
        using var w = new BinaryWriter(file);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var image in images)
            w.Write(image);
    }

    /// <summary>Icon-Bild im DIB-Format: BITMAPINFOHEADER (doppelte Höhe), BGRA von unten nach oben, leere UND-Maske.</summary>
    private static void WriteDib(Bitmap bmp, Stream stream)
    {
        int size = bmp.Width;
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(40);
        w.Write(size);
        w.Write(size * 2);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(0); // BI_RGB
        int maskStride = (size + 31) / 32 * 4;
        w.Write(size * size * 4 + maskStride * size);
        w.Write(0L);
        w.Write(0L);
        var data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[size * 4];
            for (int y = size - 1; y >= 0; y--)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                w.Write(row);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        w.Write(new byte[maskStride * size]);
    }
}
