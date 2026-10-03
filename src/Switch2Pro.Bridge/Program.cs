namespace Switch2Pro.Bridge;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Nur eine Instanz pro Benutzer: zwei Programme würden um denselben Controller streiten.
        using var mutex = new Mutex(true, @"Local\Switch2ProBridge", out bool first);
        if (!first)
            return;

        Log.Info($"Start, Version {typeof(Program).Assembly.GetName().Version}, Windows {Environment.OSVersion.Version}");
        Application.ThreadException += (_, e) => Log.Error("Unbehandelter Fehler (UI)", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unbehandelter Fehler", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unbeobachteter Task-Fehler", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
        Log.Info("Ende");
    }
}
