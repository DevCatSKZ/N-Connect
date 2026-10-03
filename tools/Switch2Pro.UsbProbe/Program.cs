using Switch2Pro.Bridge.Usb;
using Switch2Pro.Protocol;

// Aufruf: UsbProbe [Sekunden] [rumble]
int seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 10;
bool rumble = args.Contains("rumble");

static string Hex(ReadOnlySpan<byte> b) => Convert.ToHexString(b).Aggregate("", (a, c) => a.Length % 3 == 2 ? a + " " + c : a + c);

var controllers = UsbEnumerator.Find();
Console.WriteLine($"Gefunden: {controllers.Count}");
foreach (var c in controllers)
    Console.WriteLine($"  {c.DeviceId}\n    WinUSB {c.WinUsbPath}\n    HID    {c.HidPath}");
if (controllers.Count == 0)
{
    foreach (var g in new[] { UsbNative.ControllerWinUsbInterface, UsbNative.GenericWinUsbInterface, UsbNative.HidInterface })
        foreach (var p in UsbNative.GetInterfacePaths(g).Where(p => p.Contains("057e", StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine($"  [{g}] {p} -> {UsbNative.GetInstanceId(p)}");
    return 1;
}

var info = controllers[0];
WinUsbChannel? usb = null;
try { usb = new WinUsbChannel(info.WinUsbPath); }
catch (Exception e) { Console.WriteLine($"WinUSB: {e.Message} (belegt?) – nur HID"); }
using var hid = new HidChannel(info.HidPath);
Console.WriteLine($"HID: Eingabe {hid.InputLength} Byte, Ausgabe {hid.OutputLength} Byte");

using var cts = new CancellationTokenSource();
int reports = 0;
ControllerState? last = null;
var reader = Task.Run(async () =>
{
    var buffer = new byte[hid.InputLength];
    while (!cts.IsCancellationRequested)
    {
        int n = await hid.ReadAsync(buffer, cts.Token);
        if (n == 0) { Console.WriteLine("HID: getrennt"); return; }
        reports++;
        if (reports <= 3 || reports % 500 == 0)
            Console.WriteLine($"HID #{reports} ({n}): {Hex(buffer.AsSpan(0, n))}");
        if (buffer[0] == 0x05 && InputReports.TryParseReport05(buffer.AsSpan(1, n - 1), out var st))
        {
            if (last is null || last.Buttons != st.Buttons
                || Math.Abs(last.LeftX - st.LeftX) > 300 || Math.Abs(last.LeftY - st.LeftY) > 300
                || Math.Abs(last.RightX - st.RightX) > 300 || Math.Abs(last.RightY - st.RightY) > 300)
                Console.WriteLine($"  Tasten={st.Buttons} L=({st.LeftX},{st.LeftY}) R=({st.RightX},{st.RightY}) " +
                                  $"Akku={st.BatteryMillivolts}mV/{st.BatteryPercent}% Laden={st.Charging} IMU={st.Motion}");
            last = st;
        }
    }
});

await Task.Delay(300);
Console.WriteLine($"Berichte vor Init: {reports}");

foreach (uint addr in new uint[] { 0x13000, 0x13040, 0x13080, 0x130C0, 0x1FC040, 0x1FC080 })
{
    var reply = usb?.Transact(Commands.ReadMemoryUsb(addr));
    Console.WriteLine($"Lese {addr:X6}: {(reply is null ? "keine Antwort" : Hex(reply))}");
}

foreach (var cmd in Commands.UsbInitSequence)
{
    var reply = usb?.Transact(cmd);
    Console.WriteLine($"> {Hex(cmd)}\n< {(reply is null ? "keine Antwort" : Hex(reply))}");
}
var led = usb?.Transact(Commands.SetPlayerLeds(0x1, Commands.TransportUsb));
Console.WriteLine($"LED: {(led is null ? "keine Antwort" : Hex(led))}");

if (rumble)
{
    Console.WriteLine("Vibration 1 s …");
    for (int i = 0; i < 80; i++)
    {
        await hid.WriteAsync(Rumble.BuildUsbReport(200, 200, i), CancellationToken.None);
        await Task.Delay(12);
    }
    await hid.WriteAsync(Rumble.BuildUsbReport(0, 0, 81), CancellationToken.None);
}

Console.WriteLine($"Lese {seconds} s Eingaben – Tasten drücken, Sticks bewegen …");
var start = reports;
await Task.Delay(seconds * 1000);
Console.WriteLine($"Berichte: {reports - start} in {seconds} s");
cts.Cancel();
try { await reader; } catch (OperationCanceledException) { }
return 0;
