using System.Runtime.InteropServices;

namespace Switch2Pro.Bridge;

/// <summary>
/// Koppelt Wii-Fernbedienungen und Wii U Pro Controller dauerhaft mit Windows. Über „Gerät hinzufügen“ klappt das
/// oft nicht, weil die Fernbedienung eine binäre PIN erwartet: die Bluetooth-Adresse des PCs (bei der roten
/// SYNC-Taste) bzw. ihre eigene (bei 1+2), Byte für Byte. Danach verbindet sie sich per Tastendruck.
/// Windows-Bluetooth-API (BluetoothApis.dll / bthprops.cpl), Vorgehen wie in Dolphin beschrieben.
/// </summary>
internal static class WiiPairing
{
    private const int BLUETOOTH_MAX_NAME_SIZE = 248;
    private const uint BLUETOOTH_SERVICE_ENABLE = 0x01;
    private static readonly Guid HidServiceClass = new("00001124-0000-1000-8000-00805f9b34fb");

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BLUETOOTH_DEVICE_INFO
    {
        public int dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        public int fConnected, fRemembered, fAuthenticated;
        public SYSTEMTIME stLastSeen, stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = BLUETOOTH_MAX_NAME_SIZE)]
        public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public int dwSize;
        public int fReturnAuthenticated, fReturnRemembered, fReturnUnknown, fReturnConnected, fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_FIND_RADIO_PARAMS { public int dwSize; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BLUETOOTH_RADIO_INFO
    {
        public int dwSize;
        public ulong address;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = BLUETOOTH_MAX_NAME_SIZE)]
        public string szName;
        public uint ulClassofDevice;
        public ushort lmpSubversion, manufacturer;
    }

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS p, out IntPtr radio);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern bool BluetoothFindRadioClose(IntPtr find);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern uint BluetoothGetRadioInfo(IntPtr radio, ref BLUETOOTH_RADIO_INFO info);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS p, ref BLUETOOTH_DEVICE_INFO info);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern bool BluetoothFindNextDevice(IntPtr find, ref BLUETOOTH_DEVICE_INFO info);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern bool BluetoothFindDeviceClose(IntPtr find);

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint BluetoothAuthenticateDevice(IntPtr parent, IntPtr radio, ref BLUETOOTH_DEVICE_INFO info,
        [MarshalAs(UnmanagedType.LPArray)] char[] passkey, uint passkeyLength);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern uint BluetoothSetServiceState(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info, ref Guid service, uint flags);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern uint BluetoothRemoveDevice(ref ulong address);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static bool IsWiiName(string? name) =>
        name is not null && (name.StartsWith("Nintendo RVL-CNT-01", StringComparison.OrdinalIgnoreCase)
                             || name.StartsWith("Nintendo RVL-WBC-01", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sucht bis zu <paramref name="timeout"/> nach Wii-Controllern im Kopplungsmodus (rote SYNC-Taste) und koppelt
    /// sie. Liefert die Namen der neu gekoppelten Geräte. Läuft blockierend – auf einem Hintergrund-Thread aufrufen.
    /// </summary>
    public static List<string> ScanAndPair(TimeSpan timeout, Action<string> progress, CancellationToken ct)
    {
        var paired = new List<string>();
        var radioParams = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
        var findRadio = BluetoothFindFirstRadio(ref radioParams, out var radio);
        if (findRadio == IntPtr.Zero)
        {
            progress("Kein Bluetooth-Adapter gefunden.");
            return paired;
        }
        BluetoothFindRadioClose(findRadio);
        try
        {
            var radioInfo = new BLUETOOTH_RADIO_INFO { dwSize = Marshal.SizeOf<BLUETOOTH_RADIO_INFO>() };
            BluetoothGetRadioInfo(radio, ref radioInfo);
            var until = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
            {
                progress("Suche … rote SYNC-Taste der Fernbedienung (im Batteriefach) bzw. des Wii U Pro Controllers drücken.");
                foreach (var device in Inquiry(radio))
                {
                    if (!IsWiiName(device.szName))
                        continue;
                    var info = device;
                    if (info.fConnected != 0 && info.fAuthenticated != 0)
                        continue;
                    // Gemerkte Fernbedienungen nur anfassen, wenn sie gerade in Reichweite sichtbar sind (SYNC gedrückt);
                    // sonst ginge die Kopplung einer anderen, nur ausgeschalteten Fernbedienung verloren.
                    if (info.fRemembered != 0 && !SeenRecently(info.stLastSeen))
                        continue;
                    // Alte, nicht verbundene Kopplung entfernen (sonst verweigert Windows die neue).
                    if (info.fRemembered != 0 && info.fConnected == 0)
                    {
                        ulong address = info.Address;
                        BluetoothRemoveDevice(ref address);
                    }
                    progress($"Gefunden: {info.szName} – kopple …");
                    if (Pair(radio, ref info, radioInfo.address) || Pair(radio, ref info, info.Address))
                    {
                        paired.Add(info.szName);
                        progress($"Gekoppelt: {info.szName}");
                        Log.Info($"Wii-Kopplung: {info.szName} ({info.Address:X12}) gekoppelt");
                    }
                }
                if (paired.Count > 0)
                    break;
            }
        }
        finally
        {
            CloseHandle(radio);
        }
        return paired;
    }

    /// <summary>Zuletzt gesehen (UTC) innerhalb der letzten 20 Sekunden?</summary>
    private static bool SeenRecently(SYSTEMTIME t)
    {
        if (t.Year < 2000)
            return false;
        try
        {
            // Windows liefert UTC; zur Sicherheit auch Ortszeit zulassen.
            var seen = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second);
            var window = TimeSpan.FromSeconds(20);
            return (DateTime.UtcNow - seen).Duration() < window || (DateTime.Now - seen).Duration() < window;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>Eine Suche (≈ 2,5 s) nach Geräten in Reichweite.</summary>
    private static List<BLUETOOTH_DEVICE_INFO> Inquiry(IntPtr radio)
    {
        var result = new List<BLUETOOTH_DEVICE_INFO>();
        var search = new BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
            fReturnAuthenticated = 1, fReturnRemembered = 1, fReturnUnknown = 1, fReturnConnected = 1, fIssueInquiry = 1,
            cTimeoutMultiplier = 2, hRadio = radio,
        };
        var info = new BLUETOOTH_DEVICE_INFO { dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(), szName = "" };
        var find = BluetoothFindFirstDevice(ref search, ref info);
        if (find == IntPtr.Zero)
            return result;
        try
        {
            do
            {
                result.Add(info);
                info = new BLUETOOTH_DEVICE_INFO { dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(), szName = "" };
            }
            while (BluetoothFindNextDevice(find, ref info));
        }
        finally
        {
            BluetoothFindDeviceClose(find);
        }
        return result;
    }

    /// <summary>Mit binärer PIN (6 Byte Adresse, niedrigstes Byte zuerst) authentifizieren und den HID-Dienst einschalten.</summary>
    private static bool Pair(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info, ulong pinAddress)
    {
        var pin = new char[6];
        for (int i = 0; i < 6; i++)
            pin[i] = (char)((pinAddress >> (8 * i)) & 0xFF);
        uint auth = BluetoothAuthenticateDevice(IntPtr.Zero, radio, ref info, pin, 6);
        if (auth != 0)
        {
            Log.Info($"Wii-Kopplung: Authentifizierung mit PIN {pinAddress:X12} → Fehler {auth}");
            return false;
        }
        var service = HidServiceClass;
        uint state = BluetoothSetServiceState(radio, ref info, ref service, BLUETOOTH_SERVICE_ENABLE);
        if (state != 0)
            Log.Info($"Wii-Kopplung: HID-Dienst einschalten → Fehler {state}");
        return state == 0;
    }
}
