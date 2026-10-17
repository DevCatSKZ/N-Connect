using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>
/// Kurze, abgeschnittene oder fremde Pakete: Jeder Parser lehnt sie ab, ohne Ausnahme. Je Parser ein gültiger
/// Bericht; daraus werden alle kürzeren Längen 0 … n−1 abgeleitet und eine falsche Report-ID eingesetzt.
/// </summary>
public class ParserRobustnessTests
{
    private sealed record Parser(int MinLength, Func<byte[]> Valid, Func<byte[], bool> Parse);

    private static byte[] Switch1Full(byte id, int length)
    {
        var r = new byte[length];
        r[0] = id;
        r[2] = 0x90;            // Akku voll, lädt
        r[3] = 0x08;            // A
        for (int i = 13; i < Math.Min(length, 49); i++)
            r[i] = (byte)(i * 7); // Byte 13–48 (u. a. die IMU-Proben) mit Inhalt
        return r;
    }

    private static byte[] Switch1Reply(byte subcommand, params byte[] payload)
    {
        var r = new byte[49];
        r[0] = Switch1.InputSubcommandReply;
        r[13] = 0x82;
        r[14] = subcommand;
        payload.CopyTo(r, 15);
        return r;
    }

    /// <summary>IMU-Kalibrierung wie aus dem SPI-Flash: Nullpunkte und Empfindlichkeiten je Achse (int16 LE).</summary>
    private static byte[] ImuBlock(params short[] values) => [.. values.SelectMany(v => new[] { (byte)v, (byte)(v >> 8) })];

    private static readonly byte[] Imu = ImuBlock(-50, 30, 10, 16384, 16384, 16384, 20, -15, 5, 13391, 13356, 13376);

    private static byte[] DsuRequest(uint message, byte[] payload, string magic = "DSUC", ushort version = Dsu.ProtocolVersion, int? length = null)
    {
        var p = new byte[20 + payload.Length];
        System.Text.Encoding.ASCII.GetBytes(magic).CopyTo(p, 0);
        BitConverter.GetBytes(version).CopyTo(p, 4);
        BitConverter.GetBytes((ushort)(length ?? 4 + payload.Length)).CopyTo(p, 6);
        BitConverter.GetBytes(message).CopyTo(p, 16);
        payload.CopyTo(p, 20);
        BitConverter.GetBytes(Dsu.Crc32(p)).CopyTo(p, 8);
        return p;
    }

    private static readonly Dictionary<string, Parser> Parsers = new()
    {
        ["Switch1.Vollbericht"] = new(25, () => Switch1Full(Switch1.InputFull, 49),
            r => Switch1.TryParseFull(r, ControllerKind.Pro1, ImuCalibration.Default, out _)),
        ["Switch1.Vollbericht_RingCon"] = new(25, () => Switch1Full(Switch1.InputFull, 49),
            r => Switch1.TryParseFull(r, ControllerKind.JoyCon1Right, ImuCalibration.Default, out _, ringActive: true)),
        ["Switch1.MCU_Bericht"] = new(25, () => Switch1Full(Nfc.InputMcu, 362),
            r => Switch1.TryParseFull(r, ControllerKind.JoyCon1Right, ImuCalibration.Default, out _)),
        ["Switch1.Antwort"] = new(16, () => Switch1Reply(Switch1.SubDeviceInfo, 0x04, 0x33, 0x02),
            r => Switch1.TryParseReply(r, Switch1.SubDeviceInfo, out _)),
        ["Switch1.SPI_Antwort"] = new(15 + 5 + 24, () => Switch1Reply(Switch1.SubSpiRead, [.. Switch1.SpiReadArgs(Switch1.SpiFactoryImu, 24), .. Imu]),
            r => Switch1.TryParseReply(r, Switch1.SubSpiRead, out _, Switch1.SpiFactoryImu)),
        ["Switch1.IMU_Kalibrierung"] = new(24, () => [.. Imu], r => Switch1.TryParseImu(r, out _)),
        ["Switch1.Stick_Kalibrierung"] = new(9, () => [0xDC, 0x55, 0x57, 0x00, 0x08, 0x80, 0x14, 0x45, 0x4B],
            r => Switch1.TryParseStick(r, left: true, out _)),
        ["Wii.Status"] = new(7, () => [Wii.InputStatus, 0, 0, 0x02, 0, 0, 200], r => Wii.TryParseStatus(r, out _, out _)),
        ["Wii.Speicher"] = new(22, () => [Wii.InputRead, 0, 0, 0x50, 0x00, 0xFA, 0, 0, 0xA4, 0x20, 0, 0, .. new byte[10]],
            r => Wii.TryParseRead(r, out _, out _, out _)),
        ["Wii.Daten_Nunchuk"] = new(3, () => [Wii.ModeButtonsAccelExt, 0x08, 0x01, 0x80, 0x80, 0x9A, 0xE0, 0x80, 0, 0, 0, 0x02, .. new byte[10]],
            r => new WiiParser().TryParse(r, WiiExtension.Nunchuk, 80, out _)),
        ["Wii.Daten_WiiUPro"] = new(3 + 11, () => [Wii.ModeButtonsExt19, 0, 0, 0xB8, 0x0B, 0x00, 0x08, 0xE8, 0x03, 0x00, 0x08, 0xEF, 0xEF, 0x49, .. new byte[8]],
            r => new WiiParser().TryParse(r, WiiExtension.WiiUPro, 80, out _)),
        ["Wii.WiiUPro_Erweiterung"] = new(11, () => [0xB8, 0x0B, 0x00, 0x08, 0xE8, 0x03, 0x00, 0x08, 0xEF, 0xEF, 0x49],
            r => Wii.TryParseWiiUPro(r, out _)),
        ["Nfc.MCU"] = new(Nfc.McuReportOffset + 9, () => { var r = new byte[362]; r[0] = Nfc.InputMcu; r[Nfc.McuReportOffset] = Nfc.ReportNfcState; return r; },
            r => Nfc.TryGetMcu(r, out _, out _)),
        ["Dsu.Anfrage"] = new(26, () => DsuRequest(Dsu.MsgPortInfo, [2, 0, 0, 0, 0, 1]), r => Dsu.ParseRequest(r, out _) is not null),
        ["PlayStation.DualSense_Seriennummer"] = new(7, () => [0x09, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, .. new byte[13]],
            r => PlayStationPad.DualSenseSerial(r) is not null),
        ["PlayStation.DualShock4_Seriennummer"] = new(7, () => [0x12, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, .. new byte[9]],
            r => PlayStationPad.DualShock4Serial(r) is not null),
        ["PlayStation.DualSense_Firmware"] = new(46, () => { var r = new byte[64]; r[0] = 0x20; r[44] = 0x24; r[45] = 0x02; return r; },
            r => PlayStationPad.DualSenseFirmware(r) is not null),
        ["Switch2.Bericht05"] = new(InputReports.Report05MinLength, () => new byte[63], r => InputReports.TryParseReport05(r, out _)),
        ["Switch2.Bericht09"] = new(InputReports.Report09MinLength, () => new byte[63], r => InputReports.TryParseReport09(r, out _)),
        ["Switch2.Speicherantwort"] = new(25, () => Commands.Hex("02 01 01 04 10 78 00 00 09 00 00 00 A8 30 01 00 B3 67 83 2E 66 5E 3A 06 5F"),
            r => Commands.TryParseMemoryRead(r, 0x130A8, out _)),
        ["Switch2.Kopplungsspeicher"] = new(PairingRecord.EntryOffset + PairingRecord.EntrySize,
            () => Commands.Hex("02 9E 11 00 00 00 00 00 48 F1 EB F7 1D 0D 00 00 00 00 00 00 00 00 00 00 00 00 9B F3 1D E7 49 AC 10 E4 01 26 5D 32 5B 44 80 68 00 00 00 00 00 00 48 F1 EB F7 1D 0C 00 00 00 00 00 00 00 00 00 00"),
            r => PairingRecord.TryParse(r, out _)),
        ["Switch2.Werbung"] = new(7, () => Commands.Hex("01 00 03 7E 05 69 20 00 01 00 00 00 00 00 00 00 0F 00 00 00 00 00 00 00"),
            r => Advertisement.Kind(Gatt.NintendoCompanyId, r) != ControllerKind.Unknown),
    };

    [Theory]
    [InlineData("Switch1.Vollbericht")]
    [InlineData("Switch1.Vollbericht_RingCon")]
    [InlineData("Switch1.MCU_Bericht")]
    [InlineData("Switch1.Antwort")]
    [InlineData("Switch1.SPI_Antwort")]
    [InlineData("Switch1.IMU_Kalibrierung")]
    [InlineData("Switch1.Stick_Kalibrierung")]
    [InlineData("Wii.Status")]
    [InlineData("Wii.Speicher")]
    [InlineData("Wii.Daten_Nunchuk")]
    [InlineData("Wii.Daten_WiiUPro")]
    [InlineData("Wii.WiiUPro_Erweiterung")]
    [InlineData("Nfc.MCU")]
    [InlineData("Dsu.Anfrage")]
    [InlineData("PlayStation.DualSense_Seriennummer")]
    [InlineData("PlayStation.DualShock4_Seriennummer")]
    [InlineData("PlayStation.DualSense_Firmware")]
    [InlineData("Switch2.Bericht05")]
    [InlineData("Switch2.Bericht09")]
    [InlineData("Switch2.Speicherantwort")]
    [InlineData("Switch2.Kopplungsspeicher")]
    [InlineData("Switch2.Werbung")]
    public void Zu_kurze_Berichte_werden_ohne_Ausnahme_abgelehnt(string name)
    {
        var parser = Parsers[name];
        byte[] valid = parser.Valid();
        Assert.True(parser.Parse(valid), "gültiger Bericht");
        for (int n = 0; n < parser.MinLength; n++)
            Assert.False(parser.Parse(valid[..n]), $"Länge {n}");
        for (int n = parser.MinLength; n < valid.Length; n++)
            parser.Parse(valid[..n]); // alle Längen dazwischen: keine Ausnahme
    }

    [Theory]
    [InlineData("Switch1.Vollbericht", 0x3F)]
    [InlineData("Switch1.Vollbericht", 0x00)]
    [InlineData("Switch1.MCU_Bericht", 0x32)]
    [InlineData("Switch1.Antwort", Switch1.InputFull)]
    [InlineData("Switch1.SPI_Antwort", Nfc.InputMcu)]
    [InlineData("Wii.Status", Wii.InputRead)]
    [InlineData("Wii.Speicher", Wii.InputStatus)]
    [InlineData("Wii.Speicher", Wii.InputAck)]
    [InlineData("Wii.Daten_Nunchuk", Wii.InputAck)]
    [InlineData("Wii.Daten_Nunchuk", 0x40)]
    [InlineData("Wii.Daten_WiiUPro", 0x2F)]
    [InlineData("Nfc.MCU", Switch1.InputFull)]
    [InlineData("Dsu.Anfrage", (byte)'X')]
    [InlineData("PlayStation.DualSense_Seriennummer", 0x12)]
    [InlineData("PlayStation.DualShock4_Seriennummer", 0x09)]
    [InlineData("PlayStation.DualSense_Firmware", 0x09)]
    [InlineData("Switch2.Speicherantwort", 0x03)]
    [InlineData("Switch2.Werbung", 0x02)]
    public void Falsche_Report_ID_wird_abgelehnt(string name, byte id)
    {
        var parser = Parsers[name];
        byte[] report = parser.Valid();
        report[0] = id;
        Assert.False(parser.Parse(report));
    }

    // ---------- Switch 1 ----------

    [Fact]
    public void Switch1_Benutzerkalibrierung_nur_mit_Kennung()
    {
        Assert.False(Switch1.HasUserMagic([]));
        Assert.False(Switch1.HasUserMagic([0xB2]));
        Assert.True(Switch1.HasUserMagic([0xB2, 0xA1]));
        Assert.True(Switch1.HasUserMagic([0xB2, 0xA1, 0x00, 0x08]));
        Assert.False(Switch1.HasUserMagic([0xA1, 0xB2]));
        Assert.False(Switch1.HasUserMagic([0xFF, 0xFF, 0x00]));   // unbeschrieben
    }

    [Fact]
    public void Switch1_RingCon_Rohwert()
    {
        var r = new byte[49];
        r[39] = 0x34; r[40] = 0x12;
        Assert.Equal(0x1234, Switch1.RingRaw(r));
        r[39] = 0x9C; r[40] = 0xFF;
        Assert.Equal(-100, Switch1.RingRaw(r));
        Assert.Equal(0, Switch1.RingRaw(r.AsSpan(0, 40)));       // zu kurz: kein Wert
        Assert.Equal(-100, Switch1.RingRaw(r.AsSpan(0, 41)));
        Assert.Equal(0, Switch1.RingRaw([]));
    }

    [Fact]
    public void Switch1_IMU_Kalibrierung_wird_gelesen_und_geprueft()
    {
        Assert.True(Switch1.TryParseImu(Imu, out var c));
        Assert.Equal(new ImuCalibration(-50, 30, 10, 16384, 16384, 16384, 20, -15, 5, 13391, 13356, 13376), c);
        Assert.False(Switch1.TryParseImu(Enumerable.Repeat((byte)0xFF, 24).ToArray(), out var none)); // unbeschrieben
        Assert.Equal(ImuCalibration.Default, none);
        // Empfindlichkeit gleich Nullpunkt (auf irgendeiner Achse): ergäbe eine Division durch 0.
        for (int axis = 0; axis < 6; axis++)
        {
            var values = new short[] { -50, 30, 10, 16384, 16384, 16384, 20, -15, 5, 13391, 13356, 13376 };
            int origin = axis < 3 ? axis : axis + 3; // Beschl. X/Y/Z: 0–2 (Empf. 3–5), Gyro X/Y/Z: 6–8 (Empf. 9–11)
            values[origin + 3] = values[origin];
            Assert.False(Switch1.TryParseImu(ImuBlock(values), out _), $"Achse {axis}");
        }
    }

    [Fact]
    public void Switch1_Bewegung_wird_in_Switch2_Achsen_umgerechnet()
    {
        // Je Achse Empfindlichkeit − Nullpunkt = 13371 (Gyro) bzw. 16384 (Beschl.): ein Ausschlag von genau
        // dieser Größe ist 936 °/s bzw. 4 g. Gyro 936 °/s → 936 × 32767/2000 ≈ 15335 (Switch-2-Rohwert).
        var cal = new ImuCalibration(0, 0, 0, 16384, 16384, 16384, 20, -15, 5, 13391, 13356, 13376);
        const short full = 15335;

        // Nur X₁ dreht → Y₂ = X₁ (bei allen Controllern gleich).
        var pro = cal.ToSwitch2Axes(0, 0, 0, 20 + 13371, -15, 5, rightJoyCon: false);
        Assert.Equal(new Motion(0, 0, 0, 0, full, 0), pro);
        Assert.Equal(pro, cal.ToSwitch2Axes(0, 0, 0, 20 + 13371, -15, 5, rightJoyCon: true));

        // Y₁ und Z₁: X₂ = −Y₁, Z₂ = Z₁; der rechte Joy-Con dreht X₂ und Z₂ um.
        Assert.Equal(new Motion(-1000, 4096, 2000, -full, 0, full),
            cal.ToSwitch2Axes(4096, 1000, 2000, 20, -15 + 13371, 5 + 13371, rightJoyCon: false));
        Assert.Equal(new Motion(1000, 4096, -2000, full, 0, -full),
            cal.ToSwitch2Axes(4096, 1000, 2000, 20, -15 + 13371, 5 + 13371, rightJoyCon: true));

        // Werkswerte: Beschleunigung 1:1, Gyro-Nullpunkt 0.
        Assert.Equal(new Motion(-200, 100, 4096, 0, 0, 0), ImuCalibration.Default.ToSwitch2Axes(100, 200, 4096, 0, 0, 0, false));

        // Winzige Empfindlichkeit → begrenzt statt Überlauf.
        var tiny = new ImuCalibration(0, 0, 0, 1, 1, 1, 0, 0, 0, 1, 1, 1);
        Assert.Equal(new Motion(-32768, 32767, 32767, -32768, 32767, 32767), tiny.ToSwitch2Axes(1000, 1000, 1000, 1000, 1000, 1000, false));
    }

    [Fact]
    public void Switch1_Vollbericht_nutzt_Kalibrierung_und_Achsdrehung()
    {
        var r = new byte[49];
        r[0] = Switch1.InputFull;
        // Neueste Probe (Byte 37–48): Beschl. X₁ = 4096, Gyro Z₁ = Nullpunkt + 13371.
        r[37] = 0x00; r[38] = 0x10;
        short gz = 5 + 13371;
        r[47] = (byte)gz; r[48] = (byte)(gz >> 8);
        var cal = new ImuCalibration(0, 0, 0, 16384, 16384, 16384, 20, -15, 5, 13391, 13356, 13376);
        Assert.True(Switch1.TryParseFull(r, ControllerKind.JoyCon1Right, cal, out var right));
        Assert.True(Switch1.TryParseFull(r, ControllerKind.JoyCon1Left, cal, out var left));
        Assert.Equal(4096, left.Motion!.Value.AccelY);
        Assert.Equal(15335, left.Motion.Value.GyroZ);
        Assert.Equal(-15335, right.Motion!.Value.GyroZ);  // rechter Joy-Con: Z₂ umgedreht
        // Ohne Bewegungsdaten (alle Beschleunigungen 0) keine Motion.
        var empty = new byte[49];
        empty[0] = Switch1.InputFull;
        Assert.True(Switch1.TryParseFull(empty, ControllerKind.Pro1, cal, out var still));
        Assert.Null(still.Motion);
    }

    // ---------- Wii ----------

    [Fact]
    public void WiiParser_verkraftet_jede_Laenge_jeden_Modus_und_jede_Erweiterung()
    {
        foreach (var ext in Enum.GetValues<WiiExtension>())
        {
            for (int mode = 0x30; mode <= 0x3F; mode++)
            {
                for (int n = 0; n <= 22; n++)
                {
                    var r = new byte[n];
                    for (int i = 1; i < n; i++)
                        r[i] = (byte)(i * 37 + mode);
                    if (n > 0)
                        r[0] = (byte)mode;
                    bool ok = new WiiParser().TryParse(r, ext, 50, out _);
                    if (ext != WiiExtension.WiiUPro) // Wii U Pro: Ergebnis hängt an der Länge der Erweiterungsdaten
                        Assert.True(ok == n >= 3, $"{ext}, Modus {mode:X2}, Länge {n}");
                }
            }
        }
    }

    [Theory]
    [InlineData(0x20)]
    [InlineData(0x21)]
    [InlineData(0x2F)]
    [InlineData(0x40)]
    [InlineData(0xFF)]
    public void WiiParser_lehnt_andere_Berichte_ab(byte id)
    {
        var r = new byte[22];
        r[0] = id;
        foreach (var ext in Enum.GetValues<WiiExtension>())
            Assert.False(new WiiParser().TryParse(r, ext, 50, out _));
    }

    [Fact]
    public void Wii_Speicherantwort_Groesse_Fehler_und_Adresse()
    {
        // Byte 3: obere 4 Bit = Größe − 1, untere = Fehler; Byte 4–5 = Adresse (niederwertige 16 Bit).
        byte[] r = [Wii.InputRead, 0, 0, 0x50, 0x00, 0xFA, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        Assert.True(Wii.TryParseRead(r, out int error, out ushort address, out var data));
        Assert.Equal(0, error);
        Assert.Equal(0x00FA, address);
        Assert.Equal([1, 2, 3, 4, 5, 6], data);
        r[3] = 0xF7; // 16 Byte, Fehler 7 (Bereich nicht lesbar)
        Assert.True(Wii.TryParseRead(r, out error, out _, out data));
        Assert.Equal(7, error);
        Assert.Equal(16, data.Length);
    }

    [Fact]
    public void Wii_Kennung_zu_kurz_ist_fremd()
    {
        for (int n = 0; n < 6; n++)
            Assert.Equal(WiiExtension.Other, Wii.ExtensionFromId(new byte[n]));
        Assert.Equal(WiiExtension.None, Wii.ExtensionFromId([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]));
        Assert.Empty(Wii.ParseIrBasic([]));
        Assert.Empty(Wii.ParseIrBasic([1, 2, 3, 4]));                 // weniger als ein Punktpaar
    }

    // ---------- NFC und IR-Kamera ----------

    [Fact]
    public void Nfc_Auswertung_verkraftet_kurze_Daten()
    {
        var full = new byte[320];
        full[0] = 0x00; full[1] = 0x05; full[5] = 0x31; full[6] = Nfc.StatusTagFound; full[14] = 7;
        for (int i = 0; i < 7; i++) full[15 + i] = (byte)(i + 1);
        for (int n = 0; n <= 40; n++)
        {
            var d = full.AsSpan(0, n);
            Assert.Equal(n >= 16, Nfc.TryGetNfcStatus(Nfc.ReportNfcState, d, out _));
            Assert.Equal(n >= 22, Nfc.TryGetTag(Nfc.ReportNfcState, d, out var uid));
            Assert.Equal(n >= 22 ? 7 : 0, uid.Length);
            Assert.Equal(n > 6, Nfc.IsMcuMode(Nfc.ReportState, d, Nfc.StatusTagFound)); // Byte 6 = 0x09
        }
        // Lesepakete: zu kurz oder ohne Nutzdaten → nicht übernommen.
        var asm = new AmiiboAssembler();
        for (int n = 0; n <= 70; n++)
        {
            var p = new byte[n];
            if (n > 5) { p[1] = 0x07; p[2] = 0x01; p[4] = 0x01; p[5] = 0x31; } // Paket 1, 305 Byte angekündigt
            Assert.Equal(n > 66, asm.Add(Nfc.ReportNfcRead, p));
            if (asm.PacketCount > 0)
                break;
        }
    }

    [Fact]
    public void Nfc_MCU_Teil_nur_bei_Inhalt()
    {
        var r = new byte[362];
        r[0] = Nfc.InputMcu;
        Assert.False(Nfc.TryGetMcu(r, out byte kind, out _));      // 0x00: noch nichts da
        Assert.Equal(0x00, kind);
        r[Nfc.McuReportOffset] = Nfc.ReportEmpty;
        Assert.False(Nfc.TryGetMcu(r, out _, out _));
        r[Nfc.McuReportOffset] = Nfc.ReportState;
        r[Nfc.McuReportOffset + 1 + 6] = Nfc.ModeNfc;
        Assert.True(Nfc.TryGetMcu(r, out kind, out var data));
        Assert.Equal(362 - Nfc.McuReportOffset - 1, data.Length);
        Assert.True(Nfc.IsMcuMode(kind, data, Nfc.ModeNfc));
    }

    private static byte[] IrFragment(byte number, int length = 362, byte id = Nfc.InputMcu, byte kind = IrCamera.ReportIrData)
    {
        var r = new byte[length];
        if (length > 0) r[0] = id;
        if (length > Nfc.McuReportOffset) r[Nfc.McuReportOffset] = kind;
        if (length > 52) r[52] = number;
        for (int i = 59; i < length; i++) r[i] = (byte)(number + 1);
        return r;
    }

    [Fact]
    public void IR_Kamera_verkraftet_kurze_und_fremde_Berichte()
    {
        // Kürzer als 59 + 300 Byte: nur die letzte Quittung wiederholen, kein Bild.
        for (int n = 0; n < 59 + IrCamera.FragmentSize; n++)
        {
            var asm = new IrFrameAssembler(IrResolution.Size40x30);
            var (request, image) = asm.Handle(IrFragment(0, n));
            Assert.Null(image);
            Assert.Equal(IrCamera.Acknowledge(3), request); // noch nichts erhalten: Quittung für das „vorige“ Bild
        }
        var a = new IrFrameAssembler(IrResolution.Size40x30);
        Assert.Equal(IrCamera.Acknowledge(3), a.Handle(IrFragment(0, id: Switch1.InputFull)).Request);   // fremder Bericht
        Assert.Equal(IrCamera.Acknowledge(3), a.Handle(IrFragment(0, kind: Nfc.ReportNfcState)).Request); // andere MCU-Art
        Assert.Equal(IrCamera.Resend(0), a.Handle(IrFragment(200)).Request);  // Stücknummer außerhalb des Bilds
        Assert.Equal(IrCamera.Acknowledge(0), a.Handle(IrFragment(0)).Request); // danach geht es normal weiter
    }

    [Theory]
    [InlineData(IrResolution.Size40x30)]
    [InlineData(IrResolution.Size80x60)]
    [InlineData(IrResolution.Size160x120)]
    [InlineData(IrResolution.Size320x240)]
    public void IR_Kamera_fuegt_jede_Aufloesung_zusammen(IrResolution resolution)
    {
        var asm = new IrFrameAssembler(resolution);
        var (width, height, _, last) = IrCamera.Format(resolution);
        byte[]? image = null;
        for (int n = 0; n <= last; n++)
            image = asm.Handle(IrFragment((byte)n)).Image ?? image;
        Assert.NotNull(image);
        Assert.Equal(width * height, image!.Length);
        Assert.Equal(1, image[0]);
        Assert.Equal((byte)(last + 1), image[^1]);
    }

    // ---------- DSU (Cemuhook) ----------

    [Fact]
    public void Dsu_gueltige_Anfrage()
    {
        var p = DsuRequest(Dsu.MsgPortInfo, [2, 0, 0, 0, 0, 1]);
        Assert.Equal(Dsu.MsgPortInfo, Dsu.ParseRequest(p, out var body));
        Assert.Equal([0, 1], Dsu.RequestedSlots(body));
        Assert.Equal(Dsu.MsgVersion, Dsu.ParseRequest(DsuRequest(Dsu.MsgVersion, []), out body));
        Assert.Equal(0, body.Length);
        Assert.Equal(Dsu.MsgVersion, Dsu.ParseRequest(DsuRequest(Dsu.MsgVersion, [], version: 1000), out _)); // ältere Version
    }

    [Fact]
    public void Dsu_falsche_Pruefsumme_wird_abgelehnt()
    {
        var p = DsuRequest(Dsu.MsgPortInfo, [2, 0, 0, 0, 0, 1]);
        p[^1] ^= 0x01;                                              // Inhalt verändert
        Assert.Null(Dsu.ParseRequest(p, out _));
        p = DsuRequest(Dsu.MsgPortInfo, [2, 0, 0, 0, 0, 1]);
        p[8] ^= 0x80;                                               // Prüfsumme verändert
        Assert.Null(Dsu.ParseRequest(p, out _));
    }

    [Theory]
    [InlineData("DSUS")] // Server-Paket (z. B. eigenes Echo)
    [InlineData("dsuc")]
    [InlineData("XSUC")]
    [InlineData("\0\0\0\0")]
    public void Dsu_falsches_Magic_wird_abgelehnt(string magic) =>
        Assert.Null(Dsu.ParseRequest(DsuRequest(Dsu.MsgVersion, [], magic), out _));

    [Fact]
    public void Dsu_Laengenfeld_wird_geprueft()
    {
        Assert.Null(Dsu.ParseRequest(DsuRequest(Dsu.MsgPortInfo, [1, 0, 0, 0, 0], length: 4 + 5 + 1), out _)); // länger als das Paket
        Assert.Null(Dsu.ParseRequest(DsuRequest(Dsu.MsgPortInfo, [1, 0, 0, 0, 0], length: ushort.MaxValue), out _));
        Assert.Null(Dsu.ParseRequest(DsuRequest(Dsu.MsgPortInfo, [1, 0, 0, 0, 0], length: 3), out _));  // ohne Nachrichtentyp
        Assert.Null(Dsu.ParseRequest(DsuRequest(Dsu.MsgVersion, [], version: Dsu.ProtocolVersion + 1), out _)); // neuere Version
        for (int n = 0; n < 20; n++)
            Assert.Null(Dsu.ParseRequest(new byte[n], out _));
    }

    [Fact]
    public void Dsu_Bytes_hinter_dem_Paket_werden_ignoriert()
    {
        var p = DsuRequest(Dsu.MsgPortInfo, [1, 0, 0, 0, 3]);
        byte[] padded = [.. p, 0xEE, 0xEE, 0xEE];
        Assert.Equal(Dsu.MsgPortInfo, Dsu.ParseRequest(padded, out var body));
        Assert.Equal(5, body.Length);
        Assert.Equal([3], Dsu.RequestedSlots(body));
    }

    [Fact]
    public void Dsu_angefragte_Slots_werden_begrenzt()
    {
        Assert.Empty(Dsu.RequestedSlots([]));
        Assert.Empty(Dsu.RequestedSlots([1, 0, 0]));                        // Anzahl unvollständig
        Assert.Empty(Dsu.RequestedSlots([0xFF, 0xFF, 0xFF, 0xFF, 0, 1]));     // negative Anzahl
        Assert.Equal([0, 1, 2, 3], Dsu.RequestedSlots([100, 0, 0, 0, 0, 1, 2, 3, 4, 5])); // höchstens MaxSlots
        Assert.Equal([2], Dsu.RequestedSlots([3, 0, 0, 0, 2]));               // weniger Slot-Bytes als angekündigt
        // Die Slot-Nummer selbst prüft der Server (DsuServer) – hier wird sie unverändert geliefert.
        Assert.Equal([200], Dsu.RequestedSlots([1, 0, 0, 0, 200]));
    }

    // ---------- PlayStation-Feature-Berichte ----------

    [Fact]
    public void PlayStation_Seriennummer_und_Firmware()
    {
        byte[] ds5 = [0x09, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0, 0];
        Assert.Equal("66:55:44:33:22:11", PlayStationPad.DualSenseSerial(ds5));  // umgedreht: höchstes Byte zuerst
        Assert.Null(PlayStationPad.DualShock4Serial(ds5));                       // anderer Bericht
        byte[] ds4 = [0x12, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06];
        Assert.Equal("06:05:04:03:02:01", PlayStationPad.DualShock4Serial(ds4));
        Assert.Null(PlayStationPad.DualSenseSerial([0x09, 0, 0, 0, 0, 0, 0]));  // leer
        Assert.Null(PlayStationPad.DualShock4Serial([0x12, 0, 0, 0, 0, 0, 0, 0xAA])); // Adresse leer, Rest egal

        var fw = new byte[64];
        fw[0] = 0x20; fw[44] = 0x24; fw[45] = 0x02;
        Assert.Equal((ushort)0x0224, PlayStationPad.DualSenseFirmware(fw));
        Assert.Equal((ushort)0x0224, PlayStationPad.DualSenseFirmware(fw.AsSpan(0, 46)));
        Assert.Null(PlayStationPad.DualSenseFirmware(fw.AsSpan(0, 45)));
    }
}
