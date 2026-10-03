using System.Drawing;

namespace Switch2Pro.Bridge;

/// <summary>Fenster „Wii-Controller koppeln“: sucht 60 s und zeigt den Fortschritt.</summary>
internal sealed class WiiPairForm : Form
{
    private readonly Label _status = new()
    {
        AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(12),
    };
    private readonly CancellationTokenSource _cts = new();

    public WiiPairForm()
    {
        Text = "Wii-Controller koppeln";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(480, 210);
        Font = new Font("Segoe UI", 9.5f);
        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 70, Padding = new Padding(12, 10, 12, 0),
            Text = "Wii-Fernbedienung: Batteriefach öffnen und die rote SYNC-Taste drücken.\n" +
                   "Wii U Pro Controller: die SYNC-Taste auf der Unterseite drücken.\n" +
                   "Danach verbindet sich der Controller künftig per Tastendruck.",
        };
        var close = new Button { Text = "Schließen", Dock = DockStyle.Bottom, Height = 34 };
        close.Click += (_, _) => Close();
        Controls.Add(_status);
        Controls.Add(hint);
        Controls.Add(close);
        Shown += async (_, _) => await RunAsync();
        Tr.Apply(this);
    }

    private async Task RunAsync()
    {
        _status.Text = Tr.T("Suche …");
        var paired = await Task.Run(() => WiiPairing.ScanAndPair(TimeSpan.FromSeconds(60),
            message => BeginInvokeSafe(() => _status.Text = Tr.T(message)), _cts.Token));
        if (IsDisposed)
            return;
        _status.Text = Tr.T(paired.Count > 0
            ? $"Gekoppelt: {string.Join(", ", paired)}\nDer Controller erscheint gleich in der Übersicht."
            : "Kein Wii-Controller gefunden. SYNC-Taste drücken und erneut versuchen.");
    }

    private void BeginInvokeSafe(Action action)
    {
        if (!IsDisposed && IsHandleCreated)
            BeginInvoke(action);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts.Cancel();
        base.OnFormClosed(e);
    }
}
