using System.Drawing;

namespace Switch2Pro.Bridge;

/// <summary>Kurzanleitung beim ersten Start (und im Menü): wie welche Controller verbunden werden.</summary>
internal sealed class WelcomeForm : UiForm
{
    public bool OpenSettings { get; private set; }

    /// <summary>Anleitung in Absätzen – jeder Absatz ist ein Schlüssel der Übersetzungstabellen.</summary>
    internal static readonly string[] Paragraphs =
    [
        "So verbindest du deine Controller:",
        "Switch 2 (Pro Controller, Joy-Con 2, GameCube):\n" +
        "   Kurz die kleine SYNC-Taste drücken – nach ein paar Sekunden vibriert der Controller.\n" +
        "   Danach reicht ein Tastendruck zum Verbinden. Pro Controller und GameCube gehen auch per USB-Kabel.\n" +
        "   Wichtig: NICHT über „Bluetooth → Gerät hinzufügen“ koppeln – das stört die Verbindung.",
        "Switch 1 (Pro Controller, Joy-Con, NES/SNES/N64/Mega Drive), Wii-Fernbedienung, Wii U Pro Controller:\n" +
        "   SYNC-Taste drücken – N-Connect koppelt den Controller selbst (kein Umweg über die Windows-Einstellungen).\n" +
        "   Danach verbindet er sich per Tastendruck. Gezielt suchen: Rechtsklick auf das Symbol → „Controller koppeln …“.",
        "Windows, Steam und Spiele sehen jeden Controller als Xbox-Controller.\n" +
        "Das Programm läuft unten rechts im Infobereich – ein Klick öffnet die Übersicht.",
    ];

    /// <summary>Ganze Anleitung in der Sprache der Oberfläche.</summary>
    internal static string Guide => string.Join("\n\n", Paragraphs.Select(Tr.T));

    public WelcomeForm()
    {
        Text = Tr.T("N-Connect – Willkommen");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        Font = UiFonts.Body;
        AutoScaleDimensions = UiScale.Dimensions;
        AutoScaleMode = AutoScaleMode.None;

        string guide = Guide;
        int padding = UiScale.Px(20), buttonsHeight = UiScale.Px(52), width = UiScale.Px(700);
        // Übersetzungen sind teils länger als das Deutsche: Höhe nach dem umbrochenen Text, höchstens Bildschirmhöhe.
        int textHeight = TextRenderer.MeasureText(guide, Font, new Size(width - 2 * padding, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        int maxHeight = (Screen.PrimaryScreen?.WorkingArea.Height ?? 720) - UiScale.Px(80);
        ClientSize = new Size(width, Math.Min(maxHeight, Math.Max(UiScale.Px(440), textHeight + 2 * padding + buttonsHeight + UiScale.Px(12))));

        var text = new Label { Dock = DockStyle.Fill, Padding = new Padding(padding), Text = guide, UseMnemonic = false };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = buttonsHeight, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(UiScale.Px(10)) };
        var ok = new Button { Text = Tr.T("Verstanden"), AutoSize = true, DialogResult = DialogResult.OK };
        var settings = new Button { Text = Tr.T("Einstellungen öffnen"), AutoSize = true, DialogResult = DialogResult.OK };
        settings.Click += (_, _) => OpenSettings = true;
        buttons.Controls.Add(ok);
        buttons.Controls.Add(settings);
        AcceptButton = ok;

        Controls.Add(text);
        Controls.Add(buttons);
        Theme.Apply(this);
    }
}
