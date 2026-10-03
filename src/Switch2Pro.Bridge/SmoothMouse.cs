using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Switch2Pro.Bridge;

/// <summary>
/// Gibt die Mausbewegung eines Joy-Con weich aus: Die Controller melden sich per Bluetooth nur alle 30–60 ms.
/// Jede gemeldete Bewegung wird über einen Teil der Zeit bis zum nächsten Bericht verteilt (Schritte von ~2 ms),
/// damit der Zeiger flüssig läuft, ohne merklich nachzulaufen. Eine Instanz je Joy-Con (eigene Taktmessung);
/// der Ausgabe-Thread läuft nur, solange es etwas auszugeben gibt.
/// </summary>
internal sealed class SmoothMouse
{
    /// <summary>Anteil des Berichtsabstands, über den eine Bewegung verteilt wird (weniger = direkter).</summary>
    private const double Spread = 0.6;

    private readonly object _gate = new();
    private float _pendingX, _pendingY;
    private float _restX, _restY;
    private double _intervalMs = 30;
    private long _lastAdd;
    private bool _running;

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);

    /// <summary>Neue Bewegung (Bildpunkte, Bruchteile erlaubt) aus einem Controller-Bericht.</summary>
    public void Add(float dx, float dy)
    {
        long now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            if (_lastAdd != 0)
            {
                // Berichtsabstand gleitend messen (begrenzt auf 8–80 ms).
                double ms = Stopwatch.GetElapsedTime(_lastAdd, now).TotalMilliseconds;
                if (ms < 200)
                    _intervalMs = Math.Clamp(_intervalMs * 0.8 + ms * 0.2, 8, 80);
            }
            _lastAdd = now;
            _pendingX += dx;
            _pendingY += dy;
            if (!_running && (_pendingX != 0 || _pendingY != 0))
            {
                _running = true;
                new Thread(Run) { IsBackground = true, Name = "Maus glätten", Priority = ThreadPriority.AboveNormal }.Start();
            }
        }
    }

    private void Run()
    {
        timeBeginPeriod(1);
        try
        {
            long last = Stopwatch.GetTimestamp();
            long idleSince = 0;
            while (true)
            {
                Thread.Sleep(2);
                long now = Stopwatch.GetTimestamp();
                double dt = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds;
                last = now;
                int mx, my;
                lock (_gate)
                {
                    float share = (float)Math.Min(1.0, dt / (_intervalMs * Spread));
                    float ox = _pendingX * share, oy = _pendingY * share;
                    _pendingX -= ox;
                    _pendingY -= oy;
                    if (Math.Abs(_pendingX) < 0.05f) _pendingX = 0;
                    if (Math.Abs(_pendingY) < 0.05f) _pendingY = 0;
                    float fx = ox + _restX, fy = oy + _restY;
                    mx = (int)fx;
                    my = (int)fy;
                    _restX = fx - mx;
                    _restY = fy - my;

                    // Nichts mehr auszugeben: nach kurzer Ruhe den Thread beenden.
                    if (_pendingX == 0 && _pendingY == 0)
                    {
                        if (idleSince == 0)
                            idleSince = now;
                        else if (Stopwatch.GetElapsedTime(idleSince, now).TotalMilliseconds > 250)
                        {
                            _running = false;
                            return;
                        }
                    }
                    else
                    {
                        idleSince = 0;
                    }
                }
                if (mx != 0 || my != 0)
                    WindowsInput.MoveMouse(mx, my);
            }
        }
        finally
        {
            timeEndPeriod(1);
        }
    }

    /// <summary>Ausstehende Bewegung verwerfen (Mausmodus beendet).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _pendingX = _pendingY = _restX = _restY = 0;
            _lastAdd = 0;
        }
    }
}
