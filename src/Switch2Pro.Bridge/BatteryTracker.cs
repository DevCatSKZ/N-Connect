using System.Collections.Concurrent;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Akkuschätzung je Controller (Schlüssel: Seriennummer, sonst Geräte-ID), gemeinsam für Bluetooth und USB – beim
/// Anstecken des Ladekabels wechselt der Controller die Verbindung, der Akkustand läuft trotzdem nahtlos weiter.
/// Schreibt einmal pro Minute Spannung und Stand ins Protokoll (zur Kontrolle der Kennlinie).
/// </summary>
internal static class BatteryTracker
{
    private sealed class Entry
    {
        public readonly BatteryEstimator Estimator = new();
        public long LastLog;
    }

    private static readonly ConcurrentDictionary<string, Entry> Entries = new();

    /// <summary>Zustand mit geschätztem Akkustand (Spannung aus dem Bericht) zurückgeben.</summary>
    public static ControllerState Apply(string key, ControllerState state)
    {
        if (state.BatteryMillivolts <= 0)
            return state;
        var entry = Entries.GetOrAdd(key, _ => new Entry());
        long now = Environment.TickCount64;
        int percent;
        lock (entry)
        {
            percent = entry.Estimator.Update(state.BatteryMillivolts, state.Charging, state.Kind, now);
            if (now - entry.LastLog >= 60_000)
            {
                entry.LastLog = now;
                Log.Info($"Akku {key}: {state.BatteryMillivolts} mV{(state.Charging ? ", lädt" : "")} → {percent} %");
            }
        }
        return state with { BatteryPercent = percent };
    }
}
