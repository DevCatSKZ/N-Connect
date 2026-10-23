using SkzConnect.Linux.Ble;
using Switch2Pro.Protocol;

namespace SkzConnect.Linux.Tests;

/// <summary>
/// Verbindungsabbruch: Lost meldet sich nur einmal. Reißt die Verbindung während der Einrichtung ab – bevor der Dienst
/// den Controller einem Spieler zugeordnet und auf Lost gehört hat –, muss er ihn trotzdem wieder entfernen. Sonst bliebe
/// ein Geist-Controller, und neue Verbindungsversuche würden als „schon verbunden“ verworfen.
/// </summary>
public class ConnectionLostTests
{
    private const string Address = "AA:BB:CC:DD:EE:01";

    [Fact]
    public void Lost_KommtNurEinmal_IsLostBleibtGesetzt()
    {
        var device = new Switch2Device(Address, ControllerKind.Pro2);
        int raised = 0;
        device.Lost += _ => raised++;

        device.RaiseLost();
        device.RaiseLost(); // Wächter und ATT-Socket melden denselben Abbruch

        Assert.Equal(1, raised);
        Assert.True(device.IsLost); // daran erkennt ein später angemeldeter Handler den Abbruch
    }

    [Fact]
    public async Task AbbruchVorDemZuordnen_ControllerWirdWiederEntfernt()
    {
        using var data = new TempData();
        await using var daemon = data.NewDaemon();
        var device = new Switch2Device(Address, ControllerKind.Pro2);
        device.RaiseLost(); // nach der IsLost-Prüfung in ConnectAsync, vor dem Anmelden des Lost-Handlers

        daemon.AddToPlayer(device);

        Assert.Empty(await data.PlayersAsync(daemon, expected: 0));
    }

    [Fact]
    public async Task AbbruchNachDemZuordnen_ControllerWirdEntfernt()
    {
        using var data = new TempData();
        await using var daemon = data.NewDaemon();
        var device = new Switch2Device(Address, ControllerKind.JoyCon2Left);
        daemon.AddToPlayer(device);
        Assert.Single(await data.PlayersAsync(daemon, expected: 1));

        device.RaiseLost();

        Assert.Empty(await data.PlayersAsync(daemon, expected: 0));
    }

    [Fact]
    public async Task OhneAbbruch_ControllerBleibtVerbunden()
    {
        using var data = new TempData();
        await using var daemon = data.NewDaemon();
        var device = new Switch2Device(Address, ControllerKind.Pro2);

        daemon.AddToPlayer(device);
        await Task.Delay(300); // ein irrtümlich ausgelöstes OnLost liefe im Hintergrund

        var status = data.Status(daemon);
        var player = Assert.Single(status["players"]!.AsArray())!;
        Assert.Equal(1, (int)player["player"]!);
        Assert.Equal(Address, (string?)player["devices"]![0]!["address"]);
        var known = Assert.Single(status["known"]!.AsArray())!;
        Assert.True((bool)known["connected"]!);
    }
}
