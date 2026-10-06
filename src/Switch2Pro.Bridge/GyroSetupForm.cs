using System.Drawing;
using System.Drawing.Drawing2D;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Gyro-Assistent: in drei Schritten zum Zielen per Bewegung – 1. Nullpunkt messen, 2. wann der Gyro zielt,
/// 3. Empfindlichkeit – mit Live-Vorschau (Fadenkreuz folgt dem Controller wie im Spiel). Ändert dieselben
/// Einstellungen wie die Seite „Gyro &amp; Maus“, sofort wirksam.
/// </summary>
internal sealed class GyroSetupForm : Form
{
    private readonly Player _player;
    private readonly Func<Settings> _settings;
    private readonly Action _save;
    private readonly GlyphButton _calibrate = new("Nullpunkt messen", Glyph.Gauge, accent: true);
    private readonly Label _calibrateState = new() { AutoSize = false, Size = new Size(300, 40), Font = UiFonts.Small };
    private readonly Segmented _mode = new("Nur per Taste", "Immer", "Beim Zielen (ZL/LT)");
    private readonly Slider _speed = new() { Minimum = 40, Maximum = 600, SmallChange = 10, Width = 320, Format = v => $"{v} °/s" };
    private readonly Slider _minimum = new() { Minimum = 0, Maximum = 40, Width = 320, Format = v => $"{v} %" };
    private readonly ToggleSwitch _invert = new();
    private readonly Preview _preview = new() { Size = new Size(300, 300) };
    private readonly GlyphButton _center = new("Mitte", Glyph.Refresh);
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private bool _loading;
    private long _lastTick = Environment.TickCount64;

    public GyroSetupForm(Player player, Func<Settings> settings, Action save)
    {
        _player = player;
        _settings = settings;
        _save = save;
        Text = "Gyro einrichten";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        Font = UiFonts.Body;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        int x = 20, y = 16, right = 380;
        Label Heading(string text)
        {
            var l = new Label { Text = text, AutoSize = true, Font = UiFonts.Strong, Location = new Point(x, y) };
            Controls.Add(l);
            y += 28;
            return l;
        }
        Label Note(string text, int width = 340)
        {
            var l = new Label { Text = text, AutoSize = false, Size = new Size(width, 36), Font = UiFonts.Small, Location = new Point(x, y) };
            Controls.Add(l);
            y += 38;
            return l;
        }

        Heading("1  Nullpunkt");
        Note("Controller flach auf den Tisch legen, nicht berühren – verhindert, dass das Ziel von selbst wandert.");
        _calibrate.Location = new Point(x, y);
        _calibrateState.Location = new Point(x, y + 40);
        _calibrate.Click += async (_, _) => await CalibrateAsync();
        Controls.AddRange([_calibrate, _calibrateState]);
        y += 80;

        Heading("2  Wann zielt der Gyro?");
        _mode.Location = new Point(x, y);
        Controls.Add(_mode);
        y += 44;
        Note("„Beim Zielen“: nur solange ZL (linker Trigger) gehalten wird – wie in vielen Shootern.");

        Heading("3  Empfindlichkeit");
        Controls.Add(new Label { Text = "Voller Ausschlag ab", AutoSize = true, Location = new Point(x, y + 6) });
        _speed.Location = new Point(x + 140, y);
        Controls.Add(_speed);
        y += 40;
        Controls.Add(new Label { Text = "Mindestausschlag", AutoSize = true, Location = new Point(x, y + 6) });
        _minimum.Location = new Point(x + 140, y);
        Controls.Add(_minimum);
        y += 40;
        Controls.Add(new Label { Text = "Hoch/runter umkehren", AutoSize = true, Location = new Point(x, y + 6) });
        _invert.Location = new Point(x + 140, y);
        Controls.Add(_invert);
        y += 44;
        Note("Kleinerer Wert bei „Voller Ausschlag ab“ = empfindlicher. „Mindestausschlag“ überwindet die Totzone des Spiels.", 460);

        _preview.Location = new Point(x + right + 120, 16);
        _center.Location = new Point(_preview.Left, _preview.Bottom + 8);
        _center.Click += (_, _) => _preview.Recenter();
        Controls.AddRange([_preview, _center]);
        Controls.Add(new Label
        {
            Text = "Vorschau: Fadenkreuz folgt dem Controller (unabhängig von Schritt 2).", AutoSize = false,
            Size = new Size(300, 36), Font = UiFonts.Small, Location = new Point(_preview.Left, _center.Bottom + 8),
        });
        ClientSize = new Size(_preview.Right + 20, Math.Max(y, _center.Bottom + 60) + 12);

        LoadValues();
        _mode.SelectedIndexChanged += (_, _) => Change(s => s.GyroStick = _mode.SelectedIndex switch
        {
            1 => GyroStickMode.Always, 2 => GyroStickMode.WhileAiming, _ => GyroStickMode.Off,
        });
        _speed.ValueChanged += (_, _) => Change(s => s.GyroStickFullSpeed = _speed.Value);
        _minimum.ValueChanged += (_, _) => Change(s => s.GyroStickAntiDeadzone = _minimum.Value / 100f);
        _invert.CheckedChanged += (_, _) => Change(s => s.GyroStickInvertY = _invert.Checked);
        _timer.Tick += (_, _) => Tick();
        Theme.Apply(this);
        Tr.Apply(this);
        _timer.Start();
    }

    private void LoadValues()
    {
        _loading = true;
        var s = _settings();
        _mode.SelectedIndex = s.GyroStick switch { GyroStickMode.Always => 1, GyroStickMode.WhileAiming => 2, _ => 0 };
        _speed.Value = (int)s.GyroStickFullSpeed;
        _minimum.Value = (int)Math.Round(s.GyroStickAntiDeadzone * 100);
        _invert.Checked = s.GyroStickInvertY;
        _loading = false;
    }

    private void Change(Action<Settings> change)
    {
        if (_loading)
            return;
        change(_settings());
        _save();
    }

    private void Tick()
    {
        long now = Environment.TickCount64;
        float dt = Math.Clamp((now - _lastTick) / 1000f, 0f, 0.1f);
        _lastTick = now;
        var (input, _) = ControllerOverview.LiveInput(_player, _settings());
        if (input?.Motion is not { } motion)
        {
            _preview.NoData = true;
            _preview.Invalidate();
            return;
        }
        _preview.NoData = false;
        var (sx, sy) = Mapping.GyroToStick(motion, _settings(), 0, 0);
        _preview.Advance(sx / 32767f, sy / 32767f, dt);
    }

    private bool _calibrating;

    private async Task CalibrateAsync()
    {
        if (_calibrating)
            return;
        _calibrating = true;
        _calibrate.Enabled = false;
        _calibrateState.Text = Tr.T("Ruhig liegen lassen …");
        try
        {
            var result = await _player.CalibrateGyroAsync(TimeSpan.FromSeconds(2));
            if (result is null)
            {
                _calibrateState.Text = Tr.T("Der Controller hat sich bewegt – bitte flach hinlegen und erneut versuchen.");
                return;
            }
            var s = _settings();
            var map = new Dictionary<string, GyroBias>(s.GyroCalibration, StringComparer.OrdinalIgnoreCase);
            foreach (var (address, bias) in result)
                map[address] = bias;
            s.GyroCalibration = map; // neue Kopie: der Bluetooth-Thread liest gleichzeitig
            _save();
            _preview.Recenter();
            _calibrateState.Text = Tr.T("Nullpunkt gemessen ✓");
        }
        finally
        {
            _calibrating = false;
            if (!IsDisposed)
                _calibrate.Enabled = true;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>Fadenkreuz, das sich wie im Spiel mit dem rechten Stick bewegt (Stick-Ausschlag = Geschwindigkeit).</summary>
    private sealed class Preview : Control
    {
        private float _x, _y, _stickX, _stickY;
        public bool NoData { get; set; }

        public Preview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        public void Recenter()
        {
            _x = _y = 0;
            Invalidate();
        }

        /// <summary>Stick-Ausschlag (−1…1) als Drehgeschwindigkeit: voller Ausschlag ≈ halbe Breite pro Sekunde.</summary>
        public void Advance(float stickX, float stickY, float dt)
        {
            _stickX = stickX;
            _stickY = stickY;
            _x = Math.Clamp(_x + stickX * dt, -1f, 1f);
            _y = Math.Clamp(_y - stickY * dt, -1f, 1f);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.Current;
            g.Clear(p.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float half = Width / 2f - 16;
            float cx = Width / 2f, cy = Height / 2f;
            using (var grid = new Pen(p.Border))
            {
                for (int i = -2; i <= 2; i++)
                {
                    g.DrawLine(grid, cx + i * half / 2, cy - half, cx + i * half / 2, cy + half);
                    g.DrawLine(grid, cx - half, cy + i * half / 2, cx + half, cy + i * half / 2);
                }
            }
            if (NoData)
            {
                TextRenderer.DrawText(g, Tr.T("Keine Bewegungsdaten"), UiFonts.Small, ClientRectangle, p.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            float px = cx + _x * half, py = cy + _y * half;
            using (var cross = new Pen(Theme.Accent, 2f))
            {
                g.DrawEllipse(cross, px - 12, py - 12, 24, 24);
                g.DrawLine(cross, px - 20, py, px - 6, py);
                g.DrawLine(cross, px + 6, py, px + 20, py);
                g.DrawLine(cross, px, py - 20, px, py - 6);
                g.DrawLine(cross, px, py + 6, px, py + 20);
            }
            // Kleiner Stick-Kreis unten rechts: aktueller Ausschlag des „rechten Sticks“.
            float r = 28, sx = Width - r - 10, sy = Height - r - 10;
            using (var ring = new Pen(p.TextMuted))
                g.DrawEllipse(ring, sx - r, sy - r, 2 * r, 2 * r);
            using (var dot = new SolidBrush(Theme.Accent))
                g.FillEllipse(dot, sx + _stickX * r - 4, sy - _stickY * r - 4, 8, 8);
        }
    }
}
