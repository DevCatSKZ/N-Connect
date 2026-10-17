using Switch2Pro.Bridge.Usb;

namespace Switch2Pro.Bridge.Tests;

/// <summary>
/// Echtes oder virtuelles Gerät (Issue #1: eigenes ViGEm-DualShock-4 als neuer Controller, bis 8 Geister-Spieler).
/// Die Ketten sind echten Gerätebäumen nachgebildet (Instanz-ID, Dienst, Hardware-IDs; Index 0 = das Gerät selbst).
/// </summary>
public class DeviceTreeTests
{
    private static DeviceNode N(string id, string? service = null, params string[] hardwareIds) => new(id, service, hardwareIds);

    /// <summary>Oberer Teil jedes echten PCs: USB-Host am PCI-Bus, darüber ACPI und die Wurzel.</summary>
    private static readonly DeviceNode[] PcTop =
    [
        N(@"USB\ROOT_HUB30\5&19b4ed14&0&0", "USBHUB3", @"USB\ROOT_HUB30&VID1022&PID43EE&REV0000", @"USB\ROOT_HUB30"),
        N(@"PCI\VEN_1022&DEV_43EE&SUBSYS_11421B21&REV_00\4&bd8ae77&0&000A", "USBXHCI", @"PCI\VEN_1022&DEV_43EE&SUBSYS_11421B21&REV_00"),
        N(@"PCI\VEN_1022&DEV_1483&SUBSYS_87C01043&REV_00\3&11583659&0&0A", "pci"),
        N(@"ACPI\PNP0A08\0", "pci", @"ACPI\PNP0A08"),
        N(@"ACPI_HAL\PNP0C08\0", "ACPI", @"ACPI_HAL\PNP0C08"),
        N(@"ROOT\ACPI_HAL\0000", @"\Driver\ACPI_HAL", "acpiapic"),
        N(@"HTREE\ROOT\0"),
    ];

    /// <summary>Der ViGEm-Bus, wie er auf einem PC gemessen wurde.</summary>
    private static readonly DeviceNode[] ViGEmTop =
    [
        N(@"ROOT\SYSTEM\0001", "ViGEmBus", @"Nefarius\ViGEmBus\Gen1"),
        N(@"HTREE\ROOT\0"),
    ];

    [Fact]
    public void EchterDs4_ImKabel_IstEcht()
    {
        DeviceNode[] chain =
        [
            N(@"HID\VID_054C&PID_05C4&MI_03\8&2a1b3c4d&0&0000", null, @"HID\VID_054C&PID_05C4&REV_0100&MI_03"),
            N(@"USB\VID_054C&PID_05C4&MI_03\7&1f2e3d4c&0&0003", "HidUsb", @"USB\VID_054C&PID_05C4&REV_0100&MI_03"),
            N(@"USB\VID_054C&PID_05C4\5&2c3d4e5f&0&2", "usbccgp", @"USB\VID_054C&PID_05C4&REV_0100"),
            .. PcTop,
        ];
        Assert.True(DeviceTree.IsPhysicalChain(chain));
    }

    [Fact]
    public void EchterDs4_HinterExternemHub_IstEcht()
    {
        DeviceNode[] chain =
        [
            N(@"HID\VID_054C&PID_09CC&MI_03\8&aa&0&0000"),
            N(@"USB\VID_054C&PID_09CC&MI_03\7&bb&0&0003", "HidUsb"),
            N(@"USB\VID_054C&PID_09CC\6&cc&0&4", "usbccgp"),
            N(@"USB\VID_2109&PID_2817\MSFT20000000000", "USBHUB3"),
            N(@"USB\VID_1A40&PID_0101\6&2b113eed&0&13", "USBHUB3"),
            .. PcTop,
        ];
        Assert.True(DeviceTree.IsPhysicalChain(chain));
    }

    [Fact]
    public void EchterDs4_PerBluetooth_IstEcht()
    {
        DeviceNode[] chain =
        [
            N(@"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&05C4\9&3a&0&0000"),
            N(@"BTHENUM\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&05C4\8&1b&0&A4AE12345678_C00000000", "HidBth"),
            N(@"BTH\MS_BTHBRB\7&2c&0&1", "BthEnum"),
            N(@"USB\VID_8087&PID_0026\5&3d&0&10", "BTHUSB"),
            .. PcTop,
        ];
        Assert.True(DeviceTree.IsPhysicalChain(chain));
    }

    [Fact]
    public void EchterController_PerBluetoothLe_IstEcht()
    {
        DeviceNode[] chain =
        [
            N(@"HID\{00001812-0000-1000-8000-00805F9B34FB}&DEV&VID_045E&PID_0B13\9&1&0&0000"),
            N(@"BTHLEDEVICE\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&02045E_PID&0B13\8&2&0&0", "HidBth"),
            N(@"BTHLE\DEV_A4AE12345678\7&3&0&A4AE12345678", "BthLEEnum"),
            N(@"BTH\MS_BTHLE\6&4&0&2", "BthLEEnum"),
            N(@"USB\VID_8087&PID_0026\5&3d&0&10", "BTHUSB"),
            .. PcTop,
        ];
        Assert.True(DeviceTree.IsPhysicalChain(chain));
    }

    [Fact]
    public void ViGEmDs4_IstVirtuell()
    {
        // Aus dem Issue-#1-Protokoll: HID#VID_054C&PID_05C4&REV_0100#2&be03103&0&0000.
        DeviceNode[] chain =
        [
            N(@"HID\VID_054C&PID_05C4&REV_0100\2&be03103&0&0000", null, @"HID\VID_054C&PID_05C4&REV_0100"),
            N(@"USB\VID_054C&PID_05C4\1&2d595ca7&1&01", "HidUsb", @"USB\VID_054C&PID_05C4&REV_0100", @"USB\VID_054C&PID_05C4"),
            .. ViGEmTop,
        ];
        Assert.False(DeviceTree.IsPhysicalChain(chain));
    }

    [Fact]
    public void ViGEmXbox360_IstVirtuell()
    {
        DeviceNode[] chain =
        [
            N(@"USB\VID_045E&PID_028E\1&2d595ca7&2&02", "xusb22", @"USB\VID_045E&PID_028E&REV_0114", @"USB\VID_045E&PID_028E"),
            .. ViGEmTop,
        ];
        Assert.False(DeviceTree.IsPhysicalChain(chain));
    }

    [Fact]
    public void ViGEmDs4_OhneLesbarenDienst_AmRootKnotenErkannt()
    {
        // Dienst und Hardware-IDs nicht lesbar: über der USB-Kette kommt trotzdem nur ein Root-Knoten.
        DeviceNode[] chain =
        [
            N(@"HID\VID_054C&PID_05C4&REV_0100\2&1e99e1a8&0&0000"),
            N(@"USB\VID_054C&PID_05C4\1&2d595ca7&3&03"),
            N(@"ROOT\SYSTEM\0001"),
            N(@"HTREE\ROOT\0"),
        ];
        Assert.False(DeviceTree.IsPhysicalChain(chain));
    }

    [Fact]
    public void ViGEmBus_NurAnDerHardwareId_IstVirtuell()
    {
        DeviceNode[] chain =
        [
            N(@"HID\VID_054C&PID_05C4&REV_0100\2&3153924d&0&0000"),
            N(@"USB\VID_054C&PID_05C4\1&2d595ca7&4&04", "HidUsb"),
            N(@"ROOT\SYSTEM\0001", null, @"Nefarius\ViGEmBus\Gen1"),
            N(@"HTREE\ROOT\0"),
        ];
        Assert.False(DeviceTree.IsPhysicalChain(chain));
    }

    [Fact]
    public void ViGEmBus_HatVorrang_AuchWennDarunterEinHubStuende()
    {
        // Ausgedacht: ein virtueller Hub unter dem ViGEm-Bus – der Dienst entscheidet trotzdem.
        DeviceNode[] chain =
        [
            N(@"HID\VID_054C&PID_05C4\1"),
            N(@"USB\VID_054C&PID_05C4\2", "HidUsb"),
            N(@"USB\ROOT_HUB30\3", "USBHUB3"),
            N(@"ROOT\SYSTEM\0001", "ViGEmBus"),
            N(@"HTREE\ROOT\0"),
        ];
        Assert.False(DeviceTree.IsPhysicalChain(chain));
    }

    [Fact]
    public void SoftwareGeraet_UeberSwd_IstVirtuell()
    {
        DeviceNode[] chain =
        [
            N(@"HID\VID_054C&PID_05C4\3&1234&0&0000"),
            N(@"USB\VID_054C&PID_05C4\2&5678&0&01", "HidUsb"),
            N(@"SWD\VIRTUALUSB\{6C1A0F4B-0000-0000-0000-000000000000}", "VirtualUsb"),
            N(@"HTREE\ROOT\0"),
        ];
        Assert.False(DeviceTree.IsPhysicalChain(chain));
    }

    [Fact]
    public void HidDirektAmRootKnoten_IstVirtuell()
    {
        DeviceNode[] chain =
        [
            N(@"HID\VID_054C&PID_0CE6\4&abc&0&0000"),
            N(@"ROOT\VHIDMINI\0000", "vhidmini"),
            N(@"HTREE\ROOT\0"),
        ];
        Assert.False(DeviceTree.IsPhysicalChain(chain));
    }

    [Fact]
    public void RootAcpiHal_GanzOben_MachtEchtesGeraetNichtVirtuell()
    {
        // Der USB-Host kommt vor ROOT\ACPI_HAL – „irgendein ROOT\-Vorfahre“ darf nicht als virtuell gelten.
        Assert.Contains(PcTop, n => n.InstanceId.StartsWith(@"ROOT\", StringComparison.Ordinal));
        DeviceNode[] chain = [N(@"HID\VID_046D&PID_C548&MI_00\A&32BD1F4B&0&0000", "kbdhid"), N(@"USB\VID_046D&PID_C548&MI_00\9&b5f801f&0&0000", "HidUsb"), .. PcTop];
        Assert.True(DeviceTree.IsPhysicalChain(chain));
    }

    [Fact]
    public void LeereOderAbgebrocheneKette_GiltNichtAlsEcht()
    {
        Assert.False(DeviceTree.IsPhysicalChain([]));
        // Gerät inzwischen weg: Kette endet in der USB-Kette, ohne Host oder Bluetooth zu erreichen.
        Assert.False(DeviceTree.IsPhysicalChain([N(@"HID\VID_054C&PID_05C4\1"), N(@"USB\VID_054C&PID_05C4\2", "HidUsb")]));
    }

    [Fact]
    public void Vergleich_IgnoriertGrossKleinschreibung()
    {
        DeviceNode[] chain = [N(@"hid\vid_054c&pid_05c4\1"), N(@"usb\vid_054c&pid_05c4\2"), N(@"root\system\0001", "vigembus")];
        Assert.False(DeviceTree.IsPhysicalChain(chain));
        Assert.True(DeviceTree.IsPhysicalChain([N(@"hid\x\1"), N(@"bthenum\{00001124}\2")]));
    }

    /// <summary>
    /// Freigeben in HidHide (Aufräumen nach Issue #1) nur mit Beweis: ein Knoten des ViGEm-Busses. Andere Software-Busse
    /// (USB über Netzwerk o. Ä.) beweisen nichts – dort kann ein echter, absichtlich versteckter Controller hängen.
    /// </summary>
    [Fact]
    public void FreigebenNurAmViGEmBus()
    {
        DeviceNode[] vigem =
        [
            N(@"HID\VID_054C&PID_05C4&REV_0100\2&be03103&0&0000", null, @"HID\VID_054C&PID_05C4&REV_0100"),
            N(@"USB\VID_054C&PID_05C4\1&2d595ca7&1&01", "HidUsb"),
            .. ViGEmTop,
        ];
        Assert.True(DeviceTree.IsViGEmChain(vigem));
        Assert.True(DeviceTree.IsViGEmChain([N(@"HID\X\1"), N(@"USB\X\2", "HidUsb"), N(@"ROOT\SYSTEM\0001", null, @"Nefarius\ViGEmBus\Gen1")]));

        // Software-Bus ohne ViGEm: nicht echt (wird nicht versteckt), aber auch nicht freigegeben.
        DeviceNode[] swd = [N(@"HID\VID_057E&PID_2009\3&1"), N(@"USB\VID_057E&PID_2009\2&5"), N(@"SWD\VIRTUALUSB\1", "VirtualUsb")];
        Assert.False(DeviceTree.IsPhysicalChain(swd));
        Assert.False(DeviceTree.IsViGEmChain(swd));

        DeviceNode[] real =
        [
            N(@"HID\VID_054C&PID_05C4&MI_03\8&2a1b3c4d&0&0000"),
            N(@"USB\VID_054C&PID_05C4&MI_03\7&1f2e3d4c&0&0003", "HidUsb"),
            N(@"USB\VID_054C&PID_05C4\5&2c3d4e5f&0&2", "usbccgp"),
            .. PcTop,
        ];
        Assert.False(DeviceTree.IsViGEmChain(real));
        // Nicht angeschlossen: CM_Locate_DevNode scheitert, die Kette ist leer.
        Assert.False(DeviceTree.IsViGEmChain([]));
    }
}
