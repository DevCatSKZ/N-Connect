using System.Text.Json.Nodes;
using Linux.Bluetooth;
using SkzConnect.Linux.Ble;
using SkzConnect.Linux.Output;
using Switch2Pro.Protocol;

namespace SkzConnect.Linux;

/// <summary>
/// Diagnose (<c>skzconnect --selftest</c>): prüft alles, was ohne Controller prüfbar ist – virtueller Controller über
/// uinput (anlegen, Eingaben senden, wieder entfernen), BlueZ über D-Bus, Adapter, Einstellungen.
/// Ausgabe: eine JSON-Zeile je Prüfung und am Ende {"result":"ok"|"fail"}; Rückgabewert 0 nur, wenn alles Nötige geht.
/// </summary>
public static class SelfTest
{
    public static async Task<int> RunAsync(string dataDir)
    {
        bool ok = true;
        void Report(string check, bool passed, string detail, bool required = true)
        {
            if (!passed && required)
                ok = false;
            Console.Out.WriteLine(new JsonObject
            {
                ["check"] = check,
                ["ok"] = passed,
                ["required"] = required,
                ["detail"] = detail,
            }.ToJsonString());
        }

        // 1. Virtueller Controller
        try
        {
            using var pad = UInputPad.Create("skzconnect/selftest");
            string? node = FindInputDevice("skzconnect/selftest");
            pad.Update(new GamepadState(XButtons.A, 0, 255, 32767, -32768, 0, 0, false));
            await Task.Delay(50).ConfigureAwait(false);
            pad.Update(default);
            Report("uinput", true, node is null ? "angelegt" : $"angelegt als {node}");
        }
        catch (Exception e)
        {
            Report("uinput", false, e.Message);
        }

        // 2. BlueZ und Adapter
        try
        {
            var adapters = await BlueZManager.GetAdaptersAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Report("bluez", true, "BlueZ erreichbar");
            if (adapters.Count == 0)
            {
                Report("adapter", false, "kein Bluetooth-Adapter gefunden");
            }
            else
            {
                foreach (var a in adapters)
                {
                    bool powered = await a.GetPoweredAsync().ConfigureAwait(false);
                    string address = await a.GetAddressAsync().ConfigureAwait(false);
                    Report("adapter", powered, $"{a.Name} {address} {(powered ? "an" : "aus")}");
                    // Bluetooth LE: nötig für Switch-2-Controller; klassische Gamepads gehen auch ohne (daher nicht Pflicht).
                    bool? le = await BlueZ.SupportsLeAsync(a).ConfigureAwait(false);
                    Report("le", le != false, le switch
                    {
                        true => $"{a.Name}: Bluetooth LE vorhanden",
                        false => $"{a.Name}: kein Bluetooth LE – Switch-2-Controller gehen nicht, andere Controller schon",
                        null => $"{a.Name}: BlueZ meldet keine Rollen (ältere Version) – Bluetooth LE angenommen",
                    }, required: false);
                    a.Dispose();
                }
            }
        }
        catch (Exception e)
        {
            Report("bluez", false, $"BlueZ nicht erreichbar – Bluetooth-Dienst aktiv? ({e.Message})");
        }

        // 3. Einstellungen
        var settings = Settings.Load(Path.Combine(dataDir, "settings.json"));
        Report("settings", settings.LoadError is null, settings.LoadError ?? "lesbar", required: false);

        Console.Out.WriteLine(new JsonObject { ["result"] = ok ? "ok" : "fail" }.ToJsonString());
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Testcontroller (<c>skzconnect --test-pad [Sekunden]</c>): legt einen virtuellen Controller an, drückt jede Sekunde
    /// kurz A und bewegt den linken Stick; Vibrationsbefehle von Spielen erscheinen als JSON-Zeilen. Zum Prüfen von
    /// Kodi/RetroArch-Erkennung und Vibration ohne echten Controller.
    /// </summary>
    public static async Task<int> TestPadAsync(int seconds)
    {
        using var pad = UInputPad.Create("skzconnect/test");
        pad.Rumble += (large, small) =>
            Console.Out.WriteLine(new JsonObject { ["rumble"] = new JsonArray(large, small) }.ToJsonString());
        Console.Out.WriteLine(new JsonObject { ["device"] = FindInputDevice("skzconnect/test") }.ToJsonString());
        for (int i = 0; i < seconds; i++)
        {
            pad.Update(new GamepadState(XButtons.A, 0, 0, (short)(i % 2 == 0 ? 32767 : -32768), 0, 0, 0, false));
            await Task.Delay(150).ConfigureAwait(false);
            pad.Update(default);
            await Task.Delay(850).ConfigureAwait(false);
        }
        return 0;
    }

    /// <summary>Eingabegerät (/dev/input/eventN) zu einer phys-Kennung suchen.</summary>
    private static string? FindInputDevice(string phys)
    {
        try
        {
            foreach (string dir in Directory.GetDirectories("/sys/class/input", "event*"))
            {
                string physFile = Path.Combine(dir, "device", "phys");
                if (File.Exists(physFile) && File.ReadAllText(physFile).Trim() == phys)
                    return "/dev/input/" + Path.GetFileName(dir);
            }
        }
        catch (Exception)
        {
            // sysfs nicht lesbar
        }
        return null;
    }
}
