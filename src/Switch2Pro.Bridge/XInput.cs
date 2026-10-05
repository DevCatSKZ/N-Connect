using System.Runtime.InteropServices;

namespace Switch2Pro.Bridge;

/// <summary>
/// XInput: Microsofts Schnittstelle für Xbox-Controller (360, One, Series, Elite …) – egal ob über USB,
/// Bluetooth oder dem Xbox-Wireless-Adapter. Deckt höchstens vier Controller ab (Slots 0–3).
/// Die Guide-Taste kommt nur über die interne erweiterte Abfrage (xinput1_4.dll, Ordinal 100);
/// <see cref="XInputGetCapabilitiesEx"/> (Ordinal 108) liefert VID/PID, damit das Modell erkannt wird.
/// </summary>
internal static partial class XInput
{
    public const int MaxControllers = 4;

    public const int ErrorSuccess = 0;
    public const int ErrorDeviceNotConnected = 1167;
    public const int ErrorEmpty = 4306;

    public const byte DeviceTypeGamepad = 0;
    public const byte DeviceTypeWheel = 1;
    public const byte DeviceTypeArcadeStick = 2;
    public const byte DeviceTypeFlightStick = 3;
    public const byte DeviceTypeDancePad = 4;
    public const byte DeviceTypeGuitar = 5;
    public const byte DeviceTypeDrumKit = 8;

    [StructLayout(LayoutKind.Sequential)]
    public struct Gamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX, LeftThumbY, RightThumbX, RightThumbY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct State
    {
        public uint PacketNumber;
        public Gamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Vibration
    {
        public ushort LeftMotor, RightMotor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BatteryInformation
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    /// <summary>Capabilities + VID/PID (über die erweiterte Abfrage, Ordinal 108).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Capabilities
    {
        public byte Type, SubType;
        public ushort Flags;
        public Gamepad Gamepad;
        public Vibration Vibration;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CapabilitiesEx
    {
        public Capabilities Capabilities;
        public ushort VendorId, ProductId, RevisionId;
        public ushort A1;
        public uint A2;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState14(uint index, out State state);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState9(uint index, out State state);

    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState13(uint index, out State state);

    /// <summary>Erweiterte Abfrage (Ordinal 100): liefert zusätzlich die Guide-Taste in wButtons.</summary>
    [DllImport("xinput1_4.dll", EntryPoint = "#100")]
    private static extern int XInputGetStateEx14(uint index, out State state);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    private static extern int XInputSetState14(uint index, ref Vibration vibration);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputSetState")]
    private static extern int XInputSetState9(uint index, ref Vibration vibration);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
    private static extern int XInputGetBatteryInformation14(uint index, byte deviceType, out BatteryInformation info);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetCapabilities")]
    private static extern int XInputGetCapabilities14(uint index, uint flags, out Capabilities capabilities);

    /// <summary>Erweiterte Fähigkeiten mit VID/PID (Ordinal 108, nur in xinput1_4.dll vorhanden).</summary>
    [DllImport("xinput1_4.dll", EntryPoint = "#108")]
    private static extern int XInputGetCapabilitiesEx14(ref CapabilitiesEx capabilities, uint index, uint flags);

    public static bool Available14 { get; }
    public static bool Available9 { get; }
    /// <summary>Erweiterte Abfrage verfügbar (Guide-Taste)?</summary>
    public static bool HasGetStateEx { get; }
    public static bool HasCapabilitiesEx { get; }

    static XInput()
    {
        try
        {
            var handle = GetModuleHandleW("xinput1_4.dll");
            if (handle == IntPtr.Zero)
                handle = LoadLibraryW("xinput1_4.dll");
            Available14 = handle != IntPtr.Zero;
            HasGetStateEx = Available14 && GetProcAddress(handle, 100) != IntPtr.Zero;
            HasCapabilitiesEx = Available14 && GetProcAddress(handle, 108) != IntPtr.Zero;
        }
        catch
        {
            Available14 = false;
        }
        try
        {
            var handle = GetModuleHandleW("xinput9_1_0.dll");
            if (handle == IntPtr.Zero)
                handle = LoadLibraryW("xinput9_1_0.dll");
            Available9 = handle != IntPtr.Zero;
        }
        catch
        {
            Available9 = false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW(string name);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetProcAddress(IntPtr module, IntPtr procName);

    /// <summary>Zustand lesen (0 = verbunden). Mit der erweiterten Abfrage inkl. Guide-Taste, sonst ohne.</summary>
    public static int GetState(int index, out State state)
    {
        if (HasGetStateEx)
        {
            int result = XInputGetStateEx14((uint)index, out state);
            if (result != ErrorEmpty) // ERROR_EMPTY = nicht verbunden bei der erweiterten Abfrage
                return result;
            return ErrorDeviceNotConnected;
        }
        if (Available14)
            return XInputGetState14((uint)index, out state);
        if (Available9)
            return XInputGetState9((uint)index, out state);
        state = default;
        return ErrorDeviceNotConnected;
    }

    public static int SetState(int index, Vibration vibration) =>
        Available14 ? XInputSetState14((uint)index, ref vibration)
        : Available9 ? XInputSetState9((uint)index, ref vibration) : ErrorDeviceNotConnected;

    /// <summary>Akkustand lesen (Typ + Stufe); null ohne XInput 1.4 bzw. Fehler.</summary>
    public static BatteryInformation? GetBatteryInformation(int index)
    {
        if (!Available14 || XInputGetBatteryInformation14((uint)index, DeviceTypeGamepad, out var info) != 0)
            return null;
        return info;
    }

    // ---------- virtuelle Slots (ViGEm) ----------

    /// <summary>XInput-Slots, die unsere eigenen virtuellen Xbox-360-Controller belegen – die sind keine Hardware.</summary>
    private static readonly HashSet<int> _virtualSlots = [];
    private static readonly object _virtualGate = new();

    /// <summary>Slot als virtuell/eigen markieren (ViGEm-Controller beim Anlegen bzw. Freigeben).</summary>
    public static void SetVirtualSlot(int index, bool isVirtual)
    {
        lock (_virtualGate)
        {
            if (isVirtual) _virtualSlots.Add(index);
            else _virtualSlots.Remove(index);
        }
    }

    /// <summary>Gehört der Slot zu einem unserer virtuellen Controller?</summary>
    public static bool IsVirtual(int index)
    {
        lock (_virtualGate)
            return _virtualSlots.Contains(index);
    }

    /// <summary>VID/PID lesen (für Modellname); null, wenn die erweiterte Abfrage fehlt.</summary>
    public static (ushort Vendor, ushort Product)? GetVidPid(int index)
    {
        if (!HasCapabilitiesEx)
            return null;
        var caps = new CapabilitiesEx();
        return XInputGetCapabilitiesEx14(ref caps, (uint)index, 0) == 0 ? (caps.VendorId, caps.ProductId) : null;
    }

    /// <summary>Produktname aus VID/PID (bekannte Microsoft-Controller und gängige Xbox-Zubehör).</summary>
    public static string? ProductName(ushort vid, ushort pid)
    {
        if (vid == 0x045E) // Microsoft
        {
            return pid switch
            {
                0x028E => "Xbox 360 Controller",
                0x028F => "Xbox 360 Wireless Controller",
                0x02A0 or 0x02A1 => "Xbox 360 Controller (Adapter)",
                0x02D1 or 0x02DD or 0x02E3 or 0x02EA => "Xbox One Controller",
                0x02E0 => "Xbox One S Controller",
                0x02FD => "Xbox One S Controller",
                0x0B00 or 0x0B0A => "Xbox Elite Series 2",
                0x0B05 or 0x0B0C => "Xbox Elite Controller",
                0x0B12 or 0x0B13 or 0x0B1E or 0x0B20 or 0x0B21 or 0x0B22 => "Xbox Wireless Controller (Series X|S)",
                0x0719 => "Xbox 360 Wireless Controller",
                _ => "Xbox-Controller (Microsoft)",
            };
        }
        return vid switch
        {
            0x044F => "Thrustmaster Xbox-Controller",
            0x0738 => "Mad Catz Xbox-Controller",
            0x0E6F => "PDP/Afterglow Xbox-Controller",
            0x0F0D => "HORI Xbox-Controller",
            0x1BB9 => "PDP Xbox-Controller",
            0x24C6 => "PowerA Xbox-Controller",
            0x2DC8 => "8BitDo Xbox-Controller",
            _ => null,
        };
    }
}
