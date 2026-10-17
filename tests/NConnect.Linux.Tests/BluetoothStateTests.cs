using Tmds.DBus;

namespace NConnect.Linux.Tests;

/// <summary>
/// Bluetooth-Zustand, wenn eine Runde (Adapter suchen, einrichten, überwachen) mit einem Fehler endet oder BlueZ keinen
/// Adapter meldet. Unter LibreELEC hält bei ausgeschaltetem Bluetooth eine D-Bus-Aktivierungsdatei org.bluez „startbar“:
/// Statt ServiceUnknown kommt nach 25 s TimedOut oder ein systemd-Fehler. Auch dann muss aus „on“ ein „off“ werden –
/// sonst erschiene der Hinweis „Bluetooth ist aus“ nie, und Menü und Widget zeigten weiter Bluetooth an.
/// </summary>
public class BluetoothStateTests
{
    private static DBusException DBus(string name) => new(name, "Test");

    private static string? Bluetooth(TempData data, Daemon daemon) => (string?)data.Status(daemon)["bluetooth"];

    /// <summary>Antwort des D-Bus auf „läuft bluetoothd?“; zählt die Nachfragen.</summary>
    private sealed class Bus(bool running)
    {
        public int Asked { get; private set; }

        public Task<bool> AskAsync()
        {
            Asked++;
            return Task.FromResult(running);
        }
    }

    [Theory]
    [InlineData("org.freedesktop.DBus.Error.TimedOut", "on")]
    [InlineData("org.freedesktop.DBus.Error.TimedOut", "no_le")]
    [InlineData("org.freedesktop.DBus.Error.TimedOut", null)] // Start bei ausgeschaltetem Bluetooth: „off“, nicht „missing“
    [InlineData("org.freedesktop.systemd1.NoSuchUnit", "on")]
    [InlineData("org.freedesktop.DBus.Error.Spawn.ChildExited", "on")]
    [InlineData("org.freedesktop.DBus.Error.ServiceUnknown", "on")]
    public async Task AktivierungVonBluetoothdScheitert_WirdAus(string error, string? before)
    {
        using var data = new TempData();
        var bus = new Bus(running: true);
        await using var daemon = data.NewDaemon(bus.AskAsync);
        if (before is not null)
            daemon.SetAdapterState(before);

        await daemon.OnRoundFailedAsync(DBus(error), adapterReached: false);

        Assert.Equal("off", Bluetooth(data, daemon));
        Assert.Equal(0, bus.Asked); // am Fehlernamen erkannt – keine Nachfrage nötig
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("on", false)]
    [InlineData("no_le", true)]
    [InlineData(null, false)]
    public async Task AndererFehler_BluetoothdLaeuftNicht_WirdAus(string? before, bool adapterReached)
    {
        using var data = new TempData();
        var bus = new Bus(running: false);
        await using var daemon = data.NewDaemon(bus.AskAsync);
        if (before is not null)
            daemon.SetAdapterState(before);

        // z. B. NoReply: bluetoothd hat sich mitten in der Abfrage beendet
        await daemon.OnRoundFailedAsync(DBus("org.freedesktop.DBus.Error.NoReply"), adapterReached);

        Assert.Equal("off", Bluetooth(data, daemon));
        Assert.Equal(1, bus.Asked);
    }

    [Theory]
    [InlineData("on")]
    [InlineData("no_le")]
    [InlineData("off")]
    [InlineData("missing")]
    public async Task BluetoothdLaeuft_FehlerBeimEinrichten_ZustandBleibt(string before)
    {
        // Ein Fehler, der bei jedem Einrichten wiederkehrt, darf den Zustand nicht alle paar Sekunden springen lassen
        // (jeder Wechsel ist eine Kodi-Meldung); die nächste Runde stellt ihn ohnehin neu fest.
        using var data = new TempData();
        await using var daemon = data.NewDaemon(new Bus(running: true).AskAsync);
        daemon.SetAdapterState(before);

        await daemon.OnRoundFailedAsync(DBus("org.bluez.Error.NotReady"), adapterReached: true);

        Assert.Equal(before, Bluetooth(data, daemon));
    }

    [Theory]
    [InlineData("on", "missing")]
    [InlineData("no_le", "missing")]
    [InlineData("off", "off")]
    [InlineData("missing", "missing")]
    public async Task BluetoothdLaeuft_KeinAdapterErreicht_OnIstVeraltet(string before, string expected)
    {
        using var data = new TempData();
        await using var daemon = data.NewDaemon(new Bus(running: true).AskAsync);
        daemon.SetAdapterState(before);

        await daemon.OnRoundFailedAsync(DBus("org.freedesktop.DBus.Error.AccessDenied"), adapterReached: false);

        Assert.Equal(expected, Bluetooth(data, daemon));
    }

    [Fact]
    public async Task OhneDBusAntwort_BeimStartMissing()
    {
        using var data = new TempData();
        await using var daemon = data.NewDaemon(); // Nachfrage scheitert

        await daemon.OnRoundFailedAsync(new IOException("Verbindung zum D-Bus abgelehnt"), adapterReached: false);

        Assert.Equal("missing", Bluetooth(data, daemon)); // nicht ewig „startet …“
        Assert.Equal("running", (string?)data.Status(daemon)["state"]);
    }

    [Theory]
    [InlineData(true, "on")]
    [InlineData(false, "missing")]
    public async Task OhneDBusAntwort_OnNurMitErreichtemAdapter(bool adapterReached, string expected)
    {
        using var data = new TempData();
        await using var daemon = data.NewDaemon();
        daemon.SetAdapterState("on");

        await daemon.OnRoundFailedAsync(new IOException("Verbindung zum D-Bus abgelehnt"), adapterReached);

        Assert.Equal(expected, Bluetooth(data, daemon));
    }

    [Fact]
    public async Task KeinAdapter_NachAus_EineRundeAbwarten()
    {
        // Bluetooth eben eingeschaltet: bluetoothd hat den D-Bus-Namen, seine Adapter aber noch nicht angemeldet.
        using var data = new TempData();
        await using var daemon = data.NewDaemon();
        daemon.SetAdapterState("off");

        daemon.OnNoAdapter();
        Assert.Equal("off", Bluetooth(data, daemon)); // kein kurzes „Kein Bluetooth-Adapter“

        daemon.OnNoAdapter();
        Assert.Equal("missing", Bluetooth(data, daemon)); // fehlt er auch in der nächsten Runde: wirklich keiner
    }

    [Fact]
    public async Task KeinAdapter_BeimStart_EineRundeAbwarten()
    {
        using var data = new TempData();
        await using var daemon = data.NewDaemon();

        daemon.OnNoAdapter();
        Assert.Equal("starting", (string?)data.Status(daemon)["state"]);
        Assert.Null(Bluetooth(data, daemon));

        daemon.OnNoAdapter();
        Assert.Equal("missing", Bluetooth(data, daemon));
    }

    [Theory]
    [InlineData("on")]
    [InlineData("no_le")]
    public async Task KeinAdapter_NachAn_SofortMissing(string before)
    {
        using var data = new TempData(); // Dongle gezogen, bluetoothd läuft weiter
        await using var daemon = data.NewDaemon();
        daemon.SetAdapterState(before);

        daemon.OnNoAdapter();

        Assert.Equal("missing", Bluetooth(data, daemon));
    }

    [Fact]
    public async Task KeinAdapter_AbwartenGiltJeZustand()
    {
        using var data = new TempData();
        await using var daemon = data.NewDaemon();
        daemon.SetAdapterState("off");
        daemon.OnNoAdapter();
        daemon.OnNoAdapter();
        Assert.Equal("missing", Bluetooth(data, daemon));

        daemon.SetAdapterState("off"); // bluetoothd wieder weg – beim nächsten Hochfahren wieder eine Runde Zeit
        daemon.OnNoAdapter();

        Assert.Equal("off", Bluetooth(data, daemon));
    }

    [Fact]
    public async Task LibreElec_BluetoothAusUndWiederAn_OhneFalscheHinweise()
    {
        // Ablauf mit Aktivierungsdatei: an → in den Einstellungen aus (Überwachung scheitert nach 25 s mit TimedOut,
        // ebenso jede weitere Suche) → wieder an (bluetoothd meldet zuerst keinen Adapter) → Adapter da.
        using var data = new TempData();
        var seen = new List<string?>();
        await using var daemon = data.NewDaemon(new Bus(running: false).AskAsync);
        daemon.SetAdapterState("on");
        seen.Add(Bluetooth(data, daemon));

        await daemon.OnRoundFailedAsync(DBus("org.freedesktop.DBus.Error.TimedOut"), adapterReached: true);
        seen.Add(Bluetooth(data, daemon));
        await daemon.OnRoundFailedAsync(DBus("org.freedesktop.DBus.Error.TimedOut"), adapterReached: false);
        seen.Add(Bluetooth(data, daemon));
        daemon.OnNoAdapter();
        seen.Add(Bluetooth(data, daemon));
        daemon.SetAdapterState("on"); // nächste Runde: Adapter gefunden und eingeschaltet
        seen.Add(Bluetooth(data, daemon));

        Assert.Equal(new string?[] { "on", "off", "off", "off", "on" }, seen);
    }
}
