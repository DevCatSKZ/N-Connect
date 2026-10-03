using System.Drawing;
using System.Drawing.Imaging;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Prüfhilfe (<c>--render &lt;Ordner&gt;</c>): zeichnet jede Controller-Art einmal in Ruhe und einmal mit gedrückten
/// Tasten/ausgelenkten Sticks und speichert die Bilder als PNG. So lassen sich Grafiken ohne Hardware prüfen.
/// </summary>
internal static class RenderCheck
{
    /// <summary>Alle sichtbaren Texte der Fenster (Steuerelemente, Listen) in eine Datei schreiben – je Zeile ein Text.</summary>
    public static void DumpTexts(string file)
    {
        var texts = new SortedSet<string>(StringComparer.Ordinal);
        void Walk(Control c)
        {
            if (!string.IsNullOrWhiteSpace(c.Text))
                texts.Add(c.Text);
            if (c is ComboBox combo)
                foreach (var item in combo.Items)
                    if (item?.ToString() is { Length: > 0 } s)
                        texts.Add(s);
            foreach (Control child in c.Controls)
                Walk(child);
        }
        var settings = new Settings();
        using (var form = new SettingsForm(settings, _ => { }, null))
            Walk(form);
        foreach (var kind in Enum.GetValues<ControllerKind>())
        {
            texts.Add(kind.DisplayName());
            foreach (var b in ControllerButtons.For(kind))
                texts.Add(ControllerButtons.Label(b, kind));
        }
        using (var welcome = new WelcomeForm())
            Walk(welcome);
        using (var capture = new KeyCaptureDialog("X"))
            Walk(capture);
        using (var wii = new WiiPairForm())
            Walk(wii);
        // Bei englischer Oberfläche: nur noch Texte ausgeben, die nach der Übersetzung deutsch aussehen.
        var lines = texts.Select(t => Tr.T(t)).Distinct();
        if (Tr.English)
            lines = lines.Where(LooksGerman);
        File.WriteAllLines(file, lines.Select(t => t.Replace("\r", "").Replace("\n", "\\n")));
    }

    private static bool LooksGerman(string t) =>
        t.IndexOfAny(['ä', 'ö', 'ü', 'Ä', 'Ö', 'Ü', 'ß']) >= 0
        || new[] { " und ", " der ", " die ", " das ", "Taste", "Spieler", " mit ", " nach ", "Belegung", "Einstellung" }
            .Any(w => t.Contains(w, StringComparison.Ordinal));

    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var kinds = new (ControllerKind Kind, WiiExtension Ext, string Name)[]
        {
            (ControllerKind.Pro2, WiiExtension.None, "pro2"),
            (ControllerKind.GameCube2, WiiExtension.None, "gamecube"),
            (ControllerKind.SnesController, WiiExtension.None, "snes"),
            (ControllerKind.NesController, WiiExtension.None, "nes"),
            (ControllerKind.N64Controller, WiiExtension.None, "n64"),
            (ControllerKind.MegaDrive, WiiExtension.None, "megadrive"),
            (ControllerKind.WiiRemote, WiiExtension.None, "wii"),
            (ControllerKind.WiiRemote, WiiExtension.Nunchuk, "wii_nunchuk"),
            (ControllerKind.WiiRemote, WiiExtension.Classic, "wii_classic"),
            (ControllerKind.WiiUPro, WiiExtension.None, "wiiupro"),
        };
        foreach (var (kind, ext, name) in kinds)
        {
            foreach (bool pressed in new[] { false, true })
            {
                using var view = new InputView { Size = new Size(580, 410), WiiExtension = ext };
                var input = new PadInput
                {
                    Kind = kind,
                    Buttons = pressed ? ProButtons.A | ProButtons.Up | ProButtons.L | ProButtons.Plus | ProButtons.ZL : ProButtons.None,
                    LeftX = pressed ? 0.8f : 0, LeftY = pressed ? 0.5f : 0, RightX = pressed ? 1 : 0, RightY = 0,
                    LeftTrigger = pressed ? 0.6f : 0, RightTrigger = 0,
                };
                var gamepad = Mapping.ToGamepad(input, new Settings());
                view.Show(input, gamepad);
                using var bmp = new Bitmap(580, 410);
                view.DrawToBitmap(bmp, new Rectangle(0, 0, 580, 410));
                bmp.Save(Path.Combine(folder, $"{name}{(pressed ? "_pressed" : "")}.png"), ImageFormat.Png);
            }
        }
    }
}
