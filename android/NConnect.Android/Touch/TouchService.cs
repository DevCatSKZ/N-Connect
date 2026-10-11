using Android.AccessibilityServices;
using Android.Content;
using Android.OS;
using Android.Views.Accessibility;
using Action = System.Action;

namespace NConnect.Android.Touch;

/// <summary>
/// Bedienungshilfe für den Touch-Modus: führt Tasten des Controllers als Bildschirmberührungen aus und zeigt den
/// Layout-Editor über dem laufenden Spiel. Liest keine Fensterinhalte (siehe res/xml/touch_service.xml).
/// Der Nutzer schaltet sie einmalig unter Einstellungen → Bedienungshilfen → N-Connect ein.
/// </summary>
[Service(Label = "N-Connect Touch-Steuerung", Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
[IntentFilter(["android.accessibilityservice.AccessibilityService"])]
[MetaData("android.accessibilityservice", Resource = "@xml/touch_service")]
public sealed class TouchService : AccessibilityService
{
    public static TouchService? Instance { get; private set; }

    public static event Action? StateChanged;

    private Handler? _main;
    private TouchEditor? _editor;

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        _main = new Handler(Looper.MainLooper!);
        Instance = this;
        Log.Info("Touch-Steuerung (Bedienungshilfe) aktiv");
        StateChanged?.Invoke();
    }

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
    }

    public override void OnInterrupt()
    {
    }

    public override bool OnUnbind(Intent? intent)
    {
        Leave();
        return base.OnUnbind(intent);
    }

    public override void OnDestroy()
    {
        Leave();
        base.OnDestroy();
    }

    private void Leave()
    {
        if (Instance != this)
            return;
        Instance = null;
        _editor?.Close();
        Log.Info("Touch-Steuerung (Bedienungshilfe) beendet");
        StateChanged?.Invoke();
    }

    /// <summary>Bildschirmgröße in Pixeln in der aktuellen Ausrichtung.</summary>
    public (float Width, float Height) ScreenSize()
    {
        var metrics = Resources!.DisplayMetrics!;
        return (metrics.WidthPixels, metrics.HeightPixels);
    }

    public void RunOnMain(Action action) => _main?.Post(action);

    /// <summary>Layout-Editor über dem aktuellen Bildschirm (z. B. über dem Spiel) öffnen.</summary>
    public void ShowEditor()
    {
        RunOnMain(() =>
        {
            _editor?.Close();
            _editor = new TouchEditor(this, () => _editor = null);
            _editor.Show();
        });
    }

    /// <summary>Prüft in den Systemeinstellungen, ob die Bedienungshilfe eingeschaltet ist (auch wenn sie gerade neu startet).</summary>
    public static bool IsEnabledInSettings(Context context)
    {
        string? enabled = global::Android.Provider.Settings.Secure.GetString(context.ContentResolver,
            global::Android.Provider.Settings.Secure.EnabledAccessibilityServices);
        return enabled is not null && enabled.Contains(context.PackageName + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Öffnet die Bedienungshilfen-Einstellungen des Systems.</summary>
    public static void OpenSystemSettings(Context context) => UI.SystemPages.TouchService(context);
}
