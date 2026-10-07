using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Switch2Pro.Bridge;

/// <summary>
/// Logo von N-Connect: ein „N“ aus zwei Controller-Hälften (links blau, rechts rot) mit weißer Diagonale auf einer
/// dunklen, abgerundeten Kachel. Wird zur Laufzeit gezeichnet (Infobereich, Fenster) und per <c>--render-brand</c>
/// als Programm-Icon und Installer-Grafiken gespeichert.
/// </summary>
internal static class Branding
{
    public static readonly Color Blue = Color.FromArgb(0, 180, 240);
    public static readonly Color Red = Color.FromArgb(255, 70, 70);

    private static Icon? _appIcon;

    /// <summary>Fenstersymbol mit allen Größen (eingebettete ICO-Datei, sonst gezeichnet).</summary>
    public static Icon AppIcon => _appIcon ??= LoadAppIcon();

    private static Icon LoadAppIcon()
    {
        try
        {
            // Nicht ExtractAssociatedIcon: das liefert nur 32 px, verkleinert auf die Titelleiste wirkt der Rand pixelig.
            using var stream = typeof(Branding).Assembly.GetManifestResourceStream("N-Connect.ico");
            if (stream is not null)
                return new Icon(stream);
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            // Fällt auf das gezeichnete Symbol zurück.
        }
        using var bmp = Render(32);
        IntPtr handle = bmp.GetHicon();
        using var temp = Icon.FromHandle(handle);
        return (Icon)temp.Clone();
    }

    private static Bitmap? _logoImage;

    /// <summary>Offizielles Logo-Bild (eingebettetes PNG), sonst null → gezeichnete Variante.</summary>
    private static Bitmap? LogoImage()
    {
        if (_logoImage is not null)
            return _logoImage;
        try
        {
            var stream = typeof(Branding).Assembly.GetManifestResourceStream("N-Connect.png");
            if (stream is not null)
                _logoImage = new Bitmap(stream);
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
        }
        return _logoImage;
    }

    /// <summary>Logo in das Quadrat <paramref name="r"/> zeichnen.</summary>
    public static void DrawLogo(Graphics g, RectangleF r, bool tile = true)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var image = LogoImage();
        if (image is not null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, r);
            return;
        }
        float s = r.Width;
        PointF P(float x, float y) => new(r.X + x * s, r.Y + y * s);
        RectangleF R(float x, float y, float w, float h) => new(r.X + x * s, r.Y + y * s, w * s, h * s);

        if (tile)
        {
            using var tilePath = Theme.RoundedRect(r, s * 0.22f);
            using (var back = new LinearGradientBrush(r, Color.FromArgb(52, 55, 70), Color.FromArgb(22, 23, 30), LinearGradientMode.Vertical))
                g.FillPath(back, tilePath);
            if (s >= 48) // bei kleinen Größen wirkt der helle Rand wie ein pixeliger weißer Saum
            {
                using var border = new Pen(Color.FromArgb(45, 255, 255, 255), Math.Max(1f, s / 64f));
                using var inner = Theme.RoundedRect(RectangleF.Inflate(r, -border.Width / 2, -border.Width / 2), s * 0.21f);
                g.DrawPath(border, inner);
            }
        }

        // Diagonale des „N“ (unter den beiden Hälften).
        using (var white = new SolidBrush(Color.FromArgb(245, 245, 250)))
            g.FillPolygon(white, [P(0.27f, 0.20f), P(0.44f, 0.20f), P(0.73f, 0.80f), P(0.56f, 0.80f)]);

        DrawHalf(g, R(0.18f, 0.17f, 0.21f, 0.66f), Blue, stickAt: 0.30f, s);
        DrawHalf(g, R(0.61f, 0.17f, 0.21f, 0.66f), Red, stickAt: 0.68f, s);
    }

    private static void DrawHalf(Graphics g, RectangleF r, Color color, float stickAt, float size)
    {
        using var path = Theme.RoundedRect(r, r.Width * 0.5f);
        using (var fill = new LinearGradientBrush(r, ControlPaint.Light(color, 0.25f), ControlPaint.Dark(color, 0.08f), LinearGradientMode.Vertical))
            g.FillPath(fill, path);
        if (size < 24)
            return; // zu klein für Details
        // Stick als dunkler Punkt mit hellem Rand, wie bei einem Joy-Con.
        float d = r.Width * 0.56f;
        float cy = r.Y + r.Height * (stickAt - 0.17f) / 0.66f;
        var stick = new RectangleF(r.X + (r.Width - d) / 2, cy - d / 2, d, d);
        using (var dark = new SolidBrush(Color.FromArgb(40, 42, 52)))
            g.FillEllipse(dark, stick);
        using (var ring = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1f, size / 96f)))
            g.DrawEllipse(ring, stick);
    }

    /// <summary>Logo als quadratisches Bild mit transparentem Rand.</summary>
    /// <remarks>Für die Oberfläche <see cref="LogoView"/> nutzen – ein verkleinertes Bild wird an den Kanten pixelig.</remarks>
    public static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        float margin = size >= 48 ? size / 32f : 0;
        DrawLogo(g, new RectangleF(margin, margin, size - 2 * margin, size - 2 * margin));
        return bmp;
    }

    /// <summary>Programm-Icon und Installer-Grafiken in <paramref name="folder"/> speichern.</summary>
    public static void RenderAll(string folder)
    {
        Directory.CreateDirectory(folder);
        WriteIco(Path.Combine(folder, "N-Connect.ico"), [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]);
        using (var png = Render(512))
            png.Save(Path.Combine(folder, "N-Connect.png"), ImageFormat.Png);
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
        using (var back = new LinearGradientBrush(all, Color.FromArgb(30, 32, 42), Color.FromArgb(12, 13, 18), LinearGradientMode.Vertical))
            g.FillRectangle(back, all);
        Glow(g, new PointF(w * 0.05f, h * 0.18f), w * 0.9f, Blue, 70);
        Glow(g, new PointF(w * 0.98f, h * 0.88f), w * 0.95f, Red, 60);

        float logo = 84 * k;
        DrawLogo(g, new RectangleF((w - logo) / 2, h * 0.20f, logo, logo));

        using var title = new Font("Segoe UI Semibold", 19 * k, GraphicsUnit.Pixel);
        using var sub = new Font("Segoe UI", 10.5f * k, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center };
        float y = h * 0.20f + logo + 18 * k;
        g.DrawString("N-Connect", title, Brushes.White, new RectangleF(0, y, w, 30 * k), center);
        using (var muted = new SolidBrush(Color.FromArgb(175, 178, 190)))
            g.DrawString("Switch · Switch 2 · Wii · Wii U\nController für Windows", sub, muted,
                new RectangleF(6 * k, y + 30 * k, w - 12 * k, 40 * k), center);

        // Feine Linie in den Controller-Farben am unteren Rand.
        float barY = h - 10 * k;
        using (var bar = new LinearGradientBrush(new RectangleF(0, barY, w, 3 * k), Blue, Red, LinearGradientMode.Horizontal))
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
            using var bmp = Render(size);
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

/// <summary>Logo in der Oberfläche: direkt in Zielgröße auf den Fensterhintergrund gezeichnet (scharf, ohne hellen Saum).</summary>
internal sealed class LogoView : Control
{
    public LogoView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Backdrop);
        Branding.DrawLogo(e.Graphics, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f));
    }
}
