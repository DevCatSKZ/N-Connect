namespace Switch2Pro.Bridge.Usb;

/// <summary>Ein Knoten der Windows-Gerätehierarchie: Instanz-ID, Treiberdienst (oder null) und Hardware-IDs.</summary>
internal sealed record DeviceNode(string InstanceId, string? Service, IReadOnlyList<string> HardwareIds);

/// <summary>
/// Echtes Gerät (hängt an einem USB-Host bzw. am Bluetooth-Stack) oder virtuelles? Virtuelle Ausgabe-Controller wie
/// unser ViGEm-DualShock-4 dürfen nie als Eingabe dienen und nie versteckt werden: N-Connect sähe sonst sein eigenes
/// Pad als neuen Controller, legte dafür wieder ein Pad an usw. (bis 8 Geister-Spieler, Issue #1).
/// <para>
/// Gemessen: Der ViGEm-Bus ist ein Root-Knoten (ROOT\SYSTEM\0001, Dienst „ViGEmBus“, Hardware-ID
/// „Nefarius\ViGEmBus\Gen1“). Seine Pads melden sich darunter als USB-Geräte (USB\VID_054C&amp;PID_05C4\… bzw.
/// USB\VID_045E&amp;PID_028E\…), darunter das HID-Gerät. Ein „USB\“-Vorfahre allein beweist also nichts; ein
/// ROOT\-Vorfahre aber auch nicht – echte Geräte haben weit oben ROOT\ACPI_HAL\0000.
/// </para>
/// </summary>
internal static class DeviceTree
{
    /// <summary>Schutz vor Schleifen; echte Ketten haben etwa 6–12 Ebenen.</summary>
    private const int MaxDepth = 32;

    /// <summary>Echtes Gerät zu einem HID-Schnittstellenpfad? false, wenn virtuell oder nicht bestimmbar.</summary>
    public static bool IsPhysical(string hidPath) =>
        UsbNative.GetInstanceId(hidPath) is { } id && IsPhysicalInstance(id);

    /// <summary>Echtes Gerät zu einer Geräteinstanz-ID (z. B. HID\VID_054C&amp;PID_05C4\…)?</summary>
    public static bool IsPhysicalInstance(string instanceId) =>
        IsPhysicalChain(UsbNative.GetDeviceChain(instanceId, MaxDepth));

    /// <summary>
    /// Entscheidung allein aus der Kette (Index 0 = das Gerät selbst, dann die Eltern bis zur Wurzel):
    /// virtuell, sobald irgendein Knoten zum ViGEmBus gehört (Dienst oder Hardware-ID, eigene und fremde Pads,
    /// z. B. DS4Windows). Sonst von unten nach oben: echt, wenn zuerst ein USB-Host/Hub (USB\ROOT_HUB…, PCI\…) oder
    /// der Bluetooth-Stack (BTH…) kommt; virtuell, wenn zuerst ein Software-Knoten (ROOT\, SWD\) oder die Wurzel
    /// (HTREE\) kommt. Bricht die Kette vorher ab, gilt das Gerät im Zweifel als nicht echt.
    /// </summary>
    public static bool IsPhysicalChain(IReadOnlyList<DeviceNode> chain)
    {
        if (chain.Any(IsViGEmBus))
            return false;
        foreach (var node in chain)
        {
            string id = node.InstanceId;
            if (Starts(id, "BTH"))
                return true; // BTHENUM\, BTHLE\, BTHLEDEVICE\, BTH\: über Bluetooth
            if (Starts(id, @"USB\ROOT_HUB") || Starts(id, @"PCI\"))
                return true; // an einem echten USB-Host-Controller
            if (Starts(id, @"ROOT\") || Starts(id, @"SWD\") || Starts(id, @"HTREE\"))
                return false; // über der USB-Kette nur Software: virtueller Bus
            // HID\…, USB\VID_… (Gerät, Schnittstelle, externer Hub) und Unbekanntes: weiter nach oben
        }
        return false;
    }

    private static bool IsViGEmBus(DeviceNode node) =>
        string.Equals(node.Service, "ViGEmBus", StringComparison.OrdinalIgnoreCase)
        || node.HardwareIds.Any(h => h.Contains("ViGEmBus", StringComparison.OrdinalIgnoreCase));

    private static bool Starts(string id, string prefix) => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
