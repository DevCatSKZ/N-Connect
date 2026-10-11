using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;
using AndroidX.Core.Widget;
using Google.Android.Material.Button;
using Google.Android.Material.Card;
using Google.Android.Material.MaterialSwitch;
using Google.Android.Material.Slider;
using NConnect.Android.Output;
using Switch2Pro.Protocol;
using Color = Android.Graphics.Color;
using View = Android.Views.View;

namespace NConnect.Android.UI;

/// <summary>
/// Kleiner Baukasten für einheitliche Seiten (Karten, Zeilen, Knöpfe). Farben kommen aus dem gewählten Farbschema
/// (<see cref="Look"/>, gleiche Schemata wie unter Windows).
/// </summary>
public static class Ui
{
    public static int Dp(Context ctx, float dp) => (int)(dp * ctx.Resources!.DisplayMetrics!.Density + 0.5f);

    private static PaletteColors P(Context ctx) => Look.P(ctx);

    /// <summary>Senkrechte Spalte mit Seitenrand, in einer ScrollView, im Fensterhintergrund des Schemas.</summary>
    public static (ScrollView Scroll, LinearLayout Column) Page(Context ctx)
    {
        var column = Column(ctx);
        int pad = Dp(ctx, 16);
        column.SetPadding(pad, Dp(ctx, 8), pad, Dp(ctx, 24));
        var scroll = new ScrollView(ctx) { FillViewport = true };
        scroll.SetBackgroundColor(Look.C(P(ctx).Window));
        scroll.AddView(column);
        return (scroll, column);
    }

    public static LinearLayout Column(Context ctx) => new(ctx) { Orientation = Orientation.Vertical };

    private static TextView Text(Context ctx, string text, int style, int rgb)
    {
        var tv = new TextView(ctx) { Text = L.T(text) };
        TextViewCompat.SetTextAppearance(tv, style);
        tv.SetTextColor(Look.C(rgb));
        return tv;
    }

    public static TextView Headline(Context ctx, string text)
    {
        var tv = Text(ctx, text, Resource.Style.TextAppearance_Material3_HeadlineSmall, P(ctx).Text);
        tv.SetPadding(0, Dp(ctx, 8), 0, Dp(ctx, 4));
        return tv;
    }

    public static TextView Title(Context ctx, string text) =>
        Text(ctx, text, Resource.Style.TextAppearance_Material3_TitleMedium, P(ctx).Text);

    public static TextView Body(Context ctx, string text) =>
        Text(ctx, text, Resource.Style.TextAppearance_Material3_BodyMedium, P(ctx).Text);

    public static TextView Caption(Context ctx, string text) =>
        Text(ctx, text, Resource.Style.TextAppearance_Material3_BodySmall, P(ctx).TextMuted);

    /// <summary>Überschrift einer Gruppe in den Einstellungen („Ausgabe“, „Controller“ …) in der Akzentfarbe.</summary>
    public static TextView Section(Context ctx, string text)
    {
        var tv = Text(ctx, text, Resource.Style.TextAppearance_Material3_LabelLarge, Look.AccentLine(ctx));
        tv.SetPadding(Dp(ctx, 4), Dp(ctx, 20), 0, Dp(ctx, 6));
        return tv;
    }

    /// <summary>Text mit einfacher Auszeichnung (&lt;b&gt;, &lt;br&gt;).</summary>
    public static TextView Rich(Context ctx, string html)
    {
        var tv = Body(ctx, "");
        tv.TextFormatted = Html.FromHtml(L.T(html), FromHtmlOptions.ModeCompact);
        return tv;
    }

    /// <summary>Karte in der Kartenfarbe des Schemas mit dezentem Rand (wie die Abschnitte unter Windows).</summary>
    public static MaterialCardView Card(Context ctx, params View[] children)
    {
        var p = P(ctx);
        var card = new MaterialCardView(ctx);
        card.SetCardBackgroundColor(Look.C(p.Surface));
        card.StrokeColor = Look.C(p.Dark ? ColorSchemes.Blend(p.Surface, p.Text, 0.08f) : p.Border).ToArgb();
        card.StrokeWidth = Math.Max(1, Dp(ctx, 1));
        card.CardElevation = 0;
        card.Radius = Dp(ctx, 12);
        var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        lp.SetMargins(0, Dp(ctx, 6), 0, Dp(ctx, 6));
        card.LayoutParameters = lp;
        var inner = Column(ctx);
        int pad = Dp(ctx, 16);
        inner.SetPadding(pad, pad, pad, pad);
        foreach (var c in children)
            inner.AddView(c);
        card.AddView(inner);
        return card;
    }

    /// <summary>Inhalt einer Karte (zum späteren Ergänzen).</summary>
    public static LinearLayout Inner(MaterialCardView card) => (LinearLayout)card.GetChildAt(0)!;

    public enum ButtonStyle { Filled, Outlined, Text }

    public static MaterialButton Button(Context ctx, string text, Action onClick, ButtonStyle style = ButtonStyle.Filled)
    {
        var p = P(ctx);
        int attr = style switch
        {
            ButtonStyle.Outlined => Resource.Attribute.materialButtonOutlinedStyle,
            ButtonStyle.Text => Resource.Attribute.borderlessButtonStyle,
            _ => Resource.Attribute.materialButtonStyle,
        };
        var b = new MaterialButton(ctx, null, attr) { Text = L.T(text) };
        switch (style)
        {
            case ButtonStyle.Filled:
                b.SetTextColor(Look.C(Look.OnAccent(ctx)));
                if (Look.Scheme.Neon)
                {
                    // Neon-Schemata: Verlauf Akzent → Akzent 2 wie die Akzentflächen unter Windows.
                    var gradient = new GradientDrawable(GradientDrawable.Orientation.LeftRight!,
                        [Look.C(p.Accent).ToArgb(), Look.C(p.Accent2).ToArgb()]);
                    gradient.SetCornerRadius(Dp(ctx, 20));
                    b.BackgroundTintList = null;
                    b.Background = new RippleDrawable(Look.Solid(0xFFFFFF).WithAlpha(60)!, gradient, null);
                }
                else
                {
                    b.BackgroundTintList = Look.Solid(p.Accent);
                }
                break;
            case ButtonStyle.Outlined:
                b.SetTextColor(Look.C(Look.AccentLine(ctx)));
                b.StrokeColor = Look.Solid(p.ControlBorder);
                b.RippleColor = Look.Solid(Look.AccentLine(ctx)).WithAlpha(40);
                break;
            default:
                b.SetTextColor(Look.C(Look.AccentLine(ctx)));
                b.RippleColor = Look.Solid(Look.AccentLine(ctx)).WithAlpha(40);
                break;
        }
        b.Click += (_, _) => onClick();
        b.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Dp(ctx, 8),
        };
        return b;
    }

    /// <summary>Waagerechte Reihe von Knöpfen.</summary>
    public static LinearLayout Buttons(Context ctx, params View[] buttons)
    {
        var row = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (i > 0 && buttons[i].LayoutParameters is LinearLayout.LayoutParams lp)
                lp.MarginStart = Dp(ctx, 8);
            row.AddView(buttons[i]);
        }
        return row;
    }

    /// <summary>Zeile mit Titel, Untertitel und Schalter. Antippen der ganzen Zeile schaltet um.</summary>
    public static View SwitchRow(Context ctx, string title, string subtitle, bool isChecked, Action<bool> onChanged)
    {
        var p = P(ctx);
        var row = Row(ctx, title, subtitle, out _);
        var sw = new MaterialSwitch(ctx) { Checked = isChecked };
        sw.TrackTintList = Look.States(p.Accent, p.SurfaceHover);
        sw.ThumbTintList = Look.States(Look.OnAccent(ctx), p.TextMuted);
        sw.TrackDecorationTintList = Look.States(p.Accent, p.ControlBorder);
        sw.CheckedChange += (_, e) => onChanged(e.IsChecked);
        row.AddView(sw);
        row.Click += (_, _) => sw.Toggle();
        return row;
    }

    /// <summary>Zeile mit Titel und Untertitel, die etwas öffnet.</summary>
    public static View ClickRow(Context ctx, string title, string subtitle, Action onClick)
    {
        var row = Row(ctx, title, subtitle, out _);
        row.Click += (_, _) => onClick();
        return row;
    }

    /// <summary>Schieberegler mit Titel und aktuellem Wert im Untertitel.</summary>
    public static View SliderRow(Context ctx, string title, Func<float, string> describe, float value, float from, float to,
        float step, Action<float> onChanged)
    {
        var p = P(ctx);
        var column = Column(ctx);
        column.SetPadding(Dp(ctx, 4), Dp(ctx, 10), Dp(ctx, 4), 0);
        column.AddView(Title(ctx, title));
        var caption = Caption(ctx, describe(value));
        column.AddView(caption);
        var slider = new Slider(ctx) { ValueFrom = from, ValueTo = to, StepSize = step };
        slider.Value = Math.Clamp(MathF.Round(value / step) * step, from, to);
        slider.LabelBehavior = 2; // LABEL_GONE: Wert steht im Untertitel
        slider.TrackActiveTintList = Look.Solid(p.Accent);
        slider.TrackInactiveTintList = Look.Solid(p.SurfaceHover);
        slider.ThumbTintList = Look.Solid(Look.AccentLine(ctx));
        slider.HaloTintList = Look.Solid(Look.AccentLine(ctx)).WithAlpha(50)!;
        slider.TickActiveTintList = Look.Solid(Look.OnAccent(ctx)).WithAlpha(120)!;
        slider.TickInactiveTintList = Look.Solid(p.TextMuted).WithAlpha(120)!;
        slider.AddOnChangeListener(new SliderListener((v, fromUser) =>
        {
            caption.Text = L.T(describe(v));
            if (fromUser)
                onChanged(v);
        }));
        column.AddView(slider);
        return column;
    }

    private static LinearLayout Row(Context ctx, string title, string subtitle, out LinearLayout texts)
    {
        var row = new LinearLayout(ctx) { Orientation = Orientation.Horizontal, Clickable = true, Focusable = true };
        row.SetGravity(GravityFlags.CenterVertical);
        int pad = Dp(ctx, 4);
        row.SetPadding(pad, Dp(ctx, 12), pad, Dp(ctx, 12));
        row.SetMinimumHeight(Dp(ctx, 56));
        row.Background = new RippleDrawable(Look.Solid(Look.AccentLine(ctx)).WithAlpha(36)!, null, new ColorDrawable(Color.White));
        texts = Column(ctx);
        texts.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { MarginEnd = Dp(ctx, 12) };
        texts.AddView(Title(ctx, title));
        if (subtitle.Length > 0)
            texts.AddView(Caption(ctx, subtitle));
        row.AddView(texts);
        return row;
    }

    /// <summary>Statuszeile: Symbol (✓ bereit / ⚠ fehlt etwas / ✗ nicht vorhanden), Text und optional ein Knopf.</summary>
    public static View StatusRow(Context ctx, PathState state, string title, string subtitle, string? action = null,
        Action? onAction = null)
    {
        var column = Column(ctx);
        column.SetPadding(0, Dp(ctx, 8), 0, Dp(ctx, 8));
        var row = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        var icon = new ImageView(ctx);
        icon.SetImageResource(state switch
        {
            PathState.Ready => Resource.Drawable.ic_check,
            PathState.NeedsAction => Resource.Drawable.ic_warn,
            _ => Resource.Drawable.ic_cross,
        });
        icon.ImageTintList = state switch
        {
            // Bereit in der Akzentfarbe des Schemas; „fehlt etwas“ bleibt als Warnung erkennbar (Bernstein, passend hell/dunkel).
            PathState.Ready => Look.Solid(Look.AccentLine(ctx)),
            PathState.NeedsAction => Look.Solid(P(ctx).Dark ? 0xF5B041 : 0xB45309),
            _ => Look.Solid(P(ctx).TextMuted),
        };
        int size = Dp(ctx, 24);
        row.AddView(icon, new LinearLayout.LayoutParams(size, size) { MarginEnd = Dp(ctx, 12) });
        var texts = Column(ctx);
        texts.AddView(Title(ctx, title));
        if (subtitle.Length > 0)
            texts.AddView(Caption(ctx, subtitle));
        row.AddView(texts, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        column.AddView(row);
        if (action is not null && onAction is not null)
        {
            var b = Button(ctx, action, onAction, state == PathState.Ready ? ButtonStyle.Text : ButtonStyle.Outlined);
            ((LinearLayout.LayoutParams)b.LayoutParameters!).MarginStart = Dp(ctx, 36);
            column.AddView(b);
        }
        return column;
    }

    /// <summary>Trennlinie zwischen Zeilen einer Karte (wie Theme.Divider unter Windows).</summary>
    public static View Divider(Context ctx)
    {
        var p = P(ctx);
        var v = new View(ctx);
        v.SetBackgroundColor(Look.C(ColorSchemes.Blend(p.Surface, p.Text, p.Dark ? 0.08f : 0.07f)));
        v.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Math.Max(1, Dp(ctx, 1)))
        {
            TopMargin = Dp(ctx, 4),
            BottomMargin = Dp(ctx, 4),
        };
        return v;
    }

    public static View Space(Context ctx, int dp)
    {
        var v = new View(ctx);
        v.LayoutParameters = new LinearLayout.LayoutParams(1, Dp(ctx, dp));
        return v;
    }

    private sealed class SliderListener(Action<float, bool> onChange) : Java.Lang.Object, Slider.IOnChangeListener
    {
        public void OnValueChange(Slider slider, float value, bool fromUser) => onChange(value, fromUser);
    }

    public static void Toast(Context ctx, string text) =>
        global::Android.Widget.Toast.MakeText(ctx, L.T(text), ToastLength.Short)!.Show();
}
