namespace Switch2Pro.Bridge;

internal static class Program
{
    /// <summary>Wird gesetzt, wenn die App ein zweites Mal gestartet wird (Fenster zeigen).</summary>
    public static EventWaitHandle? ShowSignal { get; private set; }

    [STAThread]
    private static void Main()
    {
        var args = Environment.GetCommandLineArgs();
        // Geplante Aufgabe des Installers (als SYSTEM): Controller per HidHide verstecken, sonst nichts. Muss als Erstes
        // geprüft werden, damit weitere Argumente in diesem Aufruf nichts anderes auslösen.
        if (args.Contains("--hidhide-helper", StringComparer.OrdinalIgnoreCase))
        {
            Environment.Exit(HidHide.RunHelper(args));
            return;
        }
        // Logo, Programm-Icon und Installer-Grafiken erzeugen.
        int brand = Array.IndexOf(args, "--render-brand");
        if (brand >= 0 && brand + 1 < args.Length)
        {
            Branding.RenderAll(args[brand + 1]);
            return;
        }
        // Prüfhilfe: alle Controller-Grafiken als PNG speichern (ohne Controller, ohne Fenster).
        int render = Array.IndexOf(args, "--render");
        if (render >= 0 && render + 1 < args.Length)
        {
            ApplicationConfiguration.Initialize();
            Theme.Init(args.Contains("--light") ? "light" : "dark", transparency: false);
            RenderCheck.Run(args[render + 1]);
            return;
        }
        int renderUi = Array.IndexOf(args, "--render-ui");
        if (renderUi >= 0 && renderUi + 1 < args.Length)
        {
            ApplicationConfiguration.Initialize();
            Tr.Init(args.Contains("--en") ? "en" : "de");
            Theme.Init(args.Contains("--light") ? "light" : "dark", transparency: false);
            RenderCheck.RenderUi(args[renderUi + 1]);
            return;
        }
        // Prüfhilfe für die Übersetzung: alle Texte der Fenster ausgeben (und welche noch nicht übersetzt sind).
        int dump = Array.IndexOf(args, "--dump-ui");
        if (dump >= 0 && dump + 1 < args.Length)
        {
            ApplicationConfiguration.Initialize();
            Tr.Init(args.Contains("--en") ? "en" : "de");
            Theme.Init(args.Contains("--light") ? "light" : "dark", transparency: false);
            RenderCheck.DumpTexts(args[dump + 1]);
            return;
        }

        // Nur eine Instanz pro Benutzer: zwei Programme würden um denselben Controller streiten.
        using var mutex = new Mutex(true, @"Local\N-Connect", out bool first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\N-Connect.Show");
        if (!first)
        {
            // Läuft schon: dort das Fenster öffnen, statt still zu beenden.
            showSignal.Set();
            return;
        }
        ShowSignal = showSignal;
        Autostart.MigrateLegacy();

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
