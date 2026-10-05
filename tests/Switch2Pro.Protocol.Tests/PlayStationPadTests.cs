using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>Sony-Controller: Erkennung an VID/PID, DualShock-4-/DualSense-Berichte (USB + Bluetooth) und Ausgabepakete.</summary>
public class PlayStationPadTests
{
    // ---------- Erkennung ----------

    [Theory]
    [InlineData(@"\\?\hid#vid_054c&pid_05c4#7&1&0&0000#{4d1e55b2}", ControllerKind.DualShock4)]
    [InlineData(@"\\?\hid#vid_054c&pid_09cc#7&1&0&0000#{4d1e55b2}", ControllerKind.DualShock4)]
    [InlineData(@"\\?\hid#vid_054c&pid_0ba0#7&1&0&0000#{4d1e55b2}", ControllerKind.DualShock4)]
    [InlineData(@"\\?\hid#vid_054c&pid_0ce6#7&1&0&0000#{4d1e55b2}", ControllerKind.DualSense)]
    [InlineData(@"\\?\hid#vid_054c&pid_0df2#7&1&0&0000#{4d1e55b2}", ControllerKind.DualSense)]
    // Bluetooth-Classic-HID-Pfade (VID eingebettet).
    [InlineData(@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&09cc#9&1&0&0000#{4d1e55b2}", ControllerKind.DualShock4)]
    [InlineData(@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#9&1&0&0000#{4d1e55b2}", ControllerKind.DualSense)]
    public void Erkennung_SonyPids(string path, ControllerKind kind) =>
        Assert.Equal(kind, PlayStationPad.KindFromHidPath(path));

    [Theory]
    [InlineData(@"\\?\hid#vid_045e&pid_02ea#x")]    // Xbox
    [InlineData(@"\\?\hid#vid_057e&pid_2009#x")]    // Nintendo
    [InlineData(@"\\?\hid#vid_054c&pid_0268#x")]    // DualShock 3: nicht unterstützt
    [InlineData(@"\\?\hid#vid_054c#x")]             // Sony-Gerät ohne Controller-PID
    public void Erkennung_AndereGeraete(string path) => Assert.Null(PlayStationPad.KindFromHidPath(path));

    [Fact]
    public void Erkennung_BluetoothPfad() =>
        Assert.True(PlayStationPad.IsBluetoothHidPath(@"\\?\hid#vid&0002054c_pid&0ce6#x"));

    // ---------- DualShock 4 ----------

    /// <summary>USB-Eingabebericht eines DualShock 4 (Report-ID 0x01, 64 Byte).</summary>
    private static byte[] Ds4Usb(byte b0 = 0x08, byte b1 = 0, byte b2 = 0, byte lt = 0, byte rt = 0,
        byte lx = 128, byte ly = 128, byte rx = 128, byte ry = 128, int battery = -1)
    {
        var r = new byte[64];
        r[0] = 0x01;
        r[1] = lx; r[2] = ly; r[3] = rx; r[4] = ry;
        r[5] = b0; r[6] = b1; r[7] = b2;
        r[8] = lt; r[9] = rt;
        // Daten-Payload verschiebt sich um 1 (Report-ID): Tasten bei p[4..6] = r[5..7], Trigger r[8..9], Akku p[29] = r[30].
        if (battery >= 0)
            r[30] = (byte)battery;
        return r;
    }

    [Fact]
    public void Ds4_Ruhezustand()
    {
        Assert.True(PlayStationPad.TryParseDualShock4(Ds4Usb(), out var s));
        Assert.Equal(ControllerKind.DualShock4, s.Kind);
        Assert.Equal(ProButtons.None, s.Buttons);
        Assert.InRange(s.LeftX, 2008, 2088);
        Assert.InRange(s.LeftY, 2008, 2088);
        Assert.Equal(0, s.LeftTrigger);
        Assert.Equal(0, s.RightTrigger);
    }

    [Fact]
    public void Ds4_TastenUndTrigger()
    {
        // Kreuz (unten) + Kreis (rechts) + R1 + Options + L3 + PS + Touchpad-Klick
        var r = Ds4Usb(b0: 0x68, b1: 0x22 | 0x40, b2: 0x03, lt: 200, rt: 90);
        Assert.True(PlayStationPad.TryParseDualShock4(r, out var s));
        Assert.True(s.Has(ProButtons.B));       // Kreuz → unten = B
        Assert.True(s.Has(ProButtons.A));       // Kreis → rechts = A
        Assert.True(s.Has(ProButtons.R));       // R1
        Assert.True(s.Has(ProButtons.Plus));    // Options
        Assert.True(s.Has(ProButtons.LeftStick));
        Assert.True(s.Has(ProButtons.Home));    // PS
        Assert.True(s.Has(ProButtons.Capture)); // Touchpad-Klick
        Assert.Equal(200, s.LeftTrigger);
        Assert.Equal(90, s.RightTrigger);
    }

    [Fact]
    public void Ds4_TasterOhneAnalogwert_GiltAlsGanzGedrueckt()
    {
        // L2-Bit gesetzt, analoger Wert 0 (manche Revisionen liefern keinen Wert).
        var r = Ds4Usb(b1: 0x04, lt: 0);
        Assert.True(PlayStationPad.TryParseDualShock4(r, out var s));
        Assert.Equal(255, s.LeftTrigger);
    }

    [Fact]
    public void Ds4_Steuerkreuz()
    {
        foreach (var (hat, erwartet) in new (byte, ProButtons)[]
        {
            (0, ProButtons.Up), (2, ProButtons.Right), (4, ProButtons.Down), (6, ProButtons.Left),
            (1, ProButtons.Up | ProButtons.Right), (8, ProButtons.None),
        })
        {
            Assert.True(PlayStationPad.TryParseDualShock4(Ds4Usb(b0: hat), out var s));
            Assert.Equal(erwartet, s.Buttons);
        }
    }

    [Fact]
    public void Ds4_Akku()
    {
        var r = Ds4Usb(battery: 0x18); // lädt, Stufe 8
        Assert.True(PlayStationPad.TryParseDualShock4(r, out var s));
        Assert.True(s.Charging);
        Assert.Equal(85, s.BatteryPercent);
    }

    [Fact]
    public void Ds4_Bluetooth_CrcPflicht()
    {
        // BT-Bericht: 0x11, Tag, dann Payload ab Byte 3, CRC32 am Ende (insg. 78 Byte).
        var r = new byte[78];
        r[0] = 0x11;
        r[1] = 0xC0;
        r[3] = 128; r[4] = 128; r[5] = 128; r[6] = 128;
        r[7] = 0x08;
        // Falscher CRC → verwerfen.
        Assert.False(PlayStationPad.TryParseDualShock4(r, out _));
        // Eigener CRC (unabhängige Referenz-Implementierung unten) → akzeptieren.
        uint crc = Crc32Ref(0xA1, r.AsSpan(0, 74));
        BitConverter.GetBytes(crc).CopyTo(r, 74);
        Assert.True(PlayStationPad.TryParseDualShock4(r, out var s));
        Assert.Equal(ProButtons.None, s.Buttons);
    }

    // ---------- DualSense ----------

    /// <summary>Voller USB-Eingabebericht eines DualSense (Report-ID 0x01, 64 Byte).</summary>
    private static byte[] Ds5Usb(byte b7 = 0x08, byte b8 = 0, byte b9 = 0, byte b10 = 0, byte lt = 0, byte rt = 0, int battery = -1)
    {
        var r = new byte[64];
        r[0] = 0x01;
        r[1] = r[2] = r[3] = r[4] = 128; // Sticks Mitte
        r[5] = lt; r[6] = rt;          // Trigger (Payload-Bytes 4/5)
        r[8] = b7; r[9] = b8; r[10] = b9; r[11] = b10;
        if (battery >= 0)
            r[53] = (byte)battery; // Payload-Byte 52
        return r;
    }

    [Fact]
    public void Ds5_TastenTriggerAkku()
    {
        // Dreieck (oben) + L2 + Create + PS + Touchpad + Mikro, Edge-Rücktaste rechts
        var r = Ds5Usb(b7: 0x88, b8: 0x14, b9: 0x07, b10: 0x80, lt: 255, rt: 40, battery: 0x1A);
        Assert.True(PlayStationPad.TryParseDualSense(r, out var s));
        Assert.Equal(ControllerKind.DualSense, s.Kind);
        Assert.True(s.Has(ProButtons.X));        // Dreieck → oben = X
        Assert.False(s.Has(ProButtons.Up));      // Hat-Nibble 8 = losgelassen
        Assert.True(s.Has(ProButtons.ZL));       // L2 digital
        Assert.True(s.Has(ProButtons.Minus));    // Create
        Assert.True(s.Has(ProButtons.Home));     // PS
        Assert.True(s.Has(ProButtons.Capture));  // Touchpad-Klick (Bit 0x02 in Byte 9)
        Assert.True(s.Has(ProButtons.Headset));  // Mikro stumm (Bit 0x04)
        Assert.True(s.Has(ProButtons.GR));       // Edge: Rücktaste rechts
        Assert.Equal(255, s.LeftTrigger);
        Assert.Equal(40, s.RightTrigger);
        Assert.Equal(100, s.BatteryPercent);     // Stufe 10 → voll
        Assert.True(s.Charging);                 // Status 1 = lädt
    }

    [Fact]
    public void Ds5_BluetoothEinfach_Grundbericht()
    {
        // Grundbericht vor dem verbesserten Modus: 0x01 + 9 Bytes (Sticks, Tasten, Trigger – ohne Gyro/Akku).
        var r = new byte[10];
        r[0] = 0x01;
        r[1] = r[2] = r[3] = r[4] = 128;
        r[5] = 0x08 | 0x20; // Kreuz
        r[6] = 0x20;        // Options
        r[8] = 77; r[9] = 33;
        Assert.True(PlayStationPad.TryParseDualSense(r, out var s));
        Assert.True(s.Has(ProButtons.B));
        Assert.True(s.Has(ProButtons.Plus));
        Assert.Equal(77, s.LeftTrigger);
        Assert.Equal(33, s.RightTrigger);
        Assert.Null(s.Motion);
    }

    [Fact]
    public void Ds5_BluetoothErweitert_Crc()
    {
        var r = new byte[78];
        r[0] = 0x31; r[1] = 0x00;
        // Payload ab Byte 2: Sticks 2–5, Trigger 6/7, Tasten 9–12
        r[2] = r[3] = r[4] = r[5] = 128;
        r[9] = 0x08;
        Assert.False(PlayStationPad.TryParseDualSense(r, out _)); // ohne CRC
        uint crc = Crc32Ref(0xA1, r.AsSpan(0, 74));
        BitConverter.GetBytes(crc).CopyTo(r, 74);
        Assert.True(PlayStationPad.TryParseDualSense(r, out var s));
        Assert.InRange(s.LeftX, 2008, 2088);
    }

    // ---------- Ausgabeberichte (Vibration/Licht) ----------

    [Fact]
    public void Ds4_Ausgabe_Usb()
    {
        var r = PlayStationPad.BuildDualShock4Output(bluetooth: false, 0x40, 0x80, 255, 0, 0);
        Assert.Equal(0x05, r[0]);
        Assert.Equal(32, r.Length);
        Assert.Equal(0x40, r[4]); // kleiner Motor
        Assert.Equal(0x80, r[5]); // großer Motor
        Assert.Equal(255, r[6]);  // Rot
    }

    [Fact]
    public void Ds4_Ausgabe_Bluetooth_MitCrc()
    {
        var r = PlayStationPad.BuildDualShock4Output(bluetooth: true, 1, 2, 10, 20, 30);
        Assert.Equal(0x11, r[0]);
        Assert.Equal(78, r.Length);
        Assert.Equal(1, r[6]);
        uint expected = Crc32Ref(0xA2, r.AsSpan(0, 74));
        Assert.Equal(expected, BitConverter.ToUInt32(r, 74));
    }

    [Fact]
    public void Ds5_Ausgabe_Usb()
    {
        var r = PlayStationPad.BuildDualSenseOutput(bluetooth: false, 0x40, 0x80, (255, 0, 0), 1, firmwareHasImprovedRumble: true);
        Assert.Equal(0x02, r[0]);
        Assert.Equal(48, r.Length);
        Assert.Equal(0x40, r[3]); // kleiner Motor (Effekt-Offset 2)
        Assert.Equal(0x80, r[4]); // großer Motor
        Assert.Equal(255, r[45]); // Lichtleiste Rot (Offset 44 + 1)
    }

    [Fact]
    public void Ds5_Ausgabe_Bluetooth_MitCrc()
    {
        var r = PlayStationPad.BuildDualSenseOutput(bluetooth: true, 0, 0, null, 0, firmwareHasImprovedRumble: false);
        Assert.Equal(0x31, r[0]);
        Assert.Equal(0x02, r[1]);
        Assert.Equal(78, r.Length);
        Assert.Equal(0x24, r[2 + 43]); // Spieler-LEDs Platz 0 (0x04 | 0x20 sofort)
        uint expected = Crc32Ref(0xA2, r.AsSpan(0, 74));
        Assert.Equal(expected, BitConverter.ToUInt32(r, 74));
    }

    // ---------- Beschädigte / zu kurze Berichte ----------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void KaputteBerichte_WerdenVerworfen(int length)
    {
        var r = new byte[length];
        if (length > 0)
            r[0] = 0x01;
        Assert.False(PlayStationPad.TryParseDualShock4(r, out _));
        Assert.False(PlayStationPad.TryParseDualSense(r, out _));
    }

    [Fact]
    public void UnbekannteReportId_WirdVerworfen()
    {
        var r = new byte[64];
        r[0] = 0x77;
        Assert.False(PlayStationPad.TryParseDualShock4(r, out _));
        Assert.False(PlayStationPad.TryParseDualSense(r, out _));
    }

    // ---------- CRC32-Referenz (langsame Bit-Variante, unabhängig vom Code unter Test) ----------

    private static uint Crc32Ref(byte seed, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        void Bit(byte b)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        Bit(seed);
        foreach (byte b in data)
            Bit(b);
        return ~crc;
    }
}
