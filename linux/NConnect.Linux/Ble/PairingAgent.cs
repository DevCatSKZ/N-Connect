using Tmds.DBus;

namespace NConnect.Linux.Ble;

[DBusInterface("org.bluez.Agent1")]
public interface IAgent1 : IDBusObject
{
    Task ReleaseAsync();
    Task<string> RequestPinCodeAsync(ObjectPath device);
    Task DisplayPinCodeAsync(ObjectPath device, string pinCode);
    Task<uint> RequestPasskeyAsync(ObjectPath device);
    Task DisplayPasskeyAsync(ObjectPath device, uint passkey, ushort entered);
    Task RequestConfirmationAsync(ObjectPath device, uint passkey);
    Task RequestAuthorizationAsync(ObjectPath device);
    Task AuthorizeServiceAsync(ObjectPath device, string uuid);
    Task CancelAsync();
}

/// <summary>Nur die Methoden von org.bluez.Device1, die über die Agent-Verbindung laufen müssen.</summary>
[DBusInterface("org.bluez.Device1")]
public interface IPairDevice : IDBusObject
{
    Task PairAsync();
    Task ConnectAsync();
}

[DBusInterface("org.bluez.AgentManager1")]
public interface IAgentManager1 : IDBusObject
{
    Task RegisterAgentAsync(ObjectPath agent, string capability);
    Task UnregisterAgentAsync(ObjectPath agent);
}

/// <summary>
/// Bluetooth-Agent für die Kopplungen, die N-Connect selbst anstößt (Xbox, PlayStation, Switch 1 …). Fähigkeit
/// „NoInputNoOutput“: Kopplung ohne PIN-Eingabe („Just Works“), wie bei Gamepads üblich. Er wird nicht als
/// Standard-Agent angemeldet – Kopplungen aus den LibreELEC-Einstellungen laufen weiter über deren eigenen Agenten.
/// </summary>
public sealed class PairingAgent : IAgent1
{
    public static readonly ObjectPath Path = new("/tv/nconnect/agent");
    // Eigene D-Bus-Verbindung: Die gemeinsame (Connection.System) erlaubt keine eigenen Objekte, und BlueZ fragt bei
    // einer Kopplung den Agenten derjenigen Verbindung, die Pair() aufruft – Agent und Pair() gehören zusammen.
    private Connection? _connection;
    private IAgentManager1? _manager;

    public ObjectPath ObjectPath => Path;

    public async Task RegisterAsync()
    {
        _connection = new Connection(Address.System);
        await _connection.ConnectAsync().ConfigureAwait(false);
        await _connection.RegisterObjectAsync(this).ConfigureAwait(false);
        _manager = _connection.CreateProxy<IAgentManager1>("org.bluez", "/org/bluez");
        await _manager.RegisterAgentAsync(Path, "NoInputNoOutput").ConfigureAwait(false);
        Log.Info("Kopplungs-Agent angemeldet");
    }

    /// <summary>Koppeln über die Agent-Verbindung (BlueZ fragt dann diesen Agenten).</summary>
    public Task PairAsync(ObjectPath device) =>
        (_connection ?? throw new InvalidOperationException("Agent nicht angemeldet."))
        .CreateProxy<IPairDevice>("org.bluez", device).PairAsync();

    public Task ConnectAsync(ObjectPath device) =>
        (_connection ?? throw new InvalidOperationException("Agent nicht angemeldet."))
        .CreateProxy<IPairDevice>("org.bluez", device).ConnectAsync();

    public async Task UnregisterAsync()
    {
        try
        {
            if (_manager is not null)
                await _manager.UnregisterAgentAsync(Path).ConfigureAwait(false);
            _connection?.UnregisterObject(Path);
        }
        catch (Exception)
        {
            // bluetoothd schon weg
        }
        _connection?.Dispose();
        _connection = null;
    }

    public Task ReleaseAsync() => Task.CompletedTask;

    // Ältere Gamepads mit fester PIN (manche 8BitDo/Generika) nehmen „0000“.
    public Task<string> RequestPinCodeAsync(ObjectPath device) => Task.FromResult("0000");

    public Task DisplayPinCodeAsync(ObjectPath device, string pinCode) => Task.CompletedTask;

    public Task<uint> RequestPasskeyAsync(ObjectPath device) => Task.FromResult(0u);

    public Task DisplayPasskeyAsync(ObjectPath device, uint passkey, ushort entered) => Task.CompletedTask;

    public Task RequestConfirmationAsync(ObjectPath device, uint passkey) => Task.CompletedTask;

    public Task RequestAuthorizationAsync(ObjectPath device) => Task.CompletedTask;

    public Task AuthorizeServiceAsync(ObjectPath device, string uuid) => Task.CompletedTask;

    public Task CancelAsync() => Task.CompletedTask;
}
