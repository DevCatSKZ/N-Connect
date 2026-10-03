using System.Collections.Concurrent;
using Switch2Pro.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;

// Aufruf: BleProbe [Sekunden Eingaben] [scan=Sekunden] [mask=FF] [rumble] [nocmd]
int seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 15;
int scanSeconds = Arg("scan") is { } sc ? int.Parse(sc) : 30;
byte mask = Arg("mask") is { } m ? Convert.ToByte(m, 16) : (byte)0xFF;
bool rumble = args.Contains("rumble");
bool noCommands = args.Contains("nocmd");

string? Arg(string name) => args.FirstOrDefault(a => a.StartsWith(name + "="))?[(name.Length + 1)..];
static string Hex(ReadOnlySpan<byte> b) => b.Length == 0 ? "" : BitConverter.ToString(b.ToArray()).Replace('-', ' ');
static string Addr(ulong a) => string.Join(':', Enumerable.Range(0, 6).Reverse().Select(i => ((a >> (i * 8)) & 0xFF).ToString("X2")));
var t0 = DateTime.Now;
void P(string text) => Console.WriteLine($"[{(DateTime.Now - t0).TotalSeconds,6:F2}] {text}");

// ---------- 1. Werbung ----------
var seen = new ConcurrentDictionary<string, byte>();
var found = new TaskCompletionSource<(ulong Address, BluetoothAddressType Type, byte[] Data)>();
var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
watcher.Received += (_, e) =>
{
    foreach (var md in e.Advertisement.ManufacturerData)
    {
        if (md.CompanyId != Gatt.NintendoCompanyId)
            continue;
        CryptographicBuffer.CopyToByteArray(md.Data, out byte[] data);
        data ??= [];
        if (seen.TryAdd($"{e.BluetoothAddress}:{Hex(data)}", 0))
        {
            int pid = data.Length >= 7 ? data[5] | (data[6] << 8) : 0;
            P($"Werbung {Addr(e.BluetoothAddress)} ({e.BluetoothAddressType}) RSSI {e.RawSignalStrengthInDBm} " +
              $"PID {pid:X4} SYNC={Advertisement.IsSyncMode(data)} Name='{e.Advertisement.LocalName}' Daten: {Hex(data)}");
        }
        if (data.Length >= 7 && data[0] == 0x01 && data[3] == 0x7E)
            found.TrySetResult((e.BluetoothAddress, e.BluetoothAddressType, data));
    }
};
watcher.Stopped += (_, e) => P($"Suche beendet: {e.Error}");
watcher.Start();
P($"Suche {scanSeconds} s nach Nintendo-Controllern … jetzt SYNC drücken.");
var winner = await Task.WhenAny(found.Task, Task.Delay(scanSeconds * 1000));
if (winner != found.Task)
{
    watcher.Stop();
    P("Kein Switch-2-Controller gefunden.");
    return 1;
}
var (address, type, adv) = await found.Task;
await Task.Delay(300); // weitere Werbungen noch anzeigen
watcher.Stop();

// ---------- 2. Verbinden ----------
if (args.Contains("unpair"))
{
    using var old = await BluetoothLEDevice.FromBluetoothAddressAsync(address, type);
    if (old is not null && old.DeviceInformation.Pairing.IsPaired)
    {
        var u = await old.DeviceInformation.Pairing.UnpairAsync();
        P($"Windows-Kopplung entfernt: {u.Status}");
        await Task.Delay(1000);
    }
}
P($"Verbinde mit {Addr(address)} ({type}) …");
var connectStart = DateTime.Now;
using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address, type);
if (device is null)
{
    P("FromBluetoothAddressAsync lieferte null.");
    return 2;
}
P($"Gerät: '{device.Name}' Id={device.DeviceId} Status={device.ConnectionStatus} " +
  $"Gekoppelt={device.DeviceInformation.Pairing.IsPaired} KannKoppeln={device.DeviceInformation.Pairing.CanPair}");
device.ConnectionStatusChanged += (d, _) => P($"Verbindungsstatus: {d.ConnectionStatus}");
using var session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);
session.MaintainConnection = true;
session.SessionStatusChanged += (_, e) => P($"GATT-Sitzung: {e.Status} ({e.Error})");
P($"Sitzung: {session.SessionStatus}, MaxPdu {session.MaxPduSize}");
if (!args.Contains("slow"))
{
    var request = device.RequestPreferredConnectionParameters(BluetoothLEPreferredConnectionParameters.ThroughputOptimized);
    P($"Schnelle Verbindung angefragt: {request.Status}");
}
device.ConnectionParametersChanged += (d, _) =>
{
    var p = d.GetConnectionParameters();
    P($"Verbindungsparameter: Intervall {p.ConnectionInterval * 1.25:F2} ms, Latenz {p.ConnectionLatency}, Timeout {p.LinkTimeout * 10} ms");
};

GattDeviceServicesResult services = null!;
for (int attempt = 1; attempt <= 5; attempt++)
{
    services = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
    P($"Dienste (Versuch {attempt}): {services.Status}, {services.Services.Count} Stück");
    if (services.Status == GattCommunicationStatus.Success && services.Services.Count > 0)
        break;
    await Task.Delay(500 * attempt);
}
if (!args.Contains("slow"))
{
    // Erneut anfragen, jetzt wo die Verbindung steht.
    var again = device.RequestPreferredConnectionParameters(BluetoothLEPreferredConnectionParameters.ThroughputOptimized);
    P($"Schnelle Verbindung erneut angefragt: {again.Status}");
    await Task.Delay(1500);
    var cp0 = device.GetConnectionParameters();
    P($"Danach: Intervall {cp0.ConnectionInterval * 1.25:F2} ms, Latenz {cp0.ConnectionLatency}");
    var phy = device.GetConnectionPhy();
    P($"PHY: senden 1M={phy.TransmitInfo.IsUncoded1MPhy} 2M={phy.TransmitInfo.IsUncoded2MPhy}, empfangen 2M={phy.ReceiveInfo.IsUncoded2MPhy}");
}
var chars = new Dictionary<Guid, GattCharacteristic>();
foreach (var service in services.Services)
{
    var r = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
    P($"  Dienst {service.Uuid}: {r.Status}");
    if (r.Status != GattCommunicationStatus.Success)
        continue;
    foreach (var c in r.Characteristics)
    {
        P($"    {c.Uuid} {c.CharacteristicProperties}");
        chars.TryAdd(c.Uuid, c);
    }
}

if (!chars.TryGetValue(Gatt.CommandOutput, out var command) || !chars.TryGetValue(Gatt.CommandResponse, out var response)
    || !chars.TryGetValue(Gatt.InputReportCommon, out var input))
{
    P("Benötigte Merkmale fehlen.");
    return 3;
}

async Task Notify(GattCharacteristic c)
{
    var r = await c.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(
        GattClientCharacteristicConfigurationDescriptorValue.Notify);
    P($"Benachrichtigung {c.Uuid}: {r.Status} {r.ProtocolError}");
}

// ---------- 3. Befehle ----------
var replies = new BlockingCollection<byte[]>();
response.ValueChanged += (_, e) =>
{
    CryptographicBuffer.CopyToByteArray(e.CharacteristicValue, out byte[] d);
    replies.Add(d ?? []);
};
await Notify(response);

async Task<byte[]?> Send(byte[] cmd, int timeoutMs = 500)
{
    while (replies.TryTake(out _)) { }
    var r = await command.WriteValueWithResultAsync(CryptographicBuffer.CreateFromByteArray(cmd), GattWriteOption.WriteWithoutResponse);
    if (r.Status != GattCommunicationStatus.Success)
    {
        P($"> {Hex(cmd)}  SCHREIBFEHLER {r.Status}");
        return null;
    }
    var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
    while (DateTime.Now < deadline)
    {
        if (replies.TryTake(out var reply, 50) && reply.Length >= 4 && reply[0] == cmd[0] && reply[3] == cmd[3])
        {
            P($"> {Hex(cmd)}\n          < {Hex(reply)}");
            return reply;
        }
    }
    P($"> {Hex(cmd)}  (keine Antwort)");
    return null;
}

// Eingaben schon vor der Initialisierung mitzählen: sendet der Controller von selbst?
int reports = 0;
ControllerState? last = null;
var times = new List<long>();
input.ValueChanged += (_, e) =>
{
    CryptographicBuffer.CopyToByteArray(e.CharacteristicValue, out byte[] d);
    if (d is null)
        return;
    int n = Interlocked.Increment(ref reports);
    lock (times) times.Add(Environment.TickCount64);
    if (n <= 2 || n % 1000 == 0)
        P($"Bericht #{n} ({d.Length} Byte): {Hex(d)}");
    if (InputReports.TryParseReport05(d, out var st))
    {
        if (last is null || last.Buttons != st.Buttons
            || Math.Abs(last.LeftX - st.LeftX) > 400 || Math.Abs(last.LeftY - st.LeftY) > 400
            || Math.Abs(last.RightX - st.RightX) > 400 || Math.Abs(last.RightY - st.RightY) > 400)
            P($"  Tasten={st.Buttons} L=({st.LeftX},{st.LeftY}) R=({st.RightX},{st.RightY}) " +
              $"Akku={st.BatteryMillivolts}mV/{st.BatteryPercent}% Laden={st.Charging} IMU={st.Motion}");
        last = st;
    }
};
await Notify(input);
await Task.Delay(500);
P($"Berichte ohne Start-Befehle: {reports}");

if (args.Contains("dumppair"))
{
    // Nur lesen: Kopplungsspeicher 0x1FA000 (Host-Adressen + Schlüssel), 0x200 Byte.
    for (uint a = 0x1FA000; a < 0x1FA200; a += 0x40)
    {
        var r = await Send(Commands.ReadMemory(a, 0x40));
        if (r is { Length: > 16 })
            P($"FLASH {a:X6}: {Hex(r.AsSpan(16))}");
    }
    var adapter = await BluetoothAdapter.GetDefaultAsync();
    P($"PC-Adapter: {Addr(adapter.BluetoothAddress)}");
}

if (args.Contains("pair"))
{
    // Kopplung mit diesem PC (wie Switch2Connect controller.pair): danach weckt ein Tastendruck den
    // Controller für den PC. Ersetzt die Kopplung mit der Konsole.
    var adapter = await BluetoothAdapter.GetDefaultAsync();
    ulong pc = adapter.BluetoothAddress;
    var pairing = new Pairing();
    await Send(Pairing.AddressesRequest(pc), 1500);
    var keys = await Send(pairing.KeysRequest(), 1500);
    if (keys is not null && pairing.TryDeriveKey(keys, out var ltk))
    {
        var confirm = await Send(pairing.ConfirmRequest(), 1500);
        bool ok = confirm is not null && pairing.VerifyConfirmation(confirm, ltk);
        P($"Schlüssel bestätigt: {ok}");
        if (ok)
            await Send(Pairing.FinishRequest(), 1500);
    }
    var r = await Send(Commands.ReadMemory(0x1FA000, 0x40));
    if (r is { Length: > 16 })
        P($"FLASH 1FA000 nachher: {Hex(r.AsSpan(16))}");
}

if (!noCommands)
{
    await Send(Commands.Hex("07 91 01 01 00 00 00 00"));
    await Send(Commands.Hex("10 91 01 01 00 00 00 00")); // Firmware-Version
    await Send(Commands.ReadMemory(0x13000, 0x40));       // Gerätedaten (Seriennummer, Farben …)
    await Send(Commands.ReadMemory(Commands.AddrLeftStickCalibration, 9));
    await Send(Commands.ReadMemory(Commands.AddrRightStickCalibration, 9));
    await Send(Commands.ReadMemory(Commands.AddrGyroCalibration, 0x10));
    if (args.Contains("full"))
    {
        // Startsequenz wie Switch2Connect (dort mit Hardware erprobt), OHNE 15/03 (Kopplung) und 03/0D (USB).
        foreach (var hex in new[]
        {
            "16 91 01 01 00 00 00 00",
            $"0C 91 01 02 00 04 00 00 {mask:X2} 00 00 00",
            "11 91 01 03 00 00 00 00",
            "0A 91 01 08 00 14 00 00 01 FF FF FF FF FF FF FF FF 35 00 46 00 00 00 00 00 00 00 00",
            $"0C 91 01 04 00 04 00 00 {mask:X2} 00 00 00",
            "03 91 01 0A 00 04 00 00 09 00 00 00",
            "01 91 01 0C 00 00 00 00",
        })
            await Send(Commands.Hex(hex));
    }
    else
    {
        await Send(Commands.Build(0x0C, 0x02, [mask, 0, 0, 0]));
        await Send(Commands.Build(0x0C, 0x04, [mask, 0, 0, 0]));
    }
    await Task.Delay(1000);
    var after = device.GetConnectionParameters();
    P($"Nach Start-Befehlen: Intervall {after.ConnectionInterval * 1.25:F2} ms");
    if (!args.Contains("slow"))
    {
        var third = device.RequestPreferredConnectionParameters(BluetoothLEPreferredConnectionParameters.ThroughputOptimized);
        P($"Schnelle Verbindung nach Init angefragt: {third.Status}");
    }
    await Send(Commands.SetPlayerLeds(0x1));
    await Send(Commands.PlayConnectSample());
}

if (rumble && chars.TryGetValue(Gatt.ProRumbleOutput, out var rumbleChar))
{
    P("Vibration 1 s …");
    for (int i = 0; i < 60; i++)
    {
        await rumbleChar.WriteValueWithResultAsync(CryptographicBuffer.CreateFromByteArray(Rumble.BuildPacket(200, 200, i)),
            GattWriteOption.WriteWithoutResponse);
        await Task.Delay(16);
    }
    await rumbleChar.WriteValueWithResultAsync(CryptographicBuffer.CreateFromByteArray(Rumble.StopPacket(61)),
        GattWriteOption.WriteWithoutResponse);
}

{ var cp = device.GetConnectionParameters(); P($"Aktuell: Intervall {cp.ConnectionInterval * 1.25:F2} ms, Latenz {cp.ConnectionLatency}"); }
P($"Lese {seconds} s Eingaben – alle Tasten drücken, Sticks kreisen, Controller bewegen …");
int start = reports;
await Task.Delay(seconds * 1000);
long[] snapshot;
lock (times) snapshot = [.. times];
var gaps = snapshot.Zip(snapshot.Skip(1), (a, b) => b - a).ToArray();
P($"Berichte: {reports - start} in {seconds} s ({(reports - start) / (double)seconds:F0}/s), " +
  $"größte Lücke {(gaps.Length > 0 ? gaps.Max() : 0)} ms, Status {device.ConnectionStatus}");
session.MaintainConnection = false;
return 0;
