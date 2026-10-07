using Microsoft.Win32;

namespace Switch2Pro.Bridge;

/// <summary>
/// Gerätemeldungen von Windows („… verbunden“, „Gerät wird eingerichtet“) stummschalten, solange N-Connect
/// die Meldung übernimmt. Windows prüft je Benachrichtigungs-Absender den Wert „Enabled“ in der Registry;
/// fehlt er, ist der Absender an. Wir setzen ihn nur herunter, wenn er nicht schon aus war, und merken uns
/// das je Absender (Markierwert „NConnect“), damit „Wiederherstellen“ genau unsere Eingriffe zurücknimmt.
/// </summary>
internal static class ToastSenders
{
    private const string Base = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    private const string Marker = "NConnect";

    /// <summary>
    /// Absender der Windows-Gerätemeldungen: Bluetooth verbunden/getrennt (Bthprops), Geräteeinrichtung
    /// fertig (Devices – darunter auch unser virtueller Controller) und USB-Geräte (Usb.Notification).
    /// </summary>
    private static readonly string[] Senders =
    [
        "Windows.SystemToast.Bthprops",
        "Windows.SystemToast.Devices",
        "Windows.SystemToast.Usb.Notification",
    ];

    /// <summary>Meldungen aus (true) oder unsere Abschaltung zurücknehmen (false).</summary>
    public static void Apply(bool suppress)
    {
        foreach (var sender in Senders)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"{Base}\{sender}", writable: true);
                if (suppress)
                {
                    // Schon aus (von Hand oder einem anderen Programm)? Dann nichts anfassen.
                    object? enabled = key.GetValue("Enabled");
                    if (enabled is int { } v && v == 0)
                        continue;
                    key.SetValue("Enabled", 0, RegistryValueKind.DWord);
                    key.SetValue(Marker, 1, RegistryValueKind.DWord);
                }
                else if (key.GetValue(Marker) is not null)
                {
                    // Nur zurücknehmen, was wir selbst gesetzt haben.
                    key.DeleteValue("Enabled", throwOnMissingValue: false);
                    key.DeleteValue(Marker, throwOnMissingValue: false);
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Log.Warn($"Meldungsabsender {sender}: {e.Message}");
            }
        }
    }
}
