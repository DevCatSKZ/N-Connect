using System.Runtime.InteropServices;

namespace Switch2Pro.Bridge;

/// <summary>
/// Prüfhilfen (<c>--render-ui</c>, <c>--render</c>, <c>--dump-ui</c>) auf einem eigenen, unsichtbaren Windows-Desktop
/// ausführen: Der Prozess startet sich dort neu und wartet auf das Ergebnis. Auf diesem Desktop sieht der Benutzer kein
/// Fenster, Fokus und Maus bleiben unberührt – auch Programme wie Mouse without Borders bemerken nichts.
/// Ein Desktopwechsel innerhalb des laufenden Prozesses geht nicht (der Hauptthread hat durch COM schon ein Fenster).
/// </summary>
internal static class HiddenDesktop
{
    private const string Name = "N-Connect-Pruefhilfe";
    private const string Marker = "NCONNECT_PRUEFHILFE_DESKTOP";

    /// <summary>Läuft dieser Prozess bereits auf dem unsichtbaren Desktop?</summary>
    public static bool Active => Environment.GetEnvironmentVariable(Marker) == "1";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devMode, int flags, uint access, IntPtr attributes);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string? application, string commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory, ref StartupInfo startup,
        out ProcessInformation info);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>Argument für die Befehlszeile in Anführungszeichen setzen (Pfade mit Leerzeichen).</summary>
    private static string Quote(string arg) =>
        arg.Length > 0 && !arg.Contains(' ') && !arg.Contains('"') ? arg : "\"" + arg.Replace("\"", "\\\"") + "\"";

    /// <summary>
    /// Diesen Aufruf auf dem unsichtbaren Desktop wiederholen und dessen Exitcode liefern; null, wenn das nicht ging
    /// (dann läuft die Prüfhilfe wie bisher im eigenen Prozess, mit gesperrtem Fokuswechsel).
    /// </summary>
    public static int? RunIsolated()
    {
        if (Active || Environment.ProcessPath is not { } exe)
            return null;
        const uint GenericAll = 0x10000000;
        IntPtr desktop = CreateDesktop(Name, IntPtr.Zero, IntPtr.Zero, 0, GenericAll, IntPtr.Zero);
        if (desktop == IntPtr.Zero)
            return null;
        try
        {
            Environment.SetEnvironmentVariable(Marker, "1"); // erbt der neue Prozess
            var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>(), lpDesktop = Name };
            // Nicht Environment.CommandLine – das beginnt bei .NET-Apps mit der .dll statt der .exe.
            string commandLine = string.Join(" ", new[] { exe }.Concat(Environment.GetCommandLineArgs().Skip(1)).Select(Quote));
            if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero,
                    Environment.CurrentDirectory, ref startup, out var info))
                return null;
            try
            {
                WaitForSingleObject(info.hProcess, 0xFFFFFFFF);
                return GetExitCodeProcess(info.hProcess, out uint code) ? (int)code : 0;
            }
            finally
            {
                CloseHandle(info.hThread);
                CloseHandle(info.hProcess);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(Marker, null);
            CloseDesktop(desktop);
        }
    }
}
