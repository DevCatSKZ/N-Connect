using System.Drawing;

namespace Switch2Pro.Bridge;

/// <summary>Einmalige Kurzanleitung beim ersten Start.</summary>
internal sealed class WelcomeForm : Form
{
    public bool OpenSettings { get; private set; }

    public WelcomeForm()
    {
        Text = "Switch 2 Pro Controller – Willkommen";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(580, 420);

        var text = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            Text =
                "So verbindest du deinen Switch 2 Pro Controller:\n\n" +
                "1.  Drück kurz die kleine SYNC-Taste oben am Controller.\n" +
                "     Die Lichter laufen hin und her.\n\n" +
                "2.  Warte ein paar Sekunden – der Controller vibriert kurz und\n" +
                "     ein Licht leuchtet. Fertig! Windows, Steam und Spiele sehen\n" +
                "     ihn jetzt als normalen Controller.\n\n" +
                "Danach reicht ein Tastendruck am Controller, um ihn wieder zu verbinden.\n\n" +
                "Wichtig:  NICHT über „Einstellungen → Bluetooth → Gerät hinzufügen“\n" +
                "koppeln – das ist nicht nötig und stört die Verbindung.\n\n" +
                "Das Programm läuft unten rechts im Infobereich (Controller-Symbol).\n" +
                "Ein Klick darauf öffnet die Einstellungen.",
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var ok = new Button { Text = "Verstanden", AutoSize = true, DialogResult = DialogResult.OK };
        var settings = new Button { Text = "Einstellungen öffnen", AutoSize = true, DialogResult = DialogResult.OK };
        settings.Click += (_, _) => OpenSettings = true;
        buttons.Controls.Add(ok);
        buttons.Controls.Add(settings);
        AcceptButton = ok;

        Controls.Add(text);
        Controls.Add(buttons);
    }
}
