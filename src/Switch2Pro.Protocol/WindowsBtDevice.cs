namespace Switch2Pro.Protocol;

/// <summary>
/// Ein in Windows gekoppeltes Bluetooth-Gerät – Adresse und gespeicherter Name. Der Name ist für jeden lesbar
/// (<c>BTHPORT\Parameters\Devices</c>) und reicht, um einen Controller zu erkennen und seine Adresse in den Export
/// aufzunehmen, auch wenn N-Connect ihn selbst noch nicht verbunden hatte. Kopplungsschlüssel liest N-Connect
/// bewusst nicht aus.
/// </summary>
public sealed record WindowsBtDevice
{
    /// <summary>"AA:BB:CC:DD:EE:FF", höchstes Byte zuerst.</summary>
    public string Address { get; init; } = "";
    public string? Name { get; init; }
}

/// <summary>Controller-Art aus dem Bluetooth-Namen, den Windows gespeichert hat.</summary>
public static class BtDeviceNames
{
    /// <summary>Art aus dem Namen; <see cref="ControllerKind.Unknown"/> = kein bekannter Controller (Kopfhörer, Tastatur …).</summary>
    public static ControllerKind KindFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ControllerKind.Unknown;
        string n = name.Trim();
        bool Starts(string prefix) => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        bool Has(string part) => n.Contains(part, StringComparison.OrdinalIgnoreCase);
        // Reihenfolge wichtig: die längeren/spezielleren Namen zuerst.
        if (Starts("Joy-Con 2 (L)")) return ControllerKind.JoyCon2Left;
        if (Starts("Joy-Con 2 (R)")) return ControllerKind.JoyCon2Right;
        if (Starts("Joy-Con (L)")) return ControllerKind.JoyCon1Left;
        if (Starts("Joy-Con (R)")) return ControllerKind.JoyCon1Right;
        if (Starts("Pro Controller 2") || Has("Switch 2 Pro")) return ControllerKind.Pro2;
        if (Has("GameCube")) return ControllerKind.GameCube2;
        if (Starts("Pro Controller") || Starts("Lic Pro Controller")) return ControllerKind.Pro1;
        if (Starts("NES Controller") || Starts("HVC Controller")) return ControllerKind.NesController;
        if (Starts("SNES Controller")) return ControllerKind.SnesController;
        if (Starts("N64 Controller")) return ControllerKind.N64Controller;
        if (Starts("MD/Gen Control Pad")) return ControllerKind.MegaDrive;
        if (Starts("Nintendo RVL-CNT-01-UC")) return ControllerKind.WiiUPro;
        if (Starts("Nintendo RVL-CNT-01")) return ControllerKind.WiiRemote;
        if (Has("DualSense")) return ControllerKind.DualSense;
        if (Starts("Wireless Controller")) return ControllerKind.DualShock4;
        if (Has("Xbox")) return ControllerKind.XboxController;
        return ControllerKind.Unknown;
    }

    /// <summary>Verbindet sich die Art über Bluetooth LE (sonst klassisches Bluetooth)?</summary>
    public static bool IsBle(ControllerKind kind) => kind.IsSwitch2() || kind == ControllerKind.XboxController;
}
