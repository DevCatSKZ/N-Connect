using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

public class AdvertisementTests
{
    // Herstellerdaten aus bluetooth_interface.md, ohne die Kennung 53 05.
    [Theory]
    [InlineData("01 00 03 7e 05 69 20 00 01 00 00 00 00 00 00 00 00 00 0f 00 00 00 00 00 00 00", true)]
    [InlineData("01 00 03 7e 05 69 20 00 01 00 5f 11 85 eb f1 48 0f 00 00 00 00 00 00 00", true)]
    [InlineData("01 00 03 7e 05 66 20 00 01 00 00 00 00 00 00 00 00 00 0f 00 00 00 00 00 00 00", false)] // Joy-Con
    [InlineData("01 00 03 7e 05", false)]
    public void Erkennt_ProController2(string hex, bool expected)
    {
        Assert.Equal(expected, Advertisement.IsProController2(0x0553, Commands.Hex(hex)));
    }

    [Theory]
    [InlineData("01 00 03 7e 05 69 20 00 01 00 00 00 00 00 00 00 0f 00 00 00 00 00 00 00", true)]  // SYNC
    [InlineData("01 00 03 7e 05 69 20 00 01 00 5f 11 85 eb f1 48 0f 00 00 00 00 00 00 00", false)] // Wiederverbinden
    [InlineData("01 00 03 7e 05 69 20 00 01 81 5f 11 85 eb f1 48 0f 00 00 00 00 00 00 00", false)] // Konsole wecken
    public void Erkennt_SYNC_Modus(string hex, bool expected) =>
        Assert.Equal(expected, Advertisement.IsSyncMode(Commands.Hex(hex)));

    [Fact]
    public void Andere_Hersteller_werden_ignoriert() =>
        Assert.False(Advertisement.IsProController2(0x004C, Commands.Hex("01 00 03 7e 05 69 20")));
}

public class CommandTests
{
    [Fact]
    public void Speicher_lesen_wie_im_Mitschnitt() =>
        Assert.Equal(Commands.Hex("02 91 01 04 00 08 00 00 09 7E 00 00 A8 30 01 00"),
            Commands.ReadMemory(Commands.AddrLeftStickCalibration, 9));

    [Fact]
    public void Spieler_LED_wie_im_Mitschnitt() =>
        Assert.Equal(Commands.Hex("09 91 01 07 00 08 00 00 01 00 00 00 00 00 00 00"),
            Commands.SetPlayerLeds(Commands.PlayerLedMask(0)));

    [Fact]
    public void Verbindungs_Vibration_wie_im_Mitschnitt() =>
        Assert.Equal(Commands.Hex("0a 91 01 02 00 04 00 00 03 00 00 00"), Commands.PlayConnectSample());

    [Fact]
    public void Init_Befehle_haben_stimmige_Laenge()
    {
        foreach (var cmd in Commands.InitSequence)
        {
            Assert.Equal(0x91, cmd[1]);
            Assert.Equal(cmd.Length - 8, cmd[5]);
        }
    }

    [Fact]
    public void Antwort_auf_Speicherlesen_wird_ausgewertet()
    {
        var response = Commands.Hex("02 01 01 04 10 78 00 00  09 00 00 00 A8 30 01 00  B3 67 83 2E 66 5E 3A 06 5F");
        Assert.True(Commands.TryParseMemoryRead(response, 0x130A8, out var data));
        Assert.Equal(Commands.Hex("B3 67 83 2E 66 5E 3A 06 5F"), data);
        Assert.False(Commands.TryParseMemoryRead(response, 0x130E8, out _));
    }
}

public class CalibrationTests
{
    [Fact]
    public void Werkskalibrierung_aus_der_Doku()
    {
        Assert.True(StickCalibration.TryParse(Commands.Hex("B3 67 83 2E 66 5E 3A 06 5F"), out var c));
        Assert.Equal(new AxisCalibration(0x7B3, 0x62E, 0x63A), c.X);
        Assert.Equal(new AxisCalibration(0x836, 0x5E6, 0x5F0), c.Y);
        Assert.Equal(0f, c.X.Normalize(0x7B3));
        Assert.Equal(1f, c.X.Normalize(0x7B3 + 0x62E));
        Assert.Equal(-1f, c.X.Normalize(0x7B3 - 0x63A));
    }

    [Fact]
    public void Leere_Kalibrierung_wird_abgelehnt() =>
        Assert.False(StickCalibration.TryParse(Commands.Hex("FF FF FF FF FF FF FF FF FF"), out _));
}

public class InputReportTests
{
    private static byte[] Report05(byte b0 = 0, byte b1 = 0, byte b2 = 0, byte b3 = 0)
    {
        var r = new byte[0x3F];
        r[4] = b0; r[5] = b1; r[6] = b2; r[7] = b3;
        // Sticks in der Mitte: X = 0x800, Y = 0x800 → 00 08 80
        r[0x0A] = 0x00; r[0x0B] = 0x08; r[0x0C] = 0x80;
        r[0x0D] = 0x00; r[0x0E] = 0x08; r[0x0F] = 0x80;
        r[0x1F] = 0x68; r[0x20] = 0x10; // 4200 mV
        return r;
    }

    [Theory]
    [InlineData(0x08, 0, 0, 0, ProButtons.A)]
    [InlineData(0x04, 0, 0, 0, ProButtons.B)]
    [InlineData(0x80, 0, 0, 0, ProButtons.ZR)]
    [InlineData(0, 0x10, 0, 0, ProButtons.Home)]
    [InlineData(0, 0x20, 0, 0, ProButtons.Capture)]
    [InlineData(0, 0x40, 0, 0, ProButtons.C)]
    [InlineData(0, 0, 0x02, 0, ProButtons.Up)]
    [InlineData(0, 0, 0x80, 0, ProButtons.ZL)]
    [InlineData(0, 0, 0, 0x02, ProButtons.GL)]
    [InlineData(0, 0, 0, 0x01, ProButtons.GR)]
    public void Bericht05_Tasten(byte b0, byte b1, byte b2, byte b3, ProButtons expected)
    {
        Assert.True(InputReports.TryParseReport05(Report05(b0, b1, b2, b3), out var s));
        Assert.Equal(expected, s.Buttons);
        Assert.Equal(0x800, s.LeftX);
        Assert.Equal(0x800, s.RightY);
        Assert.Equal(4200, s.BatteryMillivolts);
        Assert.Equal(100, s.BatteryPercent);
        Assert.Null(s.Motion);
    }

    [Fact]
    public void Bericht05_Bewegung()
    {
        var r = Report05();
        r[0x30] = 0x00; r[0x31] = 0x10; // AccelX = 4096 (1 g)
        r[0x36] = 0x10; r[0x37] = 0x00; // GyroX = 16
        Assert.True(InputReports.TryParseReport05(r, out var s));
        Assert.Equal(new Motion(4096, 0, 0, 16, 0, 0), s.Motion);
    }

    [Fact]
    public void Bericht09_Tasten_und_Akku()
    {
        var r = new byte[0x3F];
        r[1] = (9 << 2) | 0x02; // voll, lädt
        r[2] = 0x02; r[3] = 0x08; r[4] = 0x08; // A, Hoch, GL
        Assert.True(InputReports.TryParseReport09(r, out var s));
        Assert.Equal(ProButtons.A | ProButtons.Up | ProButtons.GL, s.Buttons);
        Assert.Equal(100, s.BatteryPercent);
        Assert.True(s.Charging);
    }

    [Fact]
    public void Zu_kurze_Berichte_werden_abgelehnt()
    {
        Assert.False(InputReports.TryParseReport05(new byte[5], out _));
        Assert.False(InputReports.TryParseReport09(new byte[5], out _));
    }
}

public class RumbleTests
{
    /// <summary>Bitweise gleich wie SDL EncodeHDRumble(hf, ha &lt;&lt; 6, lf, la &lt;&lt; 6).</summary>
    [Fact]
    public void Frame_wie_SDL()
    {
        static byte[] Sdl(int hf, int ha16, int lf, int la16) =>
        [
            (byte)(hf & 0xFF),
            (byte)(((ha16 >> 4) & 0xFC) | ((hf >> 8) & 0x03)),
            (byte)((ha16 >> 12) | (lf << 4)),
            (byte)((la16 & 0xC0) | ((lf >> 4) & 0x3F)),
            (byte)(la16 >> 8),
        ];
        Assert.Equal(Sdl(0x187, 300 << 6, 0x112, 453 << 6), Rumble.EncodeFrame(0x187, 300, 0x112, 453));
        Assert.Equal(Sdl(0x187, 0, 0x112, 0), Rumble.EncodeFrame(0x187, 0, 0x112, 0));
    }

    [Fact]
    public void Paket_hat_Zaehler_auf_beiden_Seiten()
    {
        var p = Rumble.BuildPacket(255, 255, 3);
        Assert.Equal(Rumble.PacketSize, p.Length);
        Assert.Equal(0, p[0]);
        Assert.Equal(0x53, p[1]);
        Assert.Equal(0x53, p[17]);
        Assert.Equal(p[2..7], p[18..23]);
        Assert.Equal(Rumble.EncodeFrame(0x187, Rumble.MaxAmplitude, 0x112, Rumble.MaxAmplitude), p[2..7]);
    }

    [Fact]
    public void Usb_Bericht_wie_SDL()
    {
        var r = Rumble.BuildUsbReport(0, 255, 1, strength: 227f / Rumble.MaxAmplitude);
        Assert.Equal(64, r.Length);
        Assert.Equal(0x02, r[0]);
        Assert.Equal(0x51, r[1]);
        Assert.Equal(0x51, r[0x11]);
        Assert.Equal(Rumble.EncodeFrame(0x187, 227, 0x112, 0), r[2..7]);
    }

    [Fact]
    public void Stopp_Paket_ohne_Amplitude() =>
        Assert.Equal(Rumble.EncodeFrame(0x187, 0, 0x112, 0), Rumble.StopPacket(0)[2..7]);
}

public class MappingTests
{
    private static readonly StickCalibration Cal = new(new AxisCalibration(2048, 1500, 1500), new AxisCalibration(2048, 1500, 1500));

    [Fact]
    public void Xbox_Belegung_tauscht_A_und_B_Switch2_Belegung_nicht()
    {
        var s = new ControllerState { Buttons = ProButtons.B | ProButtons.X };
        var g = Mapping.ToGamepad(s, new Settings(), Cal, Cal);
        Assert.Equal(XButtons.A | XButtons.Y, g.Buttons);

        g = Mapping.ToGamepad(s, new Settings { Layout = FaceButtonLayout.Switch2 }, Cal, Cal);
        Assert.Equal(XButtons.B | XButtons.X, g.Buttons);
    }

    [Fact]
    public void Alle_Tasten_haben_eine_Standardbelegung()
    {
        var all = Enum.GetValues<ProButtons>().Aggregate(ProButtons.None, (a, b) => a | b);
        var g = Mapping.ToGamepad(new ControllerState { Buttons = all }, new Settings(), Cal, Cal);
        Assert.Equal((XButtons)0xF7FF, g.Buttons);
        Assert.Equal(255, g.LeftTrigger);
        Assert.Equal(255, g.RightTrigger);
    }

    [Fact]
    public void Freie_Umbelegung_ueberschreibt_Layout()
    {
        var settings = new Settings();
        settings.Remap[ProButtons.A] = ExtraButtonTarget.Y;
        settings.Remap[ProButtons.ZR] = ExtraButtonTarget.None;
        var g = Mapping.ToGamepad(new ControllerState { Buttons = ProButtons.A | ProButtons.ZR }, settings, Cal, Cal);
        Assert.Equal(XButtons.Y, g.Buttons);
        Assert.Equal(0, g.RightTrigger);
    }

    [Fact]
    public void Trigger_und_Zusatztasten()
    {
        var s = new ControllerState { Buttons = ProButtons.ZL | ProButtons.GR | ProButtons.Capture };
        var settings = new Settings();
        settings.Remap[ProButtons.GR] = ExtraButtonTarget.RT;
        var g = Mapping.ToGamepad(s, settings, Cal, Cal);
        Assert.Equal(255, g.LeftTrigger);
        Assert.Equal(255, g.RightTrigger);
        Assert.True(g.Touchpad);

        settings.Remap[ProButtons.GR] = ExtraButtonTarget.Back;
        Assert.Equal(XButtons.Back, Mapping.ToGamepad(s, settings, Cal, Cal).Buttons);
    }

    [Fact]
    public void Stick_Totzone_und_Vollausschlag()
    {
        Assert.Equal((0, 0), ((int, int))Mapping.Stick(2060, 2040, Cal, 0.06f));
        var (x, y) = Mapping.Stick(2048 + 1500, 2048, Cal, 0.06f);
        Assert.Equal(32767, x);
        Assert.Equal(0, y);
        (_, y) = Mapping.Stick(2048, 2048 - 1500, Cal, 0.06f);
        Assert.Equal(-32767, y);
    }

    [Fact]
    public void Ds4_Bericht_Grundzustand()
    {
        byte counter = 0;
        var r = Mapping.ToDs4Report(default, new PadInput(), 0, ref counter);
        Assert.Equal(63, r.Length);
        Assert.Equal(128, r[0]);
        Assert.Equal(128, r[1]);
        Assert.Equal(8, r[4] & 0x0F); // Steuerkreuz losgelassen
        Assert.Equal(0x80, r[34]);    // kein Finger auf dem Touchpad
    }

    [Fact]
    public void Ds4_Bericht_Steuerkreuz_und_Stick_oben()
    {
        byte counter = 0;
        var g = new GamepadState(XButtons.Up | XButtons.Right | XButtons.A, 0, 255, 0, 32767, 0, 0, false);
        var r = Mapping.ToDs4Report(g, new PadInput(), 0, ref counter);
        Assert.Equal(1, r[4] & 0x0F);         // oben rechts
        Assert.NotEqual(0, r[4] & (1 << 5));  // Kreuz
        Assert.NotEqual(0, r[5] & (1 << 3));  // R2
        Assert.Equal(255, r[8]);
        Assert.Equal(0, r[1]);                // DS4: oben = 0
    }

    [Fact]
    public void Ds4_Gyro_Umrechnung()
    {
        byte counter = 0;
        var s = new PadInput { Motion = new Motion(0, 0, 4096, 1000, 0, 0) };
        var r = Mapping.ToDs4Report(default, s, 0, ref counter);
        short gyroX = (short)(r[12] | (r[13] << 8));
        short accelY = (short)(r[20] | (r[21] << 8));
        Assert.InRange(gyroX, 975, 978);  // 1000 LSB ≈ 61 °/s ≈ 977 DS4-LSB
        Assert.Equal(8192, accelY);       // 1 g
    }
}

public class SettingsTests
{
    [Fact]
    public void Speichern_und_Laden()
    {
        var path = Path.Combine(Path.GetTempPath(), $"s2p-{Guid.NewGuid():N}", "settings.json");
        var s = new Settings { OutputMode = OutputMode.DualShock4, Layout = FaceButtonLayout.Switch2, RumbleStrength = 5f };
        s.Remap[ProButtons.GL] = ExtraButtonTarget.LS;
        s.Save(path);
        var loaded = Settings.Load(path);
        Assert.Equal(OutputMode.DualShock4, loaded.OutputMode);
        Assert.Equal(FaceButtonLayout.Switch2, loaded.Layout);
        Assert.Equal(ExtraButtonTarget.LS, loaded.Remap[ProButtons.GL]);
        Assert.Contains("\"GL\": \"LS\"", File.ReadAllText(path));
        Assert.Equal(1f, loaded.RumbleStrength);
        Assert.Contains("\"DualShock4\"", File.ReadAllText(path));
    }

    [Fact]
    public void Kaputte_Datei_liefert_Standardwerte()
    {
        var path = Path.Combine(Path.GetTempPath(), $"s2p-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ kaputt");
        var s = Settings.Load(path);
        Assert.NotNull(s.LoadError);
        Assert.Equal(OutputMode.Xbox360, s.OutputMode);
        Assert.Equal("{ kaputt", File.ReadAllText(path));
    }

    [Fact]
    public void Freigabeliste()
    {
        var s = new Settings();
        Assert.True(s.IsAllowed("AA:BB:CC:DD:EE:FF"));
        s.AllowedControllers.Add("aa:bb:cc:dd:ee:ff");
        Assert.True(s.IsAllowed("AA:BB:CC:DD:EE:FF"));
        Assert.False(s.IsAllowed("11:22:33:44:55:66"));
    }

    [Fact]
    public void Bekannte_Controller()
    {
        var s = new Settings { KnownControllers = ["aa:bb:cc:dd:ee:ff"] };
        Assert.True(s.IsKnown("AA:BB:CC:DD:EE:FF"));
        Assert.False(s.IsKnown("11:22:33:44:55:66"));
        var copy = new Settings();
        copy.CopyFrom(s);
        Assert.True(copy.IsKnown("AA:BB:CC:DD:EE:FF"));
        Assert.NotSame(s.KnownControllers, copy.KnownControllers);
    }
}
