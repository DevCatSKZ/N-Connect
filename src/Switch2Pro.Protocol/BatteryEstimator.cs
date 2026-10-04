namespace Switch2Pro.Protocol;

/// <summary>
/// Akkustand aus der gemeldeten Spannung (Switch-2-Controller melden keine Prozent): geglättet, damit die Anzeige
/// nicht springt, und beim Laden korrigiert – am Ladekabel liegt die Spannung über der Ruhespannung des Akkus, die
/// reine Umrechnung zeigte dann sofort „voll“ an und erst nach dem Abstecken wieder den echten Stand.
/// Beim Laden steigt die Anzeige nur, ohne Kabel sinkt sie nur (kleine Messschwankungen bleiben unsichtbar).
/// Eine Instanz je Controller – sie überlebt den Wechsel zwischen Bluetooth und USB.
/// </summary>
public sealed class BatteryEstimator
{
    /// <summary>Zeitkonstante der Glättung (Millisekunden).</summary>
    private const double SmoothingMs = 4000;

    private double _millivolts;
    private long _lastTicks;
    private bool _charging;
    private int _shown = -1;

    /// <summary>Zuletzt angezeigter Stand in Prozent (−1 = noch unbekannt).</summary>
    public int Percent => _shown;

    /// <summary>Wie weit die Spannung beim Laden über der Ruhespannung liegt (geschätzt, je Controller-Art).</summary>
    public static int ChargeOffsetMillivolts(ControllerKind kind) =>
        kind is ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right ? 45 : 130;

    /// <summary>Neuen Messwert verarbeiten und den anzuzeigenden Stand liefern (−1 ohne Messwert).</summary>
    public int Update(int millivolts, bool charging, ControllerKind kind, long nowMs)
    {
        if (millivolts <= 0)
            return _shown;
        if (_lastTicks == 0 || charging != _charging)
        {
            // Erster Wert oder Kabel an/ab: die Spannung springt – neu ansetzen statt zu glätten.
            _millivolts = millivolts;
        }
        else
        {
            double dt = Math.Clamp(nowMs - _lastTicks, 0, 60_000);
            double alpha = 1 - Math.Exp(-dt / SmoothingMs);
            _millivolts += (millivolts - _millivolts) * alpha;
        }
        _lastTicks = nowMs;
        _charging = charging;

        int resting = (int)Math.Round(_millivolts) - (charging ? ChargeOffsetMillivolts(kind) : 0);
        int estimate = InputReports.BatteryPercentFromMillivolts(resting, kind);
        if (_shown < 0)
            _shown = estimate;
        else if (charging)
            _shown = Math.Max(_shown, estimate); // lädt: nur steigen (beim Anstecken vom Stand davor aus)
        else if (estimate < _shown || estimate > _shown + 5)
            _shown = estimate; // entlädt: nur sinken; deutlich höher nur nach dem Laden bzw. Akkuwechsel
        return _shown;
    }
}
