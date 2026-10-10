using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Switch2Pro.Bridge;

/// <summary>
/// Logo von N-Connect im Stil von Windows 11: weiße Controller-Silhouette mit Verbindungssignal auf einer blauen,
/// abgerundeten Kachel. Wird als Vektor gezeichnet (Fenster, Infobereich) und per <c>--render-brand</c> als
/// Programm-Icon und Installer-Grafiken gespeichert (ohne Fenster).
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

    // Farben des App-Icons (Windows-Blau, Kachel von hell oben nach tief unten).
    private static readonly Color TileTop = Color.FromArgb(0x4C, 0xB4, 0xFF);
    private static readonly Color TileBottom = Color.FromArgb(0x00, 0x5A, 0xD0);
    private static readonly Color Ink = Color.FromArgb(0x0B, 0x4F, 0xB5); // Details auf der weißen Silhouette

    /// <summary>
    /// App-Icon im Stil von Windows 11: abgerundete Kachel im Windows-Blau, weiße Controller-Silhouette, darüber ein
    /// Verbindungssignal. Vektor – in jeder Größe scharf; kleine Größen lassen Details weg (16 px: nur Kachel und
    /// Silhouette, ab 24 px Steuerkreuz und Tasten, ab 32 px Signal, ab 48 px Lichtkante und Schatten).
    /// </summary>
    public static void DrawLogo(Graphics g, RectangleF r, bool tile = true)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float s = r.Width;
        PointF P(float x, float y) => new(r.X + x * s, r.Y + y * s);
        RectangleF R(float x, float y, float w, float h) => new(r.X + x * s, r.Y + y * s, w * s, h * s);

        if (tile)
        {
            using var tilePath = Theme.RoundedRect(r, s * 0.23f);
            using (var back = new LinearGradientBrush(r, TileTop, TileBottom, LinearGradientMode.Vertical))
                g.FillPath(back, tilePath);
            if (s >= 48)
            {
                // Lichtkante oben und leicht dunklerer Rand – gibt der Kachel Tiefe wie bei Windows-11-Icons.
                using var edge = new Pen(Color.FromArgb(70, 0, 30, 90), Math.Max(1f, s / 96f));
                g.DrawPath(edge, tilePath);
                using var shine = new LinearGradientBrush(r, Color.FromArgb(28, 255, 255, 255), Color.FromArgb(0, 255, 255, 255),
                    LinearGradientMode.Vertical);
                using var upper = Theme.RoundedRect(R(0.02f, 0.02f, 0.96f, 0.5f), s * 0.21f);
                g.FillPath(shine, upper);
            }
        }

        // Controller-Silhouette: Körper und zwei Griffe als eine Fläche (Winding = Vereinigung).
        using (var pad = new GraphicsPath(FillMode.Winding))
        {
            pad.AddPath(Theme.RoundedRect(R(0.15f, 0.40f, 0.70f, 0.29f), s * 0.145f), false);
            pad.AddEllipse(R(0.155f, 0.50f, 0.235f, 0.30f));
            pad.AddEllipse(R(0.61f, 0.50f, 0.235f, 0.30f));
            if (tile && s >= 48)
            {
                // weicher Schatten unter der Silhouette
                using var shadowMatrix = new Matrix();
                shadowMatrix.Translate(0, s * 0.025f);
                using var shadow = (GraphicsPath)pad.Clone();
                shadow.Transform(shadowMatrix);
                using var shadowBrush = new SolidBrush(Color.FromArgb(55, 0, 20, 70));
                g.FillPath(shadowBrush, shadow);
            }
            using var white = new SolidBrush(Color.FromArgb(0xF7, 0xFA, 0xFF));
            g.FillPath(white, pad);
        }

        if (s >= 24)
        {
            // Steuerkreuz links, vier Tasten rechts.
            using var ink = new SolidBrush(Ink);
            using (var dpad = new GraphicsPath(FillMode.Winding))
            {
                dpad.AddPath(Theme.RoundedRect(R(0.235f, 0.512f, 0.15f, 0.05f), s * 0.012f), false);
                dpad.AddPath(Theme.RoundedRect(R(0.285f, 0.462f, 0.05f, 0.15f), s * 0.012f), false);
                g.FillPath(ink, dpad);
            }
            float b = 0.042f;
            foreach (var (x, y) in new[] { (0.69f, 0.47f), (0.75f, 0.537f), (0.69f, 0.604f), (0.63f, 0.537f) })
                g.FillEllipse(ink, R(x - b / 2, y - b / 2, b, b));
        }

        if (s >= 32)
        {
            // Verbindungssignal über dem Controller: Punkt und zwei Bögen.
            float w = Math.Max(1.5f, s * 0.04f);
            using var pen = new Pen(Color.FromArgb(0xF7, 0xFA, 0xFF), w) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var center = P(0.5f, 0.355f);
            foreach (float radius in new[] { 0.085f, 0.15f })
            {
                float rr = radius * s;
                g.DrawArc(pen, center.X - rr, center.Y - rr, rr * 2, rr * 2, 232, 76);
            }
            float d = s * 0.055f;
            using var dot = new SolidBrush(Color.FromArgb(0xF7, 0xFA, 0xFF));
            g.FillEllipse(dot, center.X - d / 2, center.Y - d / 2, d, d);
        }
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
        using (var back = new LinearGradientBrush(all, Color.FromArgb(0x2B, 0x2B, 0x2B), Color.FromArgb(0x1A, 0x1A, 0x1A), LinearGradientMode.Vertical))
            g.FillRectangle(back, all);
        Glow(g, new PointF(w * 0.5f, h * 0.30f), w * 0.85f, TileTop, 55); // dezenter blauer Schein hinter dem Icon

        float logo = 84 * k;
        DrawLogo(g, new RectangleF((w - logo) / 2, h * 0.20f, logo, logo));

        using var title = new Font("Segoe UI Semibold", 19 * k, GraphicsUnit.Pixel);
        using var sub = new Font("Segoe UI", 10.5f * k, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center };
        float y = h * 0.20f + logo + 18 * k;
        g.DrawString("N-Connect", title, Brushes.White, new RectangleF(0, y, w, 30 * k), center);
        using (var muted = new SolidBrush(Color.FromArgb(0xC5, 0xC5, 0xC5)))
            g.DrawString("Switch · Switch 2 · Wii · Wii U\nController für Windows", sub, muted,
                new RectangleF(6 * k, y + 30 * k, w - 12 * k, 40 * k), center);

        // Feine Akzentlinie im Windows-Blau am unteren Rand.
        float barY = h - 10 * k;
        using (var bar = new LinearGradientBrush(new RectangleF(0, barY, w, 3 * k), TileTop, TileBottom, LinearGradientMode.Horizontal))
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
