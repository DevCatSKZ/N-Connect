using System.Text.Json.Nodes;

namespace NConnect.Android.Kodi;

/// <summary>
/// Gemeinsamer status.json-Vertrag mit SKZ Connect für Windows und dem Linux-Dienst (das Kodi-Add-on liest ihn) – ohne
/// Android-Abhängigkeiten, damit er sich ohne Gerät testen lässt (android/NConnect.Android.Tests).
/// </summary>
public sealed partial class KodiStatusWriter
{
    /// <summary>Feld „platform“: das Add-on wählt danach den Hinweistext (hier: Bluetooth in Android einschalten).</summary>
    internal const string Platform = "android";

    /// <summary>
    /// Feld „bluetooth“: "missing" (kein Adapter), "no_le" (Adapter ohne Bluetooth LE – Switch-2-Controller gehen nicht,
    /// klassische schon), sonst "on" bzw. "off".
    /// </summary>
    internal static string BluetoothState(bool adapter, bool lowEnergy, bool enabled) =>
        !adapter ? "missing" : !lowEnergy ? "no_le" : enabled ? "on" : "off";

    /// <summary>
    /// Vergleichswert, ob sich der Stand geändert hat: alles außer der Berichtsrate („rate“ schwankt laufend – sonst würde
    /// beim Spielen bei jedem Takt geschrieben statt nur beim Herzschlag).
    /// </summary>
    internal static string ChangeKey(JsonObject status)
    {
        var copy = status.DeepClone().AsObject();
        if (copy["players"] is JsonArray players)
        {
            foreach (var player in players)
            {
                if (player?["devices"] is JsonArray devices)
                {
                    foreach (var device in devices)
                        (device as JsonObject)?.Remove("rate");
                }
            }
        }
        return copy.ToJsonString();
    }
}
