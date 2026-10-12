using Android.OS;

namespace NConnect.Android.Core;

/// <summary>
/// Hänger-Wächter: prüft jede halbe Sekunde, ob der Haupt-Thread noch Nachrichten abarbeitet. Steht er länger als
/// 2 s (Android meldet nach 5 s „App reagiert nicht“), wird seine Aufrufkette einmal ins Protokoll geschrieben – so
/// lässt sich die Ursache eines Hängers später genau finden (Release-Builds liefern in ANR-Berichten keine .NET-Stacks).
/// </summary>
public static class HangWatchdog
{
    private const int CheckMs = 1000;
    private const int StallMs = 2000;
    private static int _started;

    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;
        var main = new Handler(Looper.MainLooper!);
        var thread = new Thread(() => Run(main)) { IsBackground = true, Name = "Hänger-Wächter" };
        thread.Start();
    }

    private static void Run(Handler main)
    {
        long lastBeat = System.Environment.TickCount64;
        bool reported = false;
        while (true)
        {
            main.Post(() => Volatile.Write(ref lastBeat, System.Environment.TickCount64));
            Thread.Sleep(CheckMs);
            long stalled = System.Environment.TickCount64 - Volatile.Read(ref lastBeat);
            if (stalled < StallMs)
            {
                reported = false;
                continue;
            }
            if (reported)
                continue;
            reported = true;
            try
            {
                var frames = Looper.MainLooper!.Thread!.GetStackTrace() ?? [];
                Log.Warn($"Haupt-Thread hängt seit {stalled} ms – Aufrufkette:\n  " +
                         string.Join("\n  ", frames.Take(40).Select(f => f.ToString())));
            }
            catch (Exception e)
            {
                Log.Warn($"Haupt-Thread hängt seit {stalled} ms (Aufrufkette nicht lesbar: {e.Message})");
            }
        }
    }
}
