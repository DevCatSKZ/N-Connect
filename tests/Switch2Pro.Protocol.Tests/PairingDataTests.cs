using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>Kopplungsdaten: Switch-SD-Karte (hekate/Bluepick-Format) und Übertragungsdatei PC → PC. Nur ausgedachte Beispieldaten.</summary>
public class PairingDataTests
{
    private const string Ini =
        "[joycon_00]\ntype=1\nmac=AA:BB:CC:00:11:22\nhost=98:B6:E9:01:02:03\nltk=00112233445566778899AABBCCDDEEFF\n\n" +
        "[joycon_01]\ntype=2\nmac=AA:BB:CC:33:44:55\nhost=98:B6:E9:01:02:03\nltk=FFEEDDCCBBAA99887766554433221100\n\n";

    private const string Cal = "imu_type=0\n\nacc_cal_off_x=0x1\n\ndevice_bt_mac=98:B6:E9:01:02:03\n";

    [Fact]
    public void Ini_LiestBeideJoyCon()
    {
        var list = SwitchPairingData.ParseIni(Ini.Replace("\n", "\r\n"));
        Assert.Equal(2, list.Count);
        Assert.Equal(SwitchPairedType.JoyConLeft, list[0].Type);
        Assert.Equal(SwitchPairedType.JoyConRight, list[1].Type);
        Assert.Equal("AA:BB:CC:00:11:22", list[0].ControllerAddress);
        Assert.Equal("98:B6:E9:01:02:03", list[0].HostAddress);
        Assert.Equal(0x00, list[0].LinkKey[0]);
        Assert.Equal(0xFF, list[0].LinkKey[15]);
        Assert.Equal(0xFF, list[0].LinkKeyReversed[0]);
        Assert.True(list[0].PairedWithSwitch);
        Assert.Equal(ControllerKind.JoyCon1Left, list[0].Kind);
    }

    [Fact]
    public void Ini_UngueltigeEintraegeWerdenUebersprungen()
    {
        var list = SwitchPairingData.ParseIni("[joycon_00]\ntype=1\nmac=XYZ\nhost=98:B6:E9:01:02:03\nltk=00\n[other]\nmac=AA:BB:CC:00:11:22\n");
        Assert.Empty(list);
    }

    [Fact]
    public void Bin_GleichesErgebnisWieIni()
    {
        var bin = new byte[58];
        bin[0] = 0x01;
        new byte[] { 0xAA, 0xBB, 0xCC, 0x00, 0x11, 0x22 }.CopyTo(bin, 1);
        new byte[] { 0x98, 0xB6, 0xE9, 0x01, 0x02, 0x03 }.CopyTo(bin, 7);
        Convert.FromHexString("00112233445566778899AABBCCDDEEFF").CopyTo(bin, 13);
        var list = SwitchPairingData.ParseBin(bin);
        Assert.Equal(2, list.Count);
        var fromIni = SwitchPairingData.ParseIni(Ini)[0];
        Assert.Equal(fromIni.ControllerAddress, list[0].ControllerAddress);
        Assert.Equal(fromIni.HostAddress, list[0].HostAddress);
        Assert.Equal(fromIni.LinkKey, list[0].LinkKey);
        // Zweiter Eintrag leer (Joy-Con nicht gefunden) → nicht gültig.
        var data = SwitchPairingData.Parse(null, bin, null);
        Assert.Single(data.Valid);
        Assert.Equal("98:B6:E9:01:02:03", data.ConsoleAddress);
    }

    [Fact]
    public void Parse_KonsolenadresseAusSwitchCal()
    {
        var data = SwitchPairingData.Parse(Ini, null, Cal.Replace("98:B6:E9:01:02:03", "98:B6:E9:0A:0B:0C"));
        Assert.Equal("98:B6:E9:0A:0B:0C", data.ConsoleAddress);
        Assert.Equal(2, data.Valid.Count());
    }

    [Fact]
    public void Typ0_NichtMitSwitchGekoppelt()
    {
        var list = SwitchPairingData.ParseIni(Ini.Replace("type=1", "type=0"));
        Assert.False(list[0].PairedWithSwitch);
        Assert.Equal(SwitchPairedType.Unknown, list[0].Type);
    }

    [Fact]
    public void SdKarte_WirdErkanntUndNurGelesen()
    {
        string root = Path.Combine(Path.GetTempPath(), "nconnect-sd-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Nintendo"));
            Directory.CreateDirectory(Path.Combine(root, "switchroot"));
            File.WriteAllText(Path.Combine(root, SwitchCard.IniFile), Ini);
            File.WriteAllText(Path.Combine(root, SwitchCard.CalFile), Cal);
            var info = SwitchCard.Inspect(root);
            Assert.True(info.IsSwitchCard);
            Assert.True(info.HasPairingExport);
            Assert.Contains("Nintendo", info.Markers);
            var data = SwitchCard.ReadPairings(root);
            Assert.NotNull(data);
            Assert.Equal(2, data!.Valid.Count());

            string other = Path.Combine(root, "leer");
            Directory.CreateDirectory(other);
            Assert.False(SwitchCard.Inspect(other).IsSwitchCard);
            Assert.Null(SwitchCard.ReadPairings(other));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("aa:bb:cc:dd:ee:ff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("AA-BB-CC-DD-EE-FF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aabbccddeeff", "AA:BB:CC:DD:EE:FF")]
    public void Adresse_WirdVereinheitlicht(string input, string expected)
    {
        Assert.True(BtAddress.TryNormalize(input, out var a));
        Assert.Equal(expected, a);
        Assert.False(BtAddress.TryNormalize("AA:BB", out _));
        Assert.Equal("00:1A:7D:DA:71:13", BtAddress.Format(0x001A7DDA7113UL));
    }

    private static PairingExport Sample() => new()
    {
        SourcePc = "DESKTOP",
        AdapterAddress = "00:1A:7D:DA:71:13",
        Controllers =
        [
            new TransferController { Address = "AA:BB:CC:00:11:22", Name = "Joy-Con (L)", HostAddress = "98:B6:E9:01:02:03", Source = "Switch", LinkKey = "00112233445566778899AABBCCDDEEFF" },
            new TransferController { Address = "11:22:33:44:55:66", HostAddress = "00:1A:7D:DA:71:13" },
        ],
        KnownControllers = ["11:22:33:44:55:66"],
        SingleJoyCons = ["AA:BB:CC:00:11:22"],
        GyroCalibration = new() { ["11:22:33:44:55:66"] = new GyroBias(1.5f, -2f, 0.25f) },
    };

    [Theory]
    [InlineData(null)]
    [InlineData("geheim 123 ÄÖÜ")]
    public void Datei_Hin_und_zurueck(string? password)
    {
        string text = PairingTransfer.Write(Sample(), password);
        Assert.Equal(password is not null, PairingTransfer.IsEncrypted(text));
        if (password is not null)
            Assert.DoesNotContain("00112233445566778899AABBCCDDEEFF", text);
        var back = PairingTransfer.Read(text, password);
        Assert.Equal("DESKTOP", back.SourcePc);
        Assert.Equal(2, back.Controllers.Count);
        Assert.Equal("00112233445566778899AABBCCDDEEFF", back.Controllers[0].LinkKey);
        Assert.True(back.HasKeys);
        Assert.Equal(new GyroBias(1.5f, -2f, 0.25f), back.GyroCalibration["11:22:33:44:55:66"]);
        Assert.Equal(["AA:BB:CC:00:11:22"], back.SingleJoyCons);
    }

    [Fact]
    public void Datei_FalschesOderFehlendesPasswort()
    {
        string text = PairingTransfer.Write(Sample(), "richtig");
        Assert.Equal(PairingFileError.WrongPassword, Assert.Throws<PairingFileException>(() => PairingTransfer.Read(text, "falsch")).Error);
        Assert.Equal(PairingFileError.PasswordRequired, Assert.Throws<PairingFileException>(() => PairingTransfer.Read(text, null)).Error);
    }

    [Fact]
    public void Datei_Beschaedigt()
    {
        string plain = PairingTransfer.Write(Sample(), null);
        Assert.Equal(PairingFileError.Corrupt, Assert.Throws<PairingFileException>(() => PairingTransfer.Read(plain[..(plain.Length / 2)], null)).Error);
        Assert.Equal(PairingFileError.NotAPairingFile, Assert.Throws<PairingFileException>(() => PairingTransfer.Read("{\"hallo\":1}", null)).Error);
        Assert.Equal(PairingFileError.NotAPairingFile, Assert.Throws<PairingFileException>(() => PairingTransfer.Read("kein json", null)).Error);
        Assert.Equal(PairingFileError.NewerVersion, Assert.Throws<PairingFileException>(
            () => PairingTransfer.Read(plain.Replace("\"version\": 1", "\"version\": 99"), null)).Error);

        // Verschlüsselte Daten verändert: GCM bemerkt es.
        string secret = PairingTransfer.Write(Sample(), "pw");
        var node = System.Text.Json.Nodes.JsonNode.Parse(secret)!.AsObject();
        var data = Convert.FromBase64String(node["data"]!.GetValue<string>());
        data[3] ^= 0x01;
        node["data"] = Convert.ToBase64String(data);
        Assert.Equal(PairingFileError.WrongPassword, Assert.Throws<PairingFileException>(() => PairingTransfer.Read(node.ToJsonString(), "pw")).Error);
        node["nonce"] = "kein base64!";
        Assert.Equal(PairingFileError.Corrupt, Assert.Throws<PairingFileException>(() => PairingTransfer.Read(node.ToJsonString(), "pw")).Error);
    }

    [Fact]
    public void Merge_ErgaenztOhneZuLoeschen()
    {
        var settings = new Settings { KnownControllers = ["22:22:22:22:22:22"] };
        settings.GyroCalibration["11:22:33:44:55:66"] = new GyroBias(9, 9, 9);
        int changes = PairingTransfer.Merge(settings, PairingTransfer.Read(PairingTransfer.Write(Sample(), null), null));
        Assert.True(changes > 0);
        Assert.Contains("22:22:22:22:22:22", settings.KnownControllers);
        Assert.Contains("AA:BB:CC:00:11:22", settings.KnownControllers);
        Assert.Contains("11:22:33:44:55:66", settings.KnownControllers);
        Assert.Empty(settings.AllowedControllers); // „alle erlaubt“ bleibt
        Assert.Equal(new GyroBias(9, 9, 9), settings.GyroCalibration["11:22:33:44:55:66"]); // eigene Kalibrierung bleibt
        Assert.Contains(settings.ImportedPairings, p => p.Address == "AA:BB:CC:00:11:22" && p.HostAddress == "98:B6:E9:01:02:03");
        // Zweimal importieren ändert nichts mehr.
        Assert.Equal(0, PairingTransfer.Merge(settings, Sample()));
        // Export enthält die übernommenen Einträge, aber keinen Schlüssel.
        var export = PairingTransfer.FromSettings(settings, "00:1A:7D:DA:71:13", "MINI");
        Assert.Contains(export.Controllers, c => c.Address == "AA:BB:CC:00:11:22" && c.Source == "Switch");
        Assert.False(export.HasKeys);
    }

    [Fact]
    public void Merge_ErweitertBestehendeFreigabeliste()
    {
        var settings = new Settings { AllowedControllers = ["22:22:22:22:22:22"] };
        PairingTransfer.Merge(settings, Sample());
        Assert.Contains("AA:BB:CC:00:11:22", settings.AllowedControllers);
    }

    [Fact]
    public void FromSwitch_SchluesselNurAufWunsch()
    {
        var data = SwitchPairingData.Parse(Ini, null, Cal);
        Assert.All(PairingTransfer.FromSwitch(data, includeKeys: false), c => Assert.Null(c.LinkKey));
        Assert.All(PairingTransfer.FromSwitch(data, includeKeys: true), c => Assert.Equal(32, c.LinkKey!.Length));
    }
}
