using System.Drawing;
using Switch2Pro.Bridge.Links;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Fenster „Controller koppeln“: zeigt je Controller-Art, wie man ihn in den Kopplungsmodus bringt, sucht 60 s nach
/// Switch-1-, NSO-, Wii-, PlayStation- und Xbox-Controllern (Switch 2 verbindet sich per SYNC ohnehin selbst) und
/// zeigt live, was passiert:
/// Restzeit, aktueller Schritt und jeder Controller, der sich während des offenen Fensters verbindet – mit Name,
/// Spielernummer und Verbindungsart. Danach „Fertig“ oder „Erneut suchen“.
/// </summary>
internal sealed class PairForm : UiForm
{
    private const int SearchSeconds = 60;

    private readonly ControllerManager? _manager;
    private readonly ScrollPage _page = new() { Padding = new Padding(UiScale.Px(24), UiScale.Px(18), UiScale.Px(24), 0), Dock = DockStyle.Fill };
    private readonly SettingRow _status = new("Suche läuft …", null, null, Glyph.Sync);
    private readonly SettingsGroup _connected;
    private readonly Heading _connectedHeading = new("Verbunden");
    private readonly GlyphButton _again = new("Erneut suchen", Glyph.Refresh);
    private readonly GlyphButton _done = new("Fertig");
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 500 };
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _cts = new();
    private long _searchEnd;
    private bool _searching, _updatePending;
    private string? _step;
    private int _count;

    public PairForm(ControllerManager? manager)
    {
        _manager = manager;
        Text = "Controller koppeln";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(UiScale.Px(640), UiScale.Px(720));
        Font = UiFonts.Body;
        AutoScaleDimensions = UiScale.Dimensions;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;

        var content = _page.Content;
        content.Controls.Add(new Heading("Controller verbinden", page: true,
            subtitle: "SYNC-Taste am Controller drücken – N-Connect findet und koppelt ihn selbst. Danach reicht künftig ein Tastendruck."));
        // Oben, was gerade passiert und was schon verbunden ist – darunter die Anleitung.
        _page.AddGroup("Status", _status);
        content.Controls.Add(_connectedHeading);
        _connected = new SettingsGroup();
        content.Controls.Add(_connected);
        content.SetShown(_connectedHeading, false);
        content.SetShown(_connected, false);
        _page.AddGroup("So kommt der Controller in den Kopplungsmodus",
            new SettingRow("Switch 2: Pro Controller, Joy-Con 2, GameCube", "Kurz die kleine SYNC-Taste drücken.", null, Glyph.Gamepad),
            new SettingRow("Switch 1: Joy-Con, Pro Controller, NES/SNES/N64/Mega Drive", "SYNC-Taste drücken, bis die Lichter laufen.", null, Glyph.Gamepad),
            new SettingRow("Wii-Fernbedienung", "Batteriefach öffnen und die rote SYNC-Taste drücken.", null, Glyph.Pointer),
            new SettingRow("Wii U Pro Controller", "Die SYNC-Taste auf der Unterseite drücken.", null, Glyph.Gamepad),
            new SettingRow("PlayStation: DualShock 4, DualSense", "PS + Teilen bzw. Create halten, bis die Lichtleiste schnell blinkt.", null, Glyph.Gamepad),
            new SettingRow("Xbox über Bluetooth: One S, Series X|S, Elite", "Kopplungstaste oben halten, bis die Xbox-Taste blinkt.", null, Glyph.Gamepad));

        var footer = new Panel { Dock = DockStyle.Bottom, Height = UiScale.Px(64), BackColor = Theme.Current.Surface };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Current.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        _again.Width = Math.Max(_again.Width, 130);
        _done.Width = Math.Max(_done.Width, 110);
        footer.Controls.AddRange([_again, _done]);
        footer.Layout += (_, _) =>
        {
            _done.Location = new Point(footer.Width - UiScale.Px(24) - _done.Width, (footer.Height - _done.Height) / 2);
            _again.Location = new Point(_done.Left - UiScale.Px(8) - _again.Width, (footer.Height - _again.Height) / 2);
        };
        _again.BackColor = _done.BackColor = footer.BackColor;
        _again.Visible = false;
        _again.Click += (_, _) => StartSearch();
        _done.Click += (_, _) => Close();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
                Close();
        };

        Controls.Add(_page);
        Controls.Add(footer);
        Theme.Apply(this);
        Tr.Apply(this);

        // Schon verbundene Controller nicht als „neu“ melden.
        foreach (var player in _manager?.Players ?? [])
            foreach (var link in player.Links)
                _known.Add(link.Id);
        if (_manager is not null)
            _manager.Changed += OnManagerChanged;
        _tick.Tick += (_, _) => UpdateStatus();
        Shown += (_, _) =>
        {
            if (Preview is { } name)
                ShowPreview(name);
            else
                StartSearch();
        };
    }

    /// <summary>Prüfhilfe: statt zu suchen einen verbundenen Controller zeigen (kein Bluetooth-Zugriff).</summary>
    public string? Preview { get; init; }

    private void ShowPreview(string name)
    {
        _searching = true;
        _searchEnd = Environment.TickCount64 + 42_000;
        _connected.Controls.Add(new SettingRow(name, "Spieler 2  ·  Bluetooth", null, Glyph.Check));
        _count = 1;
        _step = $"✓ {name} verbunden – Spieler 2";
        _page.Content.SetShown(_connectedHeading, true);
        _page.Content.SetShown(_connected, true);
        UpdateStatus();
    }

    private void StartSearch()
    {
        if (_searching)
            return;
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _searching = true;
        _step = null;
        _searchEnd = Environment.TickCount64 + SearchSeconds * 1000L;
        _again.Visible = false;
        _tick.Start();
        UpdateStatus();
        RunAsync(_cts.Token).Forget("Controller koppeln");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        List<string> paired;
        try
        {
            paired = await Task.Run(() => ControllerPairing.ScanAndPair(TimeSpan.FromSeconds(SearchSeconds),
                message => BeginInvokeSafe(() =>
                {
                    _step = message;
                    UpdateStatus();
                }), ct));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException
                                      or System.ComponentModel.Win32Exception or ObjectDisposedException or OperationCanceledException)
        {
            if (e is not (OperationCanceledException or ObjectDisposedException))
            {
                Log.Error("Kopplung", e);
                _step = "Kopplung fehlgeschlagen – Details im Protokoll.";
            }
            paired = [];
        }
        if (IsDisposed)
            return;
        _searching = false;
        _tick.Stop();
        if (paired.Count > 0 && _count == 0)
            _step = $"Gekoppelt: {string.Join(", ", paired)} – verbindet gleich …";
        UpdateStatus();
    }

    /// <summary>Statuszeile: läuft die Suche (Restzeit, Schritt), wurde etwas verbunden, oder ist sie zu Ende.</summary>
    private void UpdateStatus()
    {
        if (IsDisposed)
            return;
        int left = (int)Math.Max(0, (_searchEnd - Environment.TickCount64 + 999) / 1000);
        if (_searching)
        {
            _status.Text = _count > 0 ? "Weitere Controller? Einfach SYNC drücken." : "Suche läuft – jetzt SYNC drücken …";
            _status.Description = Tr.T(_step ?? "Wartet auf einen Controller im Kopplungsmodus.") + "  ·  " + Tr.T($"noch {left} s");
        }
        else
        {
            _status.Text = _count > 0 ? $"Fertig – {_count} Controller verbunden" : "Suche beendet";
            _status.Description = _count > 0
                ? "Die Controller stehen in der Übersicht und verbinden sich künftig per Tastendruck."
                : _step ?? "Kein Controller im Kopplungsmodus gefunden. SYNC-Taste drücken und „Erneut suchen“ klicken.";
            _again.Visible = true;
        }
        _done.Accent = _count > 0;
        _status.Invalidate();
        _page.PerformLayout();
    }

    private void OnManagerChanged()
    {
        // Changed kommt von vielen Threads und oft – zusammenfassen und im UI-Thread auswerten.
        if (_updatePending)
            return;
        _updatePending = true;
        BeginInvokeSafe(() =>
        {
            _updatePending = false;
            UpdateConnected();
        });
    }

    /// <summary>Neu verbundene Controller (seit das Fenster offen ist) als Zeile mit ✓ anzeigen.</summary>
    private void UpdateConnected()
    {
        if (IsDisposed || _manager is null)
            return;
        var settings = _manager.Settings;
        foreach (var player in _manager.Players)
        {
            foreach (var link in player.Links)
            {
                if (!_known.Add(link.Id))
                    continue;
                string name = settings.NameFor(link.Address) ?? link.ProductName ?? link.Kind.DisplayName();
                string via = link.Transport switch
                {
                    Transport.BluetoothLE => "Bluetooth LE",
                    Transport.Bluetooth => "Bluetooth",
                    _ => "USB",
                };
                var row = new SettingRow(name, $"Spieler {player.Index + 1}  ·  {via}", null, Glyph.Check);
                _connected.Controls.Add(row);
                _count++;
                _step = $"✓ {Tr.T(name)} verbunden – Spieler {player.Index + 1}";
                Log.Info($"Fenster „Controller koppeln“: {name} verbunden (Spieler {player.Index + 1})");
            }
        }
        if (_count > 0)
        {
            _page.Content.SetShown(_connectedHeading, true);
            _page.Content.SetShown(_connected, true);
        }
        UpdateStatus();
    }

    private void BeginInvokeSafe(Action action)
    {
        try
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Fenster wurde gerade geschlossen.
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_manager is not null)
            _manager.Changed -= OnManagerChanged;
        _tick.Stop();
        _tick.Dispose();
        _cts.Cancel();
        base.OnFormClosed(e);
    }
}
