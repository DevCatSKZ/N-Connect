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

    /// <summary>Spielraum an Prozentgrenzen (Millivolt), gegen Flackern zwischen zwei Werten.</summary>
    private const int ToleranceMillivolts = 3;

    /// <summary>So lange nach dem Anstecken wird gemessen, bevor der Spannungssprung übernommen wird (Millisekunden).</summary>
    private const int MeasureMs = 10_000;

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
        else if (_plugTicks >= 0 && nowMs - _plugTicks >= MeasureMs)
        {
            // Einige Sekunden nach dem Anstecken (Ladespannung geglättet) den Sprung übernehmen. Vorsichtig: immer den
            // größten gemessenen Wert – ein zu großer schätzt beim Laden etwas zu niedrig (die nächste Ladepause
            // korrigiert nach oben), ein zu kleiner ließe die Anzeige erst hoch- und dann wieder herunterspringen.
            int measured = Math.Clamp((int)Math.Round(_millivolts - _resting), 0, 250);
            int offset = Math.Max(Math.Max(ChargeOffset ?? 0, DefaultChargeOffsetMillivolts(kind)), measured);
            _plugTicks = -1;
            if (offset != ChargeOffset)
            {
                ChargeOffset = offset;
                ChargeOffsetMeasured?.Invoke(offset);
            }
        }

        int offsetNow = charging ? Math.Max(ChargeOffset ?? 0, DefaultChargeOffsetMillivolts(kind)) : 0;
        int mv = (int)Math.Round(_millivolts) - offsetNow;
        int estimate = InputReports.BatteryPercentFromMillivolts(mv, kind);
        // Toleranz: liegt die Spannung genau an einer Prozentgrenze, soll die Anzeige nicht hin- und herspringen.
        int low = InputReports.BatteryPercentFromMillivolts(mv - ToleranceMillivolts, kind);
        int high = InputReports.BatteryPercentFromMillivolts(mv + ToleranceMillivolts, kind);
        if (_shown < 0)
            _shown = estimate;
        else if (unplugged)
        {
            // Laden beendet bzw. Ladepause: die Ruhespannung zeigt den echten Stand
            if (high < _shown || low > _shown)
                _shown = estimate;
        }
        else if (charging && _plugTicks >= 0)
        {
            // gerade angesteckt: Stand von vorher halten, bis der Spannungssprung gemessen ist
        }
        else if (charging)
            _shown = Math.Max(_shown, low); // lädt: nur steigen (beim Anstecken vom Stand davor aus)
        else if (high < _shown)
            _shown = high; // entlädt: nur sinken
        else if (low > _shown + 5)
            _shown = low; // deutlich höher nur nach dem Laden bzw. Akkuwechsel
        return _shown;
    }
}
