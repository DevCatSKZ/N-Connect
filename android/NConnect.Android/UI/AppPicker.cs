using Android.Content;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;
using Google.Android.Material.Dialog;
using Google.Android.Material.TextField;
using NConnect.Android.Profiles;
using View = Android.Views.View;

namespace NConnect.Android.UI;

/// <summary>
/// Auswahl einer installierten App für „Profile je Spiel“: alle startbaren Apps (auch Fernseher-Apps) mit Name und
/// Symbol – ohne SKZ Connect selbst und ohne Startbildschirme. Die Liste wird im Hintergrund gelesen, ein Suchfeld
/// filtert nach Name oder Paketname.
/// </summary>
public static class AppPicker
{
    public sealed record App(string Package, string Label);

    /// <summary>Liste laden und als Dialog zeigen; <paramref name="onChosen"/> bekommt die gewählte App.</summary>
    public static void Show(MainActivity host, Action<App> onChosen)
    {
        Ui.Toast(host, "Lade App-Liste …");
        _ = Task.Run(() =>
        {
            var apps = Load(host);
            host.RunOnUiThread(() =>
            {
                if (apps.Count == 0)
                    Ui.Toast(host, "Keine passenden Apps gefunden");
                else
                    ShowList(host, apps, onChosen);
            });
        });
    }

    /// <summary>Angezeigter Name einer App (Paketname, wenn sie nicht installiert ist).</summary>
    public static string LabelOf(Context ctx, string package)
    {
        try
        {
            var pm = ctx.PackageManager!;
            using var info = pm.GetApplicationInfo(package, 0);
            return pm.GetApplicationLabel(info) is { Length: > 0 } label ? label : package;
        }
        catch (Exception)
        {
            return package;
        }
    }

    private static List<App> Load(Context ctx)
    {
        var packages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var category in new[] { Intent.CategoryLauncher, Intent.CategoryLeanbackLauncher })
        {
            using var intent = new Intent(Intent.ActionMain).AddCategory(category)!;
            packages.UnionWith(GameProfileWatcher.QueryActivities(ctx, intent));
        }
        packages.Remove(ctx.PackageName!);
        packages.ExceptWith(GameProfileWatcher.Launchers(ctx));
        return [.. packages.Select(p => new App(p, LabelOf(ctx, p))).OrderBy(a => a.Label, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static void ShowList(MainActivity host, List<App> apps, Action<App> onChosen)
    {
        var box = Ui.Column(host);
        int pad = Ui.Dp(host, 24);
        box.SetPadding(pad, Ui.Dp(host, 8), pad, 0);
        var layout = new TextInputLayout(host) { Hint = L.T("Suchen") };
        var search = new TextInputEditText(layout.Context!) { InputType = InputTypes.ClassText };
        search.SetSingleLine(true);
        layout.AddView(search);
        box.AddView(layout);
        var adapter = new AppAdapter(host, apps);
        var list = new ListView(host) { Adapter = adapter };
        // Feste Höhe: ein ListView in einem Dialog misst sich sonst auf eine Zeile zusammen.
        int height = Math.Min(Ui.Dp(host, 420), (int)(host.Resources!.DisplayMetrics!.HeightPixels * 0.55));
        box.AddView(list, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, height));
        search.TextChanged += (_, _) => adapter.Filter(search.Text ?? "");
        var dialog = new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Spiel wählen"))!
            .SetView(box)!
            .SetNegativeButton(L.T("Abbrechen"), (_, _) => { })!
            .Create()!;
        list.ItemClick += (_, e) =>
        {
            dialog.Dismiss();
            onChosen(adapter[e.Position]);
        };
        dialog.Show();
    }

    /// <summary>Zeilen mit Symbol, Name und Paketname. Symbole werden erst beim Anzeigen geladen und gemerkt.</summary>
    private sealed class AppAdapter(Context ctx, List<App> all) : BaseAdapter<App>
    {
        private List<App>? _filtered;
        private readonly Dictionary<string, Drawable?> _icons = [];

        private List<App> Shown => _filtered ?? all;

        public void Filter(string text)
        {
            text = text.Trim();
            _filtered = text.Length == 0 ? null
                : [.. all.Where(a => a.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                                     || a.Package.Contains(text, StringComparison.OrdinalIgnoreCase))];
            NotifyDataSetChanged();
        }

        public override App this[int position] => Shown[position];
        public override int Count => Shown.Count;
        public override long GetItemId(int position) => position;

        public override View GetView(int position, View? convertView, ViewGroup? parent)
        {
            var app = Shown[position];
            if (convertView is not LinearLayout row || row.ChildCount != 2)
            {
                row = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
                row.SetGravity(GravityFlags.CenterVertical);
                row.SetPadding(0, Ui.Dp(ctx, 8), 0, Ui.Dp(ctx, 8));
                int size = Ui.Dp(ctx, 40);
                row.AddView(new ImageView(ctx), new LinearLayout.LayoutParams(size, size) { MarginEnd = Ui.Dp(ctx, 16) });
                var texts = Ui.Column(ctx);
                texts.AddView(Ui.Title(ctx, ""));
                texts.AddView(Ui.Caption(ctx, ""));
                row.AddView(texts, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            }
            ((ImageView)row.GetChildAt(0)!).SetImageDrawable(Icon(app.Package));
            var column = (LinearLayout)row.GetChildAt(1)!;
            ((TextView)column.GetChildAt(0)!).Text = app.Label;
            ((TextView)column.GetChildAt(1)!).Text = app.Package;
            return row;
        }

        private Drawable? Icon(string package)
        {
            if (_icons.TryGetValue(package, out var icon))
                return icon;
            try
            {
                icon = ctx.PackageManager!.GetApplicationIcon(package);
            }
            catch (Exception)
            {
                icon = null;
            }
            return _icons[package] = icon;
        }
    }
}
