using NConnect.Android.Ble;
using NConnect.Android.Output;
using Switch2Pro.Protocol;

namespace NConnect.Android.Core;

/// <summary>
/// Ein Spielerplatz: ein Controller oder ein Joy-Con-Paar, dazu sein Ausgabeweg (virtueller Controller/Touch).
/// Die Verarbeitung entspricht der Windows-Version (Player.cs): zusammenführen → Belegung → Gyro-Stick → Ausgabe.
/// </summary>
public sealed class Player : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<Switch2Device, ControllerState> _states = [];
    private IVirtualPad? _pad;

    public int Index { get; set; }
    public List<Switch2Device> Devices { get; } = [];
    public bool IsPair => Devices.Count == 2;

    /// <summary>Zuletzt ausgegebener Gamepad-Zustand (für die Testanzeige).</summary>
    public GamepadState LastGamepad { get; private set; }
    public PadInput? LastInput { get; private set; }
    public bool HasOutput => _pad is not null;

    public Player(int index) => Index = index;

    public string Name => IsPair ? "Joy-Con-Paar" : Texts.ShortName(Devices.FirstOrDefault()?.Kind ?? ControllerKind.Unknown);

    public int BatteryPercent
    {
        get
        {
            var values = Devices.Select(d => d.LastState?.BatteryPercent ?? -1).Where(v => v >= 0).ToList();
            return values.Count == 0 ? -1 : values.Min();
        }
    }

    public bool Charging => Devices.Any(d => d.LastState?.Charging == true);

    /// <summary>Kann <paramref name="device"/> mit dem einzelnen Joy-Con dieses Spielers ein Paar bilden?</summary>
    public bool CanPairWith(Switch2Device device) =>
        Devices.Count == 1 && Devices[0].Kind.IsJoyCon() && device.Kind.IsJoyCon()
        && Devices[0].Kind.IsLeftJoyCon() != device.Kind.IsLeftJoyCon();

    public void AttachOutput(IVirtualPad? pad)
    {
        lock (_lock)
        {
            if (_pad is not null)
                _pad.Rumble -= OnRumble;
            _pad?.Dispose();
            _pad = pad;
            if (_pad is not null)
                _pad.Rumble += OnRumble;
        }
    }

    public void Add(Switch2Device device)
    {
        lock (_lock)
            Devices.Add(device);
    }

    public void Remove(Switch2Device device)
    {
        lock (_lock)
        {
            Devices.Remove(device);
            _states.Remove(device);
        }
    }

    public void OnState(Switch2Device source, ControllerState state)
    {
        var settings = AppPrefs.Settings;
        PadInput input;
        lock (_lock)
        {
            _states[source] = state;
            if (Devices.Count == 2)
            {
                if (Devices.Any(d => !_states.ContainsKey(d)))
                    return;
                var left = Devices.First(d => d.Kind.IsLeftJoyCon());
                var right = Devices.First(d => !d.Kind.IsLeftJoyCon());
                input = Mapping.Merge(_states[left], left.Calibration, _states[right], right.Calibration, settings.PairGyroSource);
            }
            else if (source.Kind.IsJoyCon() && settings.IsUprightJoyCon(source.Address))
            {
                // Hochkant: Tasten unter ihren echten Namen, daher Belegung „Joy-Con-Paar“ (wie Windows).
                input = Mapping.Normalize(state, source.Calibration, sideways: false) with { Kind = ControllerKind.JoyConPair };
            }
            else
            {
                input = Mapping.Normalize(state, source.Calibration);
            }
        }

        var output = Mapping.Evaluate(input, settings);
        var gamepad = output.Gamepad;
        bool gyroStick = settings.GyroStick == GyroStickMode.Always
                         || settings.GyroStick == GyroStickMode.WhileAiming && gamepad.LeftTrigger > 40
                         || output.Specials.HasFlag(SpecialAction.GyroStick);
        bool gyroPaused = output.Specials.HasFlag(SpecialAction.GyroPause);
        if (gyroStick && !gyroPaused && input.Motion is { } motion)
        {
            var (gx, gy) = Mapping.GyroToStick(motion, settings, gamepad.RightX, gamepad.RightY);
            gamepad = gamepad with { RightX = gx, RightY = gy };
        }
        LastInput = input;
        LastGamepad = gamepad;
        try
        {
            _pad?.Update(gamepad);
        }
        catch (Exception e)
        {
            Log.Error($"Spieler {Index + 1}: Ausgabe", e);
        }
    }

    private void OnRumble(byte large, byte small)
    {
        var settings = AppPrefs.Settings;
        if (!settings.RumbleEnabled)
            large = small = 0;
        foreach (var d in Devices.ToList())
            d.SetRumble(large, small, settings.RumbleStrength);
    }

    public void Dispose()
    {
        AttachOutput(null);
        foreach (var d in Devices.ToList())
            d.Dispose();
        Devices.Clear();
    }
}
