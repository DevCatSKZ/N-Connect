namespace Switch2Pro.Bridge;

internal static class Program
{
    /// <summary>Wird gesetzt, wenn die App ein zweites Mal gestartet wird (Fenster zeigen).</summary>
    public static EventWaitHandle? ShowSignal { get; private set; }

    [STAThread]
    private static void Main()
    {
        // Prüfhilfe: alle Controller-Grafiken als PNG speichern (ohne Controller, ohne Fenster).
        var args = Environment.GetCommandLineArgs();
        int render = Array.IndexOf(args, "--render");
        if (render >= 0 && render + 1 < args.Length)
        {
            ApplicationConfiguration.Initialize();
            RenderCheck.Run(args[render + 1]);
            return;
        }

        // Nur eine Instanz pro Benutzer: zwei Programme würden um denselben Controller streiten.
        using var mutex = new Mutex(true, @"Local\Switch2ProBridge", out bool first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Switch2ProBridge.Show");
        if (!first)
        {
            // Läuft schon: dort das Fenster öffnen, statt still zu beenden.
            showSignal.Set();
            return;
        }
        ShowSignal = showSignal;

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
