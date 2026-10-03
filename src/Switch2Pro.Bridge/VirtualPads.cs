using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>Ein virtueller Controller, den Windows, Steam und Spiele als echtes Gerät sehen.</summary>
internal interface IVirtualPad : IDisposable
{
    void Update(GamepadState gamepad, PadInput input);
    /// <summary>Vibrationswunsch vom Spiel (großer, kleiner Motor; je 0–255).</summary>
    event Action<byte, byte>? Rumble;
    /// <summary>Spielernummer, die Windows dem virtuellen Xbox-Controller zuteilt (0–3).</summary>
    event Action<int>? PlayerIndexAssigned;
}

/// <summary>Verwaltet die Verbindung zum ViGEmBus-Treiber.</summary>
internal sealed class PadFactory : IDisposable
{
    private readonly ViGEmClient _client;

    private PadFactory(ViGEmClient client) => _client = client;

    /// <summary>Liefert null, wenn der ViGEmBus-Treiber fehlt.</summary>
    public static PadFactory? TryCreate()
    {
        try
        {
            return new PadFactory(new ViGEmClient());
        }
        catch (Exception e) when (e is Nefarius.ViGEm.Client.Exceptions.VigemBusNotFoundException
                                       or Nefarius.ViGEm.Client.Exceptions.VigemBusAccessFailedException
                                       or Nefarius.ViGEm.Client.Exceptions.VigemBusVersionMismatchException
                                       or DllNotFoundException or System.ComponentModel.Win32Exception)
        {
            Log.Error("ViGEmBus-Treiber nicht verfügbar", e);
            return null;
        }
    }

    public IVirtualPad Create(OutputMode mode) => mode switch
    {
        OutputMode.DualShock4 => new Ds4Pad(_client.CreateDualShock4Controller()),
        _ => new Xbox360Pad(_client.CreateXbox360Controller()),
    };

    public void Dispose() => _client.Dispose();
}

internal sealed class Xbox360Pad : IVirtualPad
{
    private readonly IXbox360Controller _pad;
    private readonly object _gate = new();
    private bool _disposed;

    public event Action<byte, byte>? Rumble;
    public event Action<int>? PlayerIndexAssigned;

    public Xbox360Pad(IXbox360Controller pad)
    {
        _pad = pad;
        _pad.AutoSubmitReport = false;
        _pad.FeedbackReceived += OnFeedback;
        _pad.Connect();
    }

    private void OnFeedback(object? sender, Xbox360FeedbackReceivedEventArgs e)
    {
        Rumble?.Invoke(e.LargeMotor, e.SmallMotor);
        PlayerIndexAssigned?.Invoke(e.LedNumber);
    }

    private static readonly (XButtons Flag, Xbox360Button Button)[] ButtonMap =
    [
        (XButtons.A, Xbox360Button.A), (XButtons.B, Xbox360Button.B),
        (XButtons.X, Xbox360Button.X), (XButtons.Y, Xbox360Button.Y),
        (XButtons.LB, Xbox360Button.LeftShoulder), (XButtons.RB, Xbox360Button.RightShoulder),
        (XButtons.Back, Xbox360Button.Back), (XButtons.Start, Xbox360Button.Start),
        (XButtons.Guide, Xbox360Button.Guide),
        (XButtons.LS, Xbox360Button.LeftThumb), (XButtons.RS, Xbox360Button.RightThumb),
        (XButtons.Up, Xbox360Button.Up), (XButtons.Down, Xbox360Button.Down),
        (XButtons.Left, Xbox360Button.Left), (XButtons.Right, Xbox360Button.Right),
    ];

    public void Update(GamepadState g, PadInput input)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            foreach (var (flag, button) in ButtonMap)
                _pad.SetButtonState(button, (g.Buttons & flag) != 0);
            _pad.SetAxisValue(Xbox360Axis.LeftThumbX, g.LeftX);
            _pad.SetAxisValue(Xbox360Axis.LeftThumbY, g.LeftY);
            _pad.SetAxisValue(Xbox360Axis.RightThumbX, g.RightX);
            _pad.SetAxisValue(Xbox360Axis.RightThumbY, g.RightY);
            _pad.SetSliderValue(Xbox360Slider.LeftTrigger, g.LeftTrigger);
            _pad.SetSliderValue(Xbox360Slider.RightTrigger, g.RightTrigger);
            _pad.SubmitReport();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _pad.FeedbackReceived -= OnFeedback;
            try { _pad.Disconnect(); } catch (Exception e) { Log.Warn($"Xbox-Pad trennen: {e.Message}"); }
            // Ohne Dispose bliebe bei jedem Neuverbinden ein natives ViGEm-Ziel übrig.
            try { ((IDisposable)_pad).Dispose(); } catch (Exception e) { Log.Warn($"Xbox-Pad freigeben: {e.Message}"); }
        }
    }
}

internal sealed class Ds4Pad : IVirtualPad
{
    private readonly IDualShock4Controller _pad;
    private readonly object _gate = new();
    private readonly long _start = System.Diagnostics.Stopwatch.GetTimestamp();
    private byte _touchCounter;
    private volatile bool _disposed;

    public event Action<byte, byte>? Rumble;
    public event Action<int>? PlayerIndexAssigned { add { } remove { } }

    public Ds4Pad(IDualShock4Controller pad)
    {
        _pad = pad;
        _pad.AutoSubmitReport = false;
        _pad.Connect();
        _outputThread = new Thread(ReadOutputReports) { IsBackground = true, Name = "DS4-Ausgabe" };
        _outputThread.Start();
    }

    private readonly Thread _outputThread;

    /// <summary>
    /// Liest die Ausgabeberichte, die Spiele an den virtuellen DS4 schicken (USB-Bericht 0x05:
    /// Byte 1 Bit 0 = Vibration gültig, Byte 4 = kleiner Motor, Byte 5 = großer Motor).
    /// </summary>
    private void ReadOutputReports()
    {
        while (!_disposed)
        {
            try
            {
                var report = _pad.AwaitRawOutputReport(250, out bool timedOut)?.ToArray();
                if (timedOut || report is null || report.Length < 6)
                    continue;
                if (report[0] == 0x05 && (report[1] & 0x01) != 0)
                    Rumble?.Invoke(report[5], report[4]);
            }
            catch (Exception e)
            {
                if (!_disposed)
                    Log.Warn($"DS4-Ausgabe: {e.Message}");
                return;
            }
        }
    }

    public void Update(GamepadState g, PadInput input)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            // DS4-Zeitstempel in Einheiten von 5,33 µs. Hohe Auflösung (Stopwatch) – mit dem
            // 15,6-ms-Raster von TickCount64 würde der Gyro in Steam ruckeln.
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_start);
            var timestamp = (ushort)(long)(elapsed.TotalMicroseconds * 3 / 16);
            _pad.SubmitRawReport(Mapping.ToDs4Report(g, input, timestamp, ref _touchCounter));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            try { _pad.Disconnect(); } catch (Exception e) { Log.Warn($"DS4-Pad trennen: {e.Message}"); }
        }
        // Ausgabe-Thread beenden, bevor das native Ziel freigegeben wird.
        _outputThread.Join(TimeSpan.FromSeconds(1));
        try { ((IDisposable)_pad).Dispose(); } catch (Exception e) { Log.Warn($"DS4-Pad freigeben: {e.Message}"); }
    }
}
