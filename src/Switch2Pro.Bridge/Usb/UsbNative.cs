using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Switch2Pro.Bridge.Usb;

/// <summary>Win32-Aufrufe für Geräteliste (CfgMgr32), WinUSB und HID.</summary>
internal static class UsbNative
{
    // ---------- Geräteschnittstellen ----------

    /// <summary>Von der Controller-Firmware (MS-OS-Deskriptor) gemeldete WinUSB-Schnittstelle (Interface 1).</summary>
    public static readonly Guid ControllerWinUsbInterface = new("6f13725e-ef0e-4fd3-ae5f-b2de989ec825");
    /// <summary>Allgemeine WinUSB-Schnittstelle (GUID_DEVINTERFACE_WINUSB), falls die Firmware keine eigene meldet.</summary>
    public static readonly Guid GenericWinUsbInterface = new("dee824ef-729b-4a0e-9c14-b7117d33a817");
    public static readonly Guid HidInterface = new("4d1e55b2-f16f-11cf-88cb-001111000030");

    private const int CR_SUCCESS = 0;
    private const int CR_BUFFER_SMALL = 0x1A;

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    private static readonly DEVPROPKEY DEVPKEY_Device_InstanceId = new()
    {
        fmtid = new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"),
        pid = 256,
    };

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid interfaceClass, string? deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_ListW(ref Guid interfaceClass, string? deviceId, char[] buffer, uint length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_PropertyW(string path, ref DEVPROPKEY key, out uint type,
        byte[]? buffer, ref uint size, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(uint devInst, char[] buffer, uint length, uint flags);

    /// <summary>Pfade aller vorhandenen Geräteschnittstellen einer Klasse.</summary>
    public static List<string> GetInterfacePaths(Guid interfaceClass)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_Interface_List_SizeW(out uint length, ref interfaceClass, null, 0) != CR_SUCCESS || length <= 1)
                return [];
            var buffer = new char[length];
            int cr = CM_Get_Device_Interface_ListW(ref interfaceClass, null, buffer, length, 0);
            if (cr == CR_BUFFER_SMALL)
                continue; // Liste hat sich zwischen den Aufrufen geändert
            if (cr != CR_SUCCESS)
                return [];
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        return [];
    }

    /// <summary>Geräteinstanz-ID zu einem Schnittstellenpfad, z. B. USB\VID_057E&amp;PID_2069&amp;MI_01\….</summary>
    public static string? GetInstanceId(string interfacePath)
    {
        var key = DEVPKEY_Device_InstanceId;
        uint size = 0;
        CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out _, null, ref size, 0);
        if (size == 0)
            return null;
        var buffer = new byte[size];
        if (CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out _, buffer, ref size, 0) != CR_SUCCESS)
            return null;
        return System.Text.Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
    }

    /// <summary>Instanz-ID des übergeordneten Geräts (oder null).</summary>
    public static string? GetParentInstanceId(string instanceId)
    {
        if (CM_Locate_DevNodeW(out uint node, instanceId, 0) != CR_SUCCESS)
            return null;
        if (CM_Get_Parent(out uint parent, node, 0) != CR_SUCCESS)
            return null;
        var buffer = new char[512];
        if (CM_Get_Device_IDW(parent, buffer, (uint)buffer.Length, 0) != CR_SUCCESS)
            return null;
        return new string(buffer).TrimEnd('\0');
    }

    // ---------- Dateien ----------

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    public static SafeFileHandle Open(string path, uint flags)
    {
        var handle = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, flags, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Gerät nicht zu öffnen: {path}");
        return handle;
    }

    // ---------- WinUSB ----------

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct USB_INTERFACE_DESCRIPTOR
    {
        public byte bLength, bDescriptorType, bInterfaceNumber, bAlternateSetting, bNumEndpoints;
        public byte bInterfaceClass, bInterfaceSubClass, bInterfaceProtocol, iInterface;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINUSB_PIPE_INFORMATION
    {
        public int PipeType; // 2 = Bulk
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    public const int UsbdPipeTypeBulk = 2;
    public const uint PIPE_TRANSFER_TIMEOUT = 0x03;

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_Initialize(SafeFileHandle device, out IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_Free(IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_QueryInterfaceSettings(IntPtr interfaceHandle, byte alternateInterfaceNumber,
        out USB_INTERFACE_DESCRIPTOR descriptor);

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_QueryPipe(IntPtr interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex,
        out WINUSB_PIPE_INFORMATION pipeInformation);

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_SetPipePolicy(IntPtr interfaceHandle, byte pipeId, uint policyType,
        uint valueLength, ref uint value);

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_WritePipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, uint length,
        out uint transferred, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_ReadPipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, uint length,
        out uint transferred, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_AbortPipe(IntPtr interfaceHandle, byte pipeId);

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_FlushPipe(IntPtr interfaceHandle, byte pipeId);

    // ---------- HID ----------

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsed);

    [DllImport("hid.dll")]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsed);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);

    /// <summary>Ausgabebericht über den Steuerkanal (für Geräte, die WriteFile ablehnen, z. B. manche Wii-Fernbedienungen).</summary>
    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetOutputReport(SafeFileHandle device, byte[] report, int length);

    public static HIDP_CAPS GetHidCaps(SafeFileHandle device)
    {
        if (!HidD_GetPreparsedData(device, out var preparsed))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HID-Beschreibung nicht lesbar");
        try
        {
            const int HIDP_STATUS_SUCCESS = 0x00110000;
            if (HidP_GetCaps(preparsed, out var caps) != HIDP_STATUS_SUCCESS)
                throw new InvalidOperationException("HID-Fähigkeiten nicht lesbar");
            return caps;
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }
}
