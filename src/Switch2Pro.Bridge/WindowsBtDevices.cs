using System.Text;
using Microsoft.Win32;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Liest die Liste der in Windows gekoppelten Bluetooth-Geräte – nur <b>Adresse und Name</b> aus
/// <c>HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices</c>. Dieser Teil der Registrierung ist für
/// jeden lesbar; es sind keine Administratorrechte nötig. Kopplungsschlüssel (Unterschlüssel <c>Keys</c>) liest
/// N-Connect bewusst nicht. Dient dem ESP32-/nRF52840-Export, damit auch Controller erscheinen, die in Windows
/// gekoppelt sind, aber von N-Connect noch nicht verbunden wurden.
/// </summary>
internal static class WindowsBtDevices
{
    private const string DevicesPath = @"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices";

    public static List<WindowsBtDevice> Read()
    {
        var result = new List<WindowsBtDevice>();
        try
        {
            using var devices = Registry.LocalMachine.OpenSubKey(DevicesPath);
            foreach (var sub in devices?.GetSubKeyNames() ?? [])
            {
                if (!TryAddress(sub, out var address))
                    continue;
                using var key = devices!.OpenSubKey(sub);
                result.Add(new WindowsBtDevice { Address = address, Name = DecodeName(key?.GetValue("Name")) });
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Bluetooth-Geräteliste nicht lesbar: {Log.Reason(e)}");
        }
        return result;
    }

    /// <summary>Unterschlüsselname ("98b6e9010203") → "98:B6:E9:01:02:03"; false, wenn es keine 12-stellige Hex-Adresse ist.</summary>
    private static bool TryAddress(string sub, out string address)
    {
        address = "";
        if (sub.Length != 12 || !sub.All(Uri.IsHexDigit))
            return false;
        address = string.Join(':', Enumerable.Range(0, 6).Select(i => sub.Substring(i * 2, 2).ToUpperInvariant()));
        return true;
    }

    /// <summary>Windows speichert den Namen als REG_BINARY (UTF-8, oft mit abschließender 0), sonst als Zeichenkette.</summary>
    private static string? DecodeName(object? value)
    {
        string? name = value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            _ => null,
        };
        name = name?.TrimEnd('\0').Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
