using Android.Content.PM;
using Android.Runtime;
using Java.Lang;
using Java.Lang.Reflect;
using Exception = System.Exception;
using Process = Java.Lang.Process;
using String = Java.Lang.String;

namespace SkzConnect.Android.Output;

/// <summary>
/// Startet Befehle mit erhöhten Rechten. Beide Wege enden beim selben Ziel: dem Android-Shell-Befehl <c>hid</c>,
/// der ein virtuelles HID-Gerät anlegt (braucht die Gruppe „uhid“, die normale Apps nicht haben).
/// </summary>
public interface IPrivilegedShell
{
    string Name { get; }

    /// <summary>Grundsätzlich vorhanden (Root installiert bzw. Shizuku läuft)?</summary>
    bool IsAvailable();

    /// <summary>Hat SKZ Connect die Freigabe (Root gewährt bzw. Shizuku erlaubt)?</summary>
    bool HasPermission();

    /// <summary>Freigabe anfragen (zeigt den Root- bzw. Shizuku-Dialog). true = erteilt.</summary>
    Task<bool> RequestPermissionAsync(CancellationToken ct = default);

    /// <summary>Startet <paramref name="command"/> mit erhöhten Rechten; stdin/stdout des Prozesses sind nutzbar.</summary>
    Process Start(params string[] command);
}

/// <summary>Root über <c>su</c> (Magisk, KernelSU, APatch …).</summary>
public sealed class RootShell : IPrivilegedShell
{
    public static readonly RootShell Instance = new();
    private static readonly string[] SuPaths =
        ["/system/bin/su", "/system/xbin/su", "/sbin/su", "/su/bin/su", "/data/adb/ksu/bin/su", "/data/adb/ap/bin/su",
         "/system/sbin/su", "/vendor/bin/su"];

    private bool _granted;

    public string Name => "Root";

    public bool IsAvailable() => SuPaths.Any(File.Exists) || CanRun("su", "-v");

    public bool HasPermission() => _granted;

    public Task<bool> RequestPermissionAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        try
        {
            var p = Runtime.GetRuntime()!.Exec(["su", "-c", "id"])!;
            string output = ReadAll(p.InputStream!);
            p.WaitFor();
            _granted = output.Contains("uid=0");
            Log.Info($"Root: {(_granted ? "gewährt" : "abgelehnt")} ({output.Trim()})");
        }
        catch (Exception e)
        {
            Log.Warn($"Root nicht verfügbar: {Log.Reason(e)}");
            _granted = false;
        }
        return _granted;
    }, ct);

    public Process Start(params string[] command) =>
        Runtime.GetRuntime()!.Exec(["su", "-c", string.Join(' ', command)])!;

    private static bool CanRun(params string[] command)
    {
        try
        {
            var p = Runtime.GetRuntime()!.Exec(command)!;
            return p.WaitFor() == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static string ReadAll(System.IO.Stream stream)
    {
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Shizuku (https://shizuku.rikka.app): gibt Apps Shell-Rechte ohne Root. Der Nutzer startet Shizuku einmal über
/// „Kabelloses Debugging“ (nach jedem Neustart erneut). Angesprochen per Reflection, weil wir nur wenige Methoden
/// brauchen; <c>newProcess</c> ist in API 13 nicht öffentlich, aber vorhanden.
/// </summary>
public sealed class ShizukuShell : IPrivilegedShell
{
    public static readonly ShizukuShell Instance = new();
    public const string PackageName = "moe.shizuku.privileged.api";
    private const int RequestCode = 4711;

    public string Name => "Shizuku";

    private static Class? _shizukuClass;

    /// <summary>
    /// Shizuku-Klasse über den Klassenlader der APP laden: Auf Hintergrund-Threads nimmt Class.forName sonst den
    /// System-Klassenlader und findet die App-Klassen nicht (ClassNotFoundException).
    /// </summary>
    private static Class? ShizukuClass
    {
        get
        {
            if (_shizukuClass is not null)
                return _shizukuClass;
            try
            {
                return _shizukuClass = Class.ForName("rikka.shizuku.Shizuku", true,
                    global::Android.App.Application.Context.ClassLoader);
            }
            catch (Exception e)
            {
                Log.Error("Shizuku-Bibliothek fehlt in der App", e);
                return null;
            }
        }
    }

    /// <summary>Ist die Shizuku-App installiert?</summary>
    public static bool IsInstalled()
    {
        try
        {
            global::Android.App.Application.Context.PackageManager!.GetPackageInfo(PackageName, 0);
            return true;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Läuft der Shizuku-Dienst (Binder erreichbar)?</summary>
    public bool IsAvailable()
    {
        try
        {
            return CallStatic<Java.Lang.Boolean>("pingBinder")?.BooleanValue() == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool HasPermission()
    {
        try
        {
            return IsAvailable() && CallStatic<Integer>("checkSelfPermission")?.IntValue() == (int)Permission.Granted;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> RequestPermissionAsync(CancellationToken ct = default)
    {
        if (!IsAvailable())
            return false;
        if (HasPermission())
            return true;
        try
        {
            var m = ShizukuClass!.GetMethod("requestPermission", Integer.Type!)!;
            m.Invoke(null, Integer.ValueOf(RequestCode));
        }
        catch (Exception e)
        {
            Log.Error("Shizuku-Freigabe anfragen", e);
            return false;
        }
        // Ergebnis kommt als Rückruf; einfacher und robust: kurz abfragen, bis der Nutzer entschieden hat.
        for (int i = 0; i < 120 && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(500, ct).ConfigureAwait(false);
            if (HasPermission())
            {
                Log.Info("Shizuku: Freigabe erteilt");
                return true;
            }
        }
        Log.Warn("Shizuku: keine Freigabe");
        return false;
    }

    public Process Start(params string[] command)
    {
        var cls = ShizukuClass ?? throw new InvalidOperationException("Shizuku-Bibliothek fehlt.");
        var stringClass = Class.ForName("java.lang.String");
        var arrayClass = Class.ForName("[Ljava.lang.String;");
        Method m = cls.GetDeclaredMethod("newProcess", arrayClass, arrayClass, stringClass)!;
        m.Accessible = true;
        var cmd = Java.Lang.Reflect.Array.NewInstance(stringClass, command.Length)!;
        for (int i = 0; i < command.Length; i++)
            Java.Lang.Reflect.Array.Set(cmd, i, new String(command[i]));
        // Umgebung und Arbeitsverzeichnis: null = wie Shizuku (Java erwartet hier ausdrücklich null).
        var process = m.Invoke(null, cmd, null!, null!) ?? throw new InvalidOperationException("Shizuku hat keinen Prozess gestartet.");
        return process.JavaCast<Process>()!;
    }

    private static T? CallStatic<T>(string method) where T : Java.Lang.Object
    {
        var m = ShizukuClass?.GetMethod(method);
        var result = m?.Invoke(null);
        return result?.JavaCast<T>();
    }
}
