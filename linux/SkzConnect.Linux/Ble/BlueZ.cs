using Linux.Bluetooth;
using Tmds.DBus;

namespace SkzConnect.Linux.Ble;

/// <summary>
/// Kleine Helfer rund um BlueZ (D-Bus). Statt der Device-Objekte von Linux.Bluetooth werden hier schlichte Proxys
/// benutzt: Jedes Device-Objekt meldet beim Anlegen einen eigenen Eigenschaften-Beobachter beim D-Bus an und gibt ihn
/// erst mit Dispose wieder frei – bei Abfragen alle paar Sekunden sammelten sich sonst über die Laufzeit Tausende an.
/// </summary>
public static class BlueZ
{
    public const string Service = "org.bluez";
    public const string DeviceInterface = "org.bluez.Device1";

    /// <summary>Proxy für ein Gerät (ohne Beobachter, kostet keinen D-Bus-Aufruf).</summary>
    public static IDevice1 Device(ObjectPath path) => Connection.System.CreateProxy<IDevice1>(Service, path);

    /// <summary>Akku-Schnittstelle eines Geräts; wirft beim Lesen, wenn das Gerät keine hat.</summary>
    public static IBattery1 Battery(ObjectPath path) => Connection.System.CreateProxy<IBattery1>(Service, path);

    /// <summary>Pfade aller Geräte dieses Adapters (ein D-Bus-Aufruf; Linux.Bluetooth liefert die aller Adapter).</summary>
    public static async Task<List<ObjectPath>> DevicePathsAsync(Adapter adapter)
    {
        string prefix = adapter.ObjectPath + "/";
        return [.. (await adapter.GetDevicesPathsAsync().ConfigureAwait(false)).Where(p => p.ToString().StartsWith(prefix, StringComparison.Ordinal))];
    }

    /// <summary>
    /// Kann der Adapter Bluetooth LE? BlueZ meldet das in „Roles“ („central“ = LE-Verbindungen möglich).
    /// null = unbekannt (BlueZ zu alt für „Roles“) – dann wird LE angenommen. Ohne LE gehen Switch-2-Controller nicht,
    /// klassische Gamepads (Xbox, PlayStation, Switch 1 …) schon.
    /// </summary>
    public static async Task<bool?> SupportsLeAsync(Adapter adapter)
    {
        try
        {
            string[] roles = await adapter.GetAsync<string[]>("Roles").ConfigureAwait(false);
            return roles.Contains("central");
        }
        catch (Exception)
        {
            return null;
        }
    }
}
