namespace Switch2Pro.Protocol;

/// <summary>
/// Akkustand aus der gemeldeten Spannung (Switch-2-Controller melden keine Prozent): geglättet, damit die Anzeige
/// nicht springt, und beim Laden korrigiert – am Ladekabel liegt die Spannung etwas über der Ruhespannung des Akkus.
/// Wie weit, misst der Schätzer beim Anstecken selbst (Spannung vorher/nachher) und merkt es sich je Controller.
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
    private double _resting; // geglättete Spannung ohne Kabel (für die Messung beim Anstecken)
    private long _plugTicks = -1; // Zeitpunkt des Ansteckens, solange der Spannungssprung noch gemessen wird

    public BatteryEstimator(int? chargeOffsetMillivolts = null) => ChargeOffset = chargeOffsetMillivolts;

    /// <summary>Zuletzt angezeigter Stand in Prozent (−1 = noch unbekannt).</summary>
    public int Percent => _shown;

    /// <summary>Gemessener Spannungssprung beim Laden (null = noch nicht gemessen, dann Standardwert).</summary>
    public int? ChargeOffset { get; private set; }

    /// <summary>Wird gesetzt, wenn der Spannungssprung neu gemessen wurde (zum Speichern).</summary>
    public event Action<int>? ChargeOffsetMeasured;

    /// <summary>Standard-Spannungssprung beim Laden, bis er gemessen ist (gemessen am Pro Controller 2: 20 mV).</summary>
    public static int DefaultChargeOffsetMillivolts(ControllerKind kind) =>
        kind is ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right ? 10 : 20;

    /// <summary>Neuen Messwert verarbeiten und den anzuzeigenden Stand liefern (−1 ohne Messwert).</summary>
    public int Update(int millivolts, bool charging, ControllerKind kind, long nowMs)
    {
        if (millivolts <= 0)
            return _shown;
        bool first = _lastTicks == 0;
        bool unplugged = !first && _charging && !charging;
        if (first || charging != _charging)
        {
            // Erster Wert oder Kabel an/ab: die Spannung springt – neu ansetzen statt zu glätten.
            if (!first && charging && _resting > 0)
                _plugTicks = nowMs; // angesteckt: Sprung gegenüber der Ruhespannung messen
            if (!charging)
                _plugTicks = -1;
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
        if (!charging)
            _resting = _millivolts;
        else if (_plugTicks >= 0 && nowMs - _plugTicks >= 3000)
        {
            // Ein paar Sekunden nach dem Anstecken hat sich die Ladespannung eingestellt: Sprung übernehmen.
            int offset = Math.Clamp((int)Math.Round(_millivolts - _resting), 0, 250);
            _plugTicks = -1;
            ChargeOffset = offset;
            ChargeOffsetMeasured?.Invoke(offset);
        }

        int offsetNow = charging ? ChargeOffset ?? DefaultChargeOffsetMillivolts(kind) : 0;
        int estimate = InputReports.BatteryPercentFromMillivolts((int)Math.Round(_millivolts) - offsetNow, kind);
        if (_shown < 0 || unplugged)
            _shown = estimate; // Laden beendet: Ruhespannung zeigt den echten Stand
        else if (charging && _plugTicks >= 0)
        {
            // gerade angesteckt: Stand von vorher halten, bis der Spannungssprung gemessen ist
        }
        else if (charging)
            _shown = Math.Max(_shown, estimate); // lädt: nur steigen (beim Anstecken vom Stand davor aus)
        else if (estimate < _shown || estimate > _shown + 5)
            _shown = estimate; // entlädt: nur sinken; deutlich höher nur nach dem Laden bzw. Akkuwechsel
        return _shown;
    }
}
