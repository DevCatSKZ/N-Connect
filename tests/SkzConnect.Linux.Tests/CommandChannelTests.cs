namespace SkzConnect.Linux.Tests;

/// <summary>Befehlskanal (stdin) des Dienstes: Ein fehlgeschlagener Befehl darf die folgenden nicht verhindern.</summary>
public class CommandChannelTests
{
    [Fact]
    public async Task FehlerInEinemBefehl_FolgendeBefehleKommenTrotzdemAn()
    {
        var handled = new List<string>();
        var input = new StringReader("gamepad-disconnect AA:BB:CC:DD:EE:FF\n  status  \nscan\n");

        bool ended = await Daemon.ReadCommandsAsync(input, line =>
        {
            handled.Add(line);
            return line switch
            {
                // z. B. BlueZ „NotConnected“: Das Gamepad hat sich gerade selbst getrennt
                "gamepad-disconnect AA:BB:CC:DD:EE:FF" => throw new InvalidOperationException("org.bluez.Error.NotConnected"),
                // Zeitüberschreitung eines D-Bus-Aufrufs – ein Abbruch, aber nicht der des Dienstes
                "status" => Task.FromException(new TaskCanceledException("D-Bus-Zeitüberschreitung")),
                _ => Task.CompletedTask,
            };
        }, CancellationToken.None);

        Assert.True(ended); // Eingabe zu Ende = stdin geschlossen (mit --kodi beendet sich der Dienst dann)
        Assert.Equal(new[] { "gamepad-disconnect AA:BB:CC:DD:EE:FF", "status", "scan" }, handled);
    }

    [Fact]
    public async Task DienstWirdBeendet_KanalEndet()
    {
        using var stop = new CancellationTokenSource();
        var handled = new List<string>();
        var input = new StringReader("disconnect-all\nstatus\n");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Daemon.ReadCommandsAsync(input, line =>
        {
            handled.Add(line);
            stop.Cancel();
            return Task.FromCanceled(stop.Token);
        }, stop.Token));

        Assert.Equal(new[] { "disconnect-all" }, handled);
    }

    [Fact]
    public async Task SchonBeendet_LiestNichts()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        int calls = 0;

        bool ended = await Daemon.ReadCommandsAsync(new StringReader("status\n"), _ =>
        {
            calls++;
            return Task.CompletedTask;
        }, stop.Token);

        Assert.False(ended);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task EchteBefehle_OhneAdapterUndMitUnsinn_BeendenDenKanalNicht()
    {
        using var data = new TempData();
        await using var daemon = data.NewDaemon();
        // Ohne Bluetooth-Sitzung: Gamepad-Befehle, Spieler, die es nicht gibt, kaputte und unbekannte Zeilen.
        var input = new StringReader(string.Join('\n',
            "gamepad-disconnect AA:BB:CC:DD:EE:FF", "gamepad-forget AA:BB:CC:DD:EE:FF", "disconnect 3", "disconnect x",
            "disconnect", "", "unbekannt 1 2", "scan", "reload", "status", "disconnect-all") + "\n");

        bool ended = await Daemon.ReadCommandsAsync(input, daemon.HandleCommandAsync, CancellationToken.None);

        Assert.True(ended);
        Assert.Equal("linux", (string?)data.Status(daemon)["platform"]);
    }
}
