using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Switch2Pro.BtIdentityProbe;

/// <summary>Angaben des Bluetooth-Adapters (aus BluetoothGetRadioInfo und IOCTL_BTH_GET_LOCAL_INFO).</summary>
internal sealed record RadioInfo(string Name, ulong Address, ushort Manufacturer, ushort LmpSubversion, byte? LmpVersion, byte? HciVersion);

/// <summary>
/// Zugriff auf den Bluetooth-Adapter über die Windows-Bluetooth-API. Herstellerbefehle laufen über
/// IOCTL_BTH_HCI_VENDOR_COMMAND (bthioctl.h): Windows reicht sie nur an Adapter mit passender Hersteller-ID weiter
/// und verlangt Adminrechte mit dem Privileg SE_LOAD_DRIVER_NAME.
/// </summary>
internal sealed class BtRadio : IDisposable
{
    private const uint IoctlGetLocalInfo = 0x00410000;
    private const uint IoctlHciVendorCommand = 0x00410050;

    private readonly SafeFileHandle _handle;

    /// <summary>Rohe Ein- und Ausgabepuffer der Herstellerbefehle ausgeben (Schalter „--diagnose“).</summary>
    public static bool Diagnostics { get; set; }

    private BtRadio(SafeFileHandle handle) => _handle = handle;

    /// <summary>GUID_BTHPORT_DEVICE_INTERFACE: Geräteschnittstelle des Bluetooth-Adapters.</summary>
    private static readonly Guid BthPortInterface = new("0850302A-B344-4FDA-9BE9-90576B8D46F0");

    /// <summary>
    /// Adapter über seine Geräteschnittstelle mit Lese- und Schreibzugriff öffnen (für Herstellerbefehle).
    /// BluetoothFindFirstRadio öffnet ihn nur mit eingeschränkten Rechten.
    /// </summary>
    public static BtRadio? OpenForCommands()
    {
        if (CM_Get_Device_Interface_List_Size(out int size, BthPortInterface, null, 0) != 0 || size <= 1)
            return null;
        var buffer = new char[size];
        if (CM_Get_Device_Interface_List(BthPortInterface, null, buffer, size, 0) != 0)
            return null;
        string path = new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (path.Length == 0)
            return null;
        var handle = CreateFile(path, 0xC0000000 /* GENERIC_READ | GENERIC_WRITE */, 3 /* FILE_SHARE_READ | WRITE */,
            IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Adapter nicht zu öffnen: {path}");
        return new BtRadio(handle);
    }

    /// <summary>Ist SeLoadDriverPrivilege im Prozess-Token aktiv? (zur Fehlersuche)</summary>
    public static bool LoadDriverPrivilegeEnabled()
    {
        try
        {
            EnableLoadDriverPrivilege();
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Erster Bluetooth-Adapter oder null.</summary>
    public static BtRadio? OpenFirst()
    {
        var p = new FindRadioParams { Size = Marshal.SizeOf<FindRadioParams>() };
        IntPtr find = BluetoothFindFirstRadio(ref p, out var radio);
        if (find == IntPtr.Zero)
            return null;
        BluetoothFindRadioClose(find);
        return new BtRadio(new SafeFileHandle(radio, ownsHandle: true));
    }

    public RadioInfo GetInfo()
    {
        var info = new RadioInfoNative { Size = Marshal.SizeOf<RadioInfoNative>() };
        int rc = BluetoothGetRadioInfo(_handle, ref info);
        if (rc != 0)
            throw new Win32Exception(rc);
        // BTH_LOCAL_RADIO_INFO: BTH_DEVICE_INFO (272 Byte) | flags | hciRevision | hciVersion | BTH_RADIO_INFO
        // (lmpSupportedFeatures @280, mfg @288, lmpSubversion @290, lmpVersion @292). Die Lage wird über die
        // Hersteller-ID gegengeprüft; passt sie nicht, bleiben LMP- und HCI-Version unbekannt.
        byte? lmp = null, hci = null;
        var buffer = new byte[512];
        if (DeviceIoControl(_handle, IoctlGetLocalInfo, null, 0, buffer, buffer.Length, out int returned, IntPtr.Zero)
            && returned >= 293 && BitConverter.ToUInt16(buffer, 288) == info.Manufacturer)
        {
            lmp = buffer[292];
            hci = buffer[278];
        }
        return new RadioInfo(info.Name, info.Address, info.Manufacturer, info.LmpSubversion, lmp, hci);
    }

    public static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Schickt einen Herstellerbefehl (OGF 0x3F) und liefert das Antwort-Event des Chips samt Event-Kopf
    /// (z. B. 0E … für Command Complete, FF … für ein Herstellerevent) oder null, wenn innerhalb der Zeit
    /// keine Antwort kam. <paramref name="patterns"/>: BTH_VENDOR_PATTERN-Einträge für Befehle, die statt
    /// Command Complete ein eigenes Event schicken (z. B. CSR BCCMD).
    /// </summary>
    public byte[]? VendorCommand(ushort manufacturer, ushort ocf, ReadOnlySpan<byte> parameters, ReadOnlySpan<byte> patterns, TimeSpan timeout)
    {
        if (parameters.Length > 255)
            throw new ArgumentException("Höchstens 255 Byte Parameter.");
        ushort opcode = (ushort)(0x3F << 10 | ocf & 0x3FF);
        // BTH_VENDOR_SPECIFIC_COMMAND: ULONG ManufacturerId, UCHAR LmpVersion (0 = alle Versionen dieses Herstellers),
        // BOOLEAN MatchAnySinglePattern, BTH_COMMAND_HEADER { USHORT OpCode, UCHAR TotalParameterLength }, Data.
        var input = new byte[Math.Max(12, 9 + parameters.Length + patterns.Length)];
        BitConverter.TryWriteBytes(input.AsSpan(0), (uint)manufacturer);
        input[4] = 0;
        input[5] = patterns.Length > 0 ? (byte)1 : (byte)0;
        BitConverter.TryWriteBytes(input.AsSpan(6), opcode);
        input[8] = (byte)parameters.Length;
        parameters.CopyTo(input.AsSpan(9));
        patterns.CopyTo(input.AsSpan(9 + parameters.Length));

        EnableLoadDriverPrivilege();
        // BTH_VENDOR_EVENT_INFO: BTH_ADDR BthAddress, ULONG EventSize, UCHAR EventInfo[].
        var output = new byte[12 + 260];
        // Der Handle ist synchron geöffnet; ein Befehl ohne passende Antwort kann hängen. Deshalb in einem eigenen
        // Thread mit Zeitlimit – ein hängender Aufruf endet spätestens mit dem Programm.
        var call = Task.Run(() =>
        {
            bool ok = DeviceIoControl(_handle, IoctlHciVendorCommand, input, input.Length, output, output.Length, out int returned, IntPtr.Zero);
            return (ok, returned, error: ok ? 0 : Marshal.GetLastWin32Error());
        });
        if (!call.Wait(timeout))
            return null;
        var (ok, returned, error) = call.Result;
        if (!ok)
            throw new Win32Exception(error);
        if (Diagnostics)
        {
            Console.WriteLine($"    [Diagnose] Eingabe ({input.Length} B): {Convert.ToHexString(input)}");
            Console.WriteLine($"    [Diagnose] zurückgegeben: {returned} B, Ausgabe: {Convert.ToHexString(output.AsSpan(0, Math.Min(output.Length, Math.Max(returned, 32))))}");
        }
        // Erprobt am 04.10.2026 (Barrot, Windows 11): Windows liefert das rohe Event (Code, Länge, Parameter) ohne
        // den in der Doku beschriebenen BTH_VENDOR_EVENT_INFO-Kopf. Beide Formen erkennen.
        if (returned >= 2 && output[1] + 2 == returned)
            return output.AsSpan(0, returned).ToArray();
        int size = (int)Math.Min(BitConverter.ToUInt32(output, 8), (uint)Math.Max(0, returned - 12));
        return output.AsSpan(12, size).ToArray();
    }

    private static void EnableLoadDriverPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008 /* TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY */, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            if (!LookupPrivilegeValue(null, "SeLoadDriverPrivilege", out long luid))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var tp = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 0x2 /* SE_PRIVILEGE_ENABLED */ };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            int error = Marshal.GetLastWin32Error();
            if (error != 0)
                throw new Win32Exception(error, "SeLoadDriverPrivilege nicht verfügbar – als Administrator starten.");
        }
    }

    public void Dispose() => _handle.Dispose();

    // ---------- P/Invoke ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct FindRadioParams { public int Size; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RadioInfoNative
    {
        public int Size;
        public ulong Address;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
        public uint ClassOfDevice;
        public ushort LmpSubversion;
        public ushort Manufacturer;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public int Count;
        public long Luid;
        public int Attributes;
    }

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref FindRadioParams p, out IntPtr radio);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern bool BluetoothFindRadioClose(IntPtr find);

    [DllImport("BluetoothApis.dll")]
    private static extern int BluetoothGetRadioInfo(SafeFileHandle radio, ref RadioInfoNative info);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputSize,
        byte[] output, int outputSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition,
        uint flags, IntPtr template);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_Size(out int size, in Guid interfaceClass, string? deviceId, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List(in Guid interfaceClass, string? deviceId, char[] buffer, int length, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disableAll, ref TokenPrivileges state,
        int bufferLength, IntPtr previous, IntPtr returnLength);
}
