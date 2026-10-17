using Android.Content;
using Android.Text;
using Android.Widget;
using Google.Android.Material.Dialog;
using Google.Android.Material.TextField;
using NConnect.Android.Profiles;
using Switch2Pro.Protocol;

namespace NConnect.Android.UI;

/// <summary>
/// „Profile je Spiel“ verwalten: Profil für ein Spiel aus der App-Liste anlegen (startet mit einer Kopie der
/// Standardbelegung), Spiel ändern, umbenennen, löschen und die Tastenbelegung je Controller-Art bearbeiten –
/// Quelltaste → Xbox-Taste, keine Funktion, Gyro oder Makro, wie die gemeinsame Belegung
/// (<see cref="Mapping.ActionFor(ProButtons, ControllerKind, Settings, NamedProfile?, bool)"/>) es vorsieht.
/// Die Änderungen selbst stehen in <see cref="ProfileEdit"/>.
/// </summary>
public sealed class ProfileDialogs(MainActivity host)
{
    private static Settings S => AppPrefs.Settings;

    private static (string Action, string Label)[] Targets => ProfileEdit.Targets;

    private const string MacroChoice = "\u0001macro";

    // ---------- Liste ----------

    public void Manage()
    {
        var profiles = S.NamedProfiles;
        var items = profiles.Select(p => $"{p.Name} – {Games(p)}").ToList();
        var box = Box();
        box.AddView(Ui.Caption(host, "Ein Profil gilt automatisch, solange sein Spiel im Vordergrund läuft, und hat eine eigene " +
                                     "Tastenbelegung. Antippen zum Bearbeiten."));
        var list = new ListView(host)
        {
            Adapter = new ArrayAdapter<string>(host, global::Android.Resource.Layout.SimpleListItem1, items),
        };
        box.AddView(list);
        var dialog = new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Profile je Spiel"))!
            .SetView(box)!
            .SetPositiveButton(L.T("Neues Profil"), (_, _) => AppPicker.Show(host, Create))!
            .SetNegativeButton(L.T("Fertig"), (_, _) => { })!
            .Create()!;
        list.ItemClick += (_, e) =>
        {
            dialog.Dismiss();
            Actions(profiles[e.Position].Name);
        };
        dialog.Show();
    }

    private LinearLayout Box()
    {
        var box = Ui.Column(host);
        int pad = Ui.Dp(host, 24);
        box.SetPadding(pad, Ui.Dp(host, 8), pad, 0);
        return box;
    }

    private string Games(NamedProfile p) => string.Join(", ", p.Programs.Select(package => AppPicker.LabelOf(host, package)));

    private static NamedProfile? Find(string name) => S.NamedProfiles.FirstOrDefault(p => p.Name == name);

    private void Save()
    {
        AppPrefs.SaveSettings();
        host.Rebuild();
    }

    // ---------- Anlegen ----------

    private void Create(AppPicker.App app)
    {
        var (box, input) = NameBox(app.Label);
        box.AddView(Ui.Caption(host, L.F("Das Profil wird automatisch aktiv, solange „{0}“ läuft.", app.Label)), 0);
        box.AddView(Ui.Caption(host, "Es startet mit einer Kopie der Standardbelegung – danach kannst du jede Taste ändern."), 1);
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Neues Profil"))!
            .SetView(box)!
            .SetPositiveButton(L.T("Anlegen"), (_, _) =>
            {
                string name = input.Text?.Trim() ?? "";
                if (!ProfileEdit.NameFree(S.NamedProfiles, name))
                {
                    Ui.Toast(host, "Name fehlt oder ist schon vergeben");
                    return;
                }
                S.NamedProfiles = [.. S.NamedProfiles, ProfileEdit.CopyOfStandard(S, name, app.Package)];
                Save();
                Ui.Toast(host, L.F("Profil „{0}“ angelegt", name));
                Actions(name);
            })!
            .SetNegativeButton(L.T("Abbrechen"), (_, _) => { })!
            .Show();
    }

    private (LinearLayout Box, TextInputEditText Input) NameBox(string text)
    {
        var layout = new TextInputLayout(host) { Hint = L.T("Profilname") };
        var input = new TextInputEditText(layout.Context!) { Text = text, InputType = InputTypes.ClassText | InputTypes.TextFlagCapSentences };
        input.SetSingleLine(true);
        layout.AddView(input);
        var box = Box();
        box.AddView(layout);
        return (box, input);
    }

    // ---------- Ein Profil ----------

    private void Actions(string name)
    {
        if (Find(name) is not { } profile)
            return;
        string[] items =
        [
            L.F("Spiel: {0}", Games(profile)), L.T("Tastenbelegung bearbeiten"), L.T("Umbenennen"), L.T("Löschen"),
        ];
        new MaterialAlertDialogBuilder(host)
            .SetTitle(profile.Name)!
            .SetItems(items, (_, e) =>
            {
                switch (e.Which)
                {
                    case 0: AppPicker.Show(host, app => ChangeGame(name, app)); break;
                    case 1: ChooseKind(name); break;
                    case 2: Rename(name); break;
                    default: Delete(name); break;
                }
            })!
            .SetNegativeButton(L.T("Schließen"), (_, _) => { })!
            .Show();
    }

    private void ChangeGame(string name, AppPicker.App app)
    {
        if (Find(name) is not { } profile)
            return;
        ProfileEdit.Replace(S, profile with { Programs = [app.Package] });
        S.DetectedProfile = null; // neu erkennen lassen
        Save();
        Actions(name);
    }

    private void Rename(string name)
    {
        var (box, input) = NameBox(name);
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Profil umbenennen"))!
            .SetView(box)!
            .SetPositiveButton(L.T("Speichern"), (_, _) =>
            {
                string renamed = input.Text?.Trim() ?? "";
                if (renamed == name || Find(name) is null)
                    return;
                if (!ProfileEdit.NameFree(S.NamedProfiles, renamed, except: name))
                {
                    Ui.Toast(host, "Name fehlt oder ist schon vergeben");
                    return;
                }
                ProfileEdit.Rename(S, name, renamed);
                Save();
                Actions(renamed);
            })!
            .SetNegativeButton(L.T("Abbrechen"), (_, _) => { })!
            .Show();
    }

    private void Delete(string name)
    {
        if (Find(name) is not { } profile)
            return;
        new MaterialAlertDialogBuilder(host)
            .SetTitle(profile.Name)!
            .SetMessage(L.F("Belegt die Spiele: {0}\n\nProfil löschen?", Games(profile)))!
            .SetPositiveButton(L.T("Löschen"), (_, _) =>
            {
                ProfileEdit.Delete(S, name);
                Save();
            })!
            .SetNegativeButton(L.T("Behalten"), (_, _) => { })!
            .Show();
    }

    // ---------- Tastenbelegung ----------

    private void ChooseKind(string name)
    {
        var kinds = ProfileEdit.Kinds;
        new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Für welchen Controller?"))!
            .SetItems(kinds.Select(Texts.ShortName).ToArray(), (_, e) => EditButtons(name, kinds[e.Which]))!
            .SetNegativeButton(L.T("Abbrechen"), (_, _) => { })!
            .Show();
    }

    private void EditButtons(string name, ControllerKind kind)
    {
        var buttons = ControllerButtons.For(kind);
        var adapter = new ArrayAdapter<string>(host, global::Android.Resource.Layout.SimpleListItem1, Rows(name, kind, buttons));
        var box = Box();
        box.AddView(Ui.Caption(host, "Tippe auf eine Taste, um ihr eine andere Funktion zu geben."));
        var list = new ListView(host) { Adapter = adapter };
        box.AddView(list);
        void Reload()
        {
            adapter.Clear();
            adapter.AddAll(Rows(name, kind, buttons));
            adapter.NotifyDataSetChanged();
        }
        list.ItemClick += (_, e) => ChooseTarget(name, kind, buttons[e.Position], Reload);
        var dialog = new MaterialAlertDialogBuilder(host)
            .SetTitle($"{name} · {Texts.ShortName(kind)}")!
            .SetView(box)!
            .SetPositiveButton(L.T("Fertig"), (_, _) => { })!
            .SetNeutralButton(L.T("Alles auf Standard"), (_, _) => { })!
            .Create()!;
        dialog.Show();
        // Eigener Klick-Handler: der Dialog bleibt offen und zeigt gleich die zurückgesetzte Belegung.
        dialog.GetButton((int)DialogButtonType.Neutral)!.Click += (_, _) =>
        {
            if (Find(name) is not { } profile)
                return;
            ProfileEdit.Replace(S, ProfileEdit.WithoutKind(profile, kind));
            AppPrefs.SaveSettings();
            Reload();
        };
    }

    private List<string> Rows(string name, ControllerKind kind, IReadOnlyList<ProButtons> buttons)
    {
        var profile = Find(name);
        return [.. buttons.Select(b => $"{L.T(ControllerButtons.Label(b, kind))}  →  {Describe(Mapping.ActionFor(b, kind, S, profile, shift: false))}")];
    }

    /// <summary>Gespeicherte Aktion in Worten.</summary>
    private static string Describe(ButtonAction action)
    {
        if (action.IsMacro)
            return L.F("⏯  Makro: {0}", action.Macro!);
        string key = action.IsSpecial ? action.Special.ToString() : action.Target.ToString();
        if (action.IsKeyboard || action.Turbo || Targets.All(t => t.Action != key))
            return action.ToString(); // unter Android nicht anlegbar (z. B. aus einer Windows-Datei) – wie gespeichert zeigen
        return L.T(Targets.First(t => t.Action == key).Label);
    }

    private void ChooseTarget(string name, ControllerKind kind, ProButtons button, Action changed)
    {
        if (Find(name) is not { } profile)
            return;
        string label = L.T(ControllerButtons.Label(button, kind));
        string? stored = profile.Buttons.TryGetValue(kind, out var map) && map.TryGetValue(button, out var s) ? s : null;
        var current = stored is null ? (ButtonAction?)null : ButtonAction.Parse(stored);
        string? currentMacro = current is { IsMacro: true } macro ? macro.Macro : null;
        var actions = new List<string?> { null };
        var labels = new List<string> { L.F("Standard: {0}", Describe(Mapping.ActionFor(button, kind, S, profile: null, shift: false))) };
        foreach (var (action, text) in Targets)
        {
            actions.Add(action);
            labels.Add(L.T(text));
        }
        actions.Add(MacroChoice);
        labels.Add(currentMacro is not null ? L.F("⏯  Makro: {0}", currentMacro) : L.T("⏯  Makro (Tastenfolge) …"));
        int selected = current switch
        {
            null => 0,
            { IsMacro: true } => actions.Count - 1,
            { IsKeyboard: true } or { Turbo: true } => -1,
            { IsSpecial: true } special => actions.IndexOf(special.Special.ToString()),
            { } target => actions.IndexOf(target.Target.ToString()),
        };
        new MaterialAlertDialogBuilder(host)
            .SetTitle(label)!
            .SetSingleChoiceItems([.. labels], selected, (d, e) =>
            {
                ((global::Android.App.Dialog)d!).Dismiss();
                if (actions[e.Which] == MacroChoice)
                {
                    EditMacro(name, kind, button, label, currentMacro, changed);
                    return;
                }
                SetAction(name, kind, button, actions[e.Which]);
                changed();
            })!
            .SetNegativeButton(L.T("Abbrechen"), (_, _) => { })!
            .Show();
    }

    /// <summary>Makro als Text mit Prüfung wie unter Windows (gemeinsame <see cref="MacroScript"/>), ohne Tastatur-Schritte.</summary>
    private void EditMacro(string name, ControllerKind kind, ProButtons button, string label, string? macro, Action changed)
    {
        var box = Box();
        box.AddView(Ui.Caption(host, L.F("Beim Drücken von „{0}“ wird diese Folge einmal abgespielt. Schritte durch Komma trennen, " +
                                         "je Schritt was gehalten wird und wie lange (ms). Tasten: A B X Y LB RB LT RT Up Down Left " +
                                         "Right LS RS Start Back Guide, mehrere gleichzeitig mit +. Warten: Pause.\n" +
                                         "Beispiele: „A 80, Pause 60, A 80“ (Doppeltipp) · „Down+B 150“", label)));
        var layout = new TextInputLayout(host) { Hint = L.T("Makro (Tastenfolge)") };
        var input = new TextInputEditText(layout.Context!) { Text = macro ?? "A 80, Pause 60, A 80" };
        layout.AddView(input);
        box.AddView(layout);
        var status = Ui.Caption(host, "");
        box.AddView(status);
        var dialog = new MaterialAlertDialogBuilder(host)
            .SetTitle(L.T("Makro (Tastenfolge)"))!
            .SetView(box)!
            .SetPositiveButton(L.T("Übernehmen"), (_, _) =>
            {
                if (ProfileEdit.ValidMacro(input.Text) is { } text)
                {
                    SetAction(name, kind, button, ButtonAction.Play(text).ToString());
                    changed();
                }
            })!
            .SetNegativeButton(L.T("Abbrechen"), (_, _) => { })!
            .Create()!;
        dialog.Show();
        var ok = dialog.GetButton((int)DialogButtonType.Positive)!;
        void Check()
        {
            string? text = ProfileEdit.ValidMacro(input.Text);
            MacroScript.TryParse(text, out var script);
            ok.Enabled = text is not null;
            status.Text = text is null ? L.T("Ungültig – bitte Schreibweise prüfen (siehe oben).")
                : L.F("✓ {0} Schritte, Dauer {1} ms", script.Steps.Count, script.TotalMs);
        }
        input.TextChanged += (_, _) => Check();
        Check();
    }

    private static void SetAction(string name, ControllerKind kind, ProButtons button, string? action)
    {
        if (Find(name) is not { } profile)
            return;
        ProfileEdit.Replace(S, ProfileEdit.WithAction(profile, kind, button, action));
        AppPrefs.SaveSettings();
    }
}
