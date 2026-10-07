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
    /// <summary>Lichtleistenfarbe, die ein Spiel dem virtuellen DualShock 4 setzt (R, G, B).</summary>
    event Action<byte, byte, byte>? Lightbar;
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
    private volatile bool _disposed;

    public event Action<byte, byte>? Rumble;
    public event Action<byte, byte, byte>? Lightbar { add { } remove { } }

    private Action<int>? _playerIndexAssigned;
    private volatile int _slot = -1;

    /// <summary>Windows meldet den Platz schon beim Verbinden – wer sich danach anmeldet, bekommt ihn nachgeliefert.</summary>
    public event Action<int>? PlayerIndexAssigned
    {
        add
        {
            _playerIndexAssigned += value;
            if (_slot >= 0)
                value?.Invoke(_slot);
        }
        remove => _playerIndexAssigned -= value;
    }

    public Xbox360Pad(IXbox360Controller pad)
    {
        _pad = pad;
        _pad.AutoSubmitReport = false;
        _pad.FeedbackReceived += OnFeedback;
        _pad.Connect();
        // Platz merken und als „virtuell“ kennzeichnen, damit die XInput-Suche unser eigenes Pad nicht
        // für einen echten Xbox-Controller hält. UserIndex ist direkt nach Connect() oft noch nicht
        // vergeben – dann kurz im Hintergrund nachlesen, sonst bliebe ein Geist-Controller in der Übersicht.
        if (!TryMarkVirtual())
        {
            Task.Run(async () =>
            {
                for (int i = 0; i < 80 && !_disposed && !TryMarkVirtual(); i++)
                    await Task.Delay(50);
                if (!_disposed && _slot < 0)
                    Log.Warn("Xbox-Pad: Platz nicht lesbar – XInput-Slot nicht als virtuell markiert");
            });
        }
    }

    /// <summary>Den XInput-Platz des virtuellen Pads lesen und als „eigen“ markieren; false, wenn noch unbekannt.</summary>
    private bool TryMarkVirtual()
    {
        try
        {
            int slot = (int)_pad.UserIndex;
            if (slot is >= 0 and <= 3)
            {
                _slot = slot;
                XInput.SetVirtualSlot(slot, true);
                _playerIndexAssigned?.Invoke(slot);
                return true;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
        }
        return false;
    }

    private void OnFeedback(object? sender, Xbox360FeedbackReceivedEventArgs e)
    {
        Rumble?.Invoke(e.LargeMotor, e.SmallMotor);
        _slot = e.LedNumber;
        // Auch hier markieren: kam die Platzmeldung zuerst über Feedback, ist der Slot damit sicher bekannt.
        if (e.LedNumber is >= 0 and <= 3)
            XInput.SetVirtualSlot(e.LedNumber, true);
        _playerIndexAssigned?.Invoke(e.LedNumber);
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
            if (_slot is >= 0 and <= 3)
                XInput.SetVirtualSlot(_slot, false);
            try { _pad.Disconnect(); } catch (Exception e) { Log.Warn($"Xbox-Pad trennen: {Log.Reason(e)}"); }
            // Ohne Dispose bliebe bei jedem Neuverbinden ein natives ViGEm-Ziel übrig.
            try { ((IDisposable)_pad).Dispose(); } catch (Exception e) { Log.Warn($"Xbox-Pad freigeben: {Log.Reason(e)}"); }
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
    public event Action<byte, byte, byte>? Lightbar;

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
    /// Byte 1 Bit 0 = Vibration gültig, Bit 1 = Lichtleiste gültig, Byte 4 = kleiner Motor, Byte 5 = großer Motor,
    /// Byte 6–8 = Lichtleiste R, G, B – wie DS4Windows).
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
                if (report[0] == 0x05 && (report[1] & 0x02) != 0 && report.Length >= 9)
                    Lightbar?.Invoke(report[6], report[7], report[8]);
            }
            catch (Exception e)
            {
                if (!_disposed)
                    Log.Warn($"DS4-Ausgabe: {Log.Reason(e)}");
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
            try { _pad.Disconnect(); } catch (Exception e) { Log.Warn($"DS4-Pad trennen: {Log.Reason(e)}"); }
        }
        // Ausgabe-Thread beenden, bevor das native Ziel freigegeben wird.
        _outputThread.Join(TimeSpan.FromSeconds(1));
        try { ((IDisposable)_pad).Dispose(); } catch (Exception e) { Log.Warn($"DS4-Pad freigeben: {Log.Reason(e)}"); }
    }
}
