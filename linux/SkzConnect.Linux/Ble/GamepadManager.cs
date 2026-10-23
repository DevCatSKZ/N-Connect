using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using Switch2Pro.Protocol;
using Advertisement = Switch2Pro.Protocol.Advertisement;

namespace SkzConnect.Linux.Ble;

/// <summary>Ein gekoppeltes Standard-Gamepad (Xbox, PlayStation, Switch 1 …) für Status und Menü.</summary>
public sealed record GamepadInfo(string Address, string Name, bool Connected, int Battery);

/// <summary>
/// Verwaltet alle Gamepads, die Linux selbst kann (Xbox: xpad/hid-microsoft, PlayStation: hid-playstation/hid-sony,
/// Switch 1 Pro/Joy-Con: hid-nintendo, Steam …). SKZ Connect übernimmt nur das Koppeln: Ein Gamepad im Kopplungsmodus
/// wird automatisch gekoppelt, als vertrauenswürdig markiert und verbunden – danach verbindet es sich per Tastendruck
/// selbst (BlueZ). Eingaben, Vibration und Akku liefert der Kernel-Treiber, Kodi sieht den Controller direkt.
/// Switch-2-Controller sind ausgenommen (die steuert SKZ Connect selbst). Wii-Fernbedienungen: Kopplung mit der
/// SYNC-Taste im Batteriefach (dauerhaft; mit 1+2 nur vorübergehend).
/// </summary>
public sealed partial class GamepadManager(Adapter adapter)
{
    private const int RetryAfterFailureMs = 20000;
    private readonly ConcurrentDictionary<string, byte> _busy = new();
    private readonly ConcurrentDictionary<string, long> _quietUntil = new();
    private readonly ConcurrentDictionary<string, bool> _lastConnected = new();
    private volatile IReadOnlyList<GamepadInfo> _known = [];

    public bool Enabled { get; set; } = true;
    /// <summary>Eigener Kopplungs-Agent (Kopplung ohne PIN); null = BlueZ-Standard.</summary>
    public PairingAgent? Agent { get; set; }
    public IReadOnlyList<GamepadInfo> Known => _known;

    /// <summary>(Ereignis, Adresse, Name): "paired", "pair-failed", "connected", "disconnected".</summary>
    public event Action<string, string, string>? Changed;

    [GeneratedRegex(@"Xbox|Wireless Controller|DualSense|DualShock|PLAYSTATION|Pro Controller|Joy-Con|8BitDo|Stadia|Steam Controller|Gamepad|GameSir|Moga|Nimbus|Shield", RegexOptions.IgnoreCase)]
    private static partial Regex GamepadName();

    /// <summary>Ist das ein Gamepad? Geräteklasse (BR/EDR), Erscheinungsbild (LE) oder bekannter Name.</summary>
    public static bool IsGamepad(Device1Properties p)
    {
        // Wii-Fernbedienung, Wii Remote Plus, Wii U Pro Controller: Die feste Wii-PIN (umgedrehte Adapter-Adresse)
        // setzt BlueZ selbst (Modul „wiimote“); den Rest übernimmt der Kernel-Treiber hid-wiimote.
        if (p.Name is { } name && name.StartsWith("Nintendo RVL-CNT", StringComparison.OrdinalIgnoreCase))
            return true;
        uint cls = p.Class;
        if (cls != 0 && ((cls >> 8) & 0x1F) == 0x05 && ((cls >> 2) & 0x0F) is 0x01 or 0x02)
            return true; // Peripheriegerät: Joystick/Gamepad
        if (p.Appearance is 0x03C3 or 0x03C4)
            return true; // LE: Joystick/Gamepad
        return p.Name is { Length: > 0 } n && GamepadName().IsMatch(n);
    }

    private static bool IsSwitch2(Device1Properties p) =>
        p.ManufacturerData is { } md && md.TryGetValue(Gatt.NintendoCompanyId, out var raw) && raw is byte[] data
        && Advertisement.Kind(Gatt.NintendoCompanyId, data) != ControllerKind.Unknown;

    /// <summary>Von der Suche gemeldetes Gerät prüfen und ggf. koppeln.</summary>
    public void OnSeen(IDevice1 device, Device1Properties p)
    {
        if (!Enabled || p.Paired || p.Blocked || p.Connected || string.IsNullOrEmpty(p.Address))
            return;
        if (IsSwitch2(p) || !IsGamepad(p))
            return;
        if (_quietUntil.TryGetValue(p.Address, out long until) && Environment.TickCount64 < until)
            return;
        if (!_busy.TryAdd(p.Address, 0))
            return;
        _ = PairAsync(device, p.Address, p.Name ?? p.Alias ?? p.Address);
    }

    private async Task PairAsync(IDevice1 device, string address, string name)
    {
        Log.Info($"{address}: Gamepad „{name}“ im Kopplungsmodus – koppele …");
        try
        {
            if (Agent is { } agent)
                await agent.PairAsync(device.ObjectPath).WaitAsync(TimeSpan.FromSeconds(40)).ConfigureAwait(false);
            else
                await device.PairAsync().WaitAsync(TimeSpan.FromSeconds(40)).ConfigureAwait(false);
            await device.SetTrustedAsync(true).ConfigureAwait(false);
            try
            {
                await device.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // Viele Gamepads verbinden sich nach dem Koppeln selbst – ein Fehler hier ist meist harmlos.
                Log.Info($"{address}: Verbinden nach dem Koppeln: {Log.Reason(e)}");
            }
            Log.Info($"{address}: „{name}“ gekoppelt");
            Changed?.Invoke("paired", address, name);
        }
        catch (Exception e) when (e.Message.Contains("InProgress", StringComparison.OrdinalIgnoreCase)
                                  || e.Message.Contains("AlreadyExists", StringComparison.OrdinalIgnoreCase))
        {
            // Wird gerade anderswo gekoppelt (z. B. über die LibreELEC-Einstellungen) – nicht dazwischenfunken.
            Log.Info($"{address}: „{name}“ wird bereits anderweitig gekoppelt – SKZ Connect hält sich heraus");
            _quietUntil[address] = Environment.TickCount64 + RetryAfterFailureMs;
        }
        catch (Exception e)
        {
            Log.Warn($"{address}: Kopplung „{name}“ fehlgeschlagen: {Log.Reason(e)}");
            _quietUntil[address] = Environment.TickCount64 + RetryAfterFailureMs;
            Changed?.Invoke("pair-failed", address, name);
        }
        finally
        {
            _busy.TryRemove(address, out _);
        }
    }

    /// <summary>
    /// Liste der gekoppelten Gamepads auffrischen (alle paar Sekunden) und Verbinden/Trennen melden. Nutzt schlichte
    /// Proxys statt Device-Objekten (siehe <see cref="BlueZ"/>) – sonst bliebe bei jedem Durchlauf je Gerät ein
    /// D-Bus-Beobachter zurück.
    /// </summary>
    public async Task RefreshAsync()
    {
        var list = new List<GamepadInfo>();
        foreach (var path in await BlueZ.DevicePathsAsync(adapter).ConfigureAwait(false))
        {
            try
            {
                var p = await BlueZ.Device(path).GetAllAsync().ConfigureAwait(false);
                if (!p.Paired || IsSwitch2(p) || !IsGamepad(p))
                    continue;
                int battery = -1;
                if (p.Connected)
                {
                    try
                    {
                        battery = await BlueZ.Battery(path).GetPercentageAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // kein Akku-Dienst
                    }
                }
                string name = p.Alias ?? p.Name ?? p.Address;
                list.Add(new GamepadInfo(p.Address, name, p.Connected, battery));
                if (_lastConnected.TryGetValue(p.Address, out bool was) && was != p.Connected)
                    Changed?.Invoke(p.Connected ? "connected" : "disconnected", p.Address, name);
                _lastConnected[p.Address] = p.Connected;
            }
            catch (Exception)
            {
                // Gerät verschwunden
            }
        }
        _known = list;
    }

    /// <summary>
    /// Trennen (Befehl aus dem Menü). D-Bus-Fehler werden hier abgefangen: Hat sich das Gamepad inzwischen selbst
    /// getrennt, meldet BlueZ einen Fehler (z. B. NotConnected) – das ist kein Grund, den Befehlskanal zu stören.
    /// </summary>
    public async Task DisconnectAsync(string address)
    {
        try
        {
            using var d = await adapter.GetDeviceAsync(address).ConfigureAwait(false);
            if (d is not null)
                await d.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Warn($"{address}: Trennen fehlgeschlagen: {Log.Reason(e)}");
        }
    }

    /// <summary>Kopplung löschen (für ein anderes Gerät freigeben); kurz ruhen, damit es nicht sofort neu koppelt.</summary>
    public async Task<bool> ForgetAsync(string address)
    {
        try
        {
            using var d = await adapter.GetDeviceAsync(address).ConfigureAwait(false);
            if (d is null)
                return false;
            _quietUntil[address] = Environment.TickCount64 + RetryAfterFailureMs;
            await adapter.RemoveDeviceAsync(d.ObjectPath).ConfigureAwait(false);
            _lastConnected.TryRemove(address, out _);
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"{address}: Kopplung löschen fehlgeschlagen: {Log.Reason(e)}");
            return false;
        }
    }
}
