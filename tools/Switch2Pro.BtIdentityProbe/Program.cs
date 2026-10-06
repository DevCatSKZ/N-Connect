using System.ComponentModel;
using Switch2Pro.BtIdentityProbe;
using Switch2Pro.Protocol;

// Prüfwerkzeug „Switch-Identität“. Aufruf:
//   BtIdentityProbe info                       Adapter anzeigen (ohne Adminrechte, ändert nichts)
//   BtIdentityProbe sd <Ordner>                Kopplungsdaten einer Switch-SD-Karte auswerten (Schlüssel maskiert)
//   BtIdentityProbe csr-lesen --ja             CSR-Leseanfrage der Adresse (Admin; ändert nichts, falls CSR-kompatibel)
//   BtIdentityProbe csr-setzen <Adresse> --ja  Adresse im RAM setzen + Warmstart (Admin; verfällt beim Abstecken)
//   BtIdentityProbe bcm-setzen <Adresse> --ja  Broadcom Write_BD_ADDR (Admin; verfällt beim Abstecken)
// „--ja“ bestätigt, dass ein Herstellerbefehl an den Chip geschickt werden darf.

string command = args.Length > 0 ? args[0].ToLowerInvariant() : "info";
bool confirmed = args.Contains("--ja");
BtRadio.Diagnostics = args.Contains("--diagnose");

static string Hex(ReadOnlySpan<byte> b) => string.Join(' ', b.ToArray().Select(x => x.ToString("X2")));
static string Mask(byte[] key) => Convert.ToHexString(key)[..4] + "… (maskiert)";

try
{
    return command switch
    {
        "info" => Info(),
        "sd" when args.Length > 1 => Sd(args[1]),
        "csr-lesen" => Vendor(() => CsrRead()),
        "csr-setzen" when args.Length > 1 => Vendor(() => CsrSet(args[1])),
        "bcm-setzen" when args.Length > 1 => Vendor(() => BcmSet(args[1])),
        _ => Usage(),
    };
}
catch (Win32Exception e)
{
    Console.WriteLine($"Windows-Fehler {e.NativeErrorCode}: {e.Message}");
    return 3;
}

int Usage()
{
    Console.WriteLine("Befehle: info | sd <Ordner> | csr-lesen --ja | csr-setzen <AA:BB:CC:DD:EE:FF> --ja | bcm-setzen <AA:BB:CC:DD:EE:FF> --ja");
    return 1;
}

int Info()
{
    using var radio = BtRadio.OpenFirst();
    if (radio is null)
    {
        Console.WriteLine("Kein Bluetooth-Adapter gefunden.");
        return 2;
    }
    var i = radio.GetInfo();
    Console.WriteLine($"Adapter:        {i.Name}");
    Console.WriteLine($"Adresse:        {BtAddress.Format(i.Address)}");
    Console.WriteLine($"Hersteller:     0x{i.Manufacturer:X4} ({ChipCommands.ManufacturerName(i.Manufacturer)})");
    Console.WriteLine($"LMP-Version:    {(i.LmpVersion is { } l ? $"{l} (Subversion 0x{i.LmpSubversion:X4})" : "unbekannt")}");
    Console.WriteLine($"HCI-Version:    {(i.HciVersion?.ToString() ?? "unbekannt")}");
    Console.WriteLine($"Adminrechte:    {(BtRadio.IsAdmin() ? "ja" : "nein (für Herstellerbefehle nötig)")}");
    return 0;
}

int Sd(string root)
{
    var pairing = SwitchCard.ReadPairings(root);
    if (pairing is not null)
    {
        Console.WriteLine($"Konsole (switch.cal): {pairing.ConsoleAddress ?? "–"}");
        foreach (var c in pairing.Controllers)
            Console.WriteLine($"  joycon_mac  {c.DisplayName,-12} {c.ControllerAddress} → {c.HostAddress}  Schlüssel {Mask(c.LinkKey)}");
    }
    string savePath = Path.Combine(root, SwitchBtSave.FileName);
    if (!File.Exists(savePath))
        savePath = Path.Combine(root, "switch", "Bluepick_RCM", SwitchBtSave.FileName);
    if (File.Exists(savePath))
    {
        var devices = SwitchBtSave.Parse(File.ReadAllBytes(savePath));
        Console.WriteLine($"BT-Save: {devices.Count} Einträge");
        foreach (var d in devices)
        {
            var match = pairing?.Controllers.FirstOrDefault(c => c.ControllerAddress == d.Address);
            string check = match is null ? "" : match.LinkKey.AsSpan().SequenceEqual(d.LinkKeyIniOrder) ? "  = joycon_mac ✓" : "  ≠ joycon_mac (anderer Schlüssel)";
            Console.WriteLine($"  @0x{d.Offset:X5}  {d.Name,-16} {d.Address}  Schlüssel {Mask(d.LinkKey)}{check}");
        }
    }
    else
    {
        Console.WriteLine("Kein BT-Save gefunden.");
    }
    return 0;
}

int Vendor(Func<int> action)
{
    if (!confirmed)
    {
        Console.WriteLine("Dieser Befehl schickt einen Herstellerbefehl an den Bluetooth-Chip. Mit --ja bestätigen.");
        return 1;
    }
    if (!BtRadio.IsAdmin())
    {
        Console.WriteLine("Herstellerbefehle brauchen Adminrechte: Terminal „Als Administrator ausführen“.");
        return 1;
    }
    return action();
}

BtRadio OpenForCommands()
{
    Console.WriteLine($"SeLoadDriverPrivilege aktiv: {(BtRadio.LoadDriverPrivilegeEnabled() ? "ja" : "nein")}");
    var radio = BtRadio.OpenForCommands();
    Console.WriteLine(radio is null ? "Geräteschnittstelle nicht gefunden – Rückfall auf BluetoothFindFirstRadio" : "Adapter über Geräteschnittstelle geöffnet (Lesen+Schreiben)");
    return radio ?? BtRadio.OpenFirst() ?? throw new InvalidOperationException("Kein Bluetooth-Adapter.");
}

int CsrRead()
{
    using var radio = OpenForCommands();
    var info = radio.GetInfo();
    Console.WriteLine($"Adapter {BtAddress.Format(info.Address)}, Hersteller 0x{info.Manufacturer:X4}");
    // Muster an Offset 0 der Event-Parameter (erprobt): CSR antwortet mit Event FF, Parameter beginnen mit 0xC2.
    var pattern = ChipCommands.CsrPattern(0);
    ushort seq = 0x0101;
    byte[]? Send(byte[] request)
    {
        var evt = radio.VendorCommand(info.Manufacturer, ChipCommands.CsrVendorOcf, request, pattern, TimeSpan.FromSeconds(3));
        if (BtRadio.Diagnostics)
            Console.WriteLine($"    → {Hex(request)}\n    ← {(evt is null ? "keine Antwort" : Hex(evt))}");
        return evt;
    }

    var build = Send(ChipCommands.CsrReadBuildId(seq++));
    if (build is null || !ChipCommands.TryParseBccmd(build, out var buildStatus, out var buildValue))
    {
        Console.WriteLine("Keine BCCMD-Antwort – Chip ist nicht CSR-kompatibel oder antwortet anders.");
        return 4;
    }
    Console.WriteLine($"BCCMD Build-ID: Status {ChipCommands.BccmdStatusName(buildStatus)}" +
                      (buildStatus == 0 && buildValue.Length > 0 ? $", Build {buildValue[0]}" : ""));

    foreach (var (varid, name) in new (ushort, string)[] { (0x281A, "Chip-Version"), (0x281B, "Chip-Revision") })
    {
        var evt = Send(ChipCommands.CsrReadVarid(seq++, varid));
        if (evt is not null && ChipCommands.TryParseBccmd(evt, out var st, out var val))
            Console.WriteLine($"BCCMD {name}: Status {ChipCommands.BccmdStatusName(st)}{(st == 0 && val.Length > 0 ? $", Wert 0x{val[0]:X4}" : "")}");
    }
    // Andere PS-Schlüssel: ist nur die Adresse gesperrt oder der ganze PS-Zugriff?
    foreach (var (key, words, name) in new (ushort, ushort, string)[] { (0x0108, 16, "Gerätename"), (0x01FE, 1, "Quarzfrequenz") })
    {
        var evt = Send(ChipCommands.CsrReadPskey(seq++, key, 0x0000, words));
        if (evt is not null && ChipCommands.TryParseBccmd(evt, out var st, out _))
            Console.WriteLine($"PSKEY 0x{key:X4} {name}: Status {ChipCommands.BccmdStatusName(st)}");
    }

    foreach (ushort store in ChipCommands.PsStores)
    {
        var evt = Send(ChipCommands.CsrReadBdAddr(seq++, store));
        if (evt is null)
        {
            Console.WriteLine($"PSKEY_BDADDR [{ChipCommands.StoreName(store),-8}]: keine Antwort");
            continue;
        }
        ChipCommands.TryParseCsrBdAddr(evt, out var address, out var status);
        Console.WriteLine($"PSKEY_BDADDR [{ChipCommands.StoreName(store),-8}]: Status {ChipCommands.BccmdStatusName(status)}" +
                          (status == 0 ? $", Adresse {address}" : ""));
    }
    return 0;
}

int CsrSet(string address)
{
    if (!BtAddress.TryNormalize(address, out var a))
        return Usage();
    using var radio = OpenForCommands();
    var info = radio.GetInfo();
    if (info.Manufacturer is not (ChipCommands.Csr or ChipCommands.Barrot))
    {
        Console.WriteLine($"Abgebrochen: Adapter antwortet nicht nachweislich auf CSR-BCCMD (Hersteller 0x{info.Manufacturer:X4}).");
        return 5;
    }
    Console.WriteLine($"Adapter {BtAddress.Format(info.Address)} → {a} (RAM, verfällt beim Abstecken)");
    var evt = radio.VendorCommand(info.Manufacturer, ChipCommands.CsrVendorOcf, ChipCommands.CsrWriteBdAddrRam(0x0102, a), ChipCommands.CsrPattern(0), TimeSpan.FromSeconds(3));
    if (evt is null || !ChipCommands.TryParseBccmd(evt, out var status, out _))
    {
        Console.WriteLine($"  SETREQ PSKEY_BDADDR: keine gültige Antwort {(evt is null ? "" : Hex(evt))}");
        return 4;
    }
    Console.WriteLine($"  SETREQ PSKEY_BDADDR: Status {ChipCommands.BccmdStatusName(status)}");
    // Der Warmstart übernimmt den RAM-Wert. Ohne „--neustart“ nur prüfen, ob das Schreiben erlaubt ist:
    // mit einer fremden Adresse verlieren alle anderen Bluetooth-Geräte des PCs bis zum Abstecken die Verbindung.
    if (status != 0 || !args.Contains("--neustart"))
    {
        Console.WriteLine(status == 0 ? "  Schreiben erlaubt. Warmstart ausgelassen (mit --neustart ausführen)." : "  Schreiben nicht erlaubt.");
        return status == 0 ? 0 : 5;
    }
    Console.WriteLine("  Warmstart …");
    evt = radio.VendorCommand(info.Manufacturer, ChipCommands.CsrVendorOcf, ChipCommands.CsrWarmReset(0x0103), ChipCommands.CsrPattern(0), TimeSpan.FromSeconds(3));
    Console.WriteLine($"  Warmstart: {(evt is null ? "keine Antwort (normal – der Adapter startet neu)" : Hex(evt))}");
    Console.WriteLine("Ein paar Sekunden warten, dann „info“ aufrufen und die Adresse prüfen.");
    return 0;
}

int BcmSet(string address)
{
    if (!BtAddress.TryNormalize(address, out var a))
        return Usage();
    using var radio = OpenForCommands();
    var info = radio.GetInfo();
    // Nur an echte Broadcom-Chips: ein fremder Chip antwortet evtl. nicht, und der Windows-Stack hält den Adapter
    // nach einer Zeitüberschreitung für hängend (Vorfall 04.10.2026, siehe docs/SWITCH-IDENTITAET.md).
    if (info.Manufacturer != ChipCommands.Broadcom)
    {
        Console.WriteLine($"Abgebrochen: Adapter ist kein Broadcom-Chip (Hersteller 0x{info.Manufacturer:X4}).");
        return 5;
    }
    Console.WriteLine($"Adapter {BtAddress.Format(info.Address)} → {a} (bis zum Abstecken)");
    var evt = radio.VendorCommand(info.Manufacturer, ChipCommands.BroadcomWriteBdAddr, ChipCommands.AddressLittleEndian(a), [], TimeSpan.FromSeconds(3));
    Console.WriteLine($"  Write_BD_ADDR: {(evt is null ? "keine Antwort" : Hex(evt))}");
    Console.WriteLine("Windows übernimmt die neue Adresse erst nach einem Neustart des Bluetooth-Stacks; danach „info“ aufrufen.");
    return 0;
}
