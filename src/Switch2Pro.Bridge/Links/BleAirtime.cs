namespace Switch2Pro.Bridge.Links;

/// <summary>
/// Funkzeit bei vielen Bluetooth-Controllern. Switch-2-Controller fordern normalerweise das schnellste Intervall an
/// (7,5 ms, ~60 Berichte/s). Ab drei Bluetooth-Controllern reicht die Funkzeit des Adapters dafür nicht mehr
/// (gemessen mit einem Barrot-5.4-Stick: drei Switch-2-Controller nur noch 10–11 Berichte/s, eine Wii-Fernbedienung
/// daneben 2 Berichte/s – Tasten kamen nicht an, ein weiterer Controller verband sich nicht). Dann wechseln alle
/// Switch-2-Controller auf das ausgeglichene Intervall (~30 ms, ~33 Berichte/s) – bei weniger Controllern zurück.
/// </summary>
internal static class BleAirtime
{
    /// <summary>Ab so vielen Bluetooth-Verbindungen (LE und klassisch, auch gerade entstehende) „ausgeglichen“.</summary>
    public const int CrowdedFrom = 3;

    private static int _crowded;

    /// <summary>Viele Bluetooth-Controller: Switch-2-Controller sollen das ausgeglichene Intervall nutzen.</summary>
    public static bool Crowded => Volatile.Read(ref _crowded) == 1;

    /// <summary>Wechsel zwischen „schnell“ und „ausgeglichen“ (läuft auf dem Thread des Aufrufers).</summary>
    public static event Action? Changed;

    /// <summary>Anzahl der Bluetooth-Verbindungen melden; ändert sich dadurch die Vorgabe, werden alle Verbindungen informiert.</summary>
    public static void Update(int bluetoothLinks)
    {
        int value = bluetoothLinks >= CrowdedFrom ? 1 : 0;
        if (Interlocked.Exchange(ref _crowded, value) == value)
            return;
        Log.Info(value == 1
            ? $"{bluetoothLinks} Bluetooth-Controller: Switch-2-Controller auf ausgeglichenes Verbindungsintervall (Funkzeit für alle)"
            : $"{bluetoothLinks} Bluetooth-Controller: Switch-2-Controller wieder auf schnelles Verbindungsintervall");
        Changed?.Invoke();
    }
}
