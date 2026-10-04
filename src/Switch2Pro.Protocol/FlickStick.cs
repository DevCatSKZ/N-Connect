namespace Switch2Pro.Protocol;

/// <summary>
/// Flick-Stick nach JoyShockMapper (FLICK_STICK): Wird der Stick über den Rand geschoben, dreht sich die Kamera
/// sofort um den Winkel, in den er zeigt (oben = 0°, rechts = +90°) – verteilt über <see cref="Settings.FlickTime"/>.
/// Bleibt er am Rand und wird gedreht, dreht die Kamera um denselben Winkel mit („Stick-Rotation“). Zurück in die
/// Mitte: nichts. Liefert Drehwinkel in Grad (positiv = nach rechts); die Umrechnung in Mausbewegung macht der Aufrufer.
/// </summary>
public sealed class FlickStick
{
    /// <summary>Ab diesem Ausschlag beginnt ein Flick (JoyShockMapper: äußere Totzone 0,1).</summary>
    public const float Threshold = 0.9f;
    /// <summary>Unter diesem Ausschlag gilt der Stick als losgelassen (Hysterese gegen Flattern am Rand).</summary>
    public const float Release = 0.75f;

    private bool _active;
    private float _lastAngle;
    private float _flickTotal, _flickDone, _flickElapsed;

    /// <summary>Läuft gerade ein Flick oder ist der Stick am Rand?</summary>
    public bool Active => _active || _flickDone != _flickTotal;

    /// <summary>Einen Zeitschritt rechnen: Stick −1…1 (y oben positiv), <paramref name="dt"/> in Sekunden.</summary>
    public float Update(float x, float y, float dt, float flickTime)
    {
        float output = 0f;
        float magnitude = MathF.Sqrt(x * x + y * y);
        if (magnitude >= Threshold)
        {
            float angle = MathF.Atan2(x, y) * 180f / MathF.PI;
            if (!_active)
            {
                // Neuer Flick: Rest eines laufenden Flicks sofort ausgeben, dann zur Stick-Richtung drehen.
                output += _flickTotal - _flickDone;
                _flickTotal = angle;
                _flickDone = 0f;
                _flickElapsed = 0f;
                _active = true;
            }
            else
            {
                output += Wrap(angle - _lastAngle); // Stick-Rotation
            }
            _lastAngle = angle;
        }
        else if (magnitude < Release)
        {
            _active = false;
        }

        if (_flickDone != _flickTotal)
        {
            _flickElapsed += Math.Max(0f, dt);
            float progress = flickTime <= 0f ? 1f : Math.Clamp(_flickElapsed / flickTime, 0f, 1f);
            float target = _flickTotal * progress;
            output += target - _flickDone;
            _flickDone = progress >= 1f ? _flickTotal : target;
        }
        return output;
    }

    /// <summary>Zustand verwerfen (Flick-Stick ausgeschaltet, Controller getrennt).</summary>
    public void Reset()
    {
        _active = false;
        _flickTotal = _flickDone = _flickElapsed = 0f;
    }

    /// <summary>Winkel auf −180…180° bringen (Drehung über die Rückseite des Sticks).</summary>
    private static float Wrap(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f) degrees -= 360f;
        if (degrees < -180f) degrees += 360f;
        return degrees;
    }
}
