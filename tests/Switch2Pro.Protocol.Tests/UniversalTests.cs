using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

public class ControllerKindTests
{
    [Theory]
    [InlineData(0x2069, ControllerKind.Pro2)]
    [InlineData(0x2067, ControllerKind.JoyCon2Left)]
    [InlineData(0x2066, ControllerKind.JoyCon2Right)]
    [InlineData(0x2073, ControllerKind.GameCube2)]
    [InlineData(0x1234, ControllerKind.Unknown)]
    public void Switch2_Produkt_IDs(int pid, ControllerKind kind) => Assert.Equal(kind, ControllerKinds.FromSwitch2ProductId(pid));

    [Theory]
    [InlineData(0x2009, ControllerKind.Pro1)]
    [InlineData(0x2006, ControllerKind.JoyCon1Left)]
    [InlineData(0x2007, ControllerKind.JoyCon1Right)]
    public void Switch1_Produkt_IDs(int pid, ControllerKind kind) => Assert.Equal(kind, ControllerKinds.FromSwitch1ProductId(pid));

    [Fact]
    public void Werbung_aller_Switch2_Controller_wird_erkannt()
    {
        // Echte Werbung des Pro Controller 2 (aufgezeichnet), PID dann ausgetauscht.
        var adv = Commands.Hex("01 00 03 7E 05 69 20 00 01 00 00 00 00 00 00 00 0F 00 00 00 00 00 00 00");
        Assert.Equal(ControllerKind.Pro2, Advertisement.Kind(Gatt.NintendoCompanyId, adv));
        Assert.True(Advertisement.IsSyncMode(adv));
        adv[5] = 0x67;
        Assert.Equal(ControllerKind.JoyCon2Left, Advertisement.Kind(Gatt.NintendoCompanyId, adv));
        adv[5] = 0x73;
        Assert.Equal(ControllerKind.GameCube2, Advertisement.Kind(Gatt.NintendoCompanyId, adv));
        Assert.Equal(ControllerKind.Unknown, Advertisement.Kind(0x004C, adv));
    }
}

public class Report05Tests
{
    [Fact]
    public void Echter_Bericht_des_Pro_Controller_2()
    {
        // Aufgezeichnet per Bluetooth (BleProbe), Controller ruhig, keine Taste.
        var r = Commands.Hex("20 8C 00 00 00 00 00 00 00 00 C5 97 84 AF F8 87 00 00 00 00 00 00 00 00 00 C5 FF 1E 00 A0 FF 70 0E 00 00 00 00 00 00 00 00 01 FD 49 A0 01 FF FF 11 00 6C FD 3C 10 F8 FF F9 FF 19 00 00 00 00");
        Assert.True(InputReports.TryParseReport05(r, out var s));
        Assert.Equal(ProButtons.None, s.Buttons);
        Assert.Equal(0x7C5, s.LeftX);
        Assert.Equal(3696, s.BatteryMillivolts);
        Assert.NotNull(s.Motion);
        Assert.Equal(-1, s.LeftTrigger);
    }

    [Fact]
    public void SL_SR_und_GameCube_Trigger()
    {
        var r = new byte[63];
        r[4] = 0x30; // SL/SR rechts
        r[6] = 0x30; // SL/SR links
        r[0x3C] = 200;
        r[0x3D] = 40;
        Assert.True(InputReports.TryParseReport05(r, out var s, ControllerKind.GameCube2));
        Assert.Equal(ProButtons.SLLeft | ProButtons.SRLeft | ProButtons.SLRight | ProButtons.SRRight, s.Buttons);
        Assert.Equal(200, s.LeftTrigger);
        Assert.Equal(40, s.RightTrigger);
    }
}

public class OrientationTests
{
    private static readonly DeviceCalibration Cal = new()
    {
        Left = new(new AxisCalibration(2048, 1500, 1500), new AxisCalibration(2048, 1500, 1500)),
        Right = new(new AxisCalibration(2048, 1500, 1500), new AxisCalibration(2048, 1500, 1500)),
    };

    [Fact]
    public void Linker_JoyCon_quer_Stick_und_Tasten()
    {
        // Stick am Gerät nach oben (+Y) = quer gehalten nach links.
        var s = new ControllerState { Kind = ControllerKind.JoyCon2Left, LeftY = 2048 + 1500, Buttons = ProButtons.Left | ProButtons.SRLeft };
        var p = Mapping.Normalize(s, Cal);
        Assert.Equal(-1f, p.LeftX, 3);
        Assert.Equal(0f, p.LeftY, 3);
        Assert.Equal(ProButtons.B | ProButtons.R, p.Buttons); // untere Taste, rechte Schultertaste
        var g = Mapping.ToGamepad(p, new Settings());
        Assert.Equal(XButtons.A | XButtons.RB, g.Buttons);
    }

    [Fact]
    public void Rechter_JoyCon_quer_Stick_und_Tasten()
    {
        // Stick am Gerät nach rechts (+X) = quer gehalten nach unten.
        var s = new ControllerState { Kind = ControllerKind.JoyCon1Right, RightX = 2048 + 1500, Buttons = ProButtons.A | ProButtons.SLRight | ProButtons.Plus };
        var p = Mapping.Normalize(s, Cal);
        Assert.Equal(0f, p.LeftX, 3);
        Assert.Equal(-1f, p.LeftY, 3);
        Assert.Equal(ProButtons.B | ProButtons.L | ProButtons.Plus, p.Buttons);
    }

    [Fact]
    public void JoyCon_Paar_wird_ein_Controller()
    {
        var l = new ControllerState { Kind = ControllerKind.JoyCon2Left, LeftX = 2048 + 1500, Buttons = ProButtons.ZL | ProButtons.SLLeft, BatteryPercent = 40 };
        var r = new ControllerState
        {
            Kind = ControllerKind.JoyCon2Right, RightY = 2048 - 1500, Buttons = ProButtons.A, BatteryPercent = 70,
            Motion = new Motion(0, 0, 4096, 10, 0, 0),
        };
        var p = Mapping.Merge(l, Cal, r, Cal);
        Assert.Equal(ControllerKind.JoyConPair, p.Kind);
        Assert.Equal(ProButtons.ZL | ProButtons.A, p.Buttons);
        Assert.Equal(1f, p.LeftX, 3);
        Assert.Equal(-1f, p.RightY, 3);
        Assert.Equal(40, p.BatteryPercent);
        Assert.NotNull(p.Motion);
    }

    [Fact]
    public void GameCube_analoge_Trigger_und_Z()
    {
        var s = new ControllerState { Kind = ControllerKind.GameCube2, LeftTrigger = 232, RightTrigger = 30, Buttons = ProButtons.ZR | ProButtons.R | ProButtons.B };
        var g = Mapping.ToGamepad(Mapping.Normalize(s, Cal), new Settings { Layout = FaceButtonLayout.Switch2 });
        Assert.Equal(255, g.LeftTrigger);
        Assert.Equal(0, g.RightTrigger);
        Assert.Equal(XButtons.RB | XButtons.A, g.Buttons); // Z = RB, große A-Taste unten = Xbox A
    }

    [Fact]
    public void Gyro_Nullpunkt_wird_abgezogen()
    {
        var s = new ControllerState { Kind = ControllerKind.Pro2, Motion = new Motion(0, 0, 4096, 110, -5, 3) };
        var p = Mapping.Normalize(s, Cal with { Gyro = new GyroBias(10, -5, 3) });
        Assert.Equal(new Motion(0, 0, 4096, 100, 0, 0), p.Motion);
    }
}

public class Switch1Tests
{
    [Fact]
    public void Unterbefehl_Aufbau()
    {
        var r = Switch1.Subcommand(17, Switch1.SubSetInputMode, [0x30]);
        Assert.Equal(49, r.Length);
        Assert.Equal(0x01, r[0]);
        Assert.Equal(1, r[1]);                                            // Zähler 0–15
        Assert.Equal(Commands.Hex("00 01 40 40 00 01 40 40"), r[2..10]); // neutrale Vibration
        Assert.Equal(0x03, r[10]);
        Assert.Equal(0x30, r[11]);
    }

    [Fact]
    public void SPI_Antwort_wird_nach_Adresse_geprueft()
    {
        var reply = new byte[49];
        reply[0] = 0x21;
        reply[13] = 0x90;
        reply[14] = Switch1.SubSpiRead;
        Switch1.SpiReadArgs(0x603D, 3).CopyTo(reply, 15);
        reply[20] = 0xAA; reply[21] = 0xBB; reply[22] = 0xCC;
        Assert.True(Switch1.TryParseReply(reply, Switch1.SubSpiRead, out var data, 0x603D));
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, data);
        Assert.False(Switch1.TryParseReply(reply, Switch1.SubSpiRead, out _, 0x6046));
    }

    private static byte[] Pack(int a, int b) => [(byte)a, (byte)((a >> 8) | (b << 4)), (byte)(b >> 4)];

    [Fact]
    public void Stick_Kalibrierung_links_und_rechts()
    {
        byte[] left = [.. Pack(1500, 1400), .. Pack(2000, 2100), .. Pack(1300, 1200)];  // Max, Mitte, Min
        Assert.True(Switch1.TryParseStick(left, left: true, out var l));
        Assert.Equal(new AxisCalibration(2000, 1500, 1300), l.X);
        byte[] right = [.. Pack(2000, 2100), .. Pack(1300, 1200), .. Pack(1500, 1400)]; // Mitte, Min, Max
        Assert.True(Switch1.TryParseStick(right, left: false, out var r));
        Assert.Equal(new AxisCalibration(2100, 1400, 1200), r.Y);
        Assert.False(Switch1.TryParseStick(Enumerable.Repeat((byte)0xFF, 9).ToArray(), true, out _));
    }

    [Fact]
    public void Vollbericht_Tasten_Sticks_Akku_IMU()
    {
        var r = new byte[49];
        r[0] = 0x30;
        r[2] = 0x90;            // Akku Stufe 4 (Bit 5–7 = 4 → voll), Bit 4 = lädt
        r[3] = 0x08 | 0x80;     // A, ZR
        r[4] = 0x10;            // Home
        r[5] = 0x02 | 0x20;     // hoch, SL links
        Pack(2048, 2048).CopyTo(r, 6);
        r[37 + 5] = 0x10;       // neueste IMU-Probe: Beschl. Z = 4096 (1 g)
        Assert.True(Switch1.TryParseFull(r, ControllerKind.Pro1, ImuCalibration.Default, out var s));
        Assert.Equal(ProButtons.A | ProButtons.ZR | ProButtons.Home | ProButtons.Up | ProButtons.SLLeft, s.Buttons);
        Assert.Equal(2048, s.LeftX);
        Assert.Equal(2048, s.LeftY);
        Assert.Equal(100, s.BatteryPercent);
        Assert.True(s.Charging);
        Assert.Equal(4096, s.Motion!.Value.AccelZ);
    }

    [Fact]
    public void Rumble_neutral_und_kodiert()
    {
        Assert.Equal(Commands.Hex("00 01 40 40"), Switch1Rumble.Frame(0, 0, 1f));
        var f = Switch1Rumble.Frame(255, 255, 1f);
        Assert.Equal(0x74, f[0]);
        Assert.Equal(0xC8, f[1]);           // höchste Hoch-Amplitude
        Assert.Equal(0x3D, f[2] & 0x7F);
        Assert.Equal(0x72, f[3]);           // höchste Tief-Amplitude
    }
}

public class PairingRecordTests
{
    [Fact]
    public void Echter_Kopplungsspeicher_des_Pro_Controller_2()
    {
        // Aufgezeichnet (BleProbe dumppair), Adresse 0x1FA000, 0x40 Byte.
        var block = Commands.Hex("02 9E 11 00 00 00 00 00 48 F1 EB F7 1D 0D 00 00 00 00 00 00 00 00 00 00 00 00 9B F3 1D E7 49 AC 10 E4 01 26 5D 32 5B 44 80 68 00 00 00 00 00 00 48 F1 EB F7 1D 0C 00 00 00 00 00 00 00 00 00 00");
        Assert.True(PairingRecord.TryParse(block, out var r));
        Assert.Equal(0x48F1EBF71D0DUL, r.HostAddresses[0]);
        Assert.Equal(Commands.Hex("9B F3 1D E7 49 AC 10 E4 01 26 5D 32 5B 44 80 68"), r.LongTermKey);
        Assert.False(PairingRecord.TryParse(Enumerable.Repeat((byte)0xFF, 0x40).ToArray(), out _));
    }
}

public class PairingTests
{
    // Mitschnitt Konsole ↔ Pro Controller 2 aus ndeadly/switch2_controller_research (bluetooth_interface.md).
    private static readonly byte[] A1 = Commands.Hex("35 03 e9 29 82 87 71 24 be a8 0c 66 46 15 83 4b");
    private static readonly byte[] B1 = Commands.Hex("5c f6 ee 79 2c df 05 e1 ba 2b 63 25 c4 1a 5f 10");
    private static readonly byte[] A2 = Commands.Hex("6f c6 df 8a d8 fe df 15 bb 8c 15 e9 1f 32 05 44");

    [Fact]
    public void Anfragen_wie_im_Mitschnitt()
    {
        var p = new Pairing(A1, A2);
        Assert.Equal(Commands.Hex("15 91 01 04 00 11 00 00 00 35 03 e9 29 82 87 71 24 be a8 0c 66 46 15 83 4b"), p.KeysRequest());
        Assert.Equal(Commands.Hex("15 91 01 03 00 01 00 00 00"), Pairing.FinishRequest());
        // Konsole: "15 91 01 01 00 0e 00 00 00 02 81 eb 3a eb f1 48 80 eb 3a eb f1 48" (Adresse 48:F1:EB:3A:EB:81)
        Assert.Equal(Commands.Hex("15 91 01 01 00 0e 00 00 00 02 81 eb 3a eb f1 48 81 eb 3a eb f1 48"),
            Pairing.AddressesRequest(0x48F1EB3AEB81));
    }

    [Fact]
    public void Schluessel_und_Bestaetigung_wie_im_Mitschnitt()
    {
        var p = new Pairing(A1, A2);
        Assert.True(p.TryDeriveKey([0x15, 0x01, 0x01, 0x04, 0x10, 0x78, 0x00, 0x00, 0x01, .. B1], out var ltk));
        // Antwort des Controllers auf 15/02 im Mitschnitt.
        var response = Commands.Hex("15 01 01 02 10 78 00 00 01 13 4c 97 f5 11 b9 b6 dd 4d 86 fd 40 f5 36 e9 ed");
        Assert.True(p.VerifyConfirmation(response, ltk));
        Assert.False(new Pairing(A1, A1).VerifyConfirmation(response, ltk));
    }
}

public class HostAddressTests
{
    [Fact]
    public void Werbung_nach_Kopplung_mit_dem_PC()
    {
        // Aufgezeichnet nach Tastendruck, Controller mit PC 00:A7:50:30:19:E5 gekoppelt.
        var adv = Commands.Hex("01 00 03 7E 05 69 20 00 01 00 E5 19 30 50 A7 00 0F 00 00 00 00 00 00 00");
        Assert.Equal(0x00A7503019E5UL, Advertisement.HostAddress(adv));
        Assert.False(Advertisement.IsSyncMode(adv));
        var sync = Commands.Hex("01 00 03 7E 05 69 20 00 01 00 00 00 00 00 00 00 0F 00 00 00 00 00 00 00");
        Assert.Equal(0UL, Advertisement.HostAddress(sync));
    }
}

public class JoyConMouseTests
{
    [Fact]
    public void Maussensor_wird_nur_bei_Joy_Con_2_gelesen()
    {
        var r = new byte[63];
        r[0x10] = 0x34; r[0x11] = 0x12; // X = 0x1234
        r[0x12] = 0xFE; r[0x13] = 0xFF; // Y = 0xFFFE
        r[0x14] = 0xE8; r[0x15] = 0x03; // Rauheit 1000
        r[0x16] = 0x2C; r[0x17] = 0x01; // Abstand 300
        Assert.True(InputReports.TryParseReport05(r, out var joyCon, ControllerKind.JoyCon2Right));
        Assert.Equal(new OpticalMouse(0x1234, 0xFFFE, 1000, 300), joyCon.Mouse);
        Assert.True(joyCon.Mouse!.Value.OnSurface);
        Assert.True(InputReports.TryParseReport05(r, out var pro, ControllerKind.Pro2));
        Assert.Null(pro.Mouse);
    }

    [Fact]
    public void Zaehler_ueberlauf_und_Oberflaeche()
    {
        Assert.Equal(3, OpticalMouse.Delta(0xFFFE, 0x0001));
        Assert.Equal(-2, OpticalMouse.Delta(0x0001, 0xFFFF));
        Assert.False(new OpticalMouse(0, 0, 100, 0).OnSurface);      // Abstand 0 = kein Signal
        Assert.False(new OpticalMouse(0, 0, 100, 2000).OnSurface);   // angehoben
        Assert.False(new OpticalMouse(0, 0, 5000, 300).OnSurface);   // zu rau / unklar
    }

    [Fact]
    public void Feature_Maske_je_Controller()
    {
        Assert.Equal(0x37, Commands.FeatureMask(ControllerKind.JoyCon2Left));
        Assert.Equal(0x2F, Commands.FeatureMask(ControllerKind.Pro2));
    }
}

public class ChargingGripTests
{
    [Fact]
    public void Griff_wird_an_seiner_Kennung_erkannt()
    {
        // Antwort aus der Protokolldoku (dort per USB, Transport 0x00): Seriennummer HDL5000348551 9, VID 057E, PID 2068.
        var reply = Commands.Hex("08 01 00 01 00 f8 00 00 00 00 00 00 01 00 48 44 4c 35 30 30 30 33 34 38 35 35 31 39 00 00 7e 05 68 20 01 03 01 ff ff ff ff ff ff ff");
        Assert.True(Commands.IsInGrip(reply));
        var empty = Commands.Hex("08 01 01 01 10 78 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00");
        Assert.False(Commands.IsInGrip(empty));
        Assert.Equal(Commands.Hex("08 91 01 02 00 04 00 00 01 00 00 00"), Commands.EnableGripButtons(true));
    }
}

public class DsuTests
{
    [Fact]
    public void Crc32_Pruefwert() =>
        Assert.Equal(0xCBF43926u, Dsu.Crc32("123456789"u8)); // Standard-Prüfwert für CRC-32

    [Fact]
    public void Datenpaket_Aufbau_und_Pruefsumme()
    {
        var g = new GamepadState(XButtons.A | XButtons.Up, 0, 255, 32767, 0, 0, 0, false);
        var p = new PadInput { Motion = new Motion(0, 0, 4096, 32767, 0, 0), BatteryPercent = 80 };
        var packet = Dsu.PadData(42, 1, 0xAABBCCDDEEFF, 7, g, p, 123456);
        Assert.Equal(100, packet.Length);
        Assert.Equal("DSUS"u8.ToArray(), packet[..4]);
        Assert.Equal(84, BitConverter.ToUInt16(packet, 6));      // Länge ohne 16-Byte-Kopf
        Assert.Equal(Dsu.MsgPadData, BitConverter.ToUInt32(packet, 16));
        Assert.Equal(1, packet[20]);                             // Slot
        Assert.Equal(0xAA, packet[24]);                          // MAC, höchstes Byte zuerst
        Assert.Equal(0x10, packet[36] & 0x10);                   // Steuerkreuz hoch
        Assert.Equal(0x40, packet[37] & 0x40);                   // Kreuz (A)
        Assert.Equal(255, packet[40]);                           // linker Stick ganz rechts
        Assert.Equal(1f, BitConverter.ToSingle(packet, 80), 3);  // Beschleunigung Y = 1 g (liegt flach)
        Assert.Equal(2000f, BitConverter.ToSingle(packet, 88), 1); // Nicken 2000 °/s

        // CRC prüfen: Feld nullen und neu rechnen
        uint crc = BitConverter.ToUInt32(packet, 8);
        var copy = (byte[])packet.Clone();
        copy[8] = copy[9] = copy[10] = copy[11] = 0;
        Assert.Equal(crc, Dsu.Crc32(copy));
    }

    [Fact]
    public void Client_Anfrage_wird_erkannt()
    {
        // Port-Info-Anfrage für Slot 0 und 1, wie Cemu sie sendet
        var body = new byte[] { 0x01, 0x00, 0x10, 0x00, 2, 0, 0, 0, 0, 1 };
        var p = new byte[16 + body.Length];
        "DSUC"u8.CopyTo(p);
        BitConverter.GetBytes((ushort)1001).CopyTo(p, 4);
        BitConverter.GetBytes((ushort)body.Length).CopyTo(p, 6);
        body.CopyTo(p, 16);
        BitConverter.GetBytes(Dsu.Crc32(p)).CopyTo(p, 8);
        Assert.Equal(Dsu.MsgPortInfo, Dsu.ParseRequest(p, out var rest));
        Assert.Equal(new[] { 0, 1 }, Dsu.RequestedSlots(rest));
    }

    [Fact]
    public void SingleJoyConIsRememberedPerAddress()
    {
        var s = new Settings();
        Assert.False(s.IsSingleJoyCon("AA:BB:CC:DD:EE:FF"));
        Assert.True(s.SetSingleJoyCon("AA:BB:CC:DD:EE:FF", true));
        Assert.False(s.SetSingleJoyCon("aa:bb:cc:dd:ee:ff", true)); // schon gemerkt
        Assert.True(s.IsSingleJoyCon("aa:bb:cc:dd:ee:ff"));
        Assert.False(s.IsSingleJoyCon(null));
        Assert.True(s.SetSingleJoyCon("AA:BB:CC:DD:EE:FF", false));
        Assert.Empty(s.SingleJoyCons);
    }

    [Fact]
    public void ShiftLayerChangesOtherButtonsWhileHeld()
    {
        var s = new Settings
        {
            Profiles = new() { [ControllerKind.Pro2] = new() { [ProButtons.GL] = "Shift", [ProButtons.GR] = "MouseLeft" } },
            ShiftProfiles = new() { [ControllerKind.Pro2] = new() { [ProButtons.A] = "Key:Win+Print" } },
        };
        var a = new PadInput { Kind = ControllerKind.Pro2, Buttons = ProButtons.A };
        var normal = Mapping.Evaluate(a, s);
        Assert.Equal(XButtons.B, normal.Gamepad.Buttons); // Xbox-Belegung: Nintendo A (rechts) = Xbox B
        Assert.Empty(normal.Keys);

        var shifted = Mapping.Evaluate(a with { Buttons = ProButtons.A | ProButtons.GL }, s);
        Assert.True(shifted.ShiftActive);
        Assert.Equal(XButtons.None, shifted.Gamepad.Buttons); // A hat auf der Shift-Ebene nur den Hotkey
        Assert.Contains("Win+Print", shifted.Keys);

        var click = Mapping.Evaluate(a with { Buttons = ProButtons.GR }, s);
        Assert.Equal(SpecialAction.MouseLeft, click.Specials);
    }

    [Fact]
    public void NamedProfileIsUsedWhenItsProgramIsActive()
    {
        var s = new Settings
        {
            NamedProfiles = [new NamedProfile { Name = "Cemu", Programs = ["Cemu.exe"], Buttons = new() { [ControllerKind.Pro2] = new() { [ProButtons.C] = "GyroMouse" } } }],
        };
        Assert.Equal(SpecialAction.None, Mapping.ActionFor(ProButtons.C, ControllerKind.Pro2, s).Special);
        s.DetectedProfile = s.NamedProfiles.FirstOrDefault(p => p.MatchesProgram("cemu.EXE"))?.Name;
        Assert.Equal(SpecialAction.GyroMouse, Mapping.ActionFor(ProButtons.C, ControllerKind.Pro2, s).Special);
        s.ForcedProfile = ""; // fest „Standard“
        Assert.Equal(SpecialAction.None, Mapping.ActionFor(ProButtons.C, ControllerKind.Pro2, s).Special);
    }

    [Fact]
    public void ButtonActionRoundTrips()
    {
        foreach (var text in new[] { "A", "LT", "None", "Key:Ctrl+Shift+S", "MouseRight", "GyroMouseToggle", "Shift" })
            Assert.Equal(text, ButtonAction.Parse(text).ToString());
        Assert.Equal(ButtonAction.Nothing, ButtonAction.Parse("42"));
        Assert.Equal(ButtonAction.Nothing, ButtonAction.Parse("Unsinn"));
    }

    [Fact]
    public void DeadzoneCanBeSetPerControllerKind()
    {
        var s = new Settings { StickDeadzone = 0.05f, Deadzones = new() { [ControllerKind.JoyConPair] = 0.3f } };
        var p = new PadInput { Kind = ControllerKind.JoyConPair, LeftX = 0.2f };
        Assert.Equal(0, Mapping.Evaluate(p, s).Gamepad.LeftX);                      // innerhalb 0,3
        Assert.NotEqual(0, Mapping.Evaluate(p with { Kind = ControllerKind.Pro2 }, s).Gamepad.LeftX); // allgemeine 0,05
    }

    [Fact]
    public void PairGyroSourceIsSelectable()
    {
        var left = new ControllerState { Kind = ControllerKind.JoyCon2Left, Motion = new Motion(0, 0, 0, 100, 0, 0) };
        var right = new ControllerState { Kind = ControllerKind.JoyCon2Right, Motion = new Motion(0, 0, 0, 200, 0, 0) };
        var cal = new DeviceCalibration();
        Assert.Equal(200, Mapping.Merge(left, cal, right, cal).Motion!.Value.GyroX);
        Assert.Equal(100, Mapping.Merge(left, cal, right, cal, GyroSource.Left).Motion!.Value.GyroX);
    }

    [Fact]
    public void GyroCalibrationIsStoredPerAddressAndSurvivesJson()
    {
        var s = new Settings { GyroCalibration = new() { ["AA:BB:CC:DD:EE:FF"] = new GyroBias(1.5f, -2f, 3f) } };
        var path = Path.Combine(Path.GetTempPath(), $"s2p_{Guid.NewGuid():N}.json");
        try
        {
            s.Save(path);
            var loaded = Settings.Load(path);
            Assert.Equal(new GyroBias(1.5f, -2f, 3f), loaded.GyroBiasFor("aa:bb:cc:dd:ee:ff", default));
            Assert.Equal(new GyroBias(9, 9, 9), loaded.GyroBiasFor("11:22:33:44:55:66", new GyroBias(9, 9, 9)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UsbStartSequenceUsesControllerFeatureMask()
    {
        var seq = Commands.UsbStartSequence(0x2F).ToList();
        Assert.Equal(Commands.UsbInitSequence.Count, seq.Count);
        Assert.All(seq.Where(c => c[0] == 0x0C), c => Assert.Equal(0x2F, c[8]));
        Assert.All(seq, c => Assert.Equal(Commands.TransportUsb, c[2]));
        Assert.Equal(0x27, Commands.UsbInitSequence.First(c => c[0] == 0x0C)[8]); // Vorlage unverändert
    }

    [Fact]
    public void GameCubeMotorSpreadsStrengthOverTime()
    {
        var motor = new Rumble.GameCubeMotor();
        int on = Enumerable.Range(0, 100).Count(i => motor.NextReport(0.25f, i)[2] == 1);
        Assert.InRange(on, 24, 26);
        var stop = motor.NextReport(0f, 0);
        Assert.Equal(0x03, stop[0]);
        Assert.Equal(2, stop[2]);
    }

    [Fact]
    public void UsbReportIsBluetoothReportWithReportId()
    {
        // USB-Bericht = Report-ID + Bluetooth-Bericht 0x05 (gleiche Offsets +1, vgl. SDL: Tasten ab Byte 5).
        var ble = new byte[63];
        ble[4] = 0x08; // A
        var usb = new byte[64];
        usb[0] = 0x09;
        ble.CopyTo(usb, 1);
        Assert.True(InputReports.TryParseReport05(usb.AsSpan(1), out var st));
        Assert.True(st.Has(ProButtons.A));
    }

    [Fact]
    public void ProfileExportImportRoundTrips()
    {
        var p = new NamedProfile
        {
            Name = "Zelda", Programs = ["Cemu.exe"],
            Buttons = new() { [ControllerKind.Pro2] = new() { [ProButtons.GL] = "Shift", [ProButtons.C] = "Key:Win+Print" } },
            ShiftButtons = new() { [ControllerKind.JoyConPair] = new() { [ProButtons.A] = "GyroMouseToggle" } },
        };
        var json = p.ToJson();
        Assert.Contains("\"Pro2\"", json); // lesbar: Namen statt Zahlen
        var back = NamedProfile.FromJson(json)!;
        Assert.Equal("Zelda", back.Name);
        Assert.Equal(["Cemu.exe"], back.Programs);
        Assert.Equal("Key:Win+Print", back.Buttons[ControllerKind.Pro2][ProButtons.C]);
        Assert.Equal("GyroMouseToggle", back.ShiftButtons[ControllerKind.JoyConPair][ProButtons.A]);
        Assert.Null(NamedProfile.FromJson("kein json"));
        Assert.Null(NamedProfile.FromJson("{\"Name\": \"\"}"));
    }

    [Fact]
    public void MacroScriptParsesStepsAndTiming()
    {
        Assert.True(MacroScript.TryParse("A 80, Pause 40, Down+B 100ms, Key:Ctrl+C 50, X", out var m));
        Assert.Equal(5, m.Steps.Count);
        Assert.Equal(80 + 40 + 100 + 50 + MacroScript.DefaultStepMs, m.TotalMs);
        Assert.Equal([ExtraButtonTarget.A], m.StepAt(0)!.Buttons);
        Assert.Empty(m.StepAt(100)!.Buttons);                                   // Pause
        Assert.Equal([ExtraButtonTarget.Down, ExtraButtonTarget.B], m.StepAt(130)!.Buttons);
        Assert.Equal("Ctrl+C", m.StepAt(225)!.Keys);
        Assert.Null(m.StepAt(m.TotalMs));                                       // zu Ende
        Assert.False(MacroScript.TryParse("Q 50", out _));
        Assert.False(MacroScript.TryParse("A 0", out _));
        Assert.False(MacroScript.TryParse("A 99999", out _));
        Assert.False(MacroScript.TryParse("", out _));
    }

    [Fact]
    public void TurboAndMacroActionsRoundTrip()
    {
        foreach (var text in new[] { "Turbo:A", "Turbo:Key:Space", "Macro:A 80, Pause 40, A 80", "GyroStick", "GyroStickToggle" })
            Assert.Equal(text, ButtonAction.Parse(text).ToString());
        Assert.Equal(ButtonAction.Nothing, ButtonAction.Parse("Turbo:Shift"));   // Sonderaktion nicht als Turbo
        Assert.Equal(ButtonAction.Nothing, ButtonAction.Parse("Turbo:Turbo:A"));
        Assert.Equal(ButtonAction.Nothing, ButtonAction.Parse("Macro:Unsinn 50"));
    }

    [Fact]
    public void TurboAlternatesAndMacrosAreReported()
    {
        var s = new Settings
        {
            TurboRate = 10, // Halbperiode 50 ms
            Profiles = new() { [ControllerKind.Pro2] = new() { [ProButtons.A] = "Turbo:A", [ProButtons.C] = "Macro:X 50" } },
            Layout = FaceButtonLayout.Switch2,
        };
        var p = new PadInput { Kind = ControllerKind.Pro2, Buttons = ProButtons.A | ProButtons.C };
        Assert.Equal(XButtons.A, Mapping.Evaluate(p, s, nowMs: 1000).Gamepad.Buttons);
        Assert.Equal(XButtons.None, Mapping.Evaluate(p, s, nowMs: 1060).Gamepad.Buttons);
        Assert.Equal(XButtons.A, Mapping.Evaluate(p, s, nowMs: 1110).Gamepad.Buttons);
        Assert.Equal(["X 50"], Mapping.Evaluate(p, s, nowMs: 1000).Macros);
    }

    [Fact]
    public void TurboStillAlternatesAfterLongUptime()
    {
        var s = new Settings
        {
            TurboRate = 12,
            Profiles = new() { [ControllerKind.Pro2] = new() { [ProButtons.A] = "Turbo:A" } },
        };
        var p = new PadInput { Kind = ControllerKind.Pro2, Buttons = ProButtons.A };
        long start = 30L * 24 * 3600 * 1000; // 30 Tage Laufzeit
        int on = 0;
        for (int ms = 0; ms < 1000; ms += 5)
            if (Mapping.Evaluate(p, s, start + ms).Gamepad.Buttons != XButtons.None)
                on++;
        Assert.InRange(on, 90, 110); // etwa die Hälfte der 200 Proben
    }

    [Fact]
    public void AmiiboPacketsOutOfOrderAndDuplicates()
    {
        byte[] Packet(byte n, int length, byte fill)
        {
            var d = new byte[320];
            d[1] = 0x07; d[2] = n;
            int payload = n == 1 ? length + 60 : length;
            d[4] = (byte)(payload >> 8); d[5] = (byte)payload;
            int offset = n == 1 ? 66 : 6;
            for (int i = 0; i < length; i++) d[offset + i] = fill;
            return d;
        }
        var asm = new AmiiboAssembler();
        Assert.True(asm.Add(Nfc.ReportNfcRead, Packet(2, 295, 0xBB)));
        Assert.False(asm.Complete);                                   // Paket 1 fehlt noch
        Assert.True(asm.Add(Nfc.ReportNfcRead, Packet(1, 245, 0xAA)));
        Assert.False(asm.Add(Nfc.ReportNfcRead, Packet(1, 245, 0xCC))); // doppelt: ignoriert
        Assert.True(asm.Complete);
        var data = asm.ToArray();
        Assert.Equal(0xAA, data[0]);
        Assert.Equal(0xAA, data[244]);
        Assert.Equal(0xBB, data[245]);
    }

    [Fact]
    public void AnalogTriggerUsesDeadzoneAndFullPoint()
    {
        Assert.Equal(0, Mapping.AnalogTrigger(0.04f, 0.05f, 1f));
        Assert.Equal(255, Mapping.AnalogTrigger(0.8f, 0.05f, 0.8f));
        Assert.InRange(Mapping.AnalogTrigger(0.425f, 0.05f, 0.8f), 125, 130); // Mitte des Bereichs
    }

    [Fact]
    public void StickCurveMakesCenterFiner()
    {
        var (linear, _) = Mapping.Stick(0.5f, 0f, 0f, 1f);
        var (fine, _) = Mapping.Stick(0.5f, 0f, 0f, 2f);
        var (full, _) = Mapping.Stick(1f, 0f, 0f, 2f);
        Assert.InRange(linear, 16000, 16800);
        Assert.InRange(fine, 8000, 8400);    // 0,5² = 0,25
        Assert.Equal(32767, full);           // Vollausschlag bleibt
    }

    [Fact]
    public void GyroStickTurnsRotationIntoStickDeflection()
    {
        var s = new Settings { GyroStickFullSpeed = 100, GyroStickAntiDeadzone = 0.1f };
        short Raw(float dps) => (short)(dps * 32767f / 2000f);
        Assert.Equal((0, 0), Mapping.GyroToStick(new Motion(0, 0, 0, 0, 0, 0), s, 0, 0));       // Ruhe
        var (x, _) = Mapping.GyroToStick(new Motion(0, 0, 0, 0, 0, Raw(-50)), s, 0, 0);          // nach rechts drehen
        Assert.InRange(x / 32767f, 0.53f, 0.57f);                                               // 0,1 + 0,9 × 0,5
        var (_, y) = Mapping.GyroToStick(new Motion(0, 0, 0, Raw(400), 0, 0), s, 0, 0);          // nach oben kippen, schnell
        Assert.Equal(32767, y);
        var (sx, _) = Mapping.GyroToStick(new Motion(0, 0, 0, 0, 0, Raw(-50)), s, 32767, 0);    // plus echter Stick: begrenzt
        Assert.Equal(32767, sx);
    }

    [Fact]
    public void NsoControllersAreDetectedByDeviceType()
    {
        Assert.Equal(ControllerKind.NesController, ControllerKinds.FromSwitch1DeviceType(0x09, ControllerKind.JoyCon1Right));
        Assert.Equal(ControllerKind.SnesController, ControllerKinds.FromSwitch1DeviceType(0x0B, ControllerKind.Unknown));
        Assert.Equal(ControllerKind.N64Controller, ControllerKinds.FromSwitch1DeviceType(0x0C, ControllerKind.Unknown));
        Assert.Equal(ControllerKind.MegaDrive, ControllerKinds.FromSwitch1DeviceType(0x0D, ControllerKind.Unknown));
        Assert.Equal(ControllerKind.JoyCon1Right, ControllerKinds.FromSwitch1DeviceType(0x02, ControllerKind.JoyCon1Right));
    }

    [Fact]
    public void N64CButtonsBecomeRightStickAndAIsBottom()
    {
        // Rohbits (hid-nintendo): A = A, Y = C-hoch, − = C-rechts, linker Stickklick = ZR.
        var raw = new ControllerState
        {
            Kind = ControllerKind.N64Controller,
            Buttons = ProButtons.A | ProButtons.Y | ProButtons.Minus | ProButtons.LeftStick,
            LeftX = 2048, LeftY = 2048, RightX = 0, RightY = 0,
        };
        var p = Mapping.Normalize(raw, new DeviceCalibration());
        Assert.Equal(1f, p.RightX);
        Assert.Equal(1f, p.RightY);
        Assert.True(p.Has(ProButtons.B));        // N64 A sitzt unten
        Assert.True(p.Has(ProButtons.ZR));
        Assert.False(p.Has(ProButtons.Minus));
        var g = Mapping.ToGamepad(p, new Settings { Layout = FaceButtonLayout.Switch2 });
        Assert.True(g.Buttons.HasFlag(XButtons.A)); // nach Position, auch bei Nintendo-Belegung
        Assert.Equal(255, g.RightTrigger);
    }

    [Fact]
    public void MegaDriveButtonsByPosition()
    {
        var raw = new ControllerState { Kind = ControllerKind.MegaDrive, Buttons = ProButtons.A | ProButtons.R | ProButtons.ZR };
        var p = Mapping.Normalize(raw, new DeviceCalibration());
        Assert.True(p.Has(ProButtons.Y));    // MD A links
        Assert.True(p.Has(ProButtons.A));    // MD C rechts
        Assert.True(p.Has(ProButtons.Minus)); // MODE
        Assert.Equal(0f, p.LeftX);           // kein Stick
    }

    [Fact]
    public void WiiRemoteSidewaysAndNunchuk()
    {
        // 0x35: Tasten (Hoch + 2), Beschleunigung, Nunchuk (Stick rechts, Z gedrückt)
        var r = new byte[22];
        r[0] = 0x35; r[1] = 0x08; r[2] = 0x01;
        r[3] = 0x80; r[4] = 0x80; r[5] = 0x80 + 26;
        r[6] = 0xE0; r[7] = 0x80; r[11] = 0x02; // C nicht gedrückt (1), Z gedrückt (0)
        Assert.True(Wii.TryParseData(r, WiiExtension.None, 80, out var alone));
        Assert.True(alone.Has(ProButtons.Left));  // quer: Hoch → Links
        Assert.True(alone.Has(ProButtons.B));     // 2
        Assert.Equal(4096, alone.Motion!.Value.AccelZ);
        Assert.True(Wii.TryParseData(r, WiiExtension.Nunchuk, 80, out var nun));
        Assert.True(nun.Has(ProButtons.Up));
        Assert.True(nun.Has(ProButtons.ZL));
        Assert.False(nun.Has(ProButtons.L));
        Assert.Equal(0xE0 << 4, nun.LeftX);
    }

    [Fact]
    public void WiiUProParsesSticksButtonsBattery()
    {
        var e = new byte[11];
        void U12(int o, int v) { e[o] = (byte)v; e[o + 1] = (byte)(v >> 8); }
        U12(0, 3000); U12(2, 2048); U12(4, 1000); U12(6, 2048);
        e[8] = 0xFF & ~0x10; // Minus gedrückt
        e[9] = 0xFF & ~0x10; // A gedrückt
        e[10] = 0x40 | 0x01 | 0x08; // Akku-Stufe 4, rechter Stick nicht, linker Stick gedrückt (Bit1 = 0), lädt (Bit2 = 0)
        Assert.True(Wii.TryParseWiiUPro(e, out var s));
        Assert.Equal(3000, s.LeftX);
        Assert.Equal(1000, s.LeftY);
        Assert.True(s.Has(ProButtons.Minus));
        Assert.True(s.Has(ProButtons.A));
        Assert.True(s.Has(ProButtons.LeftStick));
        Assert.False(s.Has(ProButtons.RightStick));
        Assert.Equal(100, s.BatteryPercent);
        Assert.True(s.Charging);
    }

    [Fact]
    public void WiiExtensionIdsAndReports()
    {
        Assert.Equal(WiiExtension.Nunchuk, Wii.ExtensionFromId([0, 0, 0xA4, 0x20, 0, 0]));
        Assert.Equal(WiiExtension.Classic, Wii.ExtensionFromId([1, 0, 0xA4, 0x20, 1, 1]));
        Assert.Equal(WiiExtension.WiiUPro, Wii.ExtensionFromId([0, 0, 0xA4, 0x20, 1, 0x20]));
        Assert.Equal(WiiExtension.Other, Wii.ExtensionFromId([0, 0, 0xA4, 0x20, 4, 5]));
        Assert.Equal([0x11, 0x21], Wii.Leds(1, true));                 // Spieler 2, Vibration an
        Assert.Equal([0x12, 0x04, 0x35], Wii.SetMode(0x35, false));
        var w = Wii.WriteRegister(0xA400F0, [0x55], false);
        Assert.Equal([0x16, 0x04, 0xA4, 0x00, 0xF0, 0x01, 0x55], w[..7]);
        Assert.True(Wii.TryParseStatus([0x20, 0, 0, 0x02, 0, 0, 200], out bool ext, out int bat));
        Assert.True(ext);
        Assert.Equal(100, bat);
    }

    [Fact]
    public void NfcCrcAndRequests()
    {
        Assert.Equal(0xF4, Nfc.Crc8("123456789"u8)); // Prüfwert CRC-8 (Polynom 0x07)
        var config = Nfc.McuConfig(Nfc.ModeNfc);
        Assert.Equal(38, config.Length);
        Assert.Equal([0x21, 0x00, 0x04], config[..3]);
        Assert.Equal(Nfc.Crc8(config.AsSpan(1, 36)), config[37]);
        var read = Nfc.ReadNtag215();
        Assert.Equal(Nfc.CmdReadNtag, read[0]);
        Assert.Equal(19, read[4]);                                 // Datenlänge
        Assert.Equal([0x03, 0x00, 0x3B, 0x3C, 0x77, 0x78, 0x86], read[15..22]);
        Assert.Equal(Nfc.Crc8(read.AsSpan(0, 36)), read[36]);
        var report = Nfc.McuReport(1, Nfc.McuReadDeviceMode, read, default);
        Assert.Equal(0x11, report[0]);
        Assert.Equal(0x02, report[10]);
        Assert.Equal(read, report[11..49]);
    }

    [Fact]
    public void NfcTagDetectionAndAssembly()
    {
        var found = new byte[60];
        found[0] = 0x00; found[1] = 0x05; found[5] = 0x31; found[6] = Nfc.StatusTagFound;
        found[14] = 7;
        new byte[] { 1, 2, 3, 4, 5, 6, 7 }.CopyTo(found, 15);
        Assert.True(Nfc.TryGetTag(Nfc.ReportNfcState, found, out var uid));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], uid);
        Assert.False(Nfc.TryGetTag(Nfc.ReportNfcRead, found, out _));

        var asm = new AmiiboAssembler();
        var first = new byte[320];
        first[1] = 0x07; first[2] = 0x01; first[4] = (60 + 245) >> 8; first[5] = (60 + 245) & 0xFF;
        for (int i = 0; i < 245; i++) first[66 + i] = (byte)i;
        var second = new byte[320];
        second[1] = 0x07; second[2] = 0x02; second[4] = 295 >> 8; second[5] = 295 & 0xFF;
        for (int i = 0; i < 295; i++) second[6 + i] = (byte)(i + 7);
        Assert.True(asm.Add(Nfc.ReportNfcRead, first));
        Assert.False(asm.Complete);
        Assert.True(asm.Add(Nfc.ReportNfcRead, second));
        Assert.True(asm.Complete);
        var data = asm.ToArray();
        Assert.Equal(540, data.Length);
        Assert.Equal(244, data[244]);
        Assert.Equal(7, data[245]);
    }

    [Fact]
    public void IrCameraRequestsAndAssembly()
    {
        var cfg = IrCamera.Configure(IrResolution.Size40x30);
        Assert.Equal([0x23, 0x01, 0x07, 0x03, 0x00, 0x05, 0x00, 0x18], cfg[..8]);
        Assert.Equal(Nfc.Crc8(cfg.AsSpan(1, 36)), cfg[37]);
        var regs = IrCamera.RegistersStep1(IrResolution.Size40x30);
        Assert.Equal([0x23, 0x04, 9, 0x00, 0x2E, 0x69], regs[..6]);   // Auflösung 40×30
        var ack = IrCamera.Acknowledge(2);
        Assert.Equal(2, ack[3]);
        Assert.Equal(0xFF, ack[37]);

        var asm = new IrFrameAssembler(IrResolution.Size40x30);
        byte[] Fragment(byte n)
        {
            var r = new byte[362];
            r[0] = 0x31; r[49] = 0x03; r[52] = n;
            for (int i = 0; i < 300; i++) r[59 + i] = (byte)(n * 10);
            return r;
        }
        byte[]? image = null;
        for (byte n = 0; n <= 3; n++)
        {
            var (request, done) = asm.Handle(Fragment(n));
            Assert.Equal(n, request[3]);           // Quittung für das erhaltene Stück
            image = done ?? image;
        }
        Assert.NotNull(image);
        Assert.Equal(1200, image!.Length);
        Assert.Equal(30, image[3 * 300]);
        var (resend, _) = asm.Handle(Fragment(2));  // Stück außer der Reihe → Neuanforderung des erwarteten
        Assert.Equal(0x01, resend[1]);
        Assert.Equal(0, resend[2]);
    }

    [Fact]
    public void RingConCalibratesRestPositionFirst()
    {
        var ring = new RingFlexCalibration();
        for (int i = 0; i < 29; i++)
            Assert.Null(ring.Update(1000));
        Assert.Null(ring.Update(1000));             // 30. Wert: Ruhelage fertig
        Assert.Equal(0f, ring.Update(1010));        // kleine Abweichung = Ruhe
        Assert.Equal(1f, ring.Update(1600));        // +600 = voll gedrückt
        Assert.Equal(-0.5f, ring.Update(700));
        var p = Mapping.Normalize(new ControllerState { Kind = ControllerKind.JoyCon1Right, RingFlex = 0.75f }, new DeviceCalibration());
        Assert.Equal(0.75f, p.RightTrigger);
        Assert.Equal(0f, p.LeftTrigger);
    }

    [Fact]
    public void GameCubeIgnoresLegacyRemap()
    {
        var s = new Settings(); // Standard-Remap enthält C = None (Pro Controller)
        Assert.Equal(ExtraButtonTarget.Back, Mapping.ActionFor(ProButtons.C, ControllerKind.GameCube2, s).Target);
        Assert.Equal(ExtraButtonTarget.None, Mapping.ActionFor(ProButtons.C, ControllerKind.Pro2, s).Target);
    }
}
