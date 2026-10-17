using Tmds.DBus;

namespace NConnect.Linux.Tests;

/// <summary>
/// Vertrag der status.json mit dem Kodi-Add-on (gleich bei Linux-Dienst, Windows- und Android-App): „platform“ sagt, wer
/// schreibt; „bluetooth“ ist genau einer von on | off | missing | no_le und fehlt, solange nichts geprüft ist.
/// </summary>
public class StatusJsonTests
{
    [Fact]
    public async Task VorDerErstenPruefung_Starting_OhneBluetoothFeld()
    {
        using var data = new TempData();
        await using var daemon = data.NewDaemon();

        var status = data.Status(daemon);

        Assert.Equal("starting", (string?)status["state"]); // Add-on zeigt „startet …“ statt eines falschen Hinweises
        Assert.Equal("linux", (string?)status["platform"]);
        Assert.False(status.ContainsKey("bluetooth"));
        Assert.Empty(status["players"]!.AsArray());
    }

    [Theory]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData("missing")]
    [InlineData("no_le")]
    public async Task BluetoothZustand_StehtUnveraendertInDerStatusdatei(string state)
    {
        using var data = new TempData();
        await using var daemon = data.NewDaemon();

        daemon.SetAdapterState(state);
        var status = data.Status(daemon);

        Assert.Equal("running", (string?)status["state"]);
        Assert.Equal("linux", (string?)status["platform"]);
        Assert.Equal(state, (string?)status["bluetooth"]);
    }

    [Fact]
    public async Task Beendet_Stopped_AuchOhneBluetoothPruefung()
    {
        using var data = new TempData();
        await using var daemon = data.NewDaemon();

        var status = data.Status(daemon, "stopped");

        Assert.Equal("stopped", (string?)status["state"]);
        Assert.Equal("linux", (string?)status["platform"]);
        Assert.False(File.Exists(Path.Combine(data.Dir, "status.json.tmp")));
    }

    [Fact]
    public async Task DienstEnde_SchreibtStopped()
    {
        using var data = new TempData();
        var daemon = data.NewDaemon();
        daemon.SetAdapterState("on");

        await daemon.DisposeAsync();

        Assert.Equal("stopped", (string?)data.ReadStatus()["state"]);
    }

    [Theory]
    [InlineData("org.freedesktop.DBus.Error.ServiceUnknown", true)]
    [InlineData("org.freedesktop.DBus.Error.NameHasNoOwner", true)]
    [InlineData("org.bluez.Error.NotReady", false)]
    [InlineData("org.freedesktop.DBus.Error.AccessDenied", false)]
    public void BluetoothdLaeuftNicht_GiltAlsAus(string errorName, bool off)
    {
        Assert.Equal(off, Daemon.IsBluetoothServiceDown(new DBusException(errorName, "Test")));
    }

    [Fact]
    public void AndereFehler_SindKeinAusgeschaltetesBluetooth()
    {
        Assert.False(Daemon.IsBluetoothServiceDown(new IOException("Verbindung zum D-Bus abgelehnt")));
        Assert.False(Daemon.IsBluetoothServiceDown(new InvalidOperationException()));
    }
}
