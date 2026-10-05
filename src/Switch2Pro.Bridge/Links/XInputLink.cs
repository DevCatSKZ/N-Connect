using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge.Links;

/// <summary>
/// Ein Xbox-Controller, den Windows selbst sieht (XInput): Xbox 360, One, Series, Elite – über USB,
/// Bluetooth oder den Xbox-Wireless-Adapter. XInput deckt vier Slots ab; der Slot ist zugleich der
/// Platz, den der Controller in Spielen einnimmt. Es wird <b>kein</b> virtueller Controller erzeugt
/// (<see cref="Native"/> = true), damit nichts doppelt ankommt. Vibration geht direkt ans Gerät;
/// die Guide-Taste kommt über die erweiterte XInput-Abfrage, der Akkustand über XInputGetBatteryInformation.
/// </summary>
internal sealed class XInputLink : IControllerLink
{
    private readonly int _index;
    private readonly CancellationTokenSource _cts = new();
    private readonly RateMeter _rate = new();
    private int _closed, _lostRaised;
    private XInput.Gamepad _last;
    private bool _haveState;
    private byte _batteryType = 255;

    public ControllerKind Kind => ControllerKind.XboxController;
    /// <summary>XInput verrät nicht, ob USB, Bluetooth oder Adapter – die Anzeige nennt es „XInput“.</summary>
    public Transport Transport => Transport.XInput;
    public string Id { get; }
    public string? Address { get; }
    public string? ProductName { get; }
    /// <summary>Nativ in Spielen sichtbar – weder verstecken noch ein virtuelles Pad nötig.</summary>
    public bool Native => true;
    /// <summary>Der XInput-Platz (0–3), den der Controller in Windows/Spielen einnimmt.</summary>
    public int? NativeSlot => _index;
    public DeviceCalibration Calibration => XboxPad.Calibration;
    public ControllerInfo Info { get; }
    public ControllerState? LastState { get; private set; }
    public double ReportRate => _rate.Rate;
    public bool IsLost => Volatile.Read(ref _lostRaised) == 1;

    public event Action<IControllerLink, ControllerState>? StateReceived;
    public event Action<IControllerLink>? Lost;

    private XInputLink(int index)
    {
        _index = index;
        Id = $"XINPUT:{index}";
        Address = Id; // Kennung für Einstellungen (Name, Platz) – der Slot bleibt pro Sitzung stabil.
        string? name = null;
        int battery = -1;
        if (XInput.GetVidPid(index) is { } vidPid)
            name = XInput.ProductName(vidPid.Vendor, vidPid.Product);
        if (XInput.GetBatteryInformation(index) is { } batteryInfo)
        {
            _batteryType = batteryInfo.BatteryType;
            battery = XboxPad.BatteryPercent(batteryInfo.BatteryType, batteryInfo.BatteryLevel);
        }
        ProductName = name;
        Info = new ControllerInfo();
        _initialBattery = battery;
    }

    private readonly int _initialBattery;

    /// <summary>Belegt der Slot gerade einen echten (nicht von uns virtuell erzeugten) Xbox-Controller?</summary>
    public static bool IsConnected(int index) =>
        XInput.GetState(index, out _) == XInput.ErrorSuccess && !XInput.IsVirtual(index);

    public static XInputLink Open(int index)
    {
        var link = new XInputLink(index);
        _ = Task.Run(() => link.PollLoopAsync(link._cts.Token));
        return link;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        int misses = 0;
        long lastBattery = Environment.TickCount64;
        try
        {
            var battery = _initialBattery;
            while (!ct.IsCancellationRequested)
            {
                int result = XInput.GetState(_index, out var state);
                // Den Platz hat inzwischen einer unserer virtuellen Controller → kein echtes Gerät mehr.
                if (XInput.IsVirtual(_index))
                    result = XInput.ErrorDeviceNotConnected;
                if (result != XInput.ErrorSuccess)
                {
                    // Einzelne Aussetzer bei Bluetooth sind normal – erst nach dreimaligem Fehlschlag trennen.
                    if (++misses >= 3)
                    {
                        Log.Info($"{Id}: XInput-Controller getrennt");
                        RaiseLost();
                        return;
                    }
                }
                else
                {
                    misses = 0;
                    _rate.Tick();
                    // Akku alle ~5 s neu lesen (kostet nichts, ändert sich selten).
                    if (Environment.TickCount64 - lastBattery >= 5000)
                    {
                        lastBattery = Environment.TickCount64;
                        if (XInput.GetBatteryInformation(_index) is { } info)
                        {
                            _batteryType = info.BatteryType;
                            battery = XboxPad.BatteryPercent(info.BatteryType, info.BatteryLevel);
                        }
                    }
                    if (!_haveState || !state.Gamepad.Equals(_last))
                    {
                        _haveState = true;
                        _last = state.Gamepad;
                        var mapped = XboxPad.FromXInput(state.Gamepad.Buttons, state.Gamepad.LeftTrigger, state.Gamepad.RightTrigger,
                            state.Gamepad.LeftThumbX, state.Gamepad.LeftThumbY, state.Gamepad.RightThumbX, state.Gamepad.RightThumbY, battery);
                        LastState = mapped;
                        try
                        {
                            StateReceived?.Invoke(this, mapped);
                        }
                        catch (Exception e)
                        {
                            Log.Error($"{Id}: Eingabe verarbeiten", e);
                        }
                    }
                }
                await Task.Delay(8, ct); // ~125 Hz – wie die Melderate der Controller
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.Warn($"{Id}: XInput-Abrufe fehlgeschlagen ({Log.Reason(e)})");
            RaiseLost();
        }
    }

    /// <summary>Vibration direkt am echten Controller (für „Identifizieren“; Spiele vibrieren selbst).</summary>
    public void SetRumble(byte large, byte small, float strength)
    {
        if (_closed != 0)
            return;
        var v = new XInput.Vibration
        {
            LeftMotor = (ushort)Math.Min(ushort.MaxValue, (int)(large * strength) * 257),
            RightMotor = (ushort)Math.Min(ushort.MaxValue, (int)(small * strength) * 257),
        };
        XInput.SetState(_index, v);
    }

    public Task SetPlayerAsync(int playerIndex) => Task.CompletedTask; // Xbox-LEDs steuert Windows selbst.

    private void RaiseLost()
    {
        if (Interlocked.Exchange(ref _lostRaised, 1) == 0)
            Lost?.Invoke(this);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return ValueTask.CompletedTask;
        _cts.Cancel();
        SetRumble(0, 0, 0);
        return ValueTask.CompletedTask;
    }
}
