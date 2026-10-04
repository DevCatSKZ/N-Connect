using System.Drawing;
using System.Drawing.Drawing2D;
using Switch2Pro.Bridge.Links;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Geführte Stick-Kalibrierung eines Controllers: Live-Anzeige (wo steht der Stick, wie rund läuft er), Messen von
/// Mitte und Rand (<see cref="StickCalibrator"/>), Übernehmen oder zurück zu den Werkswerten. Gespeichert wird nur in
/// N-Connect (Einstellungen), nicht im Controller.
/// </summary>
internal sealed class StickCalibrationForm : Form
{
    private sealed record StickRef(IControllerLink Link, bool Left, string Title);

    private readonly Player _player;
    private readonly Func<Settings> _settings;
    private readonly Action _save;
    private readonly List<StickRef> _sticks;
    private readonly Segmented? _choice;
    private readonly Canvas _canvas = new() { Size = new Size(300, 300) };
    private readonly Label _step = new() { AutoSize = false, Size = new Size(250, 120), Font = UiFonts.Body };
    private readonly Label _result = new() { AutoSize = false, Size = new Size(250, 90), Font = UiFonts.Small };
    private readonly GlyphButton _measure = new("Neu messen", Glyph.Stick, accent: true);
    private readonly GlyphButton _apply = new("Übernehmen", Glyph.Check);
    private readonly GlyphButton _factory = new("Werkswerte", Glyph.Undo);
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private StickCalibrator? _calibrator;
    private StickRef? _current;

    /// <summary>Kalibrierbare Sticks eines Spielers (Switch-1/2-Controller; Wii-Sticks haben ein anderes Format).</summary>
    private static List<StickRef> SticksOf(Player player)
    {
        var list = new List<StickRef>();
        foreach (var link in player.Links)
        {
            // DemoLink: simulierte Controller der Prüfhilfe (--demo, --render-ui).
            if (link is not (Switch2BleLink or Switch2UsbLink or Switch1HidLink or DemoLink) || link.Address is null)
                continue;
            var k = link.Kind;
            bool both = k is ControllerKind.Pro2 or ControllerKind.Pro1 or ControllerKind.GameCube2;
            if (both || k.IsLeftJoyCon() || k == ControllerKind.N64Controller)
                list.Add(new StickRef(link, true, both ? "Linker Stick" : k.IsJoyCon() ? "Stick (linker Joy-Con)" : "Stick"));
            if (both || k.IsJoyCon() && !k.IsLeftJoyCon())
                list.Add(new StickRef(link, false, both ? "Rechter Stick" : "Stick (rechter Joy-Con)"));
        }
        return list;
    }

    public static bool CanCalibrate(Player player) => SticksOf(player).Count > 0;

    public StickCalibrationForm(Player player, Func<Settings> settings, Action save)
    {
        _player = player;
        _settings = settings;
        _save = save;
        _sticks = SticksOf(player);
        Text = "Sticks kalibrieren";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        Font = UiFonts.Body;
        ClientSize = new Size(600, 420);

        int top = 16;
        if (_sticks.Count > 1)
        {
            _choice = new Segmented(_sticks.Select(s => s.Title).ToArray()) { Location = new Point(20, top) };
            _choice.SelectedIndexChanged += (_, _) => SelectStick(_choice.SelectedIndex);
            Controls.Add(_choice);
            top += 48;
        }
        _canvas.Location = new Point(20, top);
        _step.Location = new Point(_canvas.Right + 24, top);
        _result.Location = new Point(_canvas.Right + 24, _step.Bottom + 8);
        int buttons = top + _canvas.Height + 16;
        _measure.Location = new Point(20, buttons);
        _apply.Location = new Point(_measure.Right + 8, buttons);
        _factory.Location = new Point(_apply.Right + 8, buttons);
        _measure.Click += (_, _) => Start();
        _apply.Click += (_, _) => Apply();
        _factory.Click += (_, _) => Factory();
        Controls.AddRange([_canvas, _step, _result, _measure, _apply, _factory]);
        ClientSize = new Size(Math.Max(600, _factory.Right + 20), buttons + _measure.Height + 20);

        _timer.Tick += (_, _) => Tick();
        Theme.Apply(this);
        Tr.Apply(this);
        SelectStick(0);
        _timer.Start();
    }

    private void SelectStick(int index)
    {
        if (index < 0 || index >= _sticks.Count)
            return;
        _current = _sticks[index];
        _calibrator = null;
        _apply.Enabled = false;
        _measure.Text = Tr.T("Neu messen");
        bool own = _settings().StickCalibrationFor(_current.Link.Address, _current.Left) is not null;
        _factory.Enabled = own;
        _step.Text = Tr.T("Der Punkt zeigt, wo der Stick gerade steht. Steht er losgelassen nicht in der Mitte oder " +
                          "erreicht er den Kreis nicht, auf „Neu messen“ klicken.");
        _result.Text = Tr.T(own ? "Gilt gerade: eigene Kalibrierung." : "Gilt gerade: Werkswerte des Controllers.");
        _canvas.Edge = null;
        _canvas.Invalidate();
    }

    private (int X, int Y)? Raw()
    {
        if (_current?.Link.LastState is not { } s)
            return null;
        return _current.Left ? (s.LeftX, s.LeftY) : (s.RightX, s.RightY);
    }

    private StickCalibration CurrentCalibration()
    {
        var cal = Player.CalibrationFor(_current!.Link, _settings());
        return _current.Left ? cal.Left : cal.Right;
    }

    private void Tick()
    {
        if (_current is null || Raw() is not { } raw)
            return;
        var cal = CurrentCalibration();
        _canvas.Point = (cal.X.Normalize(raw.X), cal.Y.Normalize(raw.Y));
        if (_calibrator is { } c && c.Current != StickCalibrator.Phase.Done)
        {
            bool changed = c.Add(raw.X, raw.Y, Environment.TickCount64);
            _canvas.Progress = c.Progress;
            if (c.Current == StickCalibrator.Phase.Rotate)
                _canvas.Edge = c.EdgePoints.Select(p => (p.X / 1500f, p.Y / 1500f)).ToList();
            if (changed)
                ShowStep();
        }
        _canvas.Invalidate();
    }

    private void Start()
    {
        _calibrator = new StickCalibrator();
        _apply.Enabled = false;
        _canvas.Edge = null;
        ShowStep();
    }

    private void ShowStep()
    {
        var c = _calibrator!;
        switch (c.Current)
        {
            case StickCalibrator.Phase.Center:
                _step.Text = Tr.T("1/2 – Stick loslassen und den Controller ruhig halten …");
                _result.Text = "";
                break;
            case StickCalibrator.Phase.Rotate:
                _step.Text = Tr.T("2/2 – Stick bis zum Anschlag drücken und langsam ein- bis zweimal im Kreis drehen.");
                break;
            case StickCalibrator.Phase.Done when c.Result is { } r:
                float before = c.RoundnessError(CurrentCalibration()), after = c.RoundnessError(r);
                _step.Text = Tr.T("Fertig. „Übernehmen“ speichert die neue Kalibrierung für diesen Controller.");
                _result.Text = Tr.T($"Mitte {r.X.Neutral} / {r.Y.Neutral} · Abweichung vom Kreis: vorher {before:F0} %, neu {after:F0} %");
                _canvas.Edge = c.EdgePoints.Select(p => (r.X.Normalize(c.Center.X + p.X), r.Y.Normalize(c.Center.Y + p.Y))).ToList();
                _apply.Enabled = true;
                break;
            default:
                _step.Text = Tr.T("Der Stick hat den Rand nicht erreicht. Bitte bis zum Anschlag drücken und erneut messen.");
                break;
        }
    }

    private void Apply()
    {
        if (_current?.Link.Address is not { } address || _calibrator?.Result is not { } r)
            return;
        _settings().SetStickCalibration(address, _current.Left, r);
        _save();
        Log.Info($"{address}: Stick {(_current.Left ? "links" : "rechts")} kalibriert (Mitte {r.X.Neutral}/{r.Y.Neutral})");
        _apply.Enabled = false;
        _factory.Enabled = true;
        _calibrator = null;
        _result.Text = Tr.T("Übernommen ✓ – gilt ab sofort.");
    }

    private void Factory()
    {
        if (_current?.Link.Address is not { } address)
            return;
        _settings().SetStickCalibration(address, _current.Left, null);
        _save();
        _factory.Enabled = false;
        _result.Text = Tr.T("Werkswerte des Controllers gelten wieder.");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>Kreis (voller Ausschlag), Achsen, aktuelle Stickstellung und beim Messen die erfassten Randpunkte.</summary>
    private sealed class Canvas : Control
    {
        public (float X, float Y) Point { get; set; }
        public List<(float X, float Y)>? Edge { get; set; }
        public float Progress { get; set; }

        public Canvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.Current;
            g.Clear(p.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float r = Math.Min(Width, Height) / 2f - 14, cx = Width / 2f, cy = Height / 2f;
            using (var axis = new Pen(p.Border))
            {
                g.DrawLine(axis, cx - r, cy, cx + r, cy);
                g.DrawLine(axis, cx, cy - r, cx, cy + r);
            }
            using (var ring = new Pen(p.TextMuted, 1.5f))
                g.DrawEllipse(ring, cx - r, cy - r, 2 * r, 2 * r);
            if (Edge is { Count: > 0 } edge)
                using (var dot = new SolidBrush(Theme.Blend(p.Surface, Theme.Accent, 0.7f)))
                    foreach (var (x, y) in edge)
                        g.FillEllipse(dot, cx + x * r - 3, cy - y * r - 3, 6, 6);
            if (Progress > 0 && Progress < 1)
                using (var arc = new Pen(Theme.Accent, 4f))
                    g.DrawArc(arc, cx - r - 8, cy - r - 8, 2 * r + 16, 2 * r + 16, -90, 360 * Progress);
            float px = cx + Point.X * r, py = cy - Point.Y * r;
            using (var line = new Pen(Theme.Blend(p.Surface, Theme.Accent, 0.5f), 1.5f))
                g.DrawLine(line, cx, cy, px, py);
            using (var stick = new SolidBrush(Theme.Accent))
                g.FillEllipse(stick, px - 7, py - 7, 14, 14);
        }
    }
}
