using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Google.Android.Material.Button;
using Color = Android.Graphics.Color;
using View = Android.Views.View;

namespace NConnect.Android.Touch;

/// <summary>
/// Layout-Editor als Overlay über dem laufenden Spiel: Bedienelemente auf die Bildschirmtasten des Spiels ziehen,
/// gedrückt halten zum Entfernen, „Speichern“ übernimmt die Belegung sofort.
/// </summary>
public sealed class TouchEditor(TouchService service, Action onClosed)
{
    private readonly Context _ctx = new ContextThemeWrapper(service, Resource.Style.AppTheme);
    private readonly TouchLayout _layout = TouchLayout.Load();
    private readonly Dictionary<TouchBinding, View> _bubbles = [];
    private IWindowManager? _wm;
    private FrameLayout? _root;
    private float _density = 1;

    private static readonly (TouchControl Control, string Text)[] Choices =
    [
        (TouchControl.A, "A (unten)"), (TouchControl.B, "B (rechts)"), (TouchControl.X, "X (links)"),
        (TouchControl.Y, "Y (oben)"), (TouchControl.LeftStick, "Linker Stick (Bewegen)"),
        (TouchControl.RightStick, "Rechter Stick (Kamera)"), (TouchControl.LB, "LB / L"), (TouchControl.RB, "RB / R"),
        (TouchControl.LT, "LT / ZL"), (TouchControl.RT, "RT / ZR"), (TouchControl.Up, "Steuerkreuz hoch"),
        (TouchControl.Down, "Steuerkreuz runter"), (TouchControl.Left, "Steuerkreuz links"),
        (TouchControl.Right, "Steuerkreuz rechts"), (TouchControl.Start, "Start (+)"), (TouchControl.Back, "Zurück (−)"),
        (TouchControl.LS, "Linken Stick drücken"), (TouchControl.RS, "Rechten Stick drücken"),
    ];

    public void Show()
    {
        _wm = service.GetSystemService(Context.WindowService)!.JavaCast<IWindowManager>();
        _density = service.Resources!.DisplayMetrics!.Density;
        _root = new FrameLayout(_ctx);
        _root.SetBackgroundColor(Color.Argb(110, 0, 0, 0));

        foreach (var b in _layout.Bindings)
            AddBubble(b);
        _root.AddView(Toolbar(), new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.CenterHorizontal) { TopMargin = Dp(24) });

        var lp = new WindowManagerLayoutParams(WindowManagerTypes.AccessibilityOverlay,
            WindowManagerFlags.LayoutInScreen | WindowManagerFlags.LayoutNoLimits, Format.Translucent)
        {
            Width = ViewGroup.LayoutParams.MatchParent,
            Height = ViewGroup.LayoutParams.MatchParent,
        };
        _wm.AddView(_root, lp);
    }

    private View Toolbar()
    {
        var bar = new LinearLayout(_ctx) { Orientation = Orientation.Vertical };
        bar.SetGravity(GravityFlags.CenterHorizontal);
        bar.SetPadding(Dp(16), Dp(12), Dp(16), Dp(12));
        bar.Background = Round(Color.Argb(235, 32, 32, 36), Dp(20));

        var hint = new TextView(_ctx)
        {
            Text = "Ziehe die Punkte auf die Bildschirmtasten des Spiels.\nGedrückt halten entfernt einen Punkt.",
            Gravity = GravityFlags.Center,
            TextSize = 13,
        };
        hint.SetTextColor(Color.White);
        bar.AddView(hint);

        var row = new LinearLayout(_ctx) { Orientation = Orientation.Horizontal };
        row.SetPadding(0, Dp(8), 0, 0);
        var add = Button("Taste hinzufügen");
        add.Click += (_, _) => ShowAddMenu(add);
        var save = Button("Speichern");
        save.Click += (_, _) => { _layout.Save(); TouchPad.ReloadLayout(); Toast("Touch-Belegung gespeichert"); Close(); };
        var cancel = Button("Abbrechen");
        cancel.Click += (_, _) => Close();
        row.AddView(add);
        row.AddView(save);
        row.AddView(cancel);
        bar.AddView(row);
        return bar;
    }

    private MaterialButton Button(string text)
    {
        var b = new MaterialButton(_ctx) { Text = text };
        var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        lp.SetMargins(Dp(4), 0, Dp(4), 0);
        b.LayoutParameters = lp;
        return b;
    }

    private void ShowAddMenu(View anchor)
    {
        var menu = new PopupMenu(_ctx, anchor);
        var used = _layout.Bindings.Select(b => b.Control).ToHashSet();
        var free = Choices.Where(c => !used.Contains(c.Control)).ToList();
        for (int i = 0; i < free.Count; i++)
            menu.Menu!.Add(0, i, i, free[i].Text);
        menu.MenuItemClick += (_, e) =>
        {
            var (w, h) = service.ScreenSize();
            var b = new TouchBinding { Control = free[e.Item!.ItemId].Control, X = 0.5f, Y = 0.55f };
            _layout.Bindings.Add(b);
            AddBubble(b);
        };
        menu.Show();
    }

    private void AddBubble(TouchBinding b)
    {
        bool stick = b.Control is TouchControl.LeftStick or TouchControl.RightStick;
        int size = Dp(stick ? 72 : 52);
        var bubble = new TextView(_ctx)
        {
            Text = TouchLayout.Label(b.Control),
            Gravity = GravityFlags.Center,
            TextSize = stick ? 13 : 16,
        };
        bubble.SetTextColor(UI.Look.C(Switch2Pro.Protocol.ColorSchemes.OnAccent(stick ? UI.Look.P(service).Accent2 : UI.Look.P(service).Accent)));
        bubble.SetTypeface(null, TypefaceStyle.Bold);
        var palette = UI.Look.P(service);
        bubble.Background = Round(UI.Look.C(stick ? palette.Accent2 : palette.Accent, 215), size / 2);
        var (w, h) = service.ScreenSize();
        var lp = new FrameLayout.LayoutParams(size, size)
        {
            LeftMargin = (int)(b.X * w) - size / 2,
            TopMargin = (int)(b.Y * h) - size / 2,
        };
        float dx = 0, dy = 0;
        bubble.Touch += (_, e) =>
        {
            var ev = e.Event!;
            switch (ev.Action)
            {
                case MotionEventActions.Down:
                    dx = ev.RawX - lp.LeftMargin;
                    dy = ev.RawY - lp.TopMargin;
                    e.Handled = false; // Lang-Drücken weiterhin erkennen
                    return;
                case MotionEventActions.Move:
                    lp.LeftMargin = (int)(ev.RawX - dx);
                    lp.TopMargin = (int)(ev.RawY - dy);
                    bubble.LayoutParameters = lp;
                    var (sw, sh) = service.ScreenSize();
                    b.X = Math.Clamp((lp.LeftMargin + size / 2f) / sw, 0, 1);
                    b.Y = Math.Clamp((lp.TopMargin + size / 2f) / sh, 0, 1);
                    bubble.CancelLongPress();
                    e.Handled = true;
                    return;
            }
            e.Handled = false;
        };
        bubble.LongClick += (_, _) =>
        {
            _layout.Bindings.Remove(b);
            _root?.RemoveView(bubble);
            _bubbles.Remove(b);
            Toast($"{TouchLayout.Label(b.Control)} entfernt");
        };
        _bubbles[b] = bubble;
        _root!.AddView(bubble, 0, lp); // unter der Werkzeugleiste
    }

    private static GradientDrawable Round(Color color, int radius)
    {
        var d = new GradientDrawable();
        d.SetColor(color);
        d.SetCornerRadius(radius);
        return d;
    }

    private void Toast(string text) => global::Android.Widget.Toast.MakeText(service, text, ToastLength.Short)!.Show();

    private int Dp(int dp) => (int)(dp * _density + 0.5f);

    public void Close()
    {
        if (_root is not null)
        {
            try
            {
                _wm?.RemoveView(_root);
            }
            catch (Exception)
            {
                // schon entfernt
            }
            _root = null;
        }
        onClosed();
    }
}
