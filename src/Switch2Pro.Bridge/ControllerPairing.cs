using System.Runtime.InteropServices;

namespace Switch2Pro.Bridge;

/// <summary>
/// Koppelt klassische Bluetooth-Controller selbst mit Windows – ohne Umweg über die Windows-Bluetooth-Einstellungen:
/// <list type="bullet">
/// <item>Switch 1 (Joy-Con, Pro Controller) und Nintendo-Switch-Online-Controller (NES, SNES, N64, Mega Drive):
/// einfache Kopplung ohne PIN („Just Works“); N-Connect bestätigt die Anfrage selbst.</item>
/// <item>Wii-Fernbedienung und Wii U Pro Controller: binäre PIN = Bluetooth-Adresse des PCs (rote SYNC-Taste) bzw.
/// die eigene (1+2), Byte für Byte – über „Gerät hinzufügen“ klappt das oft nicht (Vorgehen wie in Dolphin).</item>
/// </list>
/// Danach den HID-Dienst einschalten; N-Connect findet den Controller dann wie jeden gekoppelten.
/// Windows-Bluetooth-API (BluetoothApis.dll / bthprops.cpl). Switch-2-Controller (Bluetooth LE) brauchen das nicht.
/// </summary>
internal static class ControllerPairing
{
    private const int BLUETOOTH_MAX_NAME_SIZE = 248;
    private const uint BLUETOOTH_SERVICE_ENABLE = 0x01;
    private static readonly Guid HidServiceClass = new("00001124-0000-1000-8000-00805f9b34fb");

    /// <summary>Nur ein Suchlauf gleichzeitig (Hintergrund und Fenster „Controller koppeln“).</summary>
    private static readonly SemaphoreSlim ScanLock = new(1, 1);

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

    /// <summary>BLUETOOTH_AUTHENTICATION_CALLBACK_PARAMS (Anfrage beim Koppeln ohne PIN).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_AUTHENTICATION_CALLBACK_PARAMS
    {
        public BLUETOOTH_DEVICE_INFO deviceInfo;
        public int authenticationMethod, ioCapability, authenticationRequirements;
        public uint numericValue;
    }

    /// <summary>BLUETOOTH_AUTHENTICATE_RESPONSE: Adresse, Verfahren, Union (bis 32 Byte), Ablehnung.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct BLUETOOTH_AUTHENTICATE_RESPONSE
    {
        [FieldOffset(0)] public ulong bthAddressRemote;
        [FieldOffset(8)] public int authMethod;
        [FieldOffset(12)] public uint numericValue;
        [FieldOffset(44)] public byte negativeResponse;
    }

    private const int BLUETOOTH_AUTHENTICATION_METHOD_LEGACY = 1;
    private const int MITMProtectionNotRequiredBonding = 2;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool AuthenticationCallback(IntPtr param, IntPtr callbackParams);

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

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothAuthenticateDeviceEx(IntPtr parent, IntPtr radio, ref BLUETOOTH_DEVICE_INFO info,
        IntPtr oobData, int authenticationRequirement);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothRegisterForAuthenticationEx(ref BLUETOOTH_DEVICE_INFO info, out IntPtr registration,
        AuthenticationCallback callback, IntPtr param);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothUnregisterAuthentication(IntPtr registration);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothSendAuthenticationResponseEx(IntPtr radio, ref BLUETOOTH_AUTHENTICATE_RESPONSE response);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern uint BluetoothSetServiceState(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info, ref Guid service, uint flags);

    [DllImport("BluetoothApis.dll", SetLastError = true)]
    private static extern uint BluetoothRemoveDevice(ref ulong address);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static bool IsWiiName(string? name) =>
        name is not null && (name.StartsWith("Nintendo RVL-CNT-01", StringComparison.OrdinalIgnoreCase)
                             || name.StartsWith("Nintendo RVL-WBC-01", StringComparison.OrdinalIgnoreCase));

    /// <summary>Bluetooth-Namen der Switch-1- und Nintendo-Switch-Online-Controller (klassisches Bluetooth).</summary>
    private static readonly string[] Switch1Names =
    [
        "Joy-Con (L)", "Joy-Con (R)", "Pro Controller", "Lic Pro Controller",
        "NES Controller", "HVC Controller", "SNES Controller", "N64 Controller", "MD/Gen Control Pad",
    ];

    public static bool IsSwitch1Name(string? name) =>
        name is not null && Switch1Names.Any(n => name.StartsWith(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Fenster „Controller koppeln“: sucht bis zu <paramref name="timeout"/> nach Controllern im Kopplungsmodus und
    /// koppelt sie. Liefert die Namen der neu gekoppelten Geräte. Läuft blockierend – auf einem Hintergrund-Thread aufrufen.
    /// </summary>
    public static List<string> ScanAndPair(TimeSpan timeout, Action<string> progress, CancellationToken ct)
    {
        var paired = new List<string>();
        ScanLock.Wait(ct);
        try
        {
            if (!OpenRadio(out var radio, out ulong radioAddress))
            {
                progress("Kein Bluetooth-Adapter gefunden.");
                return paired;
            }
            try
            {
                var until = DateTime.UtcNow + timeout;
                while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
                {
                    progress("Suche … SYNC-Taste am Controller drücken (Joy-Con: an der Schiene, Wii-Fernbedienung: im Batteriefach).");
                    paired.AddRange(ScanOnce(radio, radioAddress, progress, repairRemembered: true));
                    if (paired.Count > 0)
                        break;
                    // Kurze Pause zwischen den Suchläufen: verbundene Controller bekommen wieder Funkzeit.
                    ct.WaitHandle.WaitOne(1000);
                }
            }
            finally
            {
                CloseHandle(radio);
            }
        }
        finally
        {
            ScanLock.Release();
        }
        return paired;
    }

    /// <summary>
    /// Eine Suche im Hintergrund (≈ 2,5 s): neu gefundene Controller im Kopplungsmodus koppeln. Überspringt, wenn gerade
    /// das Fenster „Controller koppeln“ sucht. Liefert die Namen der neu gekoppelten Geräte.
    /// </summary>
    public static List<string> BackgroundScan()
    {
        if (!ScanLock.Wait(0))
            return [];
        try
        {
            if (!OpenRadio(out var radio, out ulong radioAddress))
                return [];
            try
            {
                return ScanOnce(radio, radioAddress, _ => { }, repairRemembered: false);
            }
            finally
            {
                CloseHandle(radio);
            }
        }
        finally
        {
            ScanLock.Release();
        }
    }

    private static bool OpenRadio(out IntPtr radio, out ulong address)
    {
        address = 0;
        var radioParams = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
        var findRadio = BluetoothFindFirstRadio(ref radioParams, out radio);
        if (findRadio == IntPtr.Zero)
            return false;
        BluetoothFindRadioClose(findRadio);
        var radioInfo = new BLUETOOTH_RADIO_INFO { dwSize = Marshal.SizeOf<BLUETOOTH_RADIO_INFO>() };
        BluetoothGetRadioInfo(radio, ref radioInfo);
        address = radioInfo.address;
        return true;
    }

    /// <param name="repairRemembered">
    /// Auch Controller neu koppeln, die Windows schon kennt (alte Kopplung wird entfernt). Nur im Fenster „Controller
    /// koppeln“, wenn der Nutzer gerade SYNC drückt – im Hintergrund nie: dort lässt sich ein gekoppelter, nur kurz
    /// sichtbarer Controller nicht sicher von einem im Kopplungsmodus unterscheiden (so ging eine Wii-Kopplung verloren).
    /// </param>
    private static List<string> ScanOnce(IntPtr radio, ulong radioAddress, Action<string> progress, bool repairRemembered)
    {
        var paired = new List<string>();
        foreach (var device in Inquiry(radio))
        {
            bool wii = IsWiiName(device.szName), switch1 = IsSwitch1Name(device.szName);
            if (!wii && !switch1)
                continue;
            var info = device;
            if (info.fConnected != 0 && info.fAuthenticated != 0)
                continue;
            if (info.fRemembered != 0 && !repairRemembered)
                continue;
            // Gemerkte Controller nur anfassen, wenn sie gerade in Reichweite sichtbar sind (SYNC gedrückt);
            // sonst ginge die Kopplung eines anderen, nur ausgeschalteten Controllers verloren.
            if (info.fRemembered != 0 && !SeenRecently(info.stLastSeen))
                continue;
            // Alte, nicht verbundene Kopplung entfernen (sonst verweigert Windows die neue).
            if (info.fRemembered != 0 && info.fConnected == 0)
            {
                ulong address = info.Address;
                BluetoothRemoveDevice(ref address);
            }
            progress($"Gefunden: {info.szName} – kopple …");
            bool ok = wii ? PairWii(radio, ref info, radioAddress) : PairJustWorks(radio, ref info);
            if (ok && EnableHid(radio, ref info))
            {
                paired.Add(info.szName);
                progress($"Gekoppelt: {info.szName}");
                Log.Info($"Kopplung: {info.szName} ({info.Address:X12}) gekoppelt");
            }
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

    /// <summary>Eine kurze Suche (≈ 1,3 s) nach Geräten in Reichweite – kurz, damit verbundene Controller nicht abreißen.</summary>
    private static List<BLUETOOTH_DEVICE_INFO> Inquiry(IntPtr radio)
    {
        var result = new List<BLUETOOTH_DEVICE_INFO>();
        var search = new BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
            fReturnAuthenticated = 1, fReturnRemembered = 1, fReturnUnknown = 1, fReturnConnected = 1, fIssueInquiry = 1,
            cTimeoutMultiplier = 1, hRadio = radio,
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

    /// <summary>
    /// Wii: bevorzugt mit der Adresse des PCs als PIN koppeln (rote SYNC-Taste) – nur dann verbindet sich die
    /// Fernbedienung später per Tastendruck von selbst. Bei vollem Funk scheitert der erste Versuch gelegentlich
    /// (Fehler 31), deshalb mehrmals; erst danach die eigene Adresse (Kopplung über 1+2, ohne Wiederverbinden).
    /// </summary>
    private static bool PairWii(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info, ulong radioAddress)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (PairWithPin(radio, ref info, radioAddress))
                return true;
            Thread.Sleep(300);
        }
        if (!PairWithPin(radio, ref info, info.Address))
            return false;
        Log.Warn($"Kopplung {info.szName}: nur über 1+2 gekoppelt – verbindet sich nicht per Tastendruck. " +
                 "Für das automatische Verbinden einmal mit der roten SYNC-Taste koppeln.");
        return true;
    }

    /// <summary>Wii: mit binärer PIN (6 Byte Adresse, niedrigstes Byte zuerst) authentifizieren.</summary>
    private static bool PairWithPin(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info, ulong pinAddress)
    {
        var pin = new char[6];
        for (int i = 0; i < 6; i++)
            pin[i] = (char)((pinAddress >> (8 * i)) & 0xFF);
        uint auth = BluetoothAuthenticateDevice(IntPtr.Zero, radio, ref info, pin, 6);
        if (auth != 0)
            Log.Info($"Kopplung {info.szName}: Authentifizierung mit PIN {pinAddress:X12} → Fehler {auth}");
        return auth == 0;
    }

    // Für die Rückfrage beim Koppeln ohne PIN (läuft auf einem Thread von Windows).
    private static IntPtr _callbackRadio;
    private static readonly AuthenticationCallback Callback = OnAuthenticationRequest;

    /// <summary>Switch 1/NSO: Kopplung ohne PIN; die Rückfrage von Windows bestätigt N-Connect selbst (kein Fenster).</summary>
    private static bool PairJustWorks(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info)
    {
        _callbackRadio = radio;
        uint registered = BluetoothRegisterForAuthenticationEx(ref info, out var registration, Callback, IntPtr.Zero);
        try
        {
            uint auth = BluetoothAuthenticateDeviceEx(IntPtr.Zero, radio, ref info, IntPtr.Zero, MITMProtectionNotRequiredBonding);
            if (auth != 0)
                Log.Info($"Kopplung {info.szName}: Authentifizierung → Fehler {auth} (Rückfrage registriert: {registered == 0})");
            return auth == 0;
        }
        finally
        {
            if (registered == 0)
                BluetoothUnregisterAuthentication(registration);
            _callbackRadio = IntPtr.Zero;
        }
    }

    private static bool OnAuthenticationRequest(IntPtr param, IntPtr callbackParams)
    {
        try
        {
            var request = Marshal.PtrToStructure<BLUETOOTH_AUTHENTICATION_CALLBACK_PARAMS>(callbackParams);
            var response = new BLUETOOTH_AUTHENTICATE_RESPONSE
            {
                bthAddressRemote = request.deviceInfo.Address,
                authMethod = request.authenticationMethod,
                numericValue = request.numericValue,
                // Alte PIN-Kopplung kennt N-Connect für diese Controller nicht: ablehnen statt mit leerer PIN zu antworten.
                negativeResponse = (byte)(request.authenticationMethod == BLUETOOTH_AUTHENTICATION_METHOD_LEGACY ? 1 : 0),
            };
            uint result = BluetoothSendAuthenticationResponseEx(_callbackRadio, ref response);
            if (result != 0)
                Log.Info($"Kopplung: Rückfrage (Verfahren {request.authenticationMethod}) beantworten → Fehler {result}");
        }
        catch (Exception e)
        {
            Log.Error("Kopplung: Rückfrage beantworten", e);
        }
        return true;
    }

    private static bool EnableHid(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info)
    {
        var service = HidServiceClass;
        uint state = BluetoothSetServiceState(radio, ref info, ref service, BLUETOOTH_SERVICE_ENABLE);
        if (state != 0)
            Log.Info($"Kopplung {info.szName}: HID-Dienst einschalten → Fehler {state}");
        return state == 0;
    }
}
