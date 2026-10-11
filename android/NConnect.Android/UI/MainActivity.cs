using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Activity;
using AndroidX.AppCompat.App;
using AndroidX.Core.View;
using Google.Android.Material.AppBar;
using Google.Android.Material.BottomNavigation;
using NConnect.Android.Core;
using NConnect.Android.Output;
using NConnect.Android.Touch;
using View = Android.Views.View;

namespace NConnect.Android.UI;

/// <summary>Eine Seite des Hauptfensters (Controller, Einstellungen, Hilfe).</summary>
public interface IPage
{
    string Title { get; }
    View Build();
    /// <summary>Anzeige aktualisieren (Status, Akku, Eingaben). Wird auch sekündlich aufgerufen.</summary>
    void Refresh();
}

/// <summary>
/// Hauptfenster: Titelleiste, Inhalt und untere Navigation (Controller · Einstellungen · Hilfe). Beim ersten Start
/// öffnet sich stattdessen der Einrichtungs-Assistent.
/// </summary>
[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop,
    Theme = "@style/AppTheme", ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AppCompatActivity
{
    private const int TabControllers = 1, TabSettings = 2, TabHelp = 3;

    private MaterialToolbar _toolbar = null!;
    private FrameLayout _content = null!;
    private BottomNavigationView _nav = null!;
    private IPage? _page;
    private readonly Dictionary<int, IPage> _pages = [];
    private Handler? _ticker;
    private bool _refreshQueued;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Look.ApplyNightMode();
        base.OnCreate(savedInstanceState);
        Core.HangWatchdog.Start();
        if (!AppPrefs.SetupDone)
        {
            StartActivity(new Intent(this, typeof(SetupWizardActivity)));
            Finish();
            return;
        }
        WindowCompat.SetDecorFitsSystemWindows(Window!, false);

        var root = Ui.Column(this);
        _toolbar = new MaterialToolbar(this);
        root.AddView(_toolbar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        _content = new FrameLayout(this);
        root.AddView(_content, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        _nav = new BottomNavigationView(this);
        _nav.Menu!.Add(0, TabControllers, 0, "Controller")!.SetIcon(Resource.Drawable.ic_nav_controller);
        _nav.Menu.Add(0, TabSettings, 1, "Einstellungen")!.SetIcon(Resource.Drawable.ic_nav_settings);
        _nav.Menu.Add(0, TabHelp, 2, "Hilfe")!.SetIcon(Resource.Drawable.ic_nav_help);
        _nav.ItemSelected += (_, e) => { Show(e.Item.ItemId); e.Handled = true; };
        root.AddView(_nav, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        SetContentView(root);
        Chrome.Apply(this, root, _toolbar, _nav);

        // Randlos: Titelleiste rückt unter die Statusleiste, die Navigation hält selbst Abstand zur Gestenleiste.
        ViewCompat.SetOnApplyWindowInsetsListener(root, new InsetsListener());

        _pages[TabControllers] = new ControllersPage(this);
        _pages[TabSettings] = new SettingsPage(this);
        _pages[TabHelp] = new HelpPage(this);
        _nav.SelectedItemId = TabControllers;
        Show(TabControllers);

        OnBackPressedDispatcher.AddCallback(this, new BackToControllers(this));
        if (AppPrefs.AutoStart)
            EnsureServiceRunning();
    }

    /// <summary>Startet den Hintergrunddienst, sobald Bluetooth erlaubt ist.</summary>
    public void EnsureServiceRunning()
    {
        if (Permissions.HasBluetooth(this))
            ControllerService.Start(this);
        else
            Permissions.Request(this);
    }

    public void Show(int tab)
    {
        if (!_pages.TryGetValue(tab, out var page))
            return;
        _page = page;
        _toolbar.Title = page.Title;
        _content.RemoveAllViews();
        _content.AddView(page.Build());
        if (_nav.SelectedItemId != tab)
            _nav.SelectedItemId = tab;
    }

    public void ShowControllers() => Show(TabControllers);
    public void ShowHelp() => Show(TabHelp);

    /// <summary>Aktuelle Seite neu aufbauen (z. B. nach geänderten Einstellungen).</summary>
    public void Rebuild()
    {
        if (_page is null)
            return;
        _content.RemoveAllViews();
        _content.AddView(_page.Build());
    }

    protected override void OnResume()
    {
        base.OnResume();
        ControllerService.Changed += QueueRefresh;
        OutputRouter.Changed += QueueRefresh;
        TouchService.StateChanged += QueueRefresh;
        Core.KnownControllers.Changed += QueueRefresh;
        _ticker = new Handler(Looper.MainLooper!);
        Tick();
        Rebuild();
    }

    protected override void OnPause()
    {
        ControllerService.Changed -= QueueRefresh;
        OutputRouter.Changed -= QueueRefresh;
        TouchService.StateChanged -= QueueRefresh;
        Core.KnownControllers.Changed -= QueueRefresh;
        _ticker?.RemoveCallbacksAndMessages(null);
        _ticker = null;
        base.OnPause();
    }

    private void Tick()
    {
        _page?.Refresh();
        _ticker?.PostDelayed(Tick, 1000);
    }

    private void QueueRefresh()
    {
        if (_refreshQueued)
            return;
        _refreshQueued = true;
        RunOnUiThread(() =>
        {
            _refreshQueued = false;
            _page?.Refresh();
        });
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == Permissions.RequestCode && Permissions.HasBluetooth(this))
            ControllerService.Start(this);
        _page?.Refresh();
    }

    private sealed class InsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(View? v, WindowInsetsCompat? insets)
        {
            if (v is null || insets is null)
                return insets;
            var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout())!;
            // Abstand zur Statusleiste am äußeren Rahmen – die Titelleiste hat eine feste Höhe und würde sonst gestaucht.
            v.SetPadding(bars.Left, bars.Top, bars.Right, 0);
            return insets; // die BottomNavigationView wertet den unteren Rand selbst aus
        }
    }

    private sealed class BackToControllers(MainActivity activity) : OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed()
        {
            if (activity._nav.SelectedItemId != TabControllers)
            {
                activity.Show(TabControllers);
                return;
            }
            Enabled = false;
            activity.OnBackPressedDispatcher.OnBackPressed();
            Enabled = true;
        }
    }
}
