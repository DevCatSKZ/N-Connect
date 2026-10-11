using System.Runtime.InteropServices;
using NConnect.Linux;

// N-Connect für Linux – Aufruf:
//   nconnect [--data <Ordner>] [--adapter hci0]
//   nconnect --selftest [--data <Ordner>]   (Diagnose ohne Controller)
// <Ordner> enthält settings.json (gleiche Einstellungsdatei wie Windows/Android), status.json und nconnect.log.
// Ereignisse für das Kodi-Add-on erscheinen als JSON-Zeilen auf stdout, das Protokoll auf stderr.

string data = Arg("--data") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "nconnect");
Directory.CreateDirectory(data);
if (args.Contains("--selftest"))
    return await SelfTest.RunAsync(data);
if (args.Contains("--test-pad"))
    return await SelfTest.TestPadAsync(int.TryParse(Arg("--test-pad"), out int seconds) ? seconds : 30);
if (args.Contains("--version"))
{
    Console.WriteLine(typeof(Daemon).Assembly.GetName().Version?.ToString(3));
    return 0;
}
Log.UseFile(Path.Combine(data, "nconnect.log"));
Log.Info($"N-Connect für Linux {typeof(Daemon).Assembly.GetName().Version?.ToString(3)} startet ({RuntimeInformation.OSArchitecture}, Daten: {data})");

using var stop = new CancellationTokenSource();
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stop.Cancel(); });
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; stop.Cancel(); });

try
{
    await using var daemon = new Daemon(data, Arg("--adapter"));
    await daemon.RunAsync(stop.Token);
}
catch (OperationCanceledException)
{
}
catch (Exception e)
{
    Log.Error("Dienst beendet", e);
    return 1;
}
Log.Info("N-Connect beendet");
return 0;

string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
