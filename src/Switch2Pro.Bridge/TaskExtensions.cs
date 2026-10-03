namespace Switch2Pro.Bridge;

internal static class TaskExtensions
{
    /// <summary>
    /// Task im Hintergrund laufen lassen, Fehler nur protokollieren. Liest die Ausnahme aus, damit sie nicht
    /// später als „unbeobachteter Task-Fehler“ auftaucht (ein leeres ContinueWith genügt dafür nicht).
    /// Abbruch und bereits getrennte Verbindungen sind dabei normal und werden nicht gemeldet.
    /// </summary>
    public static void Forget(this Task task, string what) =>
        task.ContinueWith(t =>
        {
            var e = t.Exception!.GetBaseException();
            if (e is not (OperationCanceledException or ObjectDisposedException))
                Log.Warn($"{what}: {e.Message}");
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
}
