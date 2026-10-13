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
            int best = -1;
            foreach (var d in Devices)
            {
                int v = d.LastState?.BatteryPercent ?? -1;
                if (v >= 0 && (best < 0 || v < best))
                    best = v;
            }
            return best;
        }
    }

    public bool Charging => Devices.Any(d => d.LastState?.Charging == true);

    /// <summary>Bluetooth-Adresse des ersten Geräts (für den DSU-Server).</summary>
    public ulong Mac => Devices.Count > 0 ? AppPrefs.ParseAddress(Devices[0].Address) : 0;

    private bool _warn15, _warn5;

    /// <summary>Akku-Warnungstext bei ≤15 % bzw. ≤5 % – je Verbindung höchstens einmal (wie unter Windows).</summary>
    public string? CheckBattery()
    {
        int percent = BatteryPercent;
        if (percent < 0)
            return null;
        if (Charging || percent > 20)
        {
            _warn15 = _warn5 = false;
            return null;
        }
        if (percent <= 5 && !_warn5)
        {
            _warn5 = true;
            return L.F("{0}: Akku fast leer ({1} %) – bitte laden", Name, percent);
        }
        if (percent <= 15 && !_warn15)
        {
            _warn15 = true;
            return L.F("{0}: Akku fast leer ({1} %)", Name, percent);
        }
        return null;
    }

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
                Switch2Device? left = null, right = null;
                bool missing = false;
                foreach (var d in Devices)
                {
                    if (!_states.ContainsKey(d))
                        missing = true;
                    if (d.Kind.IsLeftJoyCon())
                        left = d;
                    else
                        right = d;
                }
                if (missing || left is null || right is null)
                    return;
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
        GamepadState gamepad;
        lock (_macros)
            gamepad = ApplyMacros(output.Gamepad, output.Macros);
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
        Dsu.DsuServer.Instance?.Publish(Index, Mac, gamepad, input);
        try
        {
            _pad?.Update(gamepad);
        }
        catch (Exception e)
        {
            Log.Error($"Spieler {Index + 1}: Ausgabe", e);
        }
    }

    // ---------- Makros ----------

    /// <summary>Laufende Makros (Skript, Startzeit) und die Makros, deren Taste gerade gehalten wird.</summary>
    private readonly List<(string Text, MacroScript Script, long Start)> _macros = [];
    private readonly HashSet<string> _macroButtons = [];

    /// <summary>
    /// Startet Makros beim Drücken ihrer Taste (nicht erneut, solange sie läuft) und fügt den aktuellen Schritt
    /// aller laufenden Makros zum Gamepad hinzu. Tastatur-Schritte (Key:) entfallen – unter Android gibt es
    /// keine Tastatur-Ausgabe. Portierung von Player.ApplyMacros (Windows).
    /// </summary>
    private GamepadState ApplyMacros(GamepadState g, IReadOnlyList<string> pressed)
    {
        // Schnellweg ohne Allokation: ~90 Berichte/s je Controller, fast immer ohne Makro.
        if (pressed.Count == 0 && _macros.Count == 0)
        {
            if (_macroButtons.Count > 0)
                _macroButtons.Clear();
            return g;
        }
        long now = System.Environment.TickCount64;
        for (int k = 0; k < pressed.Count; k++)
        {
            string text = pressed[k];
            bool known = _macroButtons.Contains(text);
            if (!known)
                foreach (var m in _macros)
                    if (m.Text == text)
                    {
                        known = true;
                        break;
                    }
            if (known || !MacroScript.TryParse(text, out var script))
                continue;
            _macros.Add((text, script, now));
        }
        _macroButtons.Clear();
        for (int k = 0; k < pressed.Count; k++)
            _macroButtons.Add(pressed[k]);
        for (int i = _macros.Count - 1; i >= 0; i--)
        {
            var (_, script, start) = _macros[i];
            if (script.StepAt(now - start) is not { } step)
            {
                _macros.RemoveAt(i);
                continue;
            }
            // step.Keys (Tastatur) gibt es unter Android nicht – nur die Gamepad-Tasten werden gesetzt.
            foreach (var target in step.Buttons)
                g = Mapping.Press(g, target);
        }
        return g;
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
        _macros.Clear();
        foreach (var d in Devices.ToList())
            d.Dispose();
        Devices.Clear();
    }
}
