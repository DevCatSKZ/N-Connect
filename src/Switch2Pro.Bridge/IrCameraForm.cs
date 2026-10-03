using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Switch2Pro.Bridge.Links;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>Live-Bild der IR-Kamera im rechten Joy-Con (Switch 1) – Graustufen, vergrößert, mit Auflösungswahl.</summary>
internal sealed class IrCameraForm : Form
{
    private readonly Switch1HidLink _link;
    private readonly ComboBox _resolution = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 6, 0, 0) };
    private readonly PictureBox _picture = new() { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
    private int _frames;
    private DateTime _since = DateTime.UtcNow;
    private bool _running;

    public IrCameraForm(Switch1HidLink link)
    {
        _link = link;
        Text = "IR-Kamera (Joy-Con R)";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(660, 540);
        Font = new Font("Segoe UI", 9.5f);
        _resolution.Items.AddRange(["40 × 30 (schnell)", "80 × 60", "160 × 120", "320 × 240 (langsam)"]);
        _resolution.SelectedIndex = 1;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6) };
        bar.Controls.Add(new Label { Text = "Auflösung:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        bar.Controls.Add(_resolution);
        bar.Controls.Add(_status);
        Controls.Add(_picture);
        Controls.Add(bar);
        _resolution.SelectedIndexChanged += async (_, _) => await RestartAsync();
        Shown += async (_, _) => await RestartAsync();
        Tr.Apply(this);
    }

    private async Task RestartAsync()
    {
        if (_running)
            await _link.StopIrAsync();
        _running = false;
        _status.Text = Tr.T("Kamera startet …");
        var resolution = (IrResolution)_resolution.SelectedIndex;
        bool ok = await _link.StartIrAsync(resolution, OnFrame, CancellationToken.None);
        if (IsDisposed)
        {
            if (ok)
                await _link.StopIrAsync();
            return;
        }
        _running = ok;
        _frames = 0;
        _since = DateTime.UtcNow;
        _status.Text = Tr.T(ok ? "läuft – Joy-Con auf eine Lichtquelle oder die Hand richten" : "Kamera startet nicht (Details im Protokoll)");
    }

    /// <summary>Kommt auf dem Lese-Thread: Bild bauen und im UI-Thread anzeigen.</summary>
    private void OnFrame(byte[] gray, int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bmp.PixelFormat);
        var row = new byte[data.Stride];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte v = gray[y * width + x];
                row[x * 3] = row[x * 3 + 1] = row[x * 3 + 2] = v;
            }
            Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, data.Stride);
        }
        bmp.UnlockBits(data);
        if (IsDisposed || !IsHandleCreated)
        {
            bmp.Dispose();
            return;
        }
        BeginInvoke(() =>
        {
            if (IsDisposed)
            {
                bmp.Dispose();
                return;
            }
            var old = _picture.Image;
            _picture.Image = bmp;
            old?.Dispose();
            _frames++;
            double seconds = (DateTime.UtcNow - _since).TotalSeconds;
            if (seconds > 1)
                _status.Text = Tr.T($"{width} × {height} · {_frames / seconds:F1} Bilder/s");
        });
    }

    protected override async void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (_running)
            await _link.StopIrAsync();
        _picture.Image?.Dispose();
    }
}
