using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Switch2Pro.Protocol;

/// <summary>Ein Controller im ESP32-Export.</summary>
public sealed record Esp32Controller
{
    /// <summary>Bluetooth-Adresse des Controllers ("AA:BB:CC:DD:EE:FF", höchstes Byte zuerst).</summary>
    public string Address { get; init; } = "";
    /// <summary>Art (Name der Aufzählung <see cref="ControllerKind"/>), "Unknown" = noch nie mit N-Connect verbunden.</summary>
    public string Kind { get; init; } = nameof(ControllerKind.Unknown);
    /// <summary>Produktkennung (PID) aus der Werbung – nur bei Switch-2-Controllern.</summary>
    public int? ProductId { get; init; }
    /// <summary>Anzeigename (eigener Name oder Name der Art).</summary>
    public string? Name { get; init; }
    /// <summary>"BLE" oder "Classic" (klassisches Bluetooth); null = unbekannt.</summary>
    public string? Transport { get; init; }
    /// <summary>
    /// Kann eine reine BLE-Platine (ESP32-S3, nRF52840) ihn ohne neues SYNC übernehmen? Nur Switch-2-Controller:
    /// Bluetooth LE ohne Verschlüsselung, es zählt nur die Host-Adresse – kein Schlüssel nötig.
    /// </summary>
    public bool SupportedBle { get; init; }
    /// <summary>Hinweis, warum (noch) nicht übernehmbar bzw. was zu beachten ist.</summary>
    public string? Note { get; init; }
    /// <summary>Gewünschter Spielerplatz 0–7, falls festgelegt.</summary>
    public int? PlayerSlot { get; init; }
    /// <summary>Ausgabeart, falls je Controller festgelegt ("Xbox360" oder "DualShock4").</summary>
    public string? Output { get; init; }
    public bool SingleJoyCon { get; init; }
    public bool UprightJoyCon { get; init; }
    /// <summary>Eigene Stick-Kalibrierung (ersetzt die Werkswerte im Controller), falls gemessen.</summary>
    public StickCalibration? StickLeft { get; init; }
    public StickCalibration? StickRight { get; init; }
    /// <summary>Eigener Gyro-Nullpunkt (Rohwerte), falls kalibriert.</summary>
    public GyroBias? GyroBias { get; init; }
}

/// <summary>Inhalt des ESP32-Exports: Adresse des PC-Bluetooth-Adapters und die bekannten Controller.</summary>
public sealed record Esp32Profile
{
    public string Format { get; init; } = Esp32Export.FormatId;
    public int Version { get; init; } = Esp32Export.Version;
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public string? SourcePc { get; init; }
    /// <summary>
    /// Bluetooth-Adresse des PC-Adapters ("AA:BB:CC:DD:EE:FF"). Mit ihr sind die Switch-2-Controller gekoppelt; der ESP32
    /// übernimmt sie als eigene Bluetooth-Adresse, dann verbinden sich die Controller ohne neues SYNC mit ihm.
    /// </summary>
    public string HostAddress { get; init; } = "";
    public List<Esp32Controller> Controllers { get; init; } = [];
}

/// <summary>
/// Export für einen ESP32 (S3), der sich gegenüber den Controllern als dieser PC ausgibt: JSON-Datei mit allen Angaben
/// und eine C-Headerdatei zum Einbinden in ein ESP-IDF-Projekt. Enthält keine geheimen Schlüssel – Switch-2-Controller
/// verbinden sich ohne Verschlüsselung, entscheidend ist nur die Host-Adresse. Anleitung: docs/ESP32.md.
/// </summary>
public static class Esp32Export
{
    public const string FormatId = "n-connect-esp32";
    public const int Version = 1;
    public const string JsonFile = "nconnect-esp32.json";
    public const string HeaderFile = "nconnect_pairing.h";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Produktkennung, mit der ein Switch-2-Controller wirbt (null bei anderen Arten).</summary>
    public static int? ProductId(ControllerKind kind) => kind switch
    {
        ControllerKind.Pro2 => 0x2069,
        ControllerKind.JoyCon2Left => 0x2067,
        ControllerKind.JoyCon2Right => 0x2066,
        ControllerKind.GameCube2 => 0x2073,
        _ => null,
    };

    /// <summary>
    /// Export aus den Einstellungen. <paramref name="live"/>: Arten der gerade verbundenen Controller (Adresse → Art),
    /// ergänzt die gemerkten Arten. <paramref name="windows"/>: in Windows gekoppelte Geräte (Name, ggf. Schlüssel) –
    /// Controller darunter werden aufgenommen, auch wenn N-Connect sie noch nicht verbunden gesehen hat; andere Geräte
    /// (Kopfhörer, Tastatur …) nur, wenn ihre Adresse als Controller bekannt ist. Unbekannte Art = „Unknown“ mit Hinweis.
    /// </summary>
    public static Esp32Profile Build(Settings settings, string hostAddress, string? pcName,
        IReadOnlyDictionary<string, ControllerKind>? live = null, IEnumerable<WindowsBtDevice>? windows = null)
    {
        if (!BtAddress.TryNormalize(hostAddress, out var host) || BtAddress.IsEmpty(host))
            throw new ArgumentException("Keine gültige Adresse des Bluetooth-Adapters.", nameof(hostAddress));
        var kinds = new Dictionary<string, ControllerKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var (address, kind) in settings.ControllerKinds)
            if (BtAddress.TryNormalize(address, out var a))
                kinds[a] = kind;
        foreach (var (address, kind) in live ?? new Dictionary<string, ControllerKind>())
            if (BtAddress.TryNormalize(address, out var a) && kind != ControllerKind.Unknown)
                kinds[a] = kind;

        var devices = new Dictionary<string, WindowsBtDevice>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in windows ?? [])
            if (BtAddress.TryNormalize(d.Address, out var a))
                devices[a] = d with { Address = a };

        var addresses = new List<string>();
        void Add(string raw)
        {
            if (BtAddress.TryNormalize(raw, out var a) && !BtAddress.IsEmpty(a) && !addresses.Contains(a))
                addresses.Add(a);
        }
        foreach (var raw in settings.KnownControllers.Concat(kinds.Keys))
            Add(raw);
        foreach (var d in devices.Values.Where(d => BtDeviceNames.KindFromName(d.Name) != ControllerKind.Unknown))
            Add(d.Address);

        var controllers = addresses.Select(a =>
        {
            devices.TryGetValue(a, out var device);
            var kind = kinds.GetValueOrDefault(a, ControllerKind.Unknown);
            if (kind == ControllerKind.Unknown)
                kind = BtDeviceNames.KindFromName(device?.Name);
            string? transport = kind == ControllerKind.Unknown ? null : BtDeviceNames.IsBle(kind) ? "BLE" : "Classic";
            // Übernehmbar ohne neues SYNC: nur Switch-2-Controller (BLE, unverschlüsselt, es zählt nur die Host-Adresse).
            bool ble = kind.IsSwitch2();
            string? note = ble ? null
                : kind == ControllerKind.Unknown ? "Art unbekannt – einmal mit N-Connect verbinden und neu exportieren"
                : transport == "Classic" ? "Klassisches Bluetooth – von einem ESP32-S3 oder nRF52840 (nur BLE) nicht übernehmbar"
                : "Verschlüsselte BLE-Kopplung (Standard-Pairing) – nicht ohne neues Koppeln übernehmbar";
            settings.StickCalibrations.TryGetValue($"{a}|L", out var left);
            settings.StickCalibrations.TryGetValue($"{a}|R", out var right);
            bool hasLeft = settings.StickCalibrations.ContainsKey($"{a}|L");
            bool hasRight = settings.StickCalibrations.ContainsKey($"{a}|R");
            return new Esp32Controller
            {
                Address = a,
                Kind = kind.ToString(),
                ProductId = ProductId(kind),
                Name = settings.NameFor(a) ?? device?.Name ?? (kind == ControllerKind.Unknown ? null : kind.DisplayName()),
                Transport = transport,
                SupportedBle = ble,
                Note = note,
                PlayerSlot = settings.PlayerSlots.TryGetValue(a, out int slot) ? slot : null,
                Output = settings.ControllerOutputs.TryGetValue(a, out var output) ? output.ToString() : null,
                SingleJoyCon = settings.SingleJoyCons.Any(s => BtAddress.Same(s, a)),
                UprightJoyCon = settings.UprightJoyCons.Any(s => BtAddress.Same(s, a)),
                StickLeft = hasLeft ? left : null,
                StickRight = hasRight ? right : null,
                GyroBias = settings.GyroCalibration.TryGetValue(a, out var bias) ? bias : null,
            };
        })
        // Übernehmbare zuerst, dann nach Adresse – stabile Reihenfolge für Vergleiche zwischen zwei Exporten.
        .OrderByDescending(c => c.SupportedBle).ThenBy(c => c.Address, StringComparer.Ordinal).ToList();

        return new Esp32Profile { SourcePc = pcName, HostAddress = host, Controllers = controllers };
    }

    public static string ToJson(Esp32Profile profile) => JsonSerializer.Serialize(profile, Json);

    /// <summary>6 Byte der Adresse, höchstes Byte zuerst (wie angezeigt und wie <c>esp_iface_mac_addr_set</c> sie erwartet).</summary>
    public static byte[] AddressBytes(string address)
    {
        if (!BtAddress.TryNormalize(address, out var a))
            throw new ArgumentException($"Ungültige Bluetooth-Adresse: {address}", nameof(address));
        return a.Split(':').Select(p => byte.Parse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
    }

    private static string CArray(IEnumerable<byte> bytes) => "{ " + string.Join(", ", bytes.Select(b => $"0x{b:X2}")) + " }";

    /// <summary>C-Zeichenkette (UTF-8 bleibt erhalten, Anführungszeichen, Backslash und Steuerzeichen werden maskiert).</summary>
    internal static string CString(string? text)
    {
        if (text is null)
            return "NULL";
        var sb = new StringBuilder("\"");
        foreach (char c in text)
        {
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(c) => "?",
                _ => c.ToString(),
            });
        }
        return sb.Append('"').ToString();
    }

    private static string Cal(StickCalibration? cal) => cal is { } c
        ? $"{{ {c.X.Neutral}, {c.X.Max}, {c.X.Min}, {c.Y.Neutral}, {c.Y.Max}, {c.Y.Min} }}"
        : "{ 0, 0, 0, 0, 0, 0 }";

    /// <summary>
    /// C-Headerdatei für ESP-IDF: Host-Adresse (für <c>esp_iface_mac_addr_set(…, ESP_MAC_BT)</c> und zum Vergleich mit
    /// Byte 10–15 der Werbung) und eine Tabelle der übernehmbaren Controller.
    /// </summary>
    public static string ToHeader(Esp32Profile profile, string? appVersion = null)
    {
        byte[] host = AddressBytes(profile.HostAddress);
        var supported = profile.Controllers.Where(c => c.SupportedBle).ToList();
        var sb = new StringBuilder();
        void L(string line = "") => sb.Append(line).Append('\n');
        L("// Erzeugt von N-Connect" + (appVersion is null ? "" : $" {appVersion}") +
          $" am {profile.Created.ToLocalTime():yyyy-MM-dd HH:mm} auf {profile.SourcePc ?? "?"} – nicht von Hand ändern, neu exportieren.");
        L("// Anleitung: https://github.com/DevCatSKZ/N-Connect/blob/main/docs/ESP32.md (ESP32) bzw. docs/NRF52840.md (nRF52840)");
        L("// Enthält keine geheimen Schlüssel: Switch-2-Controller verbinden sich unverschlüsselt mit dem Host, für den sie");
        L("// werben. Übernimmt die Platine die Host-Adresse, verbindet sie sich ohne neues SYNC.");
        L("#pragma once");
        L();
        L("#include <stdint.h>");
        L();
        L($"#define NCONNECT_EXPORT_VERSION {profile.Version}");
        L($"#define NCONNECT_HOST_ADDR_STR \"{profile.HostAddress}\"");
        L();
        L("// Adresse des PC-Bluetooth-Adapters, höchstes Byte zuerst – ESP32: esp_iface_mac_addr_set(mac, ESP_MAC_BT)");
        L("// VOR esp_bt_controller_init() bzw. nimble_port_init().");
        L($"static const uint8_t NCONNECT_HOST_ADDR[6] = {CArray(host)};");
        L("// Dieselbe Adresse, niedrigstes Byte zuerst – so steht sie in Byte 10–15 der Herstellerdaten (Kennung 0x0553,");
        L("// ohne die 2 Kennungs-Bytes), in NimBLE ble_addr_t.val, Zephyr bt_addr_t.val / bt_ctlr_set_public_addr() und");
        L("// nRF-SoftDevice ble_gap_addr_t.addr.");
        L($"static const uint8_t NCONNECT_HOST_ADDR_LE[6] = {CArray(host.Reverse())};");
        L();
        L("#define NCONNECT_PID_PRO2        0x2069");
        L("#define NCONNECT_PID_JOYCON2_L   0x2067");
        L("#define NCONNECT_PID_JOYCON2_R   0x2066");
        L("#define NCONNECT_PID_GAMECUBE2   0x2073");
        L();
        L("#define NCONNECT_OUTPUT_DEFAULT  0");
        L("#define NCONNECT_OUTPUT_XBOX360  1");
        L("#define NCONNECT_OUTPUT_DS4      2");
        L();
        L("typedef struct {");
        L("    uint8_t addr[6];        // Adresse des Controllers, höchstes Byte zuerst");
        L("    uint8_t addr_le[6];     // dieselbe, niedrigstes Byte zuerst (NimBLE ble_addr_t.val)");
        L("    uint16_t pid;           // NCONNECT_PID_*");
        L("    const char *name;       // Anzeigename (UTF-8)");
        L("    int8_t player_slot;     // gewünschter Spielerplatz 0–7, -1 = keiner");
        L("    uint8_t output;         // NCONNECT_OUTPUT_*");
        L("    uint8_t single_joycon;  // 1 = einzelner Joy-Con quer als eigener Controller");
        L("    uint8_t has_stick_cal;  // Bit 0 = linker, Bit 1 = rechter Stick eigen kalibriert");
        L("    int16_t stick_cal[2][6];// je Stick: Mitte X, Max X, Min X, Mitte Y, Max Y, Min Y (12 Bit)");
        L("} nconnect_controller_t;");
        L();
        L($"#define NCONNECT_CONTROLLER_COUNT {supported.Count}");
        L();
        if (supported.Count == 0)
        {
            L("// Noch keine Switch-2-Controller bekannt: einmal per SYNC mit N-Connect koppeln und neu exportieren.");
            L("static const nconnect_controller_t NCONNECT_CONTROLLERS[1] = { 0 };");
        }
        else
        {
            L("static const nconnect_controller_t NCONNECT_CONTROLLERS[NCONNECT_CONTROLLER_COUNT] = {");
            foreach (var c in supported)
            {
                byte[] a = AddressBytes(c.Address);
                int output = c.Output switch
                {
                    nameof(OutputMode.Xbox360) => 1,
                    nameof(OutputMode.DualShock4) => 2,
                    _ => 0,
                };
                int cal = (c.StickLeft is null ? 0 : 1) | (c.StickRight is null ? 0 : 2);
                L($"    {{ // {c.Address}");
                L($"        {CArray(a)},");
                L($"        {CArray(a.Reverse())},");
                L($"        0x{c.ProductId ?? 0:X4}, {CString(c.Name)}, {c.PlayerSlot ?? -1}, {output}, {(c.SingleJoyCon ? 1 : 0)}, {cal},");
                L($"        {{ {Cal(c.StickLeft)}, {Cal(c.StickRight)} }},");
                L("    },");
            }
            L("};");
        }
        return sb.ToString();
    }
}
