using System.Drawing;
using System.Drawing.Drawing2D;
using Switch2Pro.Bridge.Links;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Einblendung beim Verbinden (<see cref="Settings.ConnectOverlay"/>): kleines Kärtchen oben rechts mit Spielernummer,
/// Controllername und Akkustand. Bekommt keinen Fokus, lässt Mausklicks durch (stört kein Spiel), blendet nach drei
/// Sekunden aus; mehrere stapeln sich untereinander. Über Spielen im exklusiven Vollbild zeigt Windows sie nicht.
/// </summary>
internal sealed class ConnectOverlay : Form
{
    private static readonly List<ConnectOverlay> Open = [];
    private readonly int _player;
    private readonly string _name;
    private readonly IControllerLink _link;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private long _shownAt;

    private ConnectOverlay(int player, string name, IControllerLink link)
    {
        _player = player;
        _name = name;
        _link = link;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Current.Surface;
        DoubleBuffered = true;
        Size = new Size(UiScale.Px(340), UiScale.Px(76));
        Opacity = 0;
        _timer.Tick += (_, _) => Animate();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT: oben, ohne Taskleiste, ohne Fokus,
            // Klicks gehen durch auf das Fenster darunter.
            cp.ExStyle |= 0x00000008 | 0x00000080 | 0x08000000 | 0x00000020;
            return cp;
        }
    }

    /// <summary>Einblendung für einen gerade verbundenen Controller zeigen (UI-Thread).</summary>
    public static void ShowFor(Player player, IControllerLink link, Settings settings)
    {
        foreach (var old in Open.Where(o => o.IsDisposed).ToList())
            Open.Remove(old);
        var overlay = new ConnectOverlay(player.Index + 1, player.DisplayName(settings), link);
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        int gap = UiScale.Px(12);
        int y = area.Top + gap + Open.Count * (overlay.Height + UiScale.Px(8));
        overlay.Location = new Point(area.Right - overlay.Width - gap, y);
        Open.Add(overlay);
        overlay.Show();
        Theme.ApplyRoundCorners(overlay);
        overlay._shownAt = Environment.TickCount64;
        overlay._timer.Start();
    }

    /// <summary>Ein-/Ausblenden: 0,2 s weich einblenden, 3 s stehen lassen, 0,4 s ausblenden.</summary>
    private void Animate()
    {
        long t = Environment.TickCount64 - _shownAt;
        if (Anim.Disabled)
            Opacity = t < 3000 ? 0.97 : 0;
        else if (t < 200)
            Opacity = 0.97 * t / 200.0;
        else if (t < 3200)
            Opacity = 0.97;
        else
            Opacity = Math.Max(0, 0.97 * (1 - (t - 3200) / 400.0));
        if (t >= 3600)
        {
            _timer.Stop();
            Open.Remove(this);
            Close();
            Dispose();
            return;
        }
        if (t % 250 < 20)
            Invalidate(); // Akkustand kommt oft erst kurz nach dem Verbinden
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Theme.Current;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(p.Surface);
        static int S(int v) => UiScale.Px(v);
        using (var pen = new Pen(Theme.Neon ? Theme.AccentLine : p.ControlBorder))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        // Spielernummer im Kreis (Akzent) links
        var badge = new Rectangle(S(16), (Height - S(40)) / 2, S(40), S(40));
        using (var fill = Theme.AccentBrush(badge, 45))
            g.FillEllipse(fill, badge);
        TextRenderer.DrawText(g, _player.ToString(), UiFonts.Subtitle, badge, Theme.OnAccent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // Name und „Spieler n · Verbunden“
        int x = badge.Right + S(14);
        int batteryW = S(72);
        var nameBox = new Rectangle(x, S(14), Width - x - batteryW - S(12), S(26));
        TextRenderer.DrawText(g, Tr.T(_name), UiFonts.Strong, nameBox, p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, $"{Tr.T($"Spieler {_player}")} · {Tr.T("Verbunden")}", UiFonts.Small,
            new Rectangle(x, nameBox.Bottom, nameBox.Width, S(22)), p.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // Akku rechts (falls der Controller ihn schon gemeldet hat)
        if (_link.LastState is { BatteryPercent: >= 0 } st)
        {
            int percent = Math.Min(100, st.BatteryPercent);
            var frame = new RectangleF(Width - batteryW - S(4), Height / 2f - S(8), S(30), S(15));
            using (var path = Theme.RoundedRect(frame, S(3)))
            using (var pen = new Pen(p.TextMuted, 1.3f))
                g.DrawPath(pen, path);
            using (var fill = new SolidBrush(StatusBand.BatteryColor(percent)))
                g.FillRectangle(fill, frame.X + S(2), frame.Y + S(2), Math.Max(S(2), (frame.Width - S(4)) * percent / 100f), frame.Height - S(4));
            TextRenderer.DrawText(g, $"{percent} %", UiFonts.Small, new Rectangle((int)frame.Right + S(4), 0, S(40), Height), p.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();
        base.Dispose(disposing);
    }
}
