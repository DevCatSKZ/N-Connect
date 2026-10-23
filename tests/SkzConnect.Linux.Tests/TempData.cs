using System.Text.Json.Nodes;

namespace SkzConnect.Linux.Tests;

/// <summary>Eigener Datenordner je Test (settings.json, status.json, slots.json …), danach gelöscht.</summary>
internal sealed class TempData : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "skzconnect-linux-tests-" + Guid.NewGuid().ToString("N"));

    public TempData()
    {
        Directory.CreateDirectory(Dir);
        // Ohne Spieler-LEDs und Verbinden-Klick: Die Testgeräte haben keine Verbindung, über die etwas gesendet würde.
        File.WriteAllText(Path.Combine(Dir, "settings.json"),
            """{ "GameLeds": false, "ConnectFeedback": false, "DsuServer": false }""");
    }

    /// <summary>
    /// Dienst ohne virtuelle Controller – auch unter Linux wird nie ein uinput-Gerät angelegt. Die Nachfrage beim D-Bus,
    /// ob bluetoothd läuft, beantwortet <paramref name="bluetoothd"/>; ohne Angabe scheitert sie (wie ohne System-D-Bus),
    /// nie wird der echte D-Bus gefragt.
    /// </summary>
    public Daemon NewDaemon(Func<Task<bool>>? bluetoothd = null) => new(Dir, adapterName: null)
    {
        CreatePad = _ => null,
        BluetoothServiceRunningAsync = bluetoothd ?? (() => throw new InvalidOperationException("kein D-Bus im Test")),
    };

    public JsonObject ReadStatus() => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, "status.json")))!;

    /// <summary>status.json schreiben lassen und lesen.</summary>
    public JsonObject Status(Daemon daemon, string state = "running")
    {
        daemon.WriteStatus(state);
        return ReadStatus();
    }

    /// <summary>Spieler laut status.json, sobald es <paramref name="expected"/> sind (höchstens 5 s warten – OnLost läuft im Hintergrund).</summary>
    public async Task<JsonArray> PlayersAsync(Daemon daemon, int expected)
    {
        var players = (JsonArray)Status(daemon)["players"]!;
        for (int i = 0; i < 100 && players.Count != expected; i++)
        {
            await Task.Delay(50);
            players = (JsonArray)Status(daemon)["players"]!;
        }
        return players;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Dir, recursive: true);
        }
        catch (IOException)
        {
            // Rest im Temp-Ordner stört nicht
        }
    }
}
