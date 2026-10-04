using System.Drawing;

namespace Switch2Pro.Bridge;

/// <summary>Kurzanleitung beim ersten Start (und im Menü): wie welche Controller verbunden werden.</summary>
internal sealed class WelcomeForm : Form
{
    public bool OpenSettings { get; private set; }

    private const string German =
        "So verbindest du deine Controller:\n\n" +
        "Switch 2 (Pro Controller, Joy-Con 2, GameCube):\n" +
        "   Kurz die kleine SYNC-Taste drücken – nach ein paar Sekunden vibriert der Controller.\n" +
        "   Danach reicht ein Tastendruck zum Verbinden. Pro Controller und GameCube gehen auch per USB-Kabel.\n" +
        "   Wichtig: NICHT über „Bluetooth → Gerät hinzufügen“ koppeln – das stört die Verbindung.\n\n" +
        "Switch 1 (Pro Controller, Joy-Con, NES/SNES/N64/Mega Drive):\n" +
        "   Einmal in Windows unter „Bluetooth → Gerät hinzufügen“ koppeln (SYNC-Taste halten).\n\n" +
        "Wii-Fernbedienung und Wii U Pro Controller:\n" +
        "   Rechtsklick auf das Symbol im Infobereich → „Wii-Controller koppeln …“.\n\n" +
        "Windows, Steam und Spiele sehen jeden Controller als Xbox-Controller.\n" +
        "Das Programm läuft unten rechts im Infobereich – ein Klick öffnet die Übersicht.";

    private const string English =
        "How to connect your controllers:\n\n" +
        "Switch 2 (Pro Controller, Joy-Con 2, GameCube):\n" +
        "   Briefly press the small SYNC button – after a few seconds the controller vibrates.\n" +
        "   Afterwards a button press is enough to connect. Pro Controller and GameCube also work via USB cable.\n" +
        "   Important: do NOT pair via “Bluetooth → Add device” – that disturbs the connection.\n\n" +
        "Switch 1 (Pro Controller, Joy-Con, NES/SNES/N64/Mega Drive):\n" +
        "   Pair once in Windows under “Bluetooth → Add device” (hold the SYNC button).\n\n" +
        "Wii Remote and Wii U Pro Controller:\n" +
        "   Right-click the tray icon → “Pair Wii controller …”.\n\n" +
        "Windows, Steam and games see every controller as an Xbox controller.\n" +
        "The program runs in the tray at the bottom right – a click opens the overview.";

    public WelcomeForm()
    {
        Text = Tr.English ? "Nintendo Controller for Windows – Welcome" : "Nintendo Controller für Windows – Willkommen";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(700, 440);

        var text = new Label { Dock = DockStyle.Fill, Padding = new Padding(20), Text = Tr.English ? English : German };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
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
