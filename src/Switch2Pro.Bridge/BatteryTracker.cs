using System.Collections.Concurrent;
using System.Text.Json;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Akkuschätzung je Controller (Schlüssel: Seriennummer), gemeinsam für Bluetooth und USB – der Akkustand läuft beim
/// Anstecken/Abziehen des Ladekabels nahtlos weiter. Der beim Anstecken gemessene Spannungssprung wird je Controller
/// gespeichert (battery.json), damit die Anzeige auch nach einem Neustart mit Kabel gleich stimmt.
/// Schreibt alle 30 Sekunden Spannung und Stand ins Protokoll (zur Kontrolle).
/// </summary>
internal static class BatteryTracker
{
    private sealed class Entry(BatteryEstimator estimator)
    {
        public readonly BatteryEstimator Estimator = estimator;
        public long LastLog;
    }

    private static readonly ConcurrentDictionary<string, Entry> Entries = new();
    private static readonly string OffsetFile = Path.Combine(Paths.LogDir, "battery.json");
    private static readonly object FileLock = new();
    private static Dictionary<string, int>? _offsets;

    /// <summary>
    /// Zustand mit geschätztem Akkustand zurückgeben. Ohne Seriennummer (die ersten Berichte vor dem Auslesen der
    /// Gerätedaten) bleibt der Stand unbekannt, statt kurz einen ungenauen Wert zu zeigen.
    /// </summary>
    /// <param name="report">Roher Eingabebericht (für das Protokoll: Bytes rund um Spannung und Ladezustand).</param>
    public static ControllerState Apply(string? serial, ControllerState state, ReadOnlySpan<byte> report = default)
    {
        if (state.BatteryMillivolts <= 0)
            return state;
        if (string.IsNullOrEmpty(serial))
            return state with { BatteryPercent = -1 };
        var entry = Entries.GetOrAdd(serial, Create);
        long now = Environment.TickCount64;
        int percent;
        bool log;
        lock (entry)
        {
            percent = entry.Estimator.Update(state.BatteryMillivolts, state.Charging, state.Kind, now);
            log = now - entry.LastLog >= 30_000;
            if (log)
                entry.LastLog = now;
        }
        if (log)
        {
            string raw = report.Length > 0x30 ? $" [1C–2F: {Convert.ToHexString(report[0x1C..0x30])}]" : "";
            Log.Info($"Akku {serial}: {state.BatteryMillivolts} mV{(state.Charging ? ", lädt" : "")} → {percent} %{raw}");
        }
        return state with { BatteryPercent = percent };
    }

    private static Entry Create(string serial)
    {
        int? saved;
        lock (FileLock)
            saved = Offsets().TryGetValue(serial, out int o) ? o : null;
        var estimator = new BatteryEstimator(saved);
        estimator.ChargeOffsetMeasured += offset => SaveOffset(serial, offset);
        return new Entry(estimator);
    }

    private static Dictionary<string, int> Offsets()
    {
        if (_offsets is not null)
            return _offsets;
        try
        {
            _offsets = File.Exists(OffsetFile)
                ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(OffsetFile)) ?? []
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _offsets = [];
        }
        return _offsets;
    }

    private static void SaveOffset(string serial, int offset)
    {
        Log.Info($"Akku {serial}: Spannung beim Laden {offset} mV über der Ruhespannung (gemessen)");
        lock (FileLock)
        {
            Offsets()[serial] = offset;
            try
            {
                File.WriteAllText(OffsetFile, JsonSerializer.Serialize(_offsets));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Akku-Messwerte speichern: {e.Message}");
            }
        }
    }
}
