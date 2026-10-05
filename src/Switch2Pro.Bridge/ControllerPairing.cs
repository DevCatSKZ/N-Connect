using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;

namespace Switch2Pro.Bridge;

/// <summary>
/// Koppelt klassische Bluetooth-Controller selbst mit Windows – ohne Umweg über die Windows-Bluetooth-Einstellungen:
/// <list type="bullet">
/// <item>Switch 1 (Joy-Con, Pro Controller) und Nintendo-Switch-Online-Controller (NES, SNES, N64, Mega Drive):
/// einfache Kopplung ohne PIN („Just Works“); N-Connect bestätigt die Anfrage selbst.</item>
/// <item>Wii-Fernbedienung und Wii U Pro Controller: binäre PIN = Bluetooth-Adresse des PCs (rote SYNC-Taste) bzw.
/// die eigene (1+2), Byte für Byte – über „Gerät hinzufügen“ klappt das oft nicht (Vorgehen wie in Dolphin).</item>
/// <item>PlayStation (DualShock 4, DualSense): klassisches Bluetooth, Name „Wireless Controller“, ohne PIN –
/// wie Switch 1. Im Koppelfenster und im Hintergrund (die klassische Suche sieht nur Geräte im Kopplungsmodus).</item>
/// <item>Xbox über Bluetooth (One S und neuer): Bluetooth LE – nicht in der klassischen Suche, darum ein eigener
/// LE-Watcher während des Koppelfensters; Kopplung ohne PIN über WinRT. Nur im Fenster, nicht im Hintergrund.</item>
/// </list>
/// Danach den HID-Dienst einschalten (klassisch); LE-Geräte richtet Windows selbst ein. N-Connect findet den
/// Controller dann wie jeden gekoppelten.
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

    /// <summary>DualShock 4 und DualSense werben im Kopplungsmodus (PS + Teilen/Create) als „Wireless Controller“.</summary>
    public static bool IsSonyName(string? name) =>
        name is not null && name.StartsWith("Wireless Controller", StringComparison.OrdinalIgnoreCase);

    /// <summary>Xbox-Controller über Bluetooth-LE (One S und neuer; der Xbox 360 hat kein Bluetooth).</summary>
    public static bool IsXboxName(string? name) =>
        name is not null && name.StartsWith("Xbox", StringComparison.OrdinalIgnoreCase);

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
                // Xbox-Controller werben per Bluetooth LE – sie fehlen in der klassischen Suche, darum läuft
                // während des Fensters zusätzlich ein LE-Watcher mit (Pairing-Modus = „Kopplungstaste halten“).
                var xboxSeen = WatchXbox();
                try
                {
                    while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
                    {
                        progress("Suche … SYNC-Taste drücken (Xbox: Kopplungstaste oben halten, PlayStation: PS + Teilen/Create halten).");
                        paired.AddRange(ScanOnce(radio, radioAddress, progress, repairRemembered: true));
                        paired.AddRange(PairXbox(xboxSeen, progress));
                        if (paired.Count > 0)
                            break;
                        // Kurze Pause zwischen den Suchläufen: verbundene Controller bekommen wieder Funkzeit.
                        ct.WaitHandle.WaitOne(1000);
                    }
                }
                finally
                {
                    xboxSeen.Watcher.Stop();
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
    /// Fenster „Controller koppeln“ (der Nutzer drückt gerade SYNC): bekannte Controller auch dann neu koppeln, wenn sie
    /// nur kürzlich gesehen wurden. Im Hintergrund nur, wenn der Controller nachweislich während DIESES Suchlaufs
    /// geantwortet hat – das tut er nur im Kopplungsmodus (SYNC). So wird z. B. ein Joy-Con, der zwischendurch wieder
    /// mit der Switch gekoppelt war, nach SYNC von selbst neu gekoppelt, ohne dass ein nur ausgeschalteter oder
    /// verbundener Controller seine Kopplung verliert.
    /// </param>
    private static List<string> ScanOnce(IntPtr radio, ulong radioAddress, Action<string> progress, bool repairRemembered)
    {
        var paired = new List<string>();
        // „Zuletzt gesehen“ der bekannten Geräte vor der Suche: Windows erneuert den Wert nur, wenn ein Gerät auf die
        // Suche antwortet (= sichtbar, also im Kopplungsmodus).
        var known = Remembered(radio);
        var before = known.ToDictionary(d => d.Address, d => Stamp(d.stLastSeen));
        long now = Environment.TickCount64;
        lock (LastConnected)
            foreach (var d in known.Where(d => d.fConnected != 0))
                LastConnected[d.Address] = now;
        foreach (var device in Inquiry(radio))
        {
            bool wii = IsWiiName(device.szName), switch1 = IsSwitch1Name(device.szName), sony = IsSonyName(device.szName);
            if (!wii && !switch1 && !sony)
                continue;
            var info = device;
            if (info.fConnected != 0)
                continue; // verbunden: läuft bereits
            // Im Hintergrund nach einem Fehlschlag eine Weile in Ruhe lassen – sonst ist der Controller bei jedem Versuch
            // belegt und taucht in der Windows-Suche („Gerät hinzufügen“) nicht auf.
            if (!repairRemembered && FailedRecently(info.Address))
                continue;
            if (info.fRemembered != 0)
            {
                bool answeredNow = before.TryGetValue(info.Address, out long earlier) && Stamp(info.stLastSeen) > earlier;
                // „Gerade eben gesehen“ zählt auch: sucht Windows selbst (z. B. offene Bluetooth-Einstellungen), ist der
                // Zeitstempel schon vor unserem Suchlauf neu. Ein ausgeschalteter oder mit der Switch verbundener
                // Controller antwortet nicht – den sieht Windows nicht.
                // Ein eben noch verbundener Controller (ausgeschaltet, Funk abgerissen) ist ebenfalls „gerade gesehen“ –
                // der ist nicht im Kopplungsmodus und behält seine Kopplung.
                if (!repairRemembered && ConnectedRecently(info.Address))
                    continue;
                if (!answeredNow && !SeenRecently(info.stLastSeen, repairRemembered ? 20 : 8))
                {
                    LogSkipped(info);
                    continue; // nicht im Kopplungsmodus – Kopplung unangetastet lassen
                }
                // Vor dem Entfernen der alten Kopplung bestätigen: antwortet er in einer zweiten Suche erneut, ist er
                // wirklich im SYNC-Modus. Sonst bleibt die Kopplung – lieber einmal zu wenig neu koppeln als eine
                // funktionierende Kopplung zu verlieren. Im Fenster „Controller koppeln“ wird die Bestätigung
                // übersprungen: Der Nutzer drückt gerade SYNC, und die zwei Suchläufe würden das kurze Kopplungsfenster
                // der Fernbedienung (ca. 20 s) aufzehren, bevor die Kopplung entfernt wird.
                if (!repairRemembered && FindAnswering(radio, info.Address, Stamp(info.stLastSeen)) is null)
                {
                    Log.Info($"Kopplung: {info.szName} ({info.Address:X12}) antwortet nicht erneut – Kopplung bleibt unverändert");
                    continue;
                }
                Log.Info($"Kopplung: {info.szName} ({info.Address:X12}) ist bekannt, aber im Kopplungsmodus – neu koppeln");
                // Alte, nicht verbundene Kopplung entfernen (sonst verweigert Windows die neue). Danach kennt Windows das
                // Gerät nicht mehr – mit den alten Gerätedaten schlägt die Kopplung fehl (Wii: Fehler 259). Deshalb
                // mehrfach neu suchen und mit den frischen Daten koppeln; ohne Neufund bleibt das Gerät unbekannt und
                // wird beim nächsten SYNC wie ein neues gekoppelt.
                ulong address = info.Address;
                BluetoothRemoveDevice(ref address);
                if (FindAnswering(radio, info.Address, long.MaxValue, attempts: 6) is { } fresh) // jetzt unbekannt: nur Antwortende
                    info = fresh;
                else
                {
                    Log.Info($"Kopplung: {info.szName} ({info.Address:X12}) nach dem Entfernen nicht wiedergefunden – " +
                             "Kopplungsmodus vermutlich beendet; erneut SYNC drücken");
                    lock (Failures)
                        Failures[info.Address] = Environment.TickCount64;
                    continue;
                }
            }
            progress($"Gefunden: {info.szName} – kopple …");
            // Sony (wie Switch 1): Kopplung ohne PIN; nur die Wii braucht ihre eigene PIN-Behandlung.
            bool ok = wii ? PairWii(radio, ref info, radioAddress) : PairJustWorks(radio, ref info);
            if (!ok && FindAnswering(radio, info.Address, LastSeenNow(radio, info.Address)) is { } retry)
            {
                // Zweiter Versuch, solange er noch antwortet (z. B. Funkstörung oder Rückfrage zu spät).
                Log.Info($"Kopplung: {info.szName} ({info.Address:X12}) – zweiter Versuch");
                info = retry;
                ok = wii ? PairWii(radio, ref info, radioAddress) : PairJustWorks(radio, ref info);
            }
            if (ok && EnableHid(radio, ref info))
            {
                paired.Add(info.szName);
                progress($"Gekoppelt: {info.szName}");
                Log.Info($"Kopplung: {info.szName} ({info.Address:X12}) gekoppelt");
                lock (Failures)
                    Failures.Remove(info.Address);
            }
            else
            {
                lock (Failures)
                    Failures[info.Address] = Environment.TickCount64;
                Log.Info($"Kopplung: {info.szName} ({info.Address:X12}) fehlgeschlagen – im Hintergrund {FailurePause.TotalSeconds:0} s Pause " +
                         "(Fenster „Controller koppeln“ und Windows-Einstellungen gehen weiter)");
            }
        }
        return paired;
    }

    // ---------- Xbox (Bluetooth LE – erscheint nicht in der klassischen Suche) ----------

    /// <summary>Während des Koppelfensters laufender LE-Watcher, der werbende Xbox-Controller sammelt.</summary>
    private sealed class XboxScan
    {
        public readonly BluetoothLEAdvertisementWatcher Watcher = new() { ScanningMode = BluetoothLEScanningMode.Active };
        public readonly ConcurrentDictionary<ulong, byte> Seen = new();
    }

    private static XboxScan WatchXbox()
    {
        var scan = new XboxScan();
        scan.Watcher.Received += (_, a) =>
        {
            if (IsXboxName(a.Advertisement.LocalName))
                scan.Seen.TryAdd(a.BluetoothAddress, 0);
        };
        scan.Watcher.Start();
        return scan;
    }

    /// <summary>Alle seit der letzten Suche gesehenen Xbox-Controller koppeln (nur ungekoppelte – eigene Werbung beim
    /// Wiederverbinden ignoriert der <see cref="DeviceInformation.Pairing.IsPaired"/>-Test).</summary>
    private static List<string> PairXbox(XboxScan scan, Action<string> progress)
    {
        var paired = new List<string>();
        foreach (var address in scan.Seen.Keys)
            if (scan.Seen.TryRemove(address, out _) && TryPairXbox(address, progress) is { } name)
                paired.Add(name);
        return paired;
    }

    /// <summary>Xbox-Controller über WinRT koppeln (Kopplung ohne PIN, Rückfrage wird sofort bestätigt).</summary>
    private static string? TryPairXbox(ulong address, Action<string> progress)
    {
        try
        {
            using var device = BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask().GetAwaiter().GetResult();
            if (device is null)
                return null;
            var info = device.DeviceInformation;
            if (info.Pairing.IsPaired)
                return null; // gehört schon diesem PC – verbindet sich von selbst
            string name = string.IsNullOrEmpty(info.Name) ? "Xbox Wireless Controller" : info.Name;
            progress($"Gefunden: {name} – kopple …");
            var custom = info.Pairing.Custom;
            void Accept(DeviceInformationCustomPairing s, DevicePairingRequestedEventArgs a) => a.Accept();
            custom.PairingRequested += Accept;
            try
            {
                var pairing = custom.PairAsync(DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch,
                    DevicePairingProtectionLevel.None).AsTask();
                if (!pairing.Wait(TimeSpan.FromSeconds(20)))
                    return null;
                var status = pairing.Result.Status;
                if (status is not (DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired))
                {
                    Log.Info($"Kopplung {name}: {status}");
                    return null;
                }
                progress($"Gekoppelt: {name}");
                Log.Info($"Kopplung: {name} (LE {address:X12}) gekoppelt");
                return name;
            }
            finally
            {
                custom.PairingRequested -= Accept;
            }
        }
        catch (Exception e)
        {
            Log.Info($"Kopplung Xbox ({address:X12}): {Log.Reason(e)}");
            return null;
        }
    }

    private static readonly TimeSpan FailurePause = TimeSpan.FromSeconds(90);
    private static readonly Dictionary<ulong, long> Failures = [];

    private static bool FailedRecently(ulong address)
    {
        lock (Failures)
            return Failures.TryGetValue(address, out long at) && Environment.TickCount64 - at < FailurePause.TotalMilliseconds;
    }

    private static readonly Dictionary<ulong, long> SkipLogged = [];

    /// <summary>Wann ein bekannter Controller zuletzt verbunden war (bei jedem Suchlauf aktualisiert).</summary>
    private static readonly Dictionary<ulong, long> LastConnected = [];

    /// <summary>Ein Controller wurde getrennt (Adresse „AA:BB:…“; andere Formen wie „USB:…“ werden ignoriert).</summary>
    public static void NoteDisconnected(string? address)
    {
        if (address is null || address.Length != 17
            || !ulong.TryParse(address.Replace(":", ""), System.Globalization.NumberStyles.HexNumber, null, out ulong value))
            return;
        lock (LastConnected)
            LastConnected[value] = Environment.TickCount64;
    }

    private static bool ConnectedRecently(ulong address)
    {
        lock (LastConnected)
            return LastConnected.TryGetValue(address, out long at) && Environment.TickCount64 - at < 30_000;
    }

    /// <summary>Übersprungenen bekannten Controller protokollieren (je Gerät höchstens einmal pro Minute).</summary>
    private static void LogSkipped(BLUETOOTH_DEVICE_INFO info)
    {
        long now = Environment.TickCount64;
        lock (SkipLogged)
        {
            if (SkipLogged.TryGetValue(info.Address, out long at) && now - at < 60_000)
                return;
            SkipLogged[info.Address] = now;
        }
        var t = info.stLastSeen;
        Log.Info($"Kopplung: {info.szName} ({info.Address:X12}) bekannt, nicht verbunden, zuletzt gesehen " +
                 $"{t.Year:D4}-{t.Month:D2}-{t.Day:D2} {t.Hour:D2}:{t.Minute:D2}:{t.Second:D2} – nicht im Kopplungsmodus erkannt");
    }

    /// <summary>Zuletzt gesehen (UTC) innerhalb der letzten <paramref name="seconds"/> Sekunden?</summary>
    private static bool SeenRecently(SYSTEMTIME t, int seconds = 20)
    {
        if (t.Year < 2000)
            return false;
        try
        {
            // Windows liefert UTC; zur Sicherheit auch Ortszeit zulassen.
            var seen = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second);
            var window = TimeSpan.FromSeconds(seconds);
            return (DateTime.UtcNow - seen).Duration() < window || (DateTime.Now - seen).Duration() < window;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>Zeitpunkt als vergleichbare Zahl (0 = nie).</summary>
    private static long Stamp(SYSTEMTIME t)
    {
        if (t.Year < 2000)
            return 0;
        try
        {
            return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, t.Milliseconds).Ticks;
        }
        catch (ArgumentOutOfRangeException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Bis zu zwei kurze Suchen nach einem bestimmten Gerät: gefunden, wenn es antwortet – bei bekannten Geräten an
    /// einem strikt neueren „zuletzt gesehen“ als <paramref name="seenBefore"/> erkennbar (bekannte liefert Windows
    /// auch ohne Antwort), bei unbekannten (nach dem Entfernen) schon daran, dass die Suche es überhaupt liefert.
    /// </summary>
    private static BLUETOOTH_DEVICE_INFO? FindAnswering(IntPtr radio, ulong address, long seenBefore, int attempts = 2)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            foreach (var d in Inquiry(radio))
            {
                if (d.Address != address)
                    continue;
                if (d.fRemembered == 0 || Stamp(d.stLastSeen) > seenBefore)
                    return d;
            }
        }
        return null;
    }

    /// <summary>„Zuletzt gesehen“ eines Geräts laut Windows (ohne Suche); 0 = unbekannt.</summary>
    private static long LastSeenNow(IntPtr radio, ulong address) =>
        Remembered(radio).Where(d => d.Address == address).Select(d => Stamp(d.stLastSeen)).DefaultIfEmpty(0).Max();

    /// <summary>Bekannte Geräte ohne Suche (nur Windows' gespeicherter Stand).</summary>
    private static List<BLUETOOTH_DEVICE_INFO> Remembered(IntPtr radio) => Find(radio, inquiry: false);

    /// <summary>Eine kurze Suche (≈ 1,3 s) nach Geräten in Reichweite – kurz, damit verbundene Controller nicht abreißen.</summary>
    private static List<BLUETOOTH_DEVICE_INFO> Inquiry(IntPtr radio) => Find(radio, inquiry: true);

    private static List<BLUETOOTH_DEVICE_INFO> Find(IntPtr radio, bool inquiry)
    {
        var result = new List<BLUETOOTH_DEVICE_INFO>();
        var search = new BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
            fReturnAuthenticated = 1, fReturnRemembered = 1, fReturnUnknown = inquiry ? 1 : 0, fReturnConnected = 1,
            fIssueInquiry = inquiry ? 1 : 0,
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

    /// <summary>
    /// Switch 1/NSO: Kopplung ohne PIN, ohne Fenster. Zuerst über WinRT (<c>DeviceInformationCustomPairing</c>, Anfrage
    /// wird sofort bestätigt) – mit der Win32-Rückfrage kam die Bestätigung bei Joy-Con oft erst nach dem Abbruch an
    /// (Fehler 1244/258, Antwort danach 1167). Win32 nur, wenn WinRT das Gerät gar nicht öffnen kann.
    /// </summary>
    private static bool PairJustWorks(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info)
    {
        var winRt = PairWinRt(info.Address, info.szName);
        if (winRt is not null)
            return winRt.Value;
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

    /// <summary>Kopplung über WinRT; null = Gerät nicht zu öffnen (dann Win32 versuchen).</summary>
    private static bool? PairWinRt(ulong address, string name)
    {
        BluetoothDevice? device;
        try
        {
            device = BluetoothDevice.FromBluetoothAddressAsync(address).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            Log.Info($"Kopplung {name}: Gerät nicht über WinRT erreichbar ({Log.Reason(e)})");
            return null;
        }
        if (device is null)
            return null;
        using (device)
        {
            var custom = device.DeviceInformation.Pairing.Custom;
            void Accept(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args) => args.Accept();
            custom.PairingRequested += Accept;
            try
            {
                var pairing = custom.PairAsync(DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch,
                    DevicePairingProtectionLevel.None).AsTask();
                if (!pairing.Wait(TimeSpan.FromSeconds(20)))
                {
                    Log.Info($"Kopplung {name}: keine Antwort in 20 s");
                    return false;
                }
                var status = pairing.Result.Status;
                if (status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired)
                    return true;
                Log.Info($"Kopplung {name}: {status}");
                return false;
            }
            catch (Exception e)
            {
                Log.Info($"Kopplung {name}: {Log.Reason(e)}");
                return false;
            }
            finally
            {
                custom.PairingRequested -= Accept;
            }
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
