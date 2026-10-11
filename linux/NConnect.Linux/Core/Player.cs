using NConnect.Linux.Ble;
using NConnect.Linux.Output;
using Switch2Pro.Protocol;

namespace NConnect.Linux.Core;

/// <summary>
/// Ein Spielerplatz: ein Controller oder ein Joy-Con-Paar mit seinem virtuellen Xbox-360-Controller.
/// Verarbeitung wie unter Windows/Android: zusammenführen → Belegung → Gyro-Stick → Ausgabe.
/// </summary>
public sealed class Player : IAsyncDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<Switch2Device, ControllerState> _states = [];
    private readonly Func<Settings> _settings;
    private UInputPad? _pad;

    public int Index { get; }
    public List<Switch2Device> Devices { get; } = [];
    public bool IsPair => Devices.Count == 2;
    public long LastActivityTicks { get; private set; } = Environment.TickCount64;

    /// <summary>Tasten, die zusätzlich eine Kodi-Aktion auslösen können (im Add-on einstellbar).</summary>
    public const ProButtons KodiButtons = ProButtons.Capture | ProButtons.C | ProButtons.GL | ProButtons.GR;
    private ProButtons _lastKodiButtons;

    /// <summary>Eine der <see cref="KodiButtons"/> wurde gedrückt (steigende Flanke).</summary>
    public event Action<Player, ProButtons>? KodiButtonPressed;

    public Player(int index, Func<Settings> settings)
    {
        Index = index;
        _settings = settings;
    }

    public string Name => IsPair ? "Joy-Con-Paar" : (Devices.FirstOrDefault()?.Kind ?? ControllerKind.Unknown).DisplayName();

    public int BatteryPercent
    {
        get
        {
            var values = Devices.ToList().Select(d => d.LastState?.BatteryPercent ?? -1).Where(v => v >= 0).ToList();
            return values.Count == 0 ? -1 : values.Min();
        }
    }

    public bool Charging => Devices.ToList().Any(d => d.LastState?.Charging == true);

    public bool CanPairWith(Switch2Device device) =>
        Devices.Count == 1 && Devices[0].Kind.IsJoyCon() && device.Kind.IsJoyCon()
        && Devices[0].Kind.IsLeftJoyCon() != device.Kind.IsLeftJoyCon();

    public void AttachOutput(UInputPad? pad)
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
        var settings = _settings();
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
                input = Mapping.Normalize(state, source.Calibration, sideways: false) with { Kind = ControllerKind.JoyConPair };
            }
            else
            {
                input = Mapping.Normalize(state, source.Calibration);
            }
        }

        var kodi = input.Buttons & KodiButtons;
        var pressed = kodi & ~_lastKodiButtons;
        _lastKodiButtons = kodi;
        if (pressed != 0)
            foreach (var b in (ProButtons[])[ProButtons.Capture, ProButtons.C, ProButtons.GL, ProButtons.GR])
                if (pressed.HasFlag(b))
                    KodiButtonPressed?.Invoke(this, b);

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
        if (gamepad.Buttons != XButtons.None || gamepad.LeftTrigger > 20 || gamepad.RightTrigger > 20
            || Math.Abs((int)gamepad.LeftX) > 8000 || Math.Abs((int)gamepad.LeftY) > 8000
            || Math.Abs((int)gamepad.RightX) > 8000 || Math.Abs((int)gamepad.RightY) > 8000)
            LastActivityTicks = Environment.TickCount64;
        try
        {
            _pad?.Update(gamepad);
        }
        catch (Exception e)
        {
            Log.Error($"Spieler {Index + 1}: Ausgabe", e);
        }
        DsuServer.Instance?.Publish(Index, Mac, gamepad, input);
    }

    /// <summary>Kennung für DSU-Clients: Bluetooth-Adresse des (ersten) Controllers.</summary>
    private ulong Mac
    {
        get
        {
            string? address = Devices.FirstOrDefault()?.Address;
            return address is not null && ulong.TryParse(address.Replace(":", ""), System.Globalization.NumberStyles.HexNumber, null, out ulong v)
                ? v : (ulong)(Index + 1);
        }
    }

    private void OnRumble(byte large, byte small)
    {
        var settings = _settings();
        if (!settings.RumbleEnabled)
            large = small = 0;
        foreach (var d in Devices.ToList())
            d.SetRumble(large, small, settings.RumbleStrengthFor(d.Kind));
    }

    public async ValueTask DisposeAsync()
    {
        AttachOutput(null);
        DsuServer.Instance?.Clear(Index);
        List<Switch2Device> devices;
        lock (_lock)
        {
            devices = [.. Devices];
            Devices.Clear();
        }
        foreach (var d in devices)
            await d.DisposeAsync().ConfigureAwait(false);
    }
}
