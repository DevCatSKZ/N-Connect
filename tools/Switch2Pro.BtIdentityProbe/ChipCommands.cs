namespace Switch2Pro.BtIdentityProbe;

/// <summary>
/// Bekannte Herstellerbefehle zum Setzen der Bluetooth-Adresse. Quellen: Linux-Treiber (drivers/bluetooth/btbcm.c,
/// BlueZ tools/bccmd.c und csr.c). Unter Windows sind sie über IOCTL_BTH_HCI_VENDOR_COMMAND noch nicht erprobt –
/// genau das prüft dieses Werkzeug.
/// </summary>
internal static class ChipCommands
{
    /// <summary>Hersteller-IDs der Bluetooth SIG.</summary>
    public const ushort Csr = 0x000A, Broadcom = 0x000F, Barrot = 0x08E7;

    public static string ManufacturerName(ushort id) => id switch
    {
        0x0002 => "Intel",
        Csr => "Cambridge Silicon Radio (CSR)",
        Broadcom => "Broadcom",
        0x001D => "Qualcomm",
        0x0046 => "MediaTek",
        0x0048 => "Marvell",
        0x005D => "Realtek",
        Barrot => "Barrot Technology",
        _ => "unbekannt",
    };

    /// <summary>Adresse „AA:BB:CC:DD:EE:FF“ als 6 Byte in HCI-Reihenfolge (niedrigstes Byte zuerst).</summary>
    public static byte[] AddressLittleEndian(string address) =>
        Convert.FromHexString(address.Replace(":", "")).Reverse().ToArray();

    // ---------- Broadcom ----------

    /// <summary>Broadcom „Write_BD_ADDR“ (OCF 0x001): 6 Byte Adresse, niedrigstes Byte zuerst. Gilt bis zum Abstecken.</summary>
    public const ushort BroadcomWriteBdAddr = 0x001;

    // ---------- CSR BCCMD ----------

    /// <summary>CSR-Herstellerbefehl (OCF 0x000) trägt BCCMD-Nachrichten; Kanal-Byte 0xC2 = erstes+letztes Fragment, Kanal 2.</summary>
    public const ushort CsrVendorOcf = 0x000;
    private const byte BccmdChannel = 0xC2;
    private const ushort GetReq = 0x0000, SetReq = 0x0002;
    private const ushort VaridPs = 0x7003, VaridWarmReset = 0x4002;
    private const ushort PskeyBdAddr = 0x0001;
    /// <summary>PS-Speicher: 0x0000 = Standard (liest den wirksamen Wert), 0x0008 = RAM (verfällt beim Abstecken).</summary>
    private const ushort StoreDefault = 0x0000, StoreRam = 0x0008;

    /// <summary>BCCMD: Lesen von PSKEY_BDADDR. Ändert nichts am Chip.</summary>
    public static byte[] CsrReadBdAddr(ushort seq) => CsrReadBdAddr(seq, StoreDefault);

    /// <summary>BCCMD: PSKEY_BDADDR aus einem bestimmten PS-Speicher lesen (0x0000 Standard, 0x0001 PSI, 0x0002 PSF,
    /// 0x0004 ROM, 0x0008 RAM). Ändert nichts am Chip.</summary>
    public static byte[] CsrReadBdAddr(ushort seq, ushort store) => Bccmd(GetReq, seq, VaridPs, [PskeyBdAddr, 4, store, 0, 0, 0, 0], 12);

    /// <summary>BCCMD: Firmware-Build-ID lesen (Variable 0x2819). Ändert nichts – prüft nur, ob BCCMD erlaubt ist.</summary>
    public static byte[] CsrReadBuildId(ushort seq) => Bccmd(GetReq, seq, 0x2819, [0], 9);

    /// <summary>BCCMD: beliebige 16-Bit-Variable lesen (z. B. 0x281A Chip-Version, 0x281B Chip-Revision).</summary>
    public static byte[] CsrReadVarid(ushort seq, ushort varid) => Bccmd(GetReq, seq, varid, [0], 9);

    /// <summary>BCCMD: beliebigen PS-Schlüssel lesen (<paramref name="words"/> = Länge in 16-Bit-Wörtern).</summary>
    public static byte[] CsrReadPskey(ushort seq, ushort pskey, ushort store, ushort words) =>
        Bccmd(GetReq, seq, VaridPs, [pskey, words, store, .. new ushort[words]], 8 + words);

    public static readonly ushort[] PsStores = [0x0000, 0x0008, 0x0001, 0x0002, 0x0004];

    public static string StoreName(ushort store) => store switch
    {
        0x0000 => "Standard", 0x0001 => "PSI", 0x0002 => "PSF", 0x0004 => "ROM", 0x0008 => "RAM", _ => $"0x{store:X4}",
    };

    /// <summary>BCCMD-Statuscodes (BlueZ csr.h).</summary>
    public static string BccmdStatusName(ushort status) => status switch
    {
        0 => "OK", 1 => "NO_SUCH_VARID", 2 => "TOO_BIG", 3 => "NO_VALUE", 4 => "BAD_REQ", 5 => "NO_ACCESS",
        6 => "READ_ONLY", 7 => "WRITE_ONLY", 8 => "ERROR", 9 => "PERMISSION_DENIED", _ => $"0x{status:X4}",
    };

    /// <summary>Allgemeine BCCMD-Antwort: Status und Nutzdaten-Wörter nach dem 5-Wort-Kopf.</summary>
    public static bool TryParseBccmd(byte[] evt, out ushort status, out ushort[] payload)
    {
        status = 0xFFFF;
        payload = [];
        if (evt.Length < 3 + 10 || evt[0] != 0xFF || evt[2] != BccmdChannel)
            return false;
        var msg = evt.AsSpan(3);
        status = BitConverter.ToUInt16(msg[8..]);
        int words = (msg.Length - 10) / 2;
        payload = new ushort[words];
        for (int i = 0; i < words; i++)
            payload[i] = BitConverter.ToUInt16(msg[(10 + i * 2)..]);
        return true;
    }

    /// <summary>BCCMD: PSKEY_BDADDR im RAM setzen (wirkt erst nach Warmstart, verfällt beim Abstecken).</summary>
    public static byte[] CsrWriteBdAddrRam(ushort seq, string address)
    {
        byte[] b = AddressLittleEndian(address);
        // CSR-Format: LAP oberes Byte, LAP untere 16 Bit, UAP, NAP (je 16-Bit-Wort).
        ushort[] value = [b[2], (ushort)(b[1] << 8 | b[0]), b[3], (ushort)(b[5] << 8 | b[4])];
        return Bccmd(SetReq, seq, VaridPs, [PskeyBdAddr, 4, StoreRam, .. value], 12);
    }

    /// <summary>BCCMD: Warmstart des Chips (übernimmt RAM-Werte; der Adapter meldet sich am USB neu an).</summary>
    public static byte[] CsrWarmReset(ushort seq) => Bccmd(SetReq, seq, VaridWarmReset, [], 9);

    /// <summary>Muster für BTH_VENDOR_PATTERN: CSR antwortet mit einem Herstellerevent (0xFF), das mit 0xC2 beginnt.</summary>
    public static byte[] CsrPattern(byte offset) => [offset, 1, BccmdChannel];

    /// <summary>Antwort auf <see cref="CsrReadBdAddr"/> auswerten: Event FF, Länge, C2, dann die BCCMD-Nachricht.</summary>
    public static bool TryParseCsrBdAddr(byte[] evt, out string address, out ushort status)
    {
        address = "";
        status = 0xFFFF;
        int c2 = Array.IndexOf(evt, BccmdChannel);
        if (evt.Length < 2 || evt[0] != 0xFF || c2 < 0 || evt.Length < c2 + 1 + 16 + 8)
            return false;
        var msg = evt.AsSpan(c2 + 1);
        status = BitConverter.ToUInt16(msg[8..]);
        var v = msg[16..];
        ushort lapHigh = BitConverter.ToUInt16(v), lapLow = BitConverter.ToUInt16(v[2..]), uap = BitConverter.ToUInt16(v[4..]), nap = BitConverter.ToUInt16(v[6..]);
        byte[] be = [(byte)(nap >> 8), (byte)nap, (byte)uap, (byte)lapHigh, (byte)(lapLow >> 8), (byte)lapLow];
        address = string.Join(':', be.Select(x => x.ToString("X2")));
        return true;
    }

    /// <summary>BCCMD-Nachricht: Typ, Länge (in 16-Bit-Wörtern), Folgenummer, Variable, Status, Nutzdaten (aufgefüllt).</summary>
    private static byte[] Bccmd(ushort type, ushort seq, ushort varid, ushort[] payload, int totalWords)
    {
        var words = new ushort[totalWords];
        words[0] = type;
        words[1] = (ushort)totalWords;
        words[2] = seq;
        words[3] = varid;
        words[4] = 0;
        payload.CopyTo(words, 5);
        var data = new byte[1 + totalWords * 2];
        data[0] = BccmdChannel;
        for (int i = 0; i < totalWords; i++)
            BitConverter.TryWriteBytes(data.AsSpan(1 + i * 2), words[i]);
        return data;
    }
}
