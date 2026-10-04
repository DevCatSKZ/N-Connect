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
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    /// <summary>Alle sichtbaren Texte der Fenster (Steuerelemente, Listen) in eine Datei schreiben – je Zeile ein Text.</summary>
    public static void DumpTexts(string file)
    {
        // Deutsche Originaltexte sammeln (Fenster auf Deutsch aufbauen) und danach einzeln prüfen.
        bool english = Tr.English;
        Tr.SetEnglish(false);
        var texts = new SortedSet<string>(StringComparer.Ordinal);
        void Walk(Control c)
        {
            if (!string.IsNullOrWhiteSpace(c.Text))
                texts.Add(c.Text);
            if (c is IExtraTexts extra)
                texts.UnionWith(extra.ExtraTexts.Where(t => t.Length > 0));
            if (c is ComboBox combo)
                foreach (var item in combo.Items)
                    if (item?.ToString() is { Length: > 0 } s)
                        texts.Add(s);
            foreach (Control child in c.Controls)
                Walk(child);
        }
        var settings = new Settings();
        using (var form = new SettingsForm(settings, _ => { }, null))
        {
            Walk(form);
        }
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
        // Meldungen, die zur Laufzeit zusammengesetzt werden (alle Fehlerfälle und Hinweise).
        string pro = ControllerKind.Pro2.DisplayName(), jc = ControllerKind.JoyCon2Left.DisplayName();
        foreach (var problem in new[] { "Kein Bluetooth-Adapter gefunden.", "Bluetooth ist ausgeschaltet.", "Dieser Bluetooth-Adapter unterstützt kein Bluetooth LE." })
            texts.Add(problem + " " + ControllerManager.AdapterHint(problem));
        texts.UnionWith(
        [
            "Bluetooth ist bereit – Controller können verbunden werden.",
            "ViGEmBus-Treiber fehlt – ohne ihn kann kein virtueller Controller erzeugt werden. Bitte das Setup erneut ausführen.",
            $"{pro} per USB ist von einem anderen Programm belegt (z. B. Steam). Das Programm schließen oder in Steam die Nintendo-Unterstützung abschalten.",
            $"Schon 8 Controller verbunden – {jc} wird nicht verwendet. Erst einen anderen trennen.",
            $"{pro}: schwache Bluetooth-Verbindung (14 statt 33–60 Berichte/s). Tipp: Bluetooth-Stick per Verlängerung näher an den Controller, weg von USB-3-Anschlüssen und Funkkopfhörern.",
            "Controller mit dem PC gekoppelt – ab jetzt reicht ein Tastendruck. Hinweis: Um ihn wieder an der Switch 2 zu nutzen, dort einmal kurz SYNC drücken.",
            $"{pro} verbunden (Spieler 1)", $"{jc} getrennt", "Joy-Con zusammengefasst (Spieler 2)",
            $"Joy-Con getrennt – {jc} ist jetzt Spieler 3", $"Spieler 1: Akku {jc} fast leer (14 %) – bitte aufladen",
            "Spieler 2: nach 15 min ohne Eingabe getrennt",
            $"{pro} per USB verbunden. Sieht ein Spiel ihn doppelt? Im Fenster „Doppelt angezeigt? Verstecken“ klicken.",
            "Profil „Zelda“ aktiv", "Bluetooth LE · 33 Berichte/s", "60 %  (3,70 V)  ⚡ lädt",
            $"Spieler 1  ·  {pro}  ·  Gyro-Maus", "Erst den Ring-Con ausschalten (Ring-Con und IR-Kamera nutzen denselben Zusatzprozessor).",
        ]);

        // Bei englischer Oberfläche: nur Texte ausgeben, die keine Übersetzung haben (oder danach noch deutsch aussehen).
        Tr.SetEnglish(english);
        File.WriteAllLines(file + ".alle.txt", texts.Select(t => Tr.T(t)).Distinct());
        var lines = english
            ? texts.Where(t => (Tr.T(t) == t && NeedsTranslation(t)) || LooksGerman(Tr.T(t)))
            : texts;
        File.WriteAllLines(file, lines.Select(t => t.Replace("\r", "").Replace("\n", "\\n")));
    }

    /// <summary>Enthält der Text Wörter (nicht nur Namen, Zahlen, Tasten wie „ZL“ oder Marken)?</summary>
    private static bool NeedsTranslation(string t)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(t, "[a-zäöüß]{3}"))
            return false;
        string[] sameInEnglish =
        [
            "N-Connect", "Xbox", "Nintendo", "linear", "Bluetooth", "Joy-Con", "DualShock", "Normal", "Charging Grip", "Version",
            "PlayStation", "English", "Deutsch", "Wii", "amiibo", "Ring-Con", "HOME", "Gyro", "Turbo", "Firmware", "Controller",
        ];
        return !sameInEnglish.Any(w => t.Equals(w, StringComparison.Ordinal))
               && !t.StartsWith("Nintendo ", StringComparison.Ordinal) && !t.StartsWith("Version ", StringComparison.Ordinal);
    }

    private static bool LooksGerman(string t) =>
        t.IndexOfAny(['ä', 'ö', 'ü', 'Ä', 'Ö', 'Ü', 'ß']) >= 0
        || new[] { " und ", " der ", " die ", " das ", "Taste", "Spieler", " mit ", " nach ", "Belegung", "Einstellung" }
            .Any(w => t.Contains(w, StringComparison.Ordinal));

    /// <summary>
    /// Prüfhilfe (<c>--render-ui &lt;Ordner&gt; [--en]</c>): Einstellungsfenster in voller Höhe und die Übersicht mit
    /// simulierten Controllern als PNG – zum Prüfen von Übersetzung und abgeschnittenen Beschriftungen.
    /// </summary>
    public static void RenderUi(string folder)
    {
        Directory.CreateDirectory(folder);
        string lang = Tr.English ? "en" : "de";
        string variant = (Environment.GetCommandLineArgs().Contains("--demo-all") ? "_alle" : Environment.GetCommandLineArgs().Contains("--demo-retro") ? "_retro" : "")
                         + (Environment.GetCommandLineArgs().Contains("--wide") ? "_breit" : "");
        var log = new List<string>();
        using var factory = PadFactory.TryCreate();
        ControllerManager? manager = factory is null ? null : new ControllerManager(() => new Settings(), factory);
        try
        {
            manager?.StartDemo();
            // Als echtes Fenster außerhalb des sichtbaren Bereichs anzeigen, damit alle Steuerelemente entstehen.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var form = new SettingsForm(new Settings(), _ => { }, manager)
            {
                StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000), ShowInTaskbar = false,
                Size = Environment.GetCommandLineArgs().Contains("--wide") ? new Size(1900, 1040) : new Size(1220, 900),
            };
            form.Show();
            log.Add($"Fenster erzeugt und gezeigt: {sw.ElapsedMilliseconds} ms");
            void Pump()
            {
                for (int i = 0; i < 20; i++)
                {
                    Application.DoEvents();
                    Thread.Sleep(20);
                }
            }
            // So, wie Windows das Fenster wirklich zeichnet (DrawToBitmap zeigt manche Steuerelemente nur im Grundzustand).
            void Window(string name)
            {
                using var bmp = new Bitmap(form.Width, form.Height);
                using (var g = Graphics.FromImage(bmp))
                {
                    var hdc = g.GetHdc();
                    PrintWindow(form.Handle, hdc, 2); // PW_RENDERFULLCONTENT
                    g.ReleaseHdc(hdc);
                }
                bmp.Save(Path.Combine(folder, $"{name}{variant}_{lang}.png"), ImageFormat.Png);
            }
            static void Full(Control c, string path)
            {
                using var bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height));
                c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                bmp.Save(path, ImageFormat.Png);
            }
            Pump();
            Window("ui_0_controller");

            // Karte aufgeklappt: jeder Reiter einmal (ganze Karte, auch wenn sie länger als das Fenster ist).
            // Jede Karte (jeder Controller), jeder Reiter, den dieser Controller hat.
            var cards = form.Overview.Cards;
            for (int c = 0; c < cards.Count; c++)
            {
                for (int tab = 0; tab < 6; tab++)
                {
                    sw.Restart();
                    bool shown = ControllerOverview.ExpandCard(cards[c], tab);
                    form.Update();
                    long ms = sw.ElapsedMilliseconds;
                    if (!shown)
                        continue;
                    log.Add($"Karte {c + 1}, Reiter {tab}: {ms} ms");
                    Pump();
                    Full(cards[c], Path.Combine(folder, $"ui_card{c + 1}_{tab}{variant}_{lang}.png"));
                }
            }
            if (cards.Count > 0)
                ControllerOverview.ExpandCard(cards[0], 0);
            Pump();
            Window("ui_0_controller_offen");

            // Übrige Seiten: Fenster und ganzer Inhalt.
            var pages = form.Pages;
            for (int i = 1; i < pages.Count; i++)
            {
                sw.Restart();
                form.ShowPage(i);
                log.Add($"Seite {i} ({pages[i].Name}): {sw.ElapsedMilliseconds} ms");
                Pump();
                Window($"ui_{i}_seite");
                if (pages[i].Page is ScrollPage scroll)
                    Full(scroll.Content, Path.Combine(folder, $"ui_{i}_inhalt{variant}_{lang}.png"));
            }

            // Meldungsfenster im Design: kurz anzeigen, abfotografieren, schließen.
            var shot = new System.Windows.Forms.Timer { Interval = 400 };
            shot.Tick += (_, _) =>
            {
                shot.Stop();
                var message = Application.OpenForms.Cast<Form>().Last();
                Full(message, Path.Combine(folder, $"ui_message_{lang}.png"));
                message.Close();
            };
            shot.Start();
            Tr.Show(form, "Alle Einstellungen und Tastenbelegungen auf Standard zurücksetzen?", "N-Connect", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            shot.Dispose();
            form.Close();
        }
        catch (Exception e)
        {
            log.Add($"FEHLER: {e}");
        }
        finally
        {
            File.WriteAllLines(Path.Combine(folder, $"timing{variant}_{lang}.txt"), log);
            if (manager is not null)
                Task.Run(async () => await manager.DisposeAsync()).Wait(TimeSpan.FromSeconds(5));
        }
    }

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
            (ControllerKind.WiiRemote, WiiExtension.MotionPlusNunchuk, "wii_motionplus_nunchuk"),
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
                    Pointer = pressed && kind == ControllerKind.WiiRemote ? (0.3f, 0.6f) : null,
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
