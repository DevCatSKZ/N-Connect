using System.Drawing;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>Nimmt eine Taste oder Tastenkombination auf (z. B. Strg+Umschalt+S, F5, Alt+Tab).</summary>
internal sealed class KeyCaptureDialog : UiForm
{
    private readonly Label _shown = new()
    {
        AutoSize = false, Dock = DockStyle.Top, Height = UiScale.Px(60), TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI Semibold", 16f), Text = "…",
    };

    /// <summary>Aufgenommene Kombination im Format von <see cref="WindowsInput.ParseCombo"/>, z. B. "Ctrl+Shift+S".</summary>
    public string? Combo { get; private set; }

    public KeyCaptureDialog(string buttonName)
    {
        Text = "Tastatur-Taste festlegen";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        ClientSize = new Size(UiScale.Px(440), UiScale.Px(170));
        Font = UiFonts.Body;
        AutoScaleDimensions = UiScale.Dimensions;
        AutoScaleMode = AutoScaleMode.None;

        var hint = new Label
        {
            AutoSize = false, Dock = DockStyle.Top, Height = UiScale.Px(48), TextAlign = ContentAlignment.MiddleCenter,
            Text = $"Controller-Taste „{buttonName}“:\nJetzt die gewünschte Taste oder Tastenkombination drücken.",
        };
        var ok = new Button { Text = "Übernehmen", DialogResult = DialogResult.OK, Enabled = false, AutoSize = true };
        var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, AutoSize = true };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(UiScale.Px(8)) };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);
        Controls.Add(_shown);
        Controls.Add(hint);
        Controls.Add(bar);
        CancelButton = cancel;
        Theme.Apply(this);
        Tr.Apply(this);

        KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (KeyName(e.KeyCode) is not { } key)
                return; // nur Zusatztaste gedrückt – auf die eigentliche Taste warten
            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Shift) parts.Add("Shift");
            if (e.Alt) parts.Add("Alt");
            if ((GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0) parts.Add("Win");
            parts.Add(key);
            Combo = string.Join("+", parts);
            _shown.Text = Combo;
            ok.Enabled = WindowsInput.ParseCombo(Combo) is not null;
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Tab, Pfeiltasten, Enter usw. sollen aufgenommen werden statt den Fokus zu wechseln.
        if (keyData is Keys.Escape)
            return base.ProcessCmdKey(ref msg, keyData);
        OnKeyDown(new KeyEventArgs(keyData));
        return true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    private static string? KeyName(Keys key) => key switch
    {
        >= Keys.A and <= Keys.Z => key.ToString(),
        >= Keys.D0 and <= Keys.D9 => ((int)(key - Keys.D0)).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => $"Num{(int)(key - Keys.NumPad0)}",
        >= Keys.F1 and <= Keys.F24 => key.ToString(),
        Keys.Space => "Space",
        Keys.Enter => "Enter",
        Keys.Tab => "Tab",
        Keys.Back => "Backspace",
        Keys.Delete => "Delete",
        Keys.Insert => "Insert",
        Keys.Home => "Home",
        Keys.End => "End",
        Keys.PageUp => "PageUp",
        Keys.PageDown => "PageDown",
        Keys.Up => "Up",
        Keys.Down => "Down",
        Keys.Left => "Left",
        Keys.Right => "Right",
        Keys.PrintScreen => "Print",
        Keys.Pause => "Pause",
        Keys.VolumeUp => "VolUp",
        Keys.VolumeDown => "VolDown",
        Keys.VolumeMute => "Mute",
        Keys.MediaPlayPause => "PlayPause",
        _ => null,
    };
}

/// <summary>Turbo festlegen: welche Gamepad-Taste bzw. Tastaturtaste im Dauerfeuer ausgelöst wird.</summary>
internal static class TurboDialog
{
    private static readonly ExtraButtonTarget[] Targets =
    [
        ExtraButtonTarget.A, ExtraButtonTarget.B, ExtraButtonTarget.X, ExtraButtonTarget.Y, ExtraButtonTarget.LB, ExtraButtonTarget.RB,
        ExtraButtonTarget.LT, ExtraButtonTarget.RT, ExtraButtonTarget.Up, ExtraButtonTarget.Down, ExtraButtonTarget.Left,
        ExtraButtonTarget.Right, ExtraButtonTarget.LS, ExtraButtonTarget.RS, ExtraButtonTarget.Start, ExtraButtonTarget.Back,
    ];

    /// <summary>Ergebnis als Aktionstext („Turbo:A“, „Turbo:Key:Space“) oder null bei Abbruch.</summary>
    public static string? Ask(IWin32Window owner, string button, string? current)
    {
        using var form = new UiForm
        {
            Text = "Turbo / Dauerfeuer", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(UiScale.Px(440), UiScale.Px(170)),
            Font = UiFonts.Body,
            AutoScaleDimensions = UiScale.Dimensions, AutoScaleMode = AutoScaleMode.None,
        };
        var info = new Label
        {
            Text = $"Solange „{button}“ gehalten wird, wird diese Taste schnell wiederholt gedrückt\n(Geschwindigkeit unter 5. „Turbo“):",
            AutoSize = true, Location = new Point(UiScale.Px(12), UiScale.Px(12)),
        };
        var target = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(UiScale.Px(12), UiScale.Px(60)), Width = UiScale.Px(416) };
        foreach (var t in Targets)
            target.Items.Add($"Gamepad: {t}");
        target.Items.Add("Tastatur-Taste aufnehmen …");
        var old = ButtonAction.Parse(current);
        string? keys = old.Turbo && old.IsKeyboard ? old.Keys : null;
        if (keys is not null)
        {
            target.Items.Insert(target.Items.Count - 1, $"Tastatur: {keys}");
            target.SelectedIndex = target.Items.Count - 2;
        }
        else
        {
            target.SelectedIndex = Math.Max(0, Array.IndexOf(Targets, old.Turbo ? old.Target : ExtraButtonTarget.A));
        }
        target.SelectedIndexChanged += (_, _) =>
        {
            if (target.SelectedIndex != target.Items.Count - 1)
                return;
            using var capture = new KeyCaptureDialog(button);
            if (capture.ShowDialog(form) == DialogResult.OK && capture.Combo is { } combo)
            {
                keys = combo;
                // Liste: Gamepad-Ziele, ggf. ein Eintrag „Tastatur: …“, zuletzt „aufnehmen …“ – nach Anzahl erkennen
                // (nicht am Text: der ist in der englischen Oberfläche übersetzt).
                if (target.Items.Count == Targets.Length + 2)
                    target.Items.RemoveAt(target.Items.Count - 2);
                target.Items.Insert(target.Items.Count - 1, Tr.T($"Tastatur: {keys}"));
                target.SelectedIndex = target.Items.Count - 2;
            }
            else
            {
                target.SelectedIndex = 0;
            }
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(UiScale.Px(256), UiScale.Px(120)), AutoSize = true };
        var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Location = new Point(UiScale.Px(342), UiScale.Px(120)), AutoSize = true };
        form.Controls.AddRange([info, target, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        Theme.Apply(form);
        Tr.Apply(form);
        if (form.ShowDialog(owner) != DialogResult.OK)
            return null;
        int i = target.SelectedIndex;
        if (i == target.Items.Count - 1)
            return null; // „aufnehmen …“ ohne Ergebnis
        if (i < Targets.Length)
            return ButtonAction.Gamepad(Targets[i]) with { Turbo = true } is var a ? a.ToString() : null;
        return keys is null ? null : (ButtonAction.Keyboard(keys) with { Turbo = true }).ToString();
    }
}

/// <summary>Makro festlegen: Tastenfolge als Text mit Prüfung und Beispielen.</summary>
internal static class MacroDialog
{
    public static string? Ask(IWin32Window owner, string button, string? current)
    {
        using var form = new UiForm
        {
            Text = "Makro (Tastenfolge)", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(UiScale.Px(560), UiScale.Px(300)),
            Font = UiFonts.Body,
            AutoScaleDimensions = UiScale.Dimensions, AutoScaleMode = AutoScaleMode.None,
        };
        var info = new Label
        {
            AutoSize = true, MaximumSize = new Size(UiScale.Px(536), 0), Location = new Point(UiScale.Px(12), UiScale.Px(10)),
            Text = $"Beim Drücken von „{button}“ wird diese Folge einmal abgespielt. Schritte durch Komma trennen, je Schritt " +
                   "was gehalten wird und wie lange (ms). Gamepad: A B X Y LB RB LT RT Up Down Left Right LS RS Start Back Guide, " +
                   "mehrere gleichzeitig mit +. Tastatur: Key:Ctrl+C. Warten: Pause.\n" +
                   "Beispiele:  „A 80, Pause 60, A 80“ (Doppeltipp)  ·  „Down+B 150“  ·  „Key:Ctrl+S 50“",
        };
        var old = ButtonAction.Parse(current);
        var box = new TextBox
        {
            Location = new Point(UiScale.Px(12), UiScale.Px(120)), Width = UiScale.Px(536), Height = UiScale.Px(80), Multiline = true, ScrollBars = ScrollBars.Vertical,
            Text = old.IsMacro ? old.Macro : "A 80, Pause 60, A 80",
        };
        var status = new Label { AutoSize = true, Location = new Point(UiScale.Px(12), UiScale.Px(210)) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(UiScale.Px(376), UiScale.Px(255)), AutoSize = true };
        var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Location = new Point(UiScale.Px(462), UiScale.Px(255)), AutoSize = true };
        void Check()
        {
            bool valid = MacroScript.TryParse((box.Text ?? "").Replace("\r", "").Replace('\n', ','), out var script);
            ok.Enabled = valid;
            status.ForeColor = valid ? Theme.Current.Text : Theme.Dark ? Color.FromArgb(255, 120, 110) : Color.Firebrick;
            status.Text = Tr.T(valid ? $"✓ {script.Steps.Count} Schritte, Dauer {script.TotalMs} ms" : "Ungültig – bitte Schreibweise prüfen (siehe oben).");
        }
        box.TextChanged += (_, _) => Check();
        Check();
        form.Controls.AddRange([info, box, status, ok, cancel]);
        form.CancelButton = cancel;
        Theme.Apply(form);
        Tr.Apply(form);
        if (form.ShowDialog(owner) != DialogResult.OK)
            return null;
        return ButtonAction.Play((box.Text ?? "").Replace("\r", "").Replace('\n', ',').Trim()).ToString();
    }
}

/// <summary>Einfache Texteingabe (Profilname, Controller-Name).</summary>
internal static class Prompt
{
    /// <param name="allowEmpty">Leere Eingabe ist erlaubt und liefert "" (z. B. Namen entfernen); null heißt dann nur „abgebrochen“.</param>
    public static string? Ask(IWin32Window owner, string title, string question, string initial, bool allowEmpty = false)
    {
        using var form = new UiForm
        {
            Text = title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(UiScale.Px(420), UiScale.Px(130)),
            Font = UiFonts.Body,
            AutoScaleDimensions = UiScale.Dimensions, AutoScaleMode = AutoScaleMode.None,
        };
        var label = new Label { Text = question, AutoSize = true, Location = new Point(UiScale.Px(12), UiScale.Px(14)) };
        var box = new TextBox { Text = initial, Location = new Point(UiScale.Px(12), UiScale.Px(40)), Width = UiScale.Px(396) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(UiScale.Px(236), UiScale.Px(88)), AutoSize = true };
        var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Location = new Point(UiScale.Px(322), UiScale.Px(88)), AutoSize = true };
        form.Controls.AddRange([label, new TextField(box), ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        Theme.Apply(form);
        Tr.Apply(form);
        if (form.ShowDialog(owner) != DialogResult.OK)
            return null;
        var text = box.Text.Trim();
        return text.Length == 0 && !allowEmpty ? null : text;
    }
}

/// <summary>Autostart über HKCU\…\Run (kein Adminrecht nötig).</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "N-Connect";
    /// <summary>Eintrag der Vorversion (vor der Umbenennung in N-Connect).</summary>
    private const string LegacyRunValue = "Switch2ProBridge";

    /// <summary>Autostart für den angemeldeten Benutzer (von der App gesetzt).</summary>
    public static bool IsEnabled
    {
        get
        {
            using var user = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return user?.GetValue(RunValue) is string;
        }
    }

    /// <summary>Autostart für alle Benutzer, vom Installer eingetragen (nur per Setup änderbar).</summary>
    public static bool IsEnabledForAllUsers
    {
        get
        {
            using var machine = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(RunKey);
            return machine?.GetValue(RunValue) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --autostart"); // im Infobereich, ohne Fenster
        else
            key.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    /// <summary>Autostart der Vorversion auf den neuen Namen und die neue EXE umstellen.</summary>
    public static void MigrateLegacy()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(LegacyRunValue) is not string)
                return;
            key.DeleteValue(LegacyRunValue, throwOnMissingValue: false);
            Set(true);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn($"Alter Autostart-Eintrag nicht umgestellt: {e.Message}");
        }
    }
}
