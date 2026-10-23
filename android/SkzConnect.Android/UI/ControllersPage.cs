using Android.Bluetooth;
using Android.Content;
using Android.Widget;
using SkzConnect.Android.Core;
using SkzConnect.Android.Output;
using SkzConnect.Android.Touch;
using Switch2Pro.Protocol;
using View = Android.Views.View;

namespace SkzConnect.Android.UI;

/// <summary>
/// Startseite: Ist alles bereit? Welche Controller sind verbunden (Akku, Live-Eingaben)? Wie verbinde ich einen?
/// </summary>
public sealed class ControllersPage(MainActivity host) : IPage
{
    private LinearLayout? _column;
    private string _signature = "";
    private readonly Dictionary<Player, (TextView Info, TextView Live)> _live = [];

    public string Title => L.T("Controller");

    public View Build()
    {
        var (scroll, column) = Ui.Page(host);
        _column = column;
        _signature = "";
        Refresh();
        return scroll;
    }

    public void Refresh()
    {
        if (_column is null)
            return;
        var service = ControllerService.Instance;
        var players = service?.Players ?? [];
        string signature = string.Join("|", players.Select(p => $"{p.Index}:{string.Join(",", p.Devices.Select(d => d.Address))}"))
                           + $"#{service is null}{service?.BluetoothOn}{Permissions.HasBluetooth(host)}"
                           + $"#{OutputRouter.Describe()}#{OutputStatus().State}#{service?.Elsewhere.Count}"
                           + $"#{AppPrefs.TouchEnabled}{TouchService.Instance is null}"
                           + "#" + string.Join(",", KnownControllers.All.Select(k => k.Address))
                           + "#" + string.Join(",", service?.Elsewhere.Select(f => f.Address) ?? []);
        if (signature != _signature)
        {
            _signature = signature;
            Rebuild(service, players);
        }
        foreach (var (player, views) in _live)
        {
            views.Info.Text = PlayerInfo(player);
            views.Live.Text = LiveText(player.LastGamepad);
        }
    }

    private void Rebuild(ControllerService? service, IReadOnlyList<Player> players)
    {
        var col = _column!;
        col.RemoveAllViews();
        _live.Clear();

        if (!Permissions.HasBluetooth(host))
        {
            col.AddView(Ui.Card(host,
                Ui.StatusRow(host, PathState.NeedsAction, "Bluetooth-Zugriff fehlt",
                    "SKZ Connect braucht die Berechtigung „Geräte in der Nähe“, um deine Controller zu finden.",
                    "Erlauben", () =>
                    {
                        if (Permissions.PermanentlyDenied(host))
                            Permissions.OpenAppSettings(host);
                        else
                            Permissions.Request(host);
                    })));
        }
        else if (service is null)
        {
            col.AddView(Ui.Card(host,
                Ui.StatusRow(host, PathState.NeedsAction, "SKZ Connect ist pausiert",
                    "Starte die Suche, damit sich deine Controller verbinden können.", "Suche starten",
                    host.EnsureServiceRunning)));
        }
        else if (!service.BluetoothOn)
        {
            col.AddView(Ui.Card(host,
                Ui.StatusRow(host, PathState.NeedsAction, "Bluetooth ist aus",
                    "Schalte Bluetooth ein – SKZ Connect sucht dann sofort nach Controllern.", "Einschalten",
                    () => host.StartActivity(new Intent(BluetoothAdapter.ActionRequestEnable)))));
        }

        var (state, title, subtitle) = OutputStatus();
        col.AddView(Ui.Card(host, Ui.StatusRow(host, state, title, subtitle,
            state == PathState.Ready ? "Ändern" : "Beheben",
            () => host.StartActivity(new Intent(host, typeof(SetupWizardActivity)).PutExtra(SetupWizardActivity.ExtraStartAtOutput, true)))));

        if (players.Count > 0)
        {
            col.AddView(Ui.Section(host, players.Count == 1 ? "VERBUNDEN" : L.F("VERBUNDEN ({0})", players.Count)));
            foreach (var p in players)
                col.AddView(PlayerCard(service!, p));
        }
        else if (service is not null && service.BluetoothOn)
        {
            col.AddView(Ui.Card(host,
                Ui.Title(host, "Controller verbinden"),
                Ui.Space(host, 6),
                Ui.Body(host, Texts.PairHowTo),
                Ui.Space(host, 8),
                Ui.Caption(host, Texts.ReconnectHowTo)));
        }

        // Fremd gekoppelte Controller, die hier noch nie verbunden waren (die bekannten stehen unten in der Liste).
        var knownAddresses = KnownControllers.All.Select(k => k.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (service is not null && service.Elsewhere.Any(f => !knownAddresses.Contains(f.Address)))
        {
            var card = Ui.Card(host, Ui.Title(host, "In der Nähe, aber woanders gekoppelt"), Ui.Space(host, 4),
                Ui.Caption(host, Texts.OtherHostHint));
            foreach (var f in service.Elsewhere.Where(f => !knownAddresses.Contains(f.Address)))
                Ui.Inner(card).AddView(Ui.Body(host, $"• {Texts.ShortName(f.Kind)}  ({f.Address})"));
            col.AddView(card);
        }

        // Gekoppelte, gerade nicht verbundene Controller – mit Namen und was zu tun ist.
        var connected = players.SelectMany(p => p.Devices.Select(d => d.Address)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var elsewhere = (service?.Elsewhere ?? []).Select(f => f.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var known = KnownControllers.All.Where(k => !connected.Contains(k.Address)).ToList();
        if (known.Count > 0)
        {
            col.AddView(Ui.Section(host, "GEKOPPELTE CONTROLLER"));
            foreach (var k in known)
            {
                string status = elsewhere.Contains(k.Address)
                    ? "Mit einem anderen Gerät gekoppelt – SYNC drücken, um ihn hier zu nutzen"
                    : "Nicht verbunden – eine Taste am Controller drücken";
                string details = (k.Serial is { Length: > 0 } s ? L.F("Seriennr. {0} · ", s) : "") + L.F("zuletzt {0}", k.LastConnected.ToString("g"));
                string address = k.Address;
                col.AddView(Ui.Card(host,
                    Ui.Title(host, Texts.ShortName(k.Kind)),
                    Ui.Caption(host, status),
                    Ui.Caption(host, details),
                    Ui.Button(host, "Vergessen", () => KnownControllers.Forget(address), Ui.ButtonStyle.Text)));
            }
        }

        bool touch = AppPrefs.TouchEnabled || AppPrefs.Mode == OutputPath.Touch;
        if (touch && TouchService.Instance is not null)
        {
            col.AddView(Ui.Card(host,
                Ui.Title(host, "Touch-Steuerung"),
                Ui.Space(host, 4),
                Ui.Caption(host, "Öffne dein Spiel, ziehe die Benachrichtigungsleiste herunter und tippe auf " +
                                 "„Touch-Layout bearbeiten“. Dann ziehst du die Tasten auf die Bildschirmknöpfe des Spiels."),
                Ui.Button(host, "Hier ausprobieren", () => TouchService.Instance?.ShowEditor(), Ui.ButtonStyle.Outlined)));
        }

        if (players.Count > 0)
            col.AddView(Ui.Button(host, "Alle Controller trennen", () => service?.DisconnectAll(), Ui.ButtonStyle.Text));
    }

    private View PlayerCard(ControllerService service, Player p)
    {
        var info = Ui.Caption(host, PlayerInfo(p));
        var live = Ui.Body(host, LiveText(p.LastGamepad));
        live.SetPadding(0, Ui.Dp(host, 8), 0, 0);
        var card = Ui.Card(host,
            Ui.Title(host, L.F("Spieler {0} · {1}", p.Index + 1, p.Name)),
            info,
            live,
            Ui.Buttons(host,
                Ui.Button(host, "Vibration testen", () => TestRumble(p), Ui.ButtonStyle.Outlined),
                Ui.Button(host, "Trennen", () => service.DisconnectPlayer(p), Ui.ButtonStyle.Text)));
        _live[p] = (info, live);
        return card;
    }

    private static string PlayerInfo(Player p)
    {
        var parts = new List<string> { Texts.Battery(p.BatteryPercent, p.Charging) };
        double rate = p.Devices.Count > 0 ? p.Devices.Min(d => d.ReportRate) : 0;
        if (rate > 0)
            parts.Add(L.F("{0} Berichte/s", rate.ToString("0")));
        parts.Add(L.T(p.HasOutput ? "Ausgabe aktiv" : "keine Ausgabe – siehe oben"));
        return string.Join(" · ", parts);
    }

    /// <summary>Was gerade gedrückt ist – so sieht man sofort, dass der Controller ankommt.</summary>
    private static string LiveText(GamepadState g)
    {
        var parts = new List<string>();
        var names = new (XButtons Flag, string Name)[]
        {
            (XButtons.A, "A"), (XButtons.B, "B"), (XButtons.X, "X"), (XButtons.Y, "Y"), (XButtons.LB, "LB"),
            (XButtons.RB, "RB"), (XButtons.Start, "Start"), (XButtons.Back, "Zurück"), (XButtons.Guide, "Home"),
            (XButtons.LS, "L3"), (XButtons.RS, "R3"), (XButtons.Up, "↑"), (XButtons.Down, "↓"),
            (XButtons.Left, "←"), (XButtons.Right, "→"),
        };
        foreach (var (flag, name) in names)
            if (g.Buttons.HasFlag(flag))
                parts.Add(L.T(name));
        if (g.LeftTrigger > 10)
            parts.Add($"LT {g.LeftTrigger * 100 / 255} %");
        if (g.RightTrigger > 10)
            parts.Add($"RT {g.RightTrigger * 100 / 255} %");
        string Stick(string name, short x, short y)
        {
            float fx = x / 32767f, fy = y / 32767f;
            if (MathF.Sqrt(fx * fx + fy * fy) < 0.25f)
                return "";
            string[] arrows = ["→", "↗", "↑", "↖", "←", "↙", "↓", "↘"];
            int sector = (int)MathF.Round(MathF.Atan2(fy, fx) / (MathF.PI / 4) + 8) % 8;
            return $"{name} {arrows[sector]}";
        }
        foreach (var s in new[] { Stick("L-Stick", g.LeftX, g.LeftY), Stick("R-Stick", g.RightX, g.RightY) })
            if (s.Length > 0)
                parts.Add(s);
        return parts.Count == 0 ? L.T("Drück eine Taste – sie erscheint hier.") : L.T("Eingabe: ") + string.Join("  ", parts);
    }

    private static void TestRumble(Player p)
    {
        float strength = AppPrefs.Settings.RumbleStrength;
        foreach (var d in p.Devices)
            d.SetRumble(160, 160, strength);
        Task.Delay(400).ContinueWith(_ =>
        {
            foreach (var d in p.Devices)
                d.SetRumble(0, 0, strength);
        });
    }

    /// <summary>Status des Ausgabewegs in Alltagssprache (übersetzt).</summary>
    public static (PathState State, string Title, string Subtitle) OutputStatus()
    {
        var (state, title, subtitle) = OutputStatusGerman();
        return (state, L.T(title), L.T(subtitle));
    }

    private static (PathState State, string Title, string Subtitle) OutputStatusGerman()
    {
        var shell = OutputRouter.Shell;
        bool touch = AppPrefs.TouchEnabled || AppPrefs.Mode == OutputPath.Touch;
        if (shell is not null)
            return (PathState.Ready, L.F("Bereit: virtueller Xbox-Controller ({0})", shell.Name),
                L.T("Spiele und Emulatoren erkennen deine Controller als Xbox-Controller.") + (touch ? L.T(" Touch-Steuerung ist zusätzlich an.") : ""));
        switch (AppPrefs.Mode)
        {
            case OutputPath.Touch when TouchService.Instance is not null:
                return (PathState.Ready, "Bereit: Touch-Steuerung", "Tasten werden zu Bildschirmberührungen.");
            case OutputPath.Touch:
                return (PathState.NeedsAction, "Touch-Steuerung ist ausgeschaltet",
                    "Schalte „SKZ Connect Touch-Steuerung“ in den Bedienungshilfen ein.");
            case OutputPath.Shizuku when !ShizukuShell.IsInstalled():
                return (PathState.NeedsAction, "Shizuku ist nicht installiert", "Ohne Shizuku kann SKZ Connect keinen Xbox-Controller anlegen.");
            case OutputPath.Shizuku when !ShizukuShell.Instance.IsAvailable():
                return (PathState.NeedsAction, "Shizuku ist nicht gestartet",
                    "Nach jedem Neustart einmal in der Shizuku-App auf „Starten“ tippen.");
            case OutputPath.Shizuku when ShizukuShell.Instance.HasPermission():
                return (PathState.NeedsAction, "Shizuku wird verbunden …", "Einen Moment – SKZ Connect übernimmt den Zugriff automatisch.");
            case OutputPath.Shizuku:
                return (PathState.NeedsAction, "SKZ Connect hat noch keinen Shizuku-Zugriff", "Tippe auf „Beheben“ und erlaube den Zugriff.");
            case OutputPath.Root:
                return (PathState.NeedsAction, "Kein Root-Zugriff", "Erlaube SKZ Connect den Root-Zugriff (z. B. in Magisk).");
            default:
                return (PathState.NeedsAction, "Noch kein Ausgabeweg eingerichtet",
                    "Richte Shizuku, Root oder die Touch-Steuerung ein, damit Spiele deine Controller sehen.");
        }
    }
}
