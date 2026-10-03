using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Mausmodus eines Joy-Con 2 wie an der Switch 2: Liegt er mit dem Sensor auf einer Fläche, steuert er den
/// Mauszeiger; hebt man ihn an, ist er wieder Controller. Im Mausmodus gilt (Joy-Con R / L):
/// R bzw. L = Linksklick, ZR bzw. ZL = Rechtsklick, Stick drücken = Mittelklick, Stick hoch/runter = Scrollen.
/// Diese Tasten gehen dann nicht an das Spiel; alle anderen Tasten bleiben Controller-Tasten.
/// </summary>
internal sealed class JoyConMouse
{
    /// <summary>So lange muss die Fläche erkannt sein, bevor der Mausmodus beginnt (gegen Fehlauslösung beim Halten).</summary>
    private const int EnterMs = 120;
    /// <summary>So lange darf die Fläche fehlen, bevor der Mausmodus endet (kurzes Anheben beim Wischen).</summary>
    private const int LeaveMs = 400;
    /// <summary>Kleine Bewegungen beim Aufsetzen ignorieren, bis sie sich zu einer echten Bewegung summieren.</summary>
    private const int StartMotion = 6;

    private readonly bool _left;
    private readonly SmoothMouse _smooth = new(); // eigene Glättung je Joy-Con
    private OpticalMouse? _previous;
    private long _surfaceSince = -1, _offSince = -1;
    private int _startMotion;
    private double _scrollRest;
    /// <summary>Zurückgelegter Weg im Mausmodus (Sensor-Zählschritte), zum Einmessen der Empfindlichkeit.</summary>
    private long _sumX, _sumY;
    private bool _leftDown, _rightDown, _middleDown;

    public bool Active { get; private set; }
    /// <summary>Hat sich der Zeiger beim letzten Bericht bewegt? (Aktivität, nicht nur „liegt auf dem Tisch“)</summary>
    public bool Moved { get; private set; }

    public JoyConMouse(bool left) => _left = left;

    private ProButtons LeftClick => _left ? ProButtons.L : ProButtons.R;
    private ProButtons RightClick => _left ? ProButtons.ZL : ProButtons.ZR;
    private ProButtons MiddleClick => _left ? ProButtons.LeftStick : ProButtons.RightStick;

    /// <summary>
    /// Neuen Zustand verarbeiten. Gibt den Zustand zurück, der ans Spiel geht – im Mausmodus ohne die
    /// Maustasten und mit ruhendem Stick (er scrollt dann).
    /// </summary>
    public ControllerState Process(ControllerState state, Settings settings, StickCalibration stick)
    {
        Moved = false;
        if (!settings.JoyConMouse || state.Mouse is not { } m)
        {
            Stop();
            return state;
        }

        long now = Environment.TickCount64;
        // Beginnen nur, wenn der Joy-Con wirklich auf der Schienenkante steht: Die Schwerkraft zeigt dann entlang
        // der Geräteachse X (quer zur Schiene). In der Hand (Gesicht nach oben/vorn) ist das nie so – sonst hält der
        // Sensor eine Hand oder den Griff für eine Fläche, und L/R/ZL/ZR würden zu Mausklicks.
        bool standing = state.Motion is not { } a || IsStandingOnRail(a);
        if (m.OnSurface && (Active || standing))
        {
            _offSince = -1;
            if (_surfaceSince < 0)
                _surfaceSince = now;
            if (!Active && now - _surfaceSince >= EnterMs)
            {
                Active = true;
                _startMotion = 0;
                _previous = m;
                Log.Info($"Joy-Con ({(_left ? "L" : "R")}): Mausmodus an" +
                         (state.Motion is { } g ? $" (Lage X {g.AccelX}, Y {g.AccelY}, Z {g.AccelZ})" : ""));
            }
        }
        else
        {
            _surfaceSince = -1;
            if (Active)
            {
                if (_offSince < 0)
                    _offSince = now;
                if (now - _offSince >= LeaveMs)
                    Stop();
            }
        }

        if (!Active)
        {
            _previous = m;
            return state;
        }

        // Beim Anheben/Umdrehen kippt der Joy-Con, und der Sensor meldet noch kurz Scheinbewegungen:
        // Bewegung nur übernehmen, solange er auf der Fläche steht und kaum gekippt ist (< ~25°).
        bool steady = m.OnSurface && (state.Motion is not { } tilt || Math.Abs((int)tilt.AccelX) > 3700);
        if (!steady)
            _smooth.Reset(); // schon verteilte Reste der Scheinbewegung verwerfen
        // Deutlich gekippt (> ~50°): sofort wieder Controller, nicht erst nach der Wartezeit.
        if (state.Motion is { } t && Math.Abs((int)t.AccelX) < 2600)
        {
            Stop();
            _previous = m;
            return state;
        }

        // Bewegung (Zählerstände laufen über; Differenz mit Vorzeichen)
        if (_previous is { } p && steady)
        {
            int dx = OpticalMouse.Delta(p.X, m.X), dy = OpticalMouse.Delta(p.Y, m.Y);
            _sumX += dx;
            _sumY += dy;
            if (_startMotion < StartMotion)
            {
                _startMotion += Math.Abs(dx) + Math.Abs(dy);
                dx = dy = 0;
            }
            Moved = dx != 0 || dy != 0;
            float speed = settings.MouseSpeed;
            // Weich verteilt ausgeben (die Joy-Con melden sich nur alle 30–60 ms).
            _smooth.Add(dx * speed * (settings.MouseInvertX ? -1 : 1), dy * speed * (settings.MouseInvertY ? -1 : 1));
        }
        _previous = m;

        // Klicks
        Button(ref _leftDown, state.Has(LeftClick), WindowsInput.MouseButton.Left);
        Button(ref _rightDown, state.Has(RightClick), WindowsInput.MouseButton.Right);
        Button(ref _middleDown, state.Has(MiddleClick), WindowsInput.MouseButton.Middle);

        // Scrollen mit dem Stick (nur die Hochachse, wie am Mausrad)
        float y = _left ? stick.Y.Normalize(state.LeftY) : stick.Y.Normalize(state.RightY);
        if (steady && Math.Abs(y) > 0.25f)
        {
            _scrollRest += y * 12; // ~ eine Raste pro 10 Berichte bei Vollausschlag
            int wheel = (int)_scrollRest;
            _scrollRest -= wheel;
            WindowsInput.Wheel(wheel);
        }

        return state with
        {
            Buttons = state.Buttons & ~(LeftClick | RightClick | MiddleClick),
            LeftY = _left ? stick.Y.Neutral : state.LeftY,
            RightY = _left ? state.RightY : stick.Y.Neutral,
        };
    }

    /// <summary>Schwerkraft (4096 ≙ 1 g) überwiegend entlang X: Joy-Con steht auf der Schiene (Sensor unten).</summary>
    internal static bool IsStandingOnRail(Motion a)
    {
        int x = Math.Abs((int)a.AccelX), y = Math.Abs((int)a.AccelY), z = Math.Abs((int)a.AccelZ);
        return x > 2900 && x > 2 * y && x > 2 * z;
    }

    private static void Button(ref bool down, bool pressed, WindowsInput.MouseButton button)
    {
        if (pressed == down)
            return;
        down = pressed;
        WindowsInput.MouseButtonState(button, pressed);
    }

    /// <summary>Mausmodus beenden; gedrückte Maustasten loslassen, damit nichts „hängen“ bleibt.</summary>
    public void Stop()
    {
        if (Active)
            Log.Info($"Joy-Con ({(_left ? "L" : "R")}): Mausmodus aus (Weg: X {_sumX}, Y {_sumY} Zählschritte)");
        _sumX = _sumY = 0;
        Active = false;
        _surfaceSince = _offSince = -1;
        _scrollRest = 0;
        _smooth.Reset();
        if (_leftDown) Button(ref _leftDown, false, WindowsInput.MouseButton.Left);
        if (_rightDown) Button(ref _rightDown, false, WindowsInput.MouseButton.Right);
        if (_middleDown) Button(ref _middleDown, false, WindowsInput.MouseButton.Middle);
    }
}
