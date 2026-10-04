using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge.Links;

/// <summary>Wie ein Controller angebunden ist.</summary>
internal enum Transport
{
    /// <summary>Bluetooth LE (Switch-2-Controller, direkt über die App).</summary>
    BluetoothLE,
    /// <summary>Bluetooth Classic HID (Switch-1-Controller, in Windows gekoppelt).</summary>
    Bluetooth,
    Usb,
}

/// <summary>Was die App über einen Controller weiß (für die Anzeige).</summary>
internal sealed record ControllerInfo
{
    public string? SerialNumber { get; init; }
    public string? Firmware { get; init; }
    /// <summary>Gehäusefarbe (RGB), falls bekannt.</summary>
    public int? BodyColor { get; init; }
    public int? ButtonColor { get; init; }
    public int? GripColor { get; init; }
}

/// <summary>
/// Eine Verbindung zu genau einem physischen Controller. Liefert vereinheitlichte Zustände und nimmt
/// Vibration und Spielernummer entgegen. Den virtuellen Controller besitzt der <see cref="Player"/>.
/// </summary>
internal interface IControllerLink : IAsyncDisposable
{
    ControllerKind Kind { get; }
    Transport Transport { get; }
    /// <summary>Eindeutige Kennung (Bluetooth-Adresse "AA:BB:…" oder Gerätepfad).</summary>
    string Id { get; }
    /// <summary>Bluetooth-Adresse zur Anzeige, falls bekannt.</summary>
    string? Address { get; }
    DeviceCalibration Calibration { get; }
    ControllerInfo Info { get; }
    ControllerState? LastState { get; }
    /// <summary>Eingabeberichte pro Sekunde (gleitend gemessen).</summary>
    double ReportRate { get; }
    bool IsLost { get; }
    /// <summary>Joy-Con 2 steckt im Charging Grip (dessen GL/GR-Tasten sind dann eingeschaltet).</summary>
    bool InGrip => false;
    /// <summary>Eigener Gerätename (z. B. Kabel-Gamepads von HORI/PowerA), sonst null = Name der Controller-Art.</summary>
    string? ProductName => null;
    /// <summary>HID-Instanz eines USB-Geräts, das Spiele zusätzlich direkt sehen (zum Verstecken per HidHide), sonst null.</summary>
    string? HidInstanceId => null;

    event Action<IControllerLink, ControllerState>? StateReceived;
    /// <summary>Wird genau einmal ausgelöst, wenn die Verbindung abbricht.</summary>
    event Action<IControllerLink>? Lost;

    /// <summary>Vibration setzen (großer/kleiner Motor 0–255, Stärke 0–1). Die Verbindung wiederholt sie selbst.</summary>
    void SetRumble(byte large, byte small, float strength);
    Task SetPlayerAsync(int playerIndex);
    /// <summary>HOME-LED-Helligkeit 0–15 (0 = aus), falls der Controller eine hat (Switch 1 Pro, Joy-Con R).</summary>
    Task SetHomeLightAsync(byte intensity) => Task.CompletedTask;
    /// <summary>Controller schlafen legen, bevor die Verbindung getrennt wird (falls das Protokoll es kann).</summary>
    Task SleepAsync() => Task.CompletedTask;
}

/// <summary>Misst die Berichtsrate (Berichte pro Sekunde, über ~1 s gemittelt).</summary>
internal sealed class RateMeter
{
    private long _windowStart = Environment.TickCount64;
    private int _count;
    private double _rate;

    public double Rate => Environment.TickCount64 - _windowStart > 3000 ? 0 : _rate;

    public void Tick()
    {
        long now = Environment.TickCount64;
        int n = Interlocked.Increment(ref _count);
        long elapsed = now - Volatile.Read(ref _windowStart);
        if (elapsed >= 1000)
        {
            _rate = n * 1000.0 / elapsed;
            Interlocked.Exchange(ref _count, 0);
            Volatile.Write(ref _windowStart, now);
        }
    }
}
