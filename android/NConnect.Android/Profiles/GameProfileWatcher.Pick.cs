namespace NConnect.Android.Profiles;

/// <summary>Auswahl der App im Vordergrund – ohne Android-Abhängigkeiten (testbar in android/NConnect.Android.Tests).</summary>
public static partial class GameProfileWatcher
{
    /// <summary>
    /// Zuletzt fortgesetzte App aus den ActivityResumed-Ereignissen – ohne N-Connect selbst (<paramref name="own"/>) und
    /// ohne Startbildschirme: Wer kurz in N-Connect oder auf den Startbildschirm wechselt, behält sein Profil.
    /// </summary>
    internal static string? Pick(IEnumerable<(long Time, string Package)> resumed, string own, IReadOnlySet<string> launchers)
    {
        string? foreground = null;
        long best = long.MinValue;
        foreach (var (time, package) in resumed)
        {
            if (time < best || package == own || launchers.Contains(package))
                continue;
            best = time;
            foreground = package;
        }
        return foreground;
    }
}
