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
    private readonly TextBox _preview = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, WordWrap = true,
        Font = new Font("Segoe UI", 9.5f), Tag = Tr.UserData,
    };
    private readonly Label _adapter = new() { AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
    private readonly Button _apply = new() { Text = "Übernehmen", AutoSize = true, Enabled = false };

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
        ClientSize = new Size(720, 600);
        MinimumSize = new Size(560, 460);
        Font = new Font("Segoe UI", 9.5f);

        var intro = new Label
        {
            Dock = DockStyle.Top, AutoSize = false, Height = 64, Padding = new Padding(12, 10, 12, 0),
            Text = "Controller-Kopplungen von der Switch-SD-Karte oder von einem anderen PC übernehmen – oder für einen anderen " +
                   "PC exportieren. Es werden nur Einstellungen von N-Connect geändert; Windows und der Bluetooth-Adapter bleiben unverändert.",
        };
        var fromSwitch = new Button { Text = "Von der Switch-SD-Karte …", AutoSize = true };
        var fromPc = new Button { Text = "Von einem anderen PC …", AutoSize = true };
        var export = new Button { Text = "Für einen anderen PC exportieren …", AutoSize = true };
        var restore = new Button { Text = "Sicherung wiederherstellen …", AutoSize = true };
        fromSwitch.Click += (_, _) => LoadFromSwitch(null);
        fromPc.Click += (_, _) => LoadFromFile();
        export.Click += (_, _) => Export();
        restore.Click += (_, _) => Restore();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 0), WrapContents = true };
        actions.Controls.AddRange([fromSwitch, fromPc, export, restore]);
        var adapterRow = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(12, 0, 12, 0) };
        adapterRow.Controls.Add(_adapter);
        _adapter.Location = new Point(12, 2);
        _adapter.Text = "Bluetooth-Adapter dieses PCs: wird ermittelt …";
        var previewHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 6) };
        previewHost.Controls.Add(_preview);

        var close = new Button { Text = "Schließen", DialogResult = DialogResult.Cancel, AutoSize = true };
        close.Click += (_, _) => Close();
        _apply.Click += (_, _) => Apply();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        bar.Controls.Add(close);
        bar.Controls.Add(_apply);

        Controls.Add(previewHost);
        Controls.Add(adapterRow);
        Controls.Add(actions);
        Controls.Add(intro);
        Controls.Add(bar);
        CancelButton = close;
        Theme.Apply(this);
        Tr.Apply(this);
        ShowText(Tr.T("Quelle wählen: Switch-SD-Karte (mit Bluepick_RCM oder hekate erstellte Kopplungsdaten) oder eine Datei von einem anderen PC."));
        Shown += async (_, _) =>
        {
            _adapterAddress = await AdapterAddressAsync();
            if (IsDisposed)
                return;
            _adapter.Text = _adapterAddress is null
                ? Tr.T("Bluetooth-Adapter dieses PCs: nicht gefunden")
                : $"{Tr.T("Bluetooth-Adapter dieses PCs:")} {_adapterAddress}";
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
        _preview.Text = text.Replace("\r", "").Replace("\n", "\r\n");
        _preview.SelectionStart = 0;
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
        using var form = new Form
        {
            Text = confirm ? "Kopplungsdaten exportieren" : "Passwort eingeben", StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
            ClientSize = new Size(460, confirm ? 250 : 130), Font = new Font("Segoe UI", 9.5f),
        };
        var info = new Label
        {
            AutoSize = true, MaximumSize = new Size(436, 0), Location = new Point(12, 12),
            Text = confirm
                ? "Passwort (optional). Ohne Passwort steht der Inhalt lesbar in der Datei. Mit Kopplungsschlüsseln ist ein Passwort Pflicht."
                : "Die Datei ist mit einem Passwort geschützt.",
        };
        var first = new TextBox { UseSystemPasswordChar = true, Location = new Point(12, confirm ? 60 : 44), Width = 436, PlaceholderText = "Passwort" };
        var second = new TextBox { UseSystemPasswordChar = true, Location = new Point(12, 94), Width = 436, PlaceholderText = "Passwort wiederholen", Visible = confirm };
        var keys = new CheckBox
        {
            AutoSize = true, Location = new Point(12, 130), Visible = confirm, Enabled = keysOffered,
            Text = keysOffered ? "Kopplungsschlüssel der Switch-Controller mitnehmen" : "Kopplungsschlüssel mitnehmen (erst Switch-SD-Karte einlesen)",
        };
        var status = new Label { AutoSize = true, Location = new Point(12, 162), Visible = confirm };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Location = new Point(286, confirm ? 205 : 84) };
        var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, AutoSize = true, Location = new Point(372, confirm ? 205 : 84) };
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
        form.Controls.AddRange([info, first, second, keys, status, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        Theme.Apply(form);
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
