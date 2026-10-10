using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Desktop-Modus: Läuft kein Spiel, steuert der Controller Windows – wie in Steam. Aktiv nur, wenn eingeschaltet
/// (<see cref="Settings.DesktopMode"/>, Standard aus), kein Vollbildprogramm im Vordergrund ist und kein Programm mit
/// eigenem Profil. Back + Start eine Sekunde halten pausiert bzw. setzt ihn fort.
/// Belegung nach Xbox-Lage (gilt auch nach Umbelegung, da hinter der Tastenbelegung):
/// linker Stick = Zeiger, rechter Stick = Scrollen, A = Linksklick (halten = ziehen), B = Rechtsklick, X = Taskansicht,
/// Y = Bildschirmtastatur, Steuerkreuz = Pfeiltasten, LB/RB = Zurück/Vorwärts, Start = Enter, Back = Esc,
/// HOME = Startmenü, rechter Stick drücken = Mittelklick, RT halten = präziser Zeiger.
/// </summary>
internal sealed class DesktopControl
{
    // ---------- Wann aktiv? (für alle Spieler gemeinsam) ----------

    /// <summary>Per Back + Start pausiert (gilt bis zum erneuten Umschalten).</summary>
    public static bool Suspended { get; private set; }

    private static long _checkedAt = -1;
    private static bool _fullscreen;

    /// <summary>Desktop-Steuerung möglich (eingeschaltet, kein Spiel im Vordergrund)? Pausiert zählt hier noch als
    /// verfügbar, damit Back + Start sie wieder einschalten kann. Vordergrund höchstens alle 300 ms prüfen.</summary>
    public static bool IsAvailable(Settings settings)
    {
        if (!settings.DesktopMode || settings.DetectedProfile is not null)
            return false;
        long now = Environment.TickCount64;
        if (_checkedAt < 0 || now - _checkedAt > 300)
        {
            _fullscreen = ForegroundIsFullscreen();
            _checkedAt = now;
        }
        return !_fullscreen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int max);

    /// <summary>
    /// Vollbildprogramm im Vordergrund (Spiel, auch randloses Vollbild)? Desktop und Taskleiste zählen nicht.
    /// Ein Video im Vollbild gilt ebenfalls als „Spiel“ – lieber zu vorsichtig als Mausbewegungen im Spiel.
    /// </summary>
    private static bool ForegroundIsFullscreen()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero)
            return false;
        var name = new StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;
        if (!GetWindowRect(window, out var r))
            return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, 2 /* MONITOR_DEFAULTTONEAREST */), ref info))
            return false;
        var m = info.Monitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    // ---------- Steuerung je Spieler ----------

    private readonly Action<string> _notify;
    private XButtons _previous;
    private long _lastTicks;
    private float _moveX, _moveY, _scrollX, _scrollY;
    private bool _leftDown, _rightDown, _middleDown;
    private long _comboSince = -1;
    private bool _comboUsed;
    private readonly Dictionary<XButtons, long> _repeatAt = [];

    public DesktopControl(Action<string> notify) => _notify = notify;

    private const ushort VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28, VkEnter = 0x0D, VkEscape = 0x1B,
        VkWin = 0x5B, VkTab = 0x09, VkAlt = 0x12;

    /// <summary>Einen Bericht verarbeiten (Xbox-Abbild nach der Tastenbelegung). true = Eingabe für den Desktop
    /// verbraucht (ans Spiel geht Neutralstellung), false = pausiert, Eingabe geht ans Spiel.</summary>
    public bool Process(GamepadState g, Settings settings)
    {
        long now = Environment.TickCount64;
        float dt = _lastTicks == 0 ? 0f : Math.Clamp((now - _lastTicks) / 1000f, 0f, 0.1f);
        _lastTicks = now;
        var held = g.Buttons;
        XButtons pressed = held & ~_previous, released = _previous & ~held;

        // Back + Start eine Sekunde: pausieren / fortsetzen (Esc und Enter werden dabei nicht ausgelöst).
        bool combo = held.HasFlag(XButtons.Back) && held.HasFlag(XButtons.Start);
        if (combo)
        {
            if (_comboSince < 0)
                _comboSince = now;
            _comboUsed = true;
            if (now - _comboSince >= 1000)
            {
                _comboSince = long.MaxValue; // nur einmal je Halten
                Toggle();
            }
        }
        else
        {
            _comboSince = -1;
        }
        if (Suspended)
        {
            Release();
            _previous = held;
            return false;
        }

        // Zeiger: linker Stick mit Kennlinie (fein in der Mitte, schnell am Rand), RT hält ihn langsam.
        float speed = 1400f * settings.DesktopPointerSpeed / 100f * UiScale.Factor;
        if (g.RightTrigger > 60)
            speed *= 0.3f;
        _moveX += Curve(g.LeftX) * speed * dt;
        _moveY -= Curve(g.LeftY) * speed * dt; // Stick oben = Bildschirm nach oben
        int mx = (int)_moveX, my = (int)_moveY;
        _moveX -= mx;
        _moveY -= my;
        WindowsInput.MoveMouse(mx, my);

        // Scrollen: rechter Stick, bis zu ~15 Rasten pro Sekunde.
        _scrollY += Curve(g.RightY) * 15f * 120f * dt;
        _scrollX += Curve(g.RightX) * 15f * 120f * dt;
        int sy = (int)_scrollY, sx = (int)_scrollX;
        _scrollY -= sy;
        _scrollX -= sx;
        WindowsInput.Wheel(sy);
        WindowsInput.HorizontalWheel(sx);

        // Maustasten: A links (halten = ziehen), B rechts, rechter Stick drücken = Mitte.
        SetButton(ref _leftDown, held.HasFlag(XButtons.A), WindowsInput.MouseButton.Left);
        SetButton(ref _rightDown, held.HasFlag(XButtons.B), WindowsInput.MouseButton.Right);
        SetButton(ref _middleDown, held.HasFlag(XButtons.RS), WindowsInput.MouseButton.Middle);

        // Einmalige Aktionen beim Drücken.
        if (pressed.HasFlag(XButtons.X))
            Tap(VkWin, VkTab);              // Taskansicht
        if (pressed.HasFlag(XButtons.Y))
            ToggleOnScreenKeyboard();
        if (pressed.HasFlag(XButtons.Guide))
            Tap(VkWin);                     // Startmenü
        if (pressed.HasFlag(XButtons.LB))
            Tap(VkAlt, VkLeft);             // Zurück
        if (pressed.HasFlag(XButtons.RB))
            Tap(VkAlt, VkRight);            // Vorwärts
        // Start/Back beim Loslassen – sonst würde Back + Start (Pause) vorher Esc/Enter auslösen.
        if (released.HasFlag(XButtons.Start) && !_comboUsed)
            Tap(VkEnter);
        if (released.HasFlag(XButtons.Back) && !_comboUsed)
            Tap(VkEscape);
        if (!held.HasFlag(XButtons.Start) && !held.HasFlag(XButtons.Back))
            _comboUsed = false;

        // Steuerkreuz = Pfeiltasten mit Wiederholung beim Halten (eingespeiste Tasten wiederholt Windows nicht selbst).
        Arrow(held, XButtons.Up, VkUp, now);
        Arrow(held, XButtons.Down, VkDown, now);
        Arrow(held, XButtons.Left, VkLeft, now);
        Arrow(held, XButtons.Right, VkRight, now);

        _previous = held;
        return true;
    }

    /// <summary>Alles loslassen (beim Wechsel ins Spiel oder Pausieren), damit keine Taste „hängen“ bleibt.</summary>
    public void Release()
    {
        SetButton(ref _leftDown, false, WindowsInput.MouseButton.Left);
        SetButton(ref _rightDown, false, WindowsInput.MouseButton.Right);
        SetButton(ref _middleDown, false, WindowsInput.MouseButton.Middle);
        foreach (var key in _repeatAt.Keys.ToList())
        {
            WindowsInput.Combo([ArrowKey(key)], down: false);
            _repeatAt.Remove(key);
        }
        _moveX = _moveY = _scrollX = _scrollY = 0;
        _lastTicks = 0;
    }

    /// <summary>Nicht mehr verfügbar (Spiel im Vordergrund, ausgeschaltet): loslassen und Tastenzustand vergessen.</summary>
    public void Leave()
    {
        Release();
        _previous = XButtons.None;
        _comboSince = -1;
        _comboUsed = false;
    }

    private void Toggle()
    {
        Suspended = !Suspended;
        Log.Info(Suspended ? "Desktop-Modus pausiert (Back + Start)" : "Desktop-Modus fortgesetzt (Back + Start)");
        _notify(Suspended ? "Desktop-Steuerung pausiert – Back + Start halten zum Fortsetzen."
            : "Desktop-Steuerung aktiv – der Controller steuert Maus und Tastatur.");
    }

    /// <summary>Stickwert (−32768…32767) → −1…1 mit Totzone und Kennlinie.</summary>
    private static float Curve(short raw)
    {
        float v = raw / 32767f;
        float a = MathF.Abs(v);
        const float dead = 0.15f;
        if (a < dead)
            return 0f;
        float t = Math.Min(1f, (a - dead) / (1f - dead));
        return MathF.Sign(v) * MathF.Pow(t, 2.2f);
    }

    private static void SetButton(ref bool down, bool want, WindowsInput.MouseButton button)
    {
        if (down == want)
            return;
        down = want;
        WindowsInput.MouseButtonState(button, want);
    }

    private static void Tap(params ushort[] keys)
    {
        WindowsInput.Combo(keys, down: true);
        WindowsInput.Combo(keys, down: false);
    }

    private static ushort ArrowKey(XButtons b) => b switch
    {
        XButtons.Up => VkUp, XButtons.Down => VkDown, XButtons.Left => VkLeft, _ => VkRight,
    };

    private void Arrow(XButtons held, XButtons button, ushort vk, long now)
    {
        if (held.HasFlag(button))
        {
            if (!_repeatAt.TryGetValue(button, out long next))
            {
                WindowsInput.Combo([vk], down: true);
                _repeatAt[button] = now + 400; // erste Wiederholung nach 0,4 s
            }
            else if (now >= next)
            {
                WindowsInput.Combo([vk], down: true);
                _repeatAt[button] = now + 60;
            }
        }
        else if (_repeatAt.Remove(button))
        {
            WindowsInput.Combo([vk], down: false);
        }
    }

    /// <summary>Bildschirmtastatur von Windows öffnen bzw. wieder schließen.</summary>
    private static void ToggleOnScreenKeyboard()
    {
        try
        {
            var running = System.Diagnostics.Process.GetProcessesByName("osk");
            if (running.Length > 0)
            {
                foreach (var p in running)
                    using (p)
                        p.CloseMainWindow();
                return;
            }
            System.Diagnostics.Process.Start(new ProcessStartInfo("osk.exe") { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Bildschirmtastatur: {Log.Reason(e)}");
        }
    }
}
