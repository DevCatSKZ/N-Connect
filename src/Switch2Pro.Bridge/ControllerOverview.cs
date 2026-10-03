using System.Drawing;
using System.Drawing.Drawing2D;
using Switch2Pro.Bridge.Links;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Übersicht aller verbundenen Controller: je Spieler eine Karte mit gezeichnetem Controller (Live-Eingaben)
/// und allen Eigenschaften (Typ, Akku, Verbindung, Seriennummer, Firmware …).
/// </summary>
internal sealed class ControllerOverview : Panel
{
    internal static readonly Color Background = Color.FromArgb(22, 23, 27);
    internal static readonly Color CardColor = Color.FromArgb(30, 31, 36);
    internal static readonly Color TextColor = Color.FromArgb(236, 237, 241);
    internal static readonly Color MutedColor = Color.FromArgb(150, 155, 166);
    // Schriften einmal für alle Karten (Karten kommen und gehen mit den Controllern).
    private static readonly Font TitleFont = new("Segoe UI Semibold", 13f);
    private static readonly Font ButtonFont = new("Segoe UI", 9.5f);
    private static readonly Font EmptyFont = new("Segoe UI", 11f);
    internal static readonly Color Accent = Color.FromArgb(0, 190, 255);

    private readonly Func<IReadOnlyList<Player>> _players;
    private readonly ControllerManager? _manager;
    private readonly Func<Settings> _settings;
    private readonly FlowLayoutPanel _cards = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
        BackColor = Background, Padding = new Padding(12),
    };
    private readonly Label _empty = new()
    {
        AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = MutedColor, BackColor = Background, Font = EmptyFont,
        Text = "Kein Controller verbunden\n\n" +
               "Switch-2-Controller: kurz die SYNC-Taste drücken – danach reicht ein beliebiger Tastendruck.\n" +
               "Switch-1- und NSO-Controller (NES, SNES, N64, Mega Drive): einmal in Windows unter „Bluetooth“ koppeln.\n" +
               "Wii-Fernbedienung / Wii U Pro: Rechtsklick auf das Symbol im Infobereich → „Wii-Controller koppeln …“.",
    };
    private readonly Dictionary<Player, Card> _byPlayer = [];

    public ControllerOverview(ControllerManager? manager, Func<Settings> settings)
    {
        _manager = manager;
        _players = () => manager?.Players ?? [];
        _settings = settings;
        Dock = DockStyle.Fill;
        BackColor = Background;
        Controls.Add(_cards);
        Controls.Add(_empty);
        Tr.Apply(_empty);
    }

    public void UpdateView()
    {
        var players = _players();
        _empty.Visible = players.Count == 0;
        _cards.Visible = players.Count > 0;
        foreach (var gone in _byPlayer.Keys.Except(players).ToList())
        {
            _cards.Controls.Remove(_byPlayer[gone]);
            _byPlayer[gone].Dispose();
            _byPlayer.Remove(gone);
        }
        foreach (var p in players)
        {
            if (!_byPlayer.TryGetValue(p, out var card))
            {
                card = new Card(_manager!, _settings) { Width = CardWidth };
                _byPlayer[p] = card;
                _cards.Controls.Add(card);
            }
            card.Show(p, _settings());
        }
    }

    private int CardWidth => Math.Max(760, _cards.ClientSize.Width - 30);

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        foreach (var card in _byPlayer.Values)
            card.Width = CardWidth;
    }

    /// <summary>Eine Karte pro Spieler: Grafik links, Eigenschaften rechts, Aktionen oben rechts.</summary>
    private sealed class Card : Panel
    {
        private readonly InputView _view = new() { Location = new Point(16, 52), Size = new Size(522, 369) };
        private readonly Label _title = new()
        {
            AutoSize = true, Location = new Point(16, 14), ForeColor = TextColor, BackColor = Color.Transparent,
            Font = TitleFont,
        };
        private readonly InfoPanel _info = new() { Location = new Point(556, 52) };
        private readonly FlowLayoutPanel _actions = new()
        {
            AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, BackColor = Color.Transparent,
        };
        private readonly ToolTip _tips = new();
        private readonly Button _disconnect = ActionButton("Trennen");
        private readonly Button _identify = ActionButton("Vibrieren");
        private readonly Button _calibrate = ActionButton("Gyro kalibrieren");
        private readonly Button _orientation = ActionButton("");
        private readonly Button _joyConButton = ActionButton("");
        private readonly Button _hide = ActionButton("Doppelt angezeigt? Verstecken");
        private readonly Button _amiibo = ActionButton("amiibo lesen");
        private readonly Button _ringCon = ActionButton("Ring-Con");
        private readonly Button _irCamera = ActionButton("IR-Kamera");
        private readonly ControllerManager _manager;
        private readonly Func<Settings> _settings;
        private Player? _player;
        private bool _calibrating;

        private static Button ActionButton(string text)
        {
            var b = new Button
            {
                Text = Tr.T(text), AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = TextColor, BackColor = Color.FromArgb(45, 47, 54),
                Font = ButtonFont, Padding = new Padding(6, 1, 6, 1), Margin = new Padding(6, 0, 0, 0),
            };
            b.FlatAppearance.BorderColor = MutedColor;
            return b;
        }

        public Card(ControllerManager manager, Func<Settings> settings)
        {
            _manager = manager;
            _settings = settings;
            DoubleBuffered = true;
            BackColor = CardColor;
            Height = 430;
            Margin = new Padding(0, 0, 0, 12);

            _disconnect.Click += (_, _) => { if (_player is not null) _manager.Disconnect(_player); };
            _identify.Click += (_, _) => { if (_player is not null) _player.IdentifyAsync().Forget("Vibrieren"); };
            _calibrate.Click += async (_, _) => await CalibrateAsync();
            _orientation.Click += (_, _) =>
            {
                if (_player?.Links is not [{ Address: { } address }])
                    return;
                var s = _settings();
                s.SetUprightJoyCon(address, !s.IsUprightJoyCon(address));
                _manager.RequestSave();
            };
            _joyConButton.Click += (_, _) =>
            {
                if (_player is null)
                    return;
                if (_player.IsPair)
                    _manager.SplitPair(_player);
                else
                    _manager.PairWithAnySingle(_player);
            };
            _tips.SetToolTip(_disconnect, Tr.T("Verbindung trennen. Der Controller verbindet sich beim nächsten Tastendruck wieder."));
            _tips.SetToolTip(_identify, Tr.T("Controller kurz vibrieren lassen – zeigt, welcher Controller dieser Spieler ist."));
            _tips.SetToolTip(_calibrate, Tr.T("Controller ruhig auf den Tisch legen und klicken: misst den Gyro-Nullpunkt neu (gegen Abdriften)."));
            _tips.SetToolTip(_orientation, Tr.T("Einzelnen Joy-Con quer (wie an der Switch) oder hochkant halten – wird je Joy-Con gemerkt. Hochkant gilt die Tastenbelegung von „Joy-Con-Paar“."));
            _tips.SetToolTip(_hide, Tr.T("Versteckt den per USB angeschlossenen Controller vor Spielen (HidHide), damit sie nur den virtuellen Xbox-Controller sehen."));
            _hide.Click += async (_, _) => await HideAsync();
            _tips.SetToolTip(_amiibo, Tr.T("amiibo mit dem NFC-Leser lesen und als Datei (.bin) speichern – z. B. für Emulatoren."));
            _amiibo.Click += async (_, _) => await ReadAmiiboAsync();
            _tips.SetToolTip(_ringCon, Tr.T("Ring-Con am rechten Joy-Con ein-/ausschalten: zusammendrücken = rechter Trigger, auseinanderziehen = linker Trigger. Beim Einschalten den Ring nicht berühren."));
            _ringCon.Click += async (_, _) => await ToggleRingConAsync();
            _tips.SetToolTip(_irCamera, Tr.T("Live-Bild der IR-Kamera im rechten Joy-Con anzeigen."));
            _irCamera.Click += (_, _) =>
            {
                if (_player?.Links.OfType<Links.Switch1HidLink>().FirstOrDefault(l => l.HasIrCamera) is { } ir)
                    IrCameraForm.ShowFor(ir, FindForm());
            };
            _actions.Controls.AddRange([_disconnect, _identify, _calibrate, _orientation, _joyConButton, _hide, _amiibo, _ringCon, _irCamera]);

            Controls.Add(_title);
            Controls.Add(_view);
            Controls.Add(_info);
            Controls.Add(_actions);
        }

        private async Task ToggleRingConAsync()
        {
            var link = _player?.Links.OfType<Links.Switch1HidLink>().FirstOrDefault(l => l.Kind == ControllerKind.JoyCon1Right);
            if (link is null)
                return;
            _ringCon.Enabled = false;
            try
            {
                if (link.RingConActive)
                {
                    await link.DisableRingConAsync();
                }
                else
                {
                    _ringCon.Text = Tr.T("Ring-Con wird gesucht …");
                    if (!await link.EnableRingConAsync(CancellationToken.None))
                        Tr.Show(FindForm(), "Kein Ring-Con gefunden. Den rechten Joy-Con (Switch 1) fest in den Ring-Con " +
                            "schieben und erneut versuchen.", "Ring-Con");
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
            {
                Tr.Show(FindForm(), $"Ring-Con: {e.Message}", "Ring-Con");
            }
            finally
            {
                if (!IsDisposed)
                    _ringCon.Enabled = true;
            }
        }

        /// <summary>amiibo über den NFC-Leser lesen und als .bin speichern (Rohabbild, 540 Byte).</summary>
        private async Task ReadAmiiboAsync()
        {
            var link = _player?.Links.OfType<Links.Switch1HidLink>().FirstOrDefault(l => l.HasNfc);
            if (link is null)
                return;
            _amiibo.Enabled = false;
            string original = _amiibo.Text;
            try
            {
                var result = await link.ReadAmiiboAsync(TimeSpan.FromSeconds(20),
                    message =>
                    {
                        if (IsDisposed || !IsHandleCreated)
                            return;
                        try { BeginInvoke(() => { if (!IsDisposed) { message = Tr.T(message); _amiibo.Text = message.Length > 40 ? message[..40] + " …" : message; } }); }
                        catch (InvalidOperationException) { /* Karte geschlossen */ }
                    },
                    CancellationToken.None);
                if (result is not { } amiibo)
                {
                    Tr.Show(FindForm(), "Kein amiibo erkannt. Bitte das amiibo flach an den NFC-Leser halten " +
                        "(Joy-Con: auf den rechten Stick, Pro Controller: auf das NFC-Logo) und erneut versuchen.", "amiibo lesen");
                    return;
                }
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "amiibo");
                Directory.CreateDirectory(folder);
                using var dialog = new SaveFileDialog
                {
                    Title = "amiibo speichern", Filter = "amiibo-Abbild (*.bin)|*.bin", InitialDirectory = folder,
                    FileName = $"amiibo_{Convert.ToHexString(amiibo.Uid)}.bin",
                };
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                    await File.WriteAllBytesAsync(dialog.FileName, amiibo.Data);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException or ObjectDisposedException)
            {
                Tr.Show(FindForm(), $"amiibo konnte nicht gelesen werden: {e.Message}", "amiibo lesen",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (!IsDisposed)
                {
                    _amiibo.Text = original;
                    _amiibo.Enabled = true;
                }
            }
        }

        /// <summary>USB-Controller per HidHide vor Spielen verstecken (einmal Adminrechte).</summary>
        private async Task HideAsync()
        {
            var ids = _player?.Links.OfType<Links.Switch2UsbLink>().Select(l => l.HidInstanceId).OfType<string>().ToList() ?? [];
            if (ids.Count == 0)
                return;
            if (!HidHide.IsInstalled)
            {
                if (Tr.Show(FindForm(),
                        "Dafür wird das kostenlose Programm HidHide gebraucht (von den Machern von ViGEmBus).\n\n" +
                        "Jetzt die Download-Seite öffnen? Nach der Installation hier erneut klicken.",
                        "Doppelte Controller verhindern", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(HidHide.Download) { UseShellExecute = true });
                return;
            }
            _hide.Enabled = false;
            try
            {
                if (await HidHide.HideAsync(ids))
                {
                    var s = _settings();
                    s.HiddenDevices = [.. s.HiddenDevices, .. ids.Where(id => !s.IsHidden(id))];
                    _manager.RequestSave();
                }
                else
                {
                    Tr.Show(FindForm(), "HidHide konnte nicht eingerichtet werden (abgebrochen oder fehlgeschlagen). Details im Protokoll.",
                        "Doppelte Controller verhindern", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                if (!IsDisposed)
                    _hide.Enabled = true;
            }
        }

        private async Task CalibrateAsync()
        {
            if (_player is null || _calibrating)
                return;
            _calibrating = true;
            _calibrate.Text = Tr.T("Ruhig liegen lassen …");
            _calibrate.Enabled = false;
            try
            {
                var result = await _player.CalibrateGyroAsync(TimeSpan.FromSeconds(2));
                if (result is null)
                {
                    Tr.Show(FindForm(), "Der Controller hat sich bewegt oder keine Bewegungsdaten geliefert.\n" +
                        "Bitte flach auf den Tisch legen, nicht berühren und erneut versuchen.", "Gyro kalibrieren",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var s = _settings();
                var map = new Dictionary<string, GyroBias>(s.GyroCalibration, StringComparer.OrdinalIgnoreCase);
                foreach (var (address, bias) in result)
                    map[address] = bias;
                s.GyroCalibration = map; // neue Kopie: der Bluetooth-Thread liest gleichzeitig
                _manager.RequestSave();
                _calibrate.Text = Tr.T("Kalibriert ✓");
                await Task.Delay(1500);
            }
            finally
            {
                _calibrating = false;
                if (!IsDisposed)
                {
                    _calibrate.Enabled = true;
                    _calibrate.Text = Tr.T("Gyro kalibrieren");
                }
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            _info.Width = Math.Max(240, Width - _info.Left - 16);
            PlaceActions();
        }

        private void PlaceActions()
        {
            // Knöpfe rechts oben, bei Platzmangel in mehreren Zeilen; die Eigenschaften rücken darunter.
            _actions.MaximumSize = new Size(Math.Max(300, Width - _title.Right - 40), 0);
            _actions.Location = new Point(Width - _actions.Width - 16, 12);
            _info.Top = Math.Max(52, _actions.Bottom + 10);
        }

        public void Show(Player player, Settings settings)
        {
            _player = player;
            var links = player.Links;
            _title.Text = Tr.T($"Spieler {player.Index + 1}  ·  {player.Kind.DisplayName()}" + (player.GyroMouseActive ? "  ·  Gyro-Maus" : "") + (player.GyroStickActive ? "  ·  Gyro-Stick" : ""));

            var (input, gamepad) = Live(player, settings);
            bool joyCon = links.Count > 0 && links.All(l => l.Kind.IsJoyCon());
            bool upright = links.Count == 1 && joyCon && settings.IsUprightJoyCon(links[0].Address);
            if (joyCon && links.All(l => l.LastState is not null))
            {
                var parts = links.Select(l => new InputView.JoyConPart(l.Kind, l.LastState!, l.Calibration, l.Info,
                    player.MouseActive(l), l.InGrip, upright)).ToList();
                _view.ShowJoyCons(parts, input);
            }
            else
            {
                var first = links.FirstOrDefault();
                if (first is not null)
                    _view.SetColors(first.Info.BodyColor, first.Info.ButtonColor, first.Info.GripColor);
                _view.WiiExtension = first is Links.WiimoteHidLink wii ? wii.Extension : WiiExtension.None;
                _view.Show(input, gamepad);
            }
            _view.PlayerIndex = player.Index;

            // Joy-Con: Paar trennen bzw. einzelnen Joy-Con mit einem passenden Partner verbinden; quer/hochkant
            SetButton(_joyConButton, player.IsPair ? "Joy-Con trennen" : joyCon && _manager.HasPartner(player) ? "Zum Paar verbinden" : null);
            SetButton(_orientation, links.Count == 1 && joyCon ? upright ? "Quer halten" : "Hochkant halten" : null);
            _hide.Visible = links.OfType<Links.Switch2UsbLink>().Any(l => l.HidInstanceId is { } id && !settings.IsHidden(id));
            _amiibo.Visible = links.OfType<Links.Switch1HidLink>().Any(l => l.HasNfc);
            var ringLink = links.OfType<Links.Switch1HidLink>().FirstOrDefault(l => l.Kind == ControllerKind.JoyCon1Right);
            SetButton(_ringCon, ringLink is null ? null : ringLink.RingConActive ? "Ring-Con aus" : "Ring-Con ein");
            _irCamera.Visible = ringLink is { HasIrCamera: true };
            _calibrate.Visible = links.Any(l => l.LastState?.Motion is not null) && links.All(l => l.Address is not null);
            PlaceActions();

            _info.Show(links, input, links.Where(player.MouseActive).ToList());
            Height = Math.Max(430, _info.Bottom + 16);
        }

        private static void SetButton(Button button, string? text)
        {
            button.Visible = text is not null;
            if (text is not null && button.Text != Tr.T(text))
                button.Text = Tr.T(text);
        }

        private static (PadInput? Input, GamepadState Gamepad) Live(Player player, Settings settings)
        {
            var links = player.Links;
            if (links.Count == 0 || links.Any(l => player.EffectiveState(l) is null))
                return (null, default);
            var input = Player.Combine(links, player.EffectiveState, settings);
            return (input, Mapping.ToGamepad(input, settings));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _tips.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Eigenschaften eines Spielers (auch beider Joy-Con) als Liste mit Akkubalken.</summary>
    private sealed class InfoPanel : Control
    {
        private IReadOnlyList<IControllerLink> _links = [];
        private PadInput? _input;
        private static readonly Font Heading = new("Segoe UI Semibold", 9.5f);
        private static readonly Font Body = new("Segoe UI", 9.5f);

        public InfoPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = CardColor;
            Height = 270;
        }

        private IReadOnlyList<IControllerLink> _mouse = [];

        public void Show(IReadOnlyList<IControllerLink> links, PadInput? input, IReadOnlyList<IControllerLink> mouse)
        {
            _links = links;
            _mouse = mouse;
            _input = input;
            int rows = links.Count * 7 + 3;
            Height = Math.Max(270, rows * 22 + 10);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float y = 0;
            foreach (var link in _links)
            {
                if (_links.Count > 1)
                {
                    Draw(g, Tr.T(link.Kind.DisplayName()), 0, y, Accent, Heading);
                    y += 24;
                }
                var st = link.LastState;
                Row(g, ref y, "Akku", null);
                DrawBattery(g, 110, y - 19, st);
                Row(g, ref y, "Verbindung", $"{Transport(link.Transport)} · {link.ReportRate:F0} Berichte/s");
                if (link.Address is { } address)
                    Row(g, ref y, "Adresse", address);
                if (link.Info.SerialNumber is { } serial)
                    Row(g, ref y, "Seriennummer", serial);
                if (link.Info.Firmware is { } firmware)
                    Row(g, ref y, "Firmware", firmware);
                if (link.InGrip)
                    Row(g, ref y, "Griff", "Charging Grip – GL/GR aktiv");
                if (link.Kind is ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right)
                    Row(g, ref y, "Maus", _mouse.Contains(link) ? "aktiv (liegt auf dem Tisch)" : "bereit – zum Benutzen auf den Tisch legen");
                y += 8;
            }

            var pressed = _input is null ? [] : Enum.GetValues<ProButtons>()
                .Where(b => b != ProButtons.None && _input.Has(b)).Select(ButtonName).ToList();
            Row(g, ref y, "Gedrückt", pressed.Count == 0 ? "–" : string.Join("  ", pressed));
        }

        private void Row(Graphics g, ref float y, string label, string? value)
        {
            Draw(g, Tr.T(label), 0, y, MutedColor, Body);
            if (value is not null)
                Draw(g, Tr.T(value), 110, y, TextColor, Body);
            y += 22;
        }

        private static void Draw(Graphics g, string text, float x, float y, Color color, Font font)
        {
            using var brush = new SolidBrush(color);
            g.DrawString(text, font, brush, x, y);
        }

        private static void DrawBattery(Graphics g, float x, float y, ControllerState? st)
        {
            var frame = new RectangleF(x, y + 3, 46, 16);
            using (var pen = new Pen(MutedColor, 1.4f))
            {
                g.DrawRectangle(pen, frame.X, frame.Y, frame.Width, frame.Height);
                using var tip = new SolidBrush(MutedColor);
                g.FillRectangle(tip, frame.Right + 1, frame.Y + 5, 3, 6);
            }
            int percent = st?.BatteryPercent ?? -1;
            if (percent >= 0)
            {
                var level = percent < 15 ? Color.FromArgb(235, 80, 70) : percent < 35 ? Color.FromArgb(240, 180, 40) : Color.FromArgb(70, 200, 110);
                using var fill = new SolidBrush(level);
                g.FillRectangle(fill, frame.X + 2, frame.Y + 2, (frame.Width - 4) * percent / 100f, frame.Height - 4);
            }
            string text = percent < 0 ? "unbekannt"
                : $"{percent} %{(st!.BatteryMillivolts > 0 ? $"  ({st.BatteryMillivolts / 1000.0:F2} V)" : "")}{(st.Charging ? "  ⚡ lädt" : "")}";
            using var brush = new SolidBrush(TextColor);
            g.DrawString(Tr.T(text), Body, brush, x + 56, y);
        }

        private static string Transport(Transport t) => t switch
        {
            Links.Transport.BluetoothLE => "Bluetooth LE",
            Links.Transport.Bluetooth => "Bluetooth",
            _ => "USB",
        };

        private static string ButtonName(ProButtons b) => b switch
        {
            ProButtons.LeftStick => "L-Stick",
            ProButtons.RightStick => "R-Stick",
            ProButtons.Minus => "−",
            ProButtons.Plus => "+",
            ProButtons.Capture => "Aufnahme",
            ProButtons.Up => "▲",
            ProButtons.Down => "▼",
            ProButtons.Left => "◀",
            ProButtons.Right => "▶",
            _ => b.ToString(),
        };
    }
}
