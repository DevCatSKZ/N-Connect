using System.Drawing;
using System.Text;
using Switch2Pro.Protocol;
using Windows.Devices.Bluetooth;

namespace Switch2Pro.Bridge;

/// <summary>
/// Fenster „Kopplungsdaten“: von der Switch-SD-Karte übernehmen (Bluepick_RCM/hekate-Export), von einem anderen
/// PC übernehmen (Datei *.ncpair) und für einen anderen PC exportieren. Übernommen wird nur in die Einstellungen
/// von N-Connect – vorher wird eine Sicherung angelegt, die sich hier wiederherstellen lässt. Windows-Registrierung
/// und Bluetooth-Adapter bleiben unverändert, deshalb ist kein Adminrecht (UAC) nötig.
/// </summary>
internal sealed class PairingDataForm : Form
{
    private readonly Settings _settings;
    private readonly Action _saved;
    private readonly Report _preview = new();
    private readonly SettingRow _adapter = new("Bluetooth-Adapter dieses PCs", "wird ermittelt …", glyph: Glyph.Bluetooth);
    private readonly GlyphButton _apply = new("Übernehmen", Glyph.Check);
    private readonly ScrollPage _page = new() { Padding = new Padding(24, 18, 24, 0) };

    /// <summary>Gerade angezeigte Daten, die „Übernehmen“ in die Einstellungen schreibt.</summary>
    private PairingExport? _pending;
    /// <summary>Zuletzt gelesene Switch-Daten (können beim Export mitgenommen werden).</summary>
    private SwitchPairingData? _switchData;
    private string? _adapterAddress;

    public PairingDataForm(Settings settings, Action saved, SwitchCardInfo? card = null)
    {
        _settings = settings;
        _saved = saved;
        Text = "Kopplungsdaten";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ClientSize = new Size(760, 760);
        MinimumSize = new Size(600, 520);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = UiFonts.Body;
        KeyPreview = true;

        GlyphButton Action(string text, string glyph, Action click)
        {
            var b = new GlyphButton(text, glyph);
            b.Click += (_, _) => click();
            return b;
        }
        var content = _page.Content;
        content.Controls.Add(new Heading("Kopplungsdaten", page: true,
            subtitle: "Controller-Kopplungen von der Switch-SD-Karte oder von einem anderen PC übernehmen – oder für einen anderen " +
                      "PC exportieren. Es werden nur Einstellungen von N-Connect geändert; Windows und der Bluetooth-Adapter bleiben unverändert."));
        _page.AddGroup("Übernehmen",
            new SettingRow("Von der Switch-SD-Karte", "Mit Bluepick_RCM oder hekate erstellte Kopplungsdaten (switchroot/joycon_mac.ini)",
                Action("Einlesen …", Glyph.Folder, () => LoadFromSwitch(null)), Glyph.Gamepad),
            new SettingRow("Von einem anderen PC", $"Mit N-Connect exportierte Datei (*{PairingTransfer.FileExtension})",
                Action("Datei öffnen …", Glyph.Import, LoadFromFile), Glyph.Device));
        _page.AddGroup("Inhalt", _preview);
        _page.AddGroup("Weitergeben und sichern",
            new SettingRow("Für einen anderen PC exportieren", "Bekannte Controller und Einstellungen je Controller, optional mit Passwort",
                Action("Exportieren …", Glyph.Export, Export), Glyph.Export),
            new SettingRow("Sicherung wiederherstellen", "Vor jedem „Übernehmen“ wird automatisch eine Sicherung angelegt",
                Action("Wiederherstellen …", Glyph.Undo, Restore), Glyph.Undo),
            _adapter);
        _page.Dock = DockStyle.Fill;

        var close = new GlyphButton("Schließen");
        close.Click += (_, _) => Close();
        _apply.Click += (_, _) => Apply();
        _apply.EnabledChanged += (_, _) => _apply.Accent = _apply.Enabled; // ausgegraut ohne Akzentfläche
        _apply.Enabled = false;
        var footer = new Footer(_apply, close);

        Controls.Add(_page);
        Controls.Add(footer);
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
                Close();
        };
        Theme.Apply(this);
        Tr.Apply(this);
        ShowText(Tr.T("Quelle wählen: Switch-SD-Karte (mit Bluepick_RCM oder hekate erstellte Kopplungsdaten) oder eine Datei von einem anderen PC."));
        Shown += async (_, _) =>
        {
            _adapterAddress = await AdapterAddressAsync();
            if (IsDisposed)
                return;
            _adapter.Description = _adapterAddress ?? "nicht gefunden";
            if (card is not null)
                LoadFromSwitch(card);
            else if (_pending is null && _switchData is null)
                ShowText(Tr.T("Quelle wählen: Switch-SD-Karte (mit Bluepick_RCM oder hekate erstellte Kopplungsdaten) oder eine Datei von einem anderen PC."));
        };
    }

    /// <summary>Adresse des Bluetooth-Adapters (nur lesen).</summary>
    internal static async Task<string?> AdapterAddressAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            return adapter is null ? null : BtAddress.Format(adapter.BluetoothAddress);
        }
        catch (Exception e)
        {
            Log.Warn($"Bluetooth-Adapter nicht lesbar: {e.Message}");
            return null;
        }
    }

    private void ShowText(string text)
    {
        _preview.Text = text.Replace("\r", "").TrimEnd('\n');
        _page.AutoScrollPosition = Point.Empty;
        _page.PerformLayout(); // Höhe der Gruppe neu berechnen (geschachtelte Stapel melden Größenänderungen nicht weiter)
    }

    /// <summary>
    /// Beschreibung der geladenen Daten, selbst gezeichnet und umbrechend: Zeilen mit „:“ am Ende als Zwischenüberschrift,
    /// „  •  “ als Aufzählung mit hängendem Einzug, Leerzeilen als Absatzabstand. Der Text ist schon übersetzt.
    /// </summary>
    private sealed class Report : Control, IHeightForWidth, ISelfTranslating
    {
        private const int Pad = 16, Indent = 20, Gap = 8;
        private const string Bullet = "  •  ";
        private const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

        public Report()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Current.Surface;
            TabStop = false;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        private IEnumerable<(string Text, Font Font, int Left, bool Bullet, int Height)> Lines(int width)
        {
            foreach (var raw in Text.Split('\n'))
            {
                if (raw.Length == 0)
                {
                    yield return ("", UiFonts.Body, 0, false, Gap);
                    continue;
                }
                bool bullet = raw.StartsWith(Bullet, StringComparison.Ordinal);
                string text = bullet ? raw[Bullet.Length..] : raw;
                var font = !bullet && text.EndsWith(':') ? UiFonts.Strong : UiFonts.Body;
                int left = Pad + (bullet ? Indent : 0);
                int h = TextRenderer.MeasureText(text, font, new Size(Math.Max(80, width - left - Pad), 0), Flags).Height + 3;
                yield return (text, font, left, bullet, h);
            }
        }

        public int HeightFor(int width) => 2 * 12 + Lines(width).Sum(l => l.Height);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.Current;
            g.Clear(p.Surface);
            int y = 12;
            foreach (var (text, font, left, bullet, h) in Lines(Width))
            {
                if (bullet)
                    TextRenderer.DrawText(g, "•", font, new Point(left - 14, y), p.TextMuted, TextFormatFlags.NoPrefix);
                if (text.Length > 0)
                    TextRenderer.DrawText(g, text, font, new Rectangle(left, y, Math.Max(80, Width - left - Pad), h), p.Text, Flags);
                y += h;
            }
        }
    }

    /// <summary>Fußleiste wie bei Windows-11-Dialogen: abgesetzte Fläche mit Trennlinie, Knöpfe rechts.</summary>
    private sealed class Footer : Panel
    {
        private readonly Control[] _buttons;

        public Footer(params Control[] buttons)
        {
            _buttons = buttons;
            Dock = DockStyle.Bottom;
            Height = 64;
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.Current.Surface;
            foreach (var b in buttons)
            {
                b.BackColor = BackColor;
                if (b is GlyphButton g)
                    g.Width = Math.Max(g.Width, 110);
                Controls.Add(b);
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            int x = ClientSize.Width - 24;
            foreach (var b in _buttons.Reverse())
            {
                x -= b.Width;
                b.Location = new Point(x, (Height - b.Height) / 2);
                x -= 8;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Theme.Current.Border);
            e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        }
    }

    /// <summary>Controllername übersetzen; „Controller“ bleibt (die Tabelle übersetzt es als Mehrzahl).</summary>
    private static string NameOf(string? name) => name is null or "Controller" ? "Controller" : Tr.T(name);

    /// <summary>Fest eingebaute Erklärtexte (für die Übersetzungsprüfung).</summary>
    internal static IEnumerable<string> ExplanationTexts => [.. SwitchExplanation, .. SameAdapterExplanation, .. OtherAdapterExplanation];

    // ---------- Von der Switch-SD-Karte ----------

    private void LoadFromSwitch(SwitchCardInfo? card)
    {
        card ??= SwitchCardWatcher.FindCards().FirstOrDefault();
        if (card is null)
        {
            if (Tr.Show(this, "Keine Switch-SD-Karte gefunden. SD-Karte in den Kartenleser stecken oder die Switch per hekate " +
                              "(„USB Tools“ → „SD Card“) als Laufwerk verbinden.\n\nOrdner selbst auswählen?",
                    "Kopplungsdaten", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                return;
            using var pick = new FolderBrowserDialog { Description = Tr.T("Stammordner der Switch-SD-Karte wählen"), UseDescriptionForTitle = true };
            if (pick.ShowDialog(this) != DialogResult.OK)
                return;
            card = SwitchCard.Inspect(pick.SelectedPath);
        }
        var data = SwitchCard.ReadPairings(card.Root);
        _switchData = data is { } d && d.Valid.Any() ? d : null;
        _pending = _switchData is null ? null : new PairingExport { Controllers = PairingTransfer.FromSwitch(_switchData, includeKeys: false) };
        _apply.Enabled = _pending is not null;
        ShowText(DescribeSwitch(card, data));
    }

    private string DescribeSwitch(SwitchCardInfo card, SwitchPairingData? data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Tr.T("Switch-SD-Karte:")} {card.Summary}");
        if (data is null || !data.Valid.Any())
        {
            sb.AppendLine();
            sb.AppendLine(Tr.T("Auf der Karte liegen keine Kopplungsdaten (switchroot/joycon_mac.ini)."));
            sb.AppendLine(Tr.T("So erstellst du sie: Joy-Con an die Switch stecken, Bluepick_RCM starten und „Dump Joy-Con BT pairing → SD“ " +
                               "(oder FULL AUTO) wählen. Alternativ in hekate: Nyx → Konsole → „Dump Joy-Con BT“."));
            if (card.HasSaveBackup)
                sb.AppendLine(Tr.T("Gefunden wurde eine Bluepick-Sicherung des Bluetooth-Speichers (8000000000000050.bin). Sie ist ein Switch-Speicherabbild und kann hier nicht ausgewertet werden."));
            return sb.ToString();
        }
        if (data.ConsoleAddress is { } console)
            sb.AppendLine($"{Tr.T("Bluetooth-Adresse der Switch:")} {console}");
        sb.AppendLine();
        sb.AppendLine(Tr.T("Gefundene Controller:"));
        foreach (var c in data.Valid)
        {
            string state = c.PairedWithSwitch ? Tr.T("mit der Switch gekoppelt") : Tr.T("zuletzt mit einem anderen Gerät gekoppelt (nicht der Switch)");
            sb.AppendLine($"  •  {NameOf(c.DisplayName)}  {c.ControllerAddress}  →  {c.HostAddress}  ({state})");
        }
        if (card.HasSaveBackup)
            sb.AppendLine(Tr.T("Hinweis: Pro Controller stehen nur im Bluetooth-Speicher der Switch (8000000000000050.bin); dieses Speicherabbild kann N-Connect nicht auswerten."));
        sb.AppendLine();
        sb.AppendLine(Tr.T("Was diese Daten können – und was nicht:"));
        foreach (var line in SwitchExplanation)
            sb.AppendLine("  •  " + Tr.T(line));
        sb.AppendLine();
        sb.AppendLine(Tr.T("„Übernehmen“ merkt sich diese Controller in N-Connect (Liste bekannter Controller, Zuordnung zur Switch). Kopplungsschlüssel werden nicht gespeichert."));
        return sb.ToString();
    }

    private static readonly string[] SwitchExplanation =
    [
        "Die Daten zeigen, welche Controller mit deiner Switch gekoppelt sind und an welche Bluetooth-Adresse sie gebunden sind.",
        "Ein Switch-1-Controller verbindet sich auf Tastendruck nur mit dem Gerät, dessen Adresse er gespeichert hat – hier mit der Switch. Der Bluetooth-Adapter des PCs hat eine andere Adresse, deshalb kann Windows diese Kopplung nicht einfach weiterverwenden.",
        "Die Adresse des PC-Adapters zu ändern, unterstützt N-Connect bewusst nicht (herstellerabhängig, riskant und nicht vorgesehen).",
        "Windows bietet keinen offiziellen Weg, fremde Kopplungsschlüssel einzutragen: Sie liegen in einem Teil der Registrierung, auf den nur das System zugreifen darf. N-Connect ändert dort nichts.",
        "So klappt der Wechsel am einfachsten: am PC den Controller einmal per SYNC-Taste in den Windows-Bluetooth-Einstellungen koppeln. Zurück an der Switch die Joy-Con an die Konsole stecken bzw. den Pro Controller per USB-Kabel anschließen – dann sind sie ohne Menü wieder mit der Switch gekoppelt.",
    ];

    // ---------- Von einem anderen PC ----------

    private void LoadFromFile()
    {
        using var open = new OpenFileDialog
        {
            Filter = Tr.T("N-Connect-Kopplungsdaten") + $" (*{PairingTransfer.FileExtension})|*{PairingTransfer.FileExtension}",
            Title = Tr.T("Kopplungsdaten von einem anderen PC öffnen"),
        };
        if (open.ShowDialog(this) != DialogResult.OK)
            return;
        string text;
        try
        {
            var info = new FileInfo(open.FileName);
            if (info.Length > 4 * 1024 * 1024)
                throw new IOException(Tr.T("Die Datei ist zu groß."));
            text = File.ReadAllText(open.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Tr.Show(this, $"Lesen fehlgeschlagen: {e.Message}", "Kopplungsdaten", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        string? password = null;
        if (PairingTransfer.IsEncrypted(text))
        {
            password = PasswordDialog.Ask(this, confirm: false, keysOffered: false, out _);
            if (password is null)
                return;
        }
        try
        {
            var data = PairingTransfer.Read(text, password);
            _pending = data;
            _switchData = null;
            _apply.Enabled = data.Controllers.Count > 0 || data.KnownControllers.Count > 0;
            ShowText(DescribeTransfer(data));
        }
        catch (PairingFileException e)
        {
            Tr.Show(this, e.Message, "Kopplungsdaten", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string DescribeTransfer(PairingExport data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Tr.T("Datei von:")} {data.SourcePc ?? "?"}  ·  {data.Created.ToLocalTime():g}");
        sb.AppendLine($"{Tr.T("Bluetooth-Adapter dort:")} {data.AdapterAddress ?? "?"}");
        sb.AppendLine($"{Tr.T("Bluetooth-Adapter hier:")} {_adapterAddress ?? "?"}");
        sb.AppendLine();
        sb.AppendLine(Tr.T("Controller:"));
        foreach (var c in data.Controllers)
        {
            string name = NameOf(c.Name);
            string from = c.Source == "Switch" ? Tr.T("gekoppelt mit der Switch") : Tr.T("gekoppelt mit dem anderen PC");
            sb.AppendLine($"  •  {name}  {c.Address}  →  {c.HostAddress ?? "?"}  ({from})");
        }
        if (data.Controllers.Count == 0)
            sb.AppendLine("  –");
        sb.AppendLine($"{Tr.T("Weitere Einstellungen je Controller:")} {data.SingleJoyCons.Count + data.UprightJoyCons.Count + data.GyroCalibration.Count}");
        sb.AppendLine();
        bool sameAdapter = BtAddress.Same(data.AdapterAddress, _adapterAddress);
        sb.AppendLine(Tr.T("Was du erwarten kannst:"));
        foreach (var line in sameAdapter ? SameAdapterExplanation : OtherAdapterExplanation)
            sb.AppendLine("  •  " + Tr.T(line));
        if (data.HasKeys)
            sb.AppendLine("  •  " + Tr.T("Die Datei enthält Kopplungsschlüssel. Windows bietet keinen offiziellen Weg, sie einzutragen – N-Connect übernimmt sie deshalb nicht."));
        sb.AppendLine();
        sb.AppendLine(Tr.T("„Übernehmen“ ergänzt die Controller-Listen und Einstellungen von N-Connect (nichts wird gelöscht). Vorher wird eine Sicherung angelegt."));
        return sb.ToString();
    }

    private static readonly string[] SameAdapterExplanation =
    [
        "Gleiche Adapter-Adresse (z. B. derselbe USB-Bluetooth-Stick umgesteckt): Switch-2-Controller verbinden sich hier auf Tastendruck wie am anderen PC.",
        "Switch-1-Controller (Pro Controller, Joy-Con) brauchen zusätzlich die Kopplung in Windows. Die kann N-Connect nicht übertragen – einmal per SYNC-Taste koppeln.",
    ];

    private static readonly string[] OtherAdapterExplanation =
    [
        "Anderer Bluetooth-Adapter: Controller verbinden sich auf Tastendruck nur mit dem Gerät, mit dem sie zuletzt gekoppelt wurden. Hier einmal SYNC drücken – danach verbinden sie sich mit diesem PC (am anderen PC dann wieder per SYNC).",
        "Tipp: Wer denselben USB-Bluetooth-Stick zwischen den PCs umsteckt, nimmt die Adresse mit – Switch-2-Controller verbinden sich dann an beiden PCs ohne SYNC.",
        "Übernommen werden die Einstellungen je Controller (Joy-Con einzeln/hochkant, Gyro-Kalibrierung) und die Liste bekannter Controller.",
    ];

    // ---------- Übernehmen, Sicherung ----------

    private void Apply()
    {
        if (_pending is not { } data)
            return;
        if (Tr.Show(this, "Kopplungsdaten in N-Connect übernehmen? Vorher wird eine Sicherung der Einstellungen angelegt.",
                "Kopplungsdaten", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        string backup;
        try
        {
            backup = Backup();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("Sicherung der Einstellungen", e);
            Tr.Show(this, $"Speichern fehlgeschlagen: {e.Message}", "Kopplungsdaten", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        int changes = PairingTransfer.Merge(_settings, data);
        _saved();
        Log.Info($"Kopplungsdaten übernommen: {changes} Änderungen, Sicherung {backup}");
        _apply.Enabled = false;
        Tr.Show(this, changes == 0 ? "Nichts zu ändern – diese Daten sind schon übernommen." : "Übernommen. Die Sicherung lässt sich hier über „Sicherung wiederherstellen“ zurückholen.",
            "Kopplungsdaten", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string BackupDir => Path.Combine(Paths.SettingsDir, "Sicherungen");

    /// <summary>Aktuelle Einstellungen in den Ordner „Sicherungen“ kopieren.</summary>
    private string Backup()
    {
        Directory.CreateDirectory(BackupDir);
        string file = Path.Combine(BackupDir, $"settings-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        if (File.Exists(Paths.SettingsFile))
            File.Copy(Paths.SettingsFile, file, overwrite: true);
        else
            _settings.Save(file);
        return file;
    }

    private void Restore()
    {
        using var open = new OpenFileDialog
        {
            InitialDirectory = Directory.Exists(BackupDir) ? BackupDir : Paths.SettingsDir,
            Filter = Tr.T("Sicherung der Einstellungen") + " (settings-*.json)|settings-*.json",
            Title = Tr.T("Sicherung wiederherstellen"),
        };
        if (open.ShowDialog(this) != DialogResult.OK)
            return;
        var backup = Settings.Load(open.FileName);
        if (backup.LoadError is not null)
        {
            Tr.Show(this, "Die Sicherung ist beschädigt und wurde nicht übernommen.", "Kopplungsdaten", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (Tr.Show(this, "Alle Einstellungen durch diese Sicherung ersetzen?", "Kopplungsdaten",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        _settings.CopyFrom(backup);
        _saved();
        Log.Info($"Einstellungen aus Sicherung wiederhergestellt: {open.FileName}");
        Tr.Show(this, "Sicherung wiederhergestellt.", "Kopplungsdaten", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ---------- Export ----------

    private void Export()
    {
        bool keysOffered = _switchData?.Valid.Any() == true;
        var password = PasswordDialog.Ask(this, confirm: true, keysOffered, out bool includeKeys);
        if (password is null)
            return; // abgebrochen
        var extra = _switchData is { } sd ? PairingTransfer.FromSwitch(sd, includeKeys && keysOffered) : null;
        var data = PairingTransfer.FromSettings(_settings, _adapterAddress, Environment.MachineName, extra);
        using var save = new SaveFileDialog
        {
            Filter = Tr.T("N-Connect-Kopplungsdaten") + $" (*{PairingTransfer.FileExtension})|*{PairingTransfer.FileExtension}",
            FileName = $"N-Connect-Kopplungsdaten-{Environment.MachineName}{PairingTransfer.FileExtension}",
            Title = Tr.T("Kopplungsdaten für einen anderen PC speichern"),
        };
        if (save.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            File.WriteAllText(save.FileName, PairingTransfer.Write(data, password.Length == 0 ? null : password));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Tr.Show(this, $"Speichern fehlgeschlagen: {e.Message}", "Kopplungsdaten", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Log.Info($"Kopplungsdaten exportiert: {data.Controllers.Count} Controller, Schlüssel: {data.HasKeys}");
        Tr.Show(this, "Gespeichert. Die Datei am anderen PC in N-Connect unter „Joy-Con & Wii“ → „Kopplungsdaten“ → „Von einem anderen PC“ öffnen.",
            "Kopplungsdaten", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

/// <summary>Passwort für die Übertragungsdatei (beim Export mit Wiederholung und Wahl, ob Schlüssel mitkommen).</summary>
internal static class PasswordDialog
{
    /// <summary>Passwort ("" = ohne Passwort, nur beim Export möglich) oder null bei Abbruch.</summary>
    public static string? Ask(IWin32Window owner, bool confirm, bool keysOffered, out bool includeKeys)
    {
        includeKeys = false;
        const int left = 24, width = 452;
        using var form = new Form
        {
            Text = confirm ? "Kopplungsdaten exportieren" : "Passwort eingeben", StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
            ClientSize = new Size(left * 2 + width, confirm ? 336 : 210), Font = UiFonts.Body, KeyPreview = true,
            AutoScaleDimensions = new SizeF(96F, 96F), AutoScaleMode = AutoScaleMode.Dpi,
        };
        var title = new Label
        {
            AutoSize = true, Location = new Point(left, 18), Font = UiFonts.Subtitle,
            Text = confirm ? "Kopplungsdaten exportieren" : "Passwort eingeben",
        };
        var info = new Label
        {
            AutoSize = true, MaximumSize = new Size(width, 0), Location = new Point(left, 52),
            Text = confirm
                ? "Passwort (optional). Ohne Passwort steht der Inhalt lesbar in der Datei. Mit Kopplungsschlüsseln ist ein Passwort Pflicht."
                : "Die Datei ist mit einem Passwort geschützt.",
        };
        var first = new TextBox { UseSystemPasswordChar = true, Location = new Point(left, confirm ? 100 : 82), Width = width, PlaceholderText = "Passwort" };
        var second = new TextBox { UseSystemPasswordChar = true, Location = new Point(left, 136), Width = width, PlaceholderText = "Passwort wiederholen", Visible = confirm };
        var keysLabel = new Label
        {
            AutoSize = true, MaximumSize = new Size(width - 110, 0), Location = new Point(left, 180), Visible = confirm,
            Text = keysOffered ? "Kopplungsschlüssel der Switch-Controller mitnehmen" : "Kopplungsschlüssel mitnehmen (erst Switch-SD-Karte einlesen)",
            ForeColor = keysOffered ? SystemColors.ControlText : SystemColors.GrayText,
        };
        var keys = new ToggleSwitch { Location = new Point(left + width - 96, 176), Visible = confirm, Enabled = keysOffered };
        var status = new Label { AutoSize = true, Location = new Point(left, 214), Visible = confirm, Font = UiFonts.Small, ForeColor = SystemColors.GrayText };
        var ok = new GlyphButton("OK", accent: true) { Width = 110 };
        var cancel = new GlyphButton("Abbrechen") { Width = 110 };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Current.Surface };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Current.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        cancel.Location = new Point(left + width - cancel.Width, 16);
        ok.Location = new Point(cancel.Left - 8 - ok.Width, 16);
        ok.BackColor = cancel.BackColor = footer.BackColor;
        footer.Controls.AddRange([ok, cancel]);
        ok.Click += (_, _) =>
        {
            if (ok.Enabled)
                form.DialogResult = DialogResult.OK;
        };
        cancel.Click += (_, _) => form.DialogResult = DialogResult.Cancel;
        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && ok.Enabled)
                form.DialogResult = DialogResult.OK;
            else if (e.KeyCode == Keys.Escape)
                form.DialogResult = DialogResult.Cancel;
            else
                return;
            e.SuppressKeyPress = true;
        };
        ok.EnabledChanged += (_, _) => ok.Accent = ok.Enabled;
        void Check()
        {
            string? problem = !confirm ? (first.Text.Length == 0 ? "" : null)
                : first.Text != second.Text ? "Die Passwörter stimmen nicht überein."
                : keys.Checked && first.Text.Length < 8 ? "Mit Schlüsseln: Passwort mit mindestens 8 Zeichen."
                : first.Text.Length is > 0 and < 8 ? "Mindestens 8 Zeichen."
                : null;
            ok.Enabled = problem is null;
            status.Text = Tr.T(problem ?? (first.Text.Length == 0 ? "Ohne Passwort." : "✓"));
        }
        first.TextChanged += (_, _) => Check();
        second.TextChanged += (_, _) => Check();
        keys.CheckedChanged += (_, _) => Check();
        form.Controls.AddRange([title, info, new TextField(first), new TextField(second), keysLabel, keys, status, footer]);
        Theme.Apply(form);
        keys.BackColor = form.BackColor;
        Tr.Apply(form);
        first.PlaceholderText = Tr.T(first.PlaceholderText);
        second.PlaceholderText = Tr.T(second.PlaceholderText);
        Check();
        if (form.ShowDialog(owner) != DialogResult.OK)
            return null;
        includeKeys = keys.Checked;
        return first.Text;
    }
}
