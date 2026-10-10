namespace Switch2Pro.Bridge;

/// <summary>
/// Weicher Übergang eines Werts (Hover, Schalter, Akkubalken, Leuchten …) zu einem Ziel. Alle laufenden Übergänge teilen
/// sich einen Takt, der nur läuft, solange sich etwas bewegt; jeder Schritt zeichnet sein Steuerelement neu.
/// Ohne Animationseffekte (Windows-Einstellung, Prüfhilfe <c>--render…</c>) springt der Wert sofort ans Ziel.
/// </summary>
internal sealed class Anim
{
    private static readonly System.Windows.Forms.Timer Clock = new() { Interval = 15 };
    private static readonly List<Anim> Running = [];

    /// <summary>Animationen aus: Windows-Animationseffekte abgeschaltet oder Bildprüfung (soll Endzustände zeigen).</summary>
    public static bool Disabled { get; set; } =
        !SystemInformation.UIEffectsEnabled || Environment.GetCommandLineArgs().Any(a => a.StartsWith("--render", StringComparison.Ordinal));

    private readonly Control _owner;
    private readonly float _speed;
    private float _target;
    private long _last;

    /// <summary>Aktueller (gezeichneter) Wert.</summary>
    public float Value { get; private set; }

    /// <param name="speed">Wie schnell der Wert folgt (größer = schneller; 14 ≈ 150 ms, 3 ≈ 1 s).</param>
    public Anim(Control owner, float initial = 0, float speed = 14f)
    {
        _owner = owner;
        _speed = speed;
        Value = _target = initial;
    }

    static Anim() => Clock.Tick += (_, _) => Step();

    public float Target
    {
        get => _target;
        set
        {
            if (_target == value && Value == value)
                return;
            _target = value;
            if (Disabled || !_owner.IsHandleCreated || !_owner.Visible)
            {
                Snap(value);
                return;
            }
            if (!Running.Contains(this))
            {
                _last = Environment.TickCount64;
                Running.Add(this);
            }
            Clock.Start();
        }
    }

    /// <summary>Sofort auf einen Wert setzen (ohne Übergang), z. B. Startwert eines Aufleuchtens.</summary>
    public void Snap(float value)
    {
        Value = _target = value;
        Running.Remove(this);
        if (!_owner.IsDisposed)
            _owner.Invalidate();
    }

    private static void Step()
    {
        long now = Environment.TickCount64;
        foreach (var a in Running.ToArray())
        {
            if (a._owner.IsDisposed)
            {
                Running.Remove(a);
                continue;
            }
            float dt = Math.Min(0.1f, (now - a._last) / 1000f);
            a._last = now;
            a.Value += (a._target - a.Value) * (1f - MathF.Exp(-a._speed * dt));
            if (MathF.Abs(a._target - a.Value) < 0.004f)
            {
                a.Value = a._target;
                Running.Remove(a);
            }
            a._owner.Invalidate();
        }
        if (Running.Count == 0)
            Clock.Stop();
    }
}
