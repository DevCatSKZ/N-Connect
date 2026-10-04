using System.Drawing;

namespace Switch2Pro.Bridge;

/// <summary>
/// Fenster „Controller koppeln“ (Switch 1, Nintendo Switch Online, Wii): sucht 60 s und zeigt den Fortschritt.
/// Meist unnötig – N-Connect koppelt solche Controller auch im Hintergrund, sobald SYNC gedrückt wird.
/// </summary>
internal sealed class PairForm : Form
{
    private readonly Label _status = new()
    {
        AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(12),
    };
    private readonly CancellationTokenSource _cts = new();

    public PairForm()
    {
        Text = "Controller koppeln";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(520, 240);
        Font = new Font("Segoe UI", 9.5f);
        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 92, Padding = new Padding(12, 10, 12, 0),
            Text = "Joy-Con, Pro Controller, NES/SNES/N64/Mega Drive: SYNC-Taste drücken, bis die Lichter laufen.\n" +
                   "Wii-Fernbedienung: Batteriefach öffnen und die rote SYNC-Taste drücken.\n" +
                   "Wii U Pro Controller: die SYNC-Taste auf der Unterseite drücken.\n" +
                   "Danach verbindet sich der Controller künftig per Tastendruck.",
        };
        var close = new Button { Text = "Schließen", Dock = DockStyle.Bottom, Height = 34 };
        close.Click += (_, _) => Close();
        Controls.Add(_status);
        Controls.Add(hint);
        Controls.Add(close);
        Shown += async (_, _) => await RunAsync();
        Theme.Apply(this);
        Tr.Apply(this);
    }

    private async Task RunAsync()
    {
        _status.Text = Tr.T("Suche …");
        List<string> paired;
        try
        {
            paired = await Task.Run(() => ControllerPairing.ScanAndPair(TimeSpan.FromSeconds(60),
                message => BeginInvokeSafe(() => _status.Text = Tr.T(message)), _cts.Token));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException
                                      or System.ComponentModel.Win32Exception or ObjectDisposedException)
        {
            Log.Error("Kopplung", e);
            if (!IsDisposed)
                _status.Text = Tr.T("Kopplung fehlgeschlagen – Details im Protokoll.");
            return;
        }
        if (IsDisposed)
            return;
        _status.Text = Tr.T(paired.Count > 0
            ? $"Gekoppelt: {string.Join(", ", paired)}\nDer Controller erscheint gleich in der Übersicht."
            : "Kein Controller im Kopplungsmodus gefunden. SYNC-Taste drücken und erneut versuchen.");
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
        _cts.Cancel();
        base.OnFormClosed(e);
    }
}
