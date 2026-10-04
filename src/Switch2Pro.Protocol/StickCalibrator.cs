namespace Switch2Pro.Protocol;

/// <summary>
/// Geführte Stick-Kalibrierung (gegen Drift und zu kleinen/großen Ausschlag) aus Rohwerten (12 Bit):
/// 1. Mitte – Stick loslassen, bis die Werte <see cref="CenterMs"/> lang ruhig sind (Mittelwert = neue Mitte).
/// 2. Rand – Stick am Rand im Kreis drehen, bis alle <see cref="Sectors"/> Richtungen erfasst sind.
/// Ergebnis: Mitte und Ausschlag je Richtung (leicht nach innen gesetzt, damit der volle Ausschlag sicher erreicht
/// wird) und die Rundheit (mittlere Abweichung des Randes vom Kreis). In den Controller wird nichts geschrieben.
/// </summary>
public sealed class StickCalibrator
{
    public enum Phase { Center, Rotate, Done }

    public const int CenterMs = 1500;
    public const int Sectors = 24;
    /// <summary>Höchste erlaubte Streuung (Rohwert) beim Loslassen – sonst wird die Mitte neu gemessen.</summary>
    private const int CenterSpread = 60;
    /// <summary>Ab diesem Abstand von der Mitte (Rohwert) zählt eine Richtung als „am Rand“ erfasst.</summary>
    private const int EdgeDistance = 700;
    /// <summary>Kleinster gültiger Ausschlag je Richtung (Rohwert).</summary>
    private const int MinRange = 400;
    /// <summary>Ausschlag leicht nach innen setzen, damit der Rand sicher den vollen Ausschlag liefert.</summary>
    private const float Margin = 0.95f;

    private readonly List<(int X, int Y)> _centerSamples = [];
    private long _centerStart = -1;
    private int _cx, _cy;
    private int _minX, _maxX, _minY, _maxY;
    private readonly (int X, int Y)?[] _edge = new (int, int)?[Sectors];

    public Phase Current { get; private set; } = Phase.Center;

    /// <summary>Fortschritt der aktuellen Phase (0–1).</summary>
    public float Progress { get; private set; }

    /// <summary>Neuer Rohwert. Liefert true, wenn sich die Phase geändert hat.</summary>
    public bool Add(int x, int y, long nowMs)
    {
        switch (Current)
        {
            case Phase.Center:
                if (_centerStart < 0 || _centerSamples.Count > 0
                    && (Math.Abs(x - _centerSamples[0].X) > CenterSpread || Math.Abs(y - _centerSamples[0].Y) > CenterSpread))
                {
                    // Erster Wert oder Stick bewegt: von vorn.
                    _centerSamples.Clear();
                    _centerStart = nowMs;
                }
                _centerSamples.Add((x, y));
                Progress = Math.Clamp((nowMs - _centerStart) / (float)CenterMs, 0f, 1f);
                if (nowMs - _centerStart < CenterMs || _centerSamples.Count < 10)
                    return false;
                _cx = (int)Math.Round(_centerSamples.Average(s => s.X));
                _cy = (int)Math.Round(_centerSamples.Average(s => s.Y));
                _minX = _maxX = _cx;
                _minY = _maxY = _cy;
                Current = Phase.Rotate;
                Progress = 0;
                return true;

            case Phase.Rotate:
                _minX = Math.Min(_minX, x);
                _maxX = Math.Max(_maxX, x);
                _minY = Math.Min(_minY, y);
                _maxY = Math.Max(_maxY, y);
                int dx = x - _cx, dy = y - _cy;
                if (dx * dx + dy * dy >= EdgeDistance * EdgeDistance)
                {
                    int sector = SectorOf(dx, dy);
                    if (_edge[sector] is not { } e || e.X * e.X + e.Y * e.Y < dx * dx + dy * dy)
                        _edge[sector] = (dx, dy);
                }
                Progress = _edge.Count(e => e is not null) / (float)Sectors;
                if (Progress < 1f)
                    return false;
                Current = Phase.Done;
                return true;

            default:
                return false;
        }
    }

    private static int SectorOf(int dx, int dy)
    {
        double angle = Math.Atan2(dy, dx); // −π … π
        int sector = (int)Math.Floor((angle + Math.PI) / (2 * Math.PI) * Sectors);
        return Math.Clamp(sector, 0, Sectors - 1);
    }

    /// <summary>Erfasste Randpunkte relativ zur Mitte (für die Anzeige).</summary>
    public IEnumerable<(int X, int Y)> EdgePoints => _edge.Where(e => e is not null).Select(e => e!.Value);

    /// <summary>Gemessene Mitte (nach Phase 1).</summary>
    public (int X, int Y) Center => (_cx, _cy);

    /// <summary>Ergebnis; null, solange nicht fertig oder wenn ein Ausschlag zu klein ist (Stick nicht bis zum Rand).</summary>
    public StickCalibration? Result
    {
        get
        {
            if (Current != Phase.Done)
                return null;
            int maxX = _maxX - _cx, minX = _cx - _minX, maxY = _maxY - _cy, minY = _cy - _minY;
            if (Math.Min(Math.Min(maxX, minX), Math.Min(maxY, minY)) < MinRange)
                return null;
            return new StickCalibration(
                new AxisCalibration(_cx, (int)(maxX * Margin), (int)(minX * Margin)),
                new AxisCalibration(_cy, (int)(maxY * Margin), (int)(minY * Margin)));
        }
    }

    /// <summary>
    /// Rundheit mit einer Kalibrierung: mittlere Abweichung des Randes vom Einheitskreis in Prozent
    /// (0 = perfekt rund). Gemessen über die erfassten Randpunkte.
    /// </summary>
    public float RoundnessError(StickCalibration calibration)
    {
        var points = EdgePoints.ToList();
        if (points.Count == 0)
            return 0;
        double sum = 0;
        foreach (var (dx, dy) in points)
        {
            float nx = calibration.X.Normalize(_cx + dx), ny = calibration.Y.Normalize(_cy + dy);
            sum += Math.Abs(Math.Sqrt(nx * nx + ny * ny) - 1);
        }
        return (float)(sum / points.Count * 100);
    }
}
