using Switch2Pro.Protocol;

namespace NConnect.Android;

/// <summary>
/// Gemeinsame Nutzertexte der Android-App. Bewusst Android-Begriffe (Benachrichtigung, Bedienungshilfe, Shizuku,
/// Schnelleinstellungen) – keine Windows-Begriffe wie Infobereich oder Treiber.
/// </summary>
public static class Texts
{
    /// <summary>Kurzer Name für Karten und Benachrichtigung.</summary>
    public static string ShortName(ControllerKind kind) => L.T(kind switch
    {
        ControllerKind.Pro2 => "Pro Controller 2",
        ControllerKind.JoyCon2Left => "Joy-Con 2 (L)",
        ControllerKind.JoyCon2Right => "Joy-Con 2 (R)",
        ControllerKind.GameCube2 => "GameCube-Controller",
        ControllerKind.JoyConPair => "Joy-Con-Paar",
        _ => "Controller",
    });

    public static string PathName(OutputPath path) => L.T(path switch
    {
        OutputPath.Auto => "Automatisch",
        OutputPath.Root => "Root",
        OutputPath.Shizuku => "Shizuku",
        OutputPath.Touch => "Touch-Steuerung",
        _ => path.ToString(),
    });

    public static string PathSummary(OutputPath path) => L.T(path switch
    {
        OutputPath.Auto => "Nimmt den besten verfügbaren Weg: Root, sonst Shizuku, sonst Touch.",
        OutputPath.Root => "Echter Xbox-Controller für alle Spiele. Nur auf gerooteten Geräten.",
        OutputPath.Shizuku => "Echter Xbox-Controller für alle Spiele – ohne Root. Einmal kurz einrichten.",
        OutputPath.Touch => "Tasten werden zu Bildschirmberührungen. Für Spiele ohne Controller-Unterstützung.",
        _ => "",
    });

    public static string Battery(int percent, bool charging) =>
        percent < 0 ? L.T("Akku unbekannt") : charging ? L.F("Akku {0} % · lädt", percent) : L.F("Akku {0} %", percent);

    public const string PairHowTo =
        "Halte am Controller die kleine runde SYNC-Taste gedrückt, bis die Lichter hin- und herlaufen. " +
        "SKZ Connect verbindet ihn dann automatisch.";

    public const string ReconnectHowTo =
        "Bereits gekoppelte Controller verbinden sich nach einem Tastendruck von selbst – SYNC ist nur beim ersten Mal nötig.";

    public const string OtherHostHint =
        "Dieser Controller ist mit einem anderen Gerät gekoppelt (z. B. deiner Switch 2 oder einem PC). " +
        "Drücke die SYNC-Taste, um ihn mit diesem Gerät zu verbinden.";
}
