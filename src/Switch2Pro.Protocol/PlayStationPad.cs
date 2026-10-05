using System.Buffers.Binary;

namespace Switch2Pro.Protocol;

/// <summary>
/// Sony-Controller (DualShock 4, DualSense/DualSense Edge) über USB-HID oder Bluetooth-Classic-HID:
/// Erkennung an VID/PID, Zerlegen der Eingabeberichte und Bauen der Ausgabeberichte (Vibration,
/// Lichtleiste, Spieler-LEDs). Formate nach SDL (SDL_hidapi_ps4/ps5) und hid-playstation (Linux).
///
/// Tasten kommen positionsgetreu im Pro-Controller-Schema an: Kreuz (unten) = B, Kreis (rechts) = A,
/// Viereck (links) = Y, Dreieck (oben) = X – wie die Xbox-Positionen, die Spiele erwarten.
/// </summary>
public static class PlayStationPad
{
    public const ushort VendorId = 0x054C;

    // Produkt-IDs (USB und Bluetooth gleich; Bluetooth-HID-Pfade: vid&0002054c_pid&xxxx).
    public const ushort DualShock4Pid = 0x05C4;        // DualShock 4 (erste Generation)
    public const ushort DualShock4V2Pid = 0x09CC;      // DualShock 4 v2 / Slim
    public const ushort DualShock4DonglePid = 0x0BA0;  // Sony USB-Adapter
    public const ushort DualSensePid = 0x0CE6;         // DualSense
    public const ushort DualSenseEdgePid = 0x0DF2;     // DualSense Edge

    /// <summary>Controller-Art aus VID/PID, sonst null (unbekannte Sony-Geräte, Dritthersteller).</summary>
    public static ControllerKind? KindFromVidPid(ushort vid, ushort pid) => vid != VendorId ? null : pid switch
    {
        DualShock4Pid or DualShock4V2Pid or DualShock4DonglePid => ControllerKind.DualShock4,
        DualSensePid or DualSenseEdgePid => ControllerKind.DualSense,
        _ => null,
    };

    /// <summary>
    /// Controller-Art aus einem HID-Gerätepfad, sonst null. Bluetooth: …vid&amp;0002054c_pid&amp;09cc…,
    /// USB: …vid_054c&amp;pid_09cc… .
    /// </summary>
    public static ControllerKind? KindFromHidPath(string path)
    {
        var p = path.ToLowerInvariant();
        bool bt = p.Contains("vid&0002054c_pid&");
        if (!bt && !p.Contains("vid_054c&pid_"))
            return null;
        if (ContainsPid(p, "05c4") || ContainsPid(p, "09cc") || ContainsPid(p, "0ba0"))
            return ControllerKind.DualShock4;
        if (ContainsPid(p, "0ce6") || ContainsPid(p, "0df2"))
            return ControllerKind.DualSense;
        return null;
    }

    private static bool ContainsPid(string path, string pid) =>
        path.Contains($"pid&{pid}", StringComparison.Ordinal) || path.Contains($"pid_{pid}", StringComparison.Ordinal);

    /// <summary>Bluetooth-HID-Gerät (VID kodiert als vid&amp;0002054c) statt USB (vid_054c)?</summary>
    public static bool IsBluetoothHidPath(string path) =>
        path.Contains("vid&0002054c_pid&", StringComparison.OrdinalIgnoreCase);

    /// <summary>Eigener Gerätename je Produkt (für DualSense Edge ein anderer als für DualSense).</summary>
    public static string? ProductName(string path)
    {
        var p = path.ToLowerInvariant();
        if (ContainsPid(p, "0df2"))
            return "Sony DualSense Edge";
        return null; // sonst Anzeigename der Controller-Art
    }

    /// <summary>8-Bit-Sticks auf den 12-Bit-Bereich wie bei den lizenzierten Kabel-Gamepads.</summary>
    public static DeviceCalibration Calibration => WiredSwitchPad.Calibration;

    // ---------- CRC32 (zlib, Polynom 0xEDB88320) – Bluetooth-Berichte sind damit abgesichert ----------

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    /// <summary>CRC32 über die Bytesequence <paramref name="seed"/> + <paramref name="data"/> (zlib-Standard, wie SDL).</summary>
    public static uint Crc32(byte seed, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        crc = CrcTable[(crc ^ seed) & 0xFF] ^ (crc >> 8);
        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    /// <summary>Bluetooth-Eingabebericht prüfen: CRC32 über 0xA1 + Bericht ohne die letzten 4 Byte.</summary>
    public static bool VerifyBluetoothCrc(ReadOnlySpan<byte> report)
    {
        if (report.Length < 8)
            return false;
        uint expected = BinaryPrimitives.ReadUInt32LittleEndian(report[^4..]);
        return Crc32(0xA1, report[..^4]) == expected;
    }

    // ---------- gemeinsames Zerlegen ----------

    /// <summary>Steuerkreuz als Hat-Wert 0–7 (oben, im Uhrzeigersinn), ≥8 = losgelassen.</summary>
    private static ProButtons Hat(int hat) => hat switch
    {
        0 => ProButtons.Up,
        1 => ProButtons.Up | ProButtons.Right,
        2 => ProButtons.Right,
        3 => ProButtons.Down | ProButtons.Right,
        4 => ProButtons.Down,
        5 => ProButtons.Down | ProButtons.Left,
        6 => ProButtons.Left,
        7 => ProButtons.Up | ProButtons.Left,
        _ => ProButtons.None,
    };

    /// <summary>
    /// Drei Tasten-Bytes (Positionen wie im Sony-Bericht: [0] = Gesichtstasten+Hat, [1] = Schultern/Auswahl,
    /// [2] = PS/Sondertasten). Positionsschema: Kreuz = unten (B), Kreis = rechts (A), Viereck = links (Y),
    /// Dreieck = oben (X). Byte 2 Bit: PS → Home, Touchpad-Klick → Aufnahme, Mikro (DualSense) → Headset,
    /// linke/rechte Funktionstaste (Edge) → C, hintere Tasten (Edge) → GL/GR.
    /// </summary>
    private static ProButtons Buttons(byte b0, byte b1, byte b2, bool dualSense, byte b3 = 0)
    {
        var buttons = Hat(b0 & 0x0F);
        if ((b0 & 0x10) != 0) buttons |= ProButtons.Y;          // Viereck
        if ((b0 & 0x20) != 0) buttons |= ProButtons.B;          // Kreuz
        if ((b0 & 0x40) != 0) buttons |= ProButtons.A;          // Kreis
        if ((b0 & 0x80) != 0) buttons |= ProButtons.X;          // Dreieck
        if ((b1 & 0x01) != 0) buttons |= ProButtons.L;          // L1
        if ((b1 & 0x02) != 0) buttons |= ProButtons.R;          // R1
        if ((b1 & 0x04) != 0) buttons |= ProButtons.ZL;         // L2 digital
        if ((b1 & 0x08) != 0) buttons |= ProButtons.ZR;         // R2 digital
        if ((b1 & 0x10) != 0) buttons |= ProButtons.Minus;      // Teilen/Create
        if ((b1 & 0x20) != 0) buttons |= ProButtons.Plus;       // Options
        if ((b1 & 0x40) != 0) buttons |= ProButtons.LeftStick;  // L3
        if ((b1 & 0x80) != 0) buttons |= ProButtons.RightStick; // R3
        if ((b2 & 0x01) != 0) buttons |= ProButtons.Home;       // PS
        if ((b2 & 0x02) != 0) buttons |= ProButtons.Capture;    // Touchpad-Klick
        if (dualSense)
        {
            if ((b2 & 0x04) != 0) buttons |= ProButtons.Headset; // Mikro stumm
            // DualSense Edge (4. Tasten-Byte): Fn-Tasten und hintere Paddles.
            if ((b3 & 0x10) != 0) buttons |= ProButtons.C;       // Fn links
            if ((b3 & 0x40) != 0) buttons |= ProButtons.GL;      // Rücktaste links
            if ((b3 & 0x80) != 0) buttons |= ProButtons.GR;      // Rücktaste rechts
        }
        return buttons;
    }

    // ---------- DualShock 4 ----------

    /// <summary>
    /// DualShock-4-Eingabebericht zerlegen (mit Report-ID als erstes Byte, wie Windows ihn liefert).
    /// USB: ID 0x01, Daten ab Byte 1. Bluetooth: ID 0x11–0x19, Daten ab Byte 3, mit CRC32 am Ende.
    /// </summary>
    public static bool TryParseDualShock4(ReadOnlySpan<byte> report, out ControllerState state)
    {
        state = new ControllerState { Kind = ControllerKind.DualShock4 };
        ReadOnlySpan<byte> p;
        if (report.Length >= 10 && report[0] == 0x01)
        {
            p = report[1..];
        }
        else if (report.Length >= 13 && report[0] is >= 0x11 and <= 0x19)
        {
            // Bluetooth: CRC prüfen, wenn der volle 78-Byte-Bericht ankommt (kürzere ohne CRC ebenfalls annehmen).
            if (report.Length >= 78 && !VerifyBluetoothCrc(report))
                return false;
            p = report[3..];
        }
        else
        {
            return false;
        }
        if (p.Length < 9)
            return false;

        var buttons = Buttons(p[4], p[5], p[6], dualSense: false);
        byte lt = p[7], rt = p[8];
        // Analoger Wert 0 + digitales Bit gesetzt → der Trigger ist ganz gedrückt (SDL-Verhalten).
        if (lt == 0 && (p[5] & 0x04) != 0) lt = 255;
        if (rt == 0 && (p[5] & 0x08) != 0) rt = 255;

        Motion? motion = null;
        if (p.Length >= 24)
        {
            // Sony-Achsen → Nintendo-Rohachsen (Umkehrung von ToDs4Report): X bleibt, Z wird Y, Y wird −Z.
            // Gyro: ±2000 °/s auf ±32767 (wie Nintendo, ~16 LSB/°/s); Beschl.: 8192 LSB/g → halbieren auf 4096 ≙ 1 g.
            motion = new Motion(
                (short)(BinaryPrimitives.ReadInt16LittleEndian(p[18..]) / 2),
                Neg((short)(BinaryPrimitives.ReadInt16LittleEndian(p[22..]) / 2)),
                (short)(BinaryPrimitives.ReadInt16LittleEndian(p[20..]) / 2),
                BinaryPrimitives.ReadInt16LittleEndian(p[12..]),
                Neg(BinaryPrimitives.ReadInt16LittleEndian(p[16..])),
                BinaryPrimitives.ReadInt16LittleEndian(p[14..]));
        }

        int percent = -1;
        bool charging = false;
        if (p.Length >= 30)
        {
            int level = p[29] & 0x0F;
            charging = (p[29] & 0x10) != 0;
            percent = level >= 11 ? 100 : Math.Min(level * 10 + 5, 100);
        }

        state = new ControllerState
        {
            Kind = ControllerKind.DualShock4,
            Buttons = buttons,
            LeftX = X(p[0]), LeftY = Y(p[1]), RightX = X(p[2]), RightY = Y(p[3]),
            LeftTrigger = lt, RightTrigger = rt,
            Motion = motion,
            BatteryPercent = percent,
            Charging = charging,
        };
        return true;
    }

    /// <summary>
    /// DualShock-4-Ausgabebericht: Vibration (kleiner/großer Motor) und Lichtleiste (RGB, Blinken).
    /// USB: 32 Byte, ID 0x05, Effekte ab Byte 4, Maske Byte 1. Bluetooth: 78 Byte, ID 0x11,
    /// Byte 1 = 0xC4 (HID+CRC, Rate 4 ms), Maske Byte 3, Effekte ab Byte 6, CRC32 über 0xA2+Daten am Ende.
    /// </summary>
    public static byte[] BuildDualShock4Output(bool bluetooth, byte small, byte large, byte red, byte green, byte blue)
    {
        var report = new byte[bluetooth ? 78 : 32];
        int offset;
        if (bluetooth)
        {
            report[0] = 0x11;
            report[1] = 0xC4; // 0xC0 | Berichtsintervall 4
            report[3] = 0x07; // Vibration + Lichtleiste + Blinken
            offset = 6;
        }
        else
        {
            report[0] = 0x05;
            report[1] = 0x07;
            offset = 4;
        }
        report[offset] = small;
        report[offset + 1] = large;
        report[offset + 2] = red;
        report[offset + 3] = green;
        report[offset + 4] = blue;
        // offset+5/+6: Blinkdauer (0 = dauerhaft an)
        if (bluetooth)
            BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(74), Crc32(0xA2, report.AsSpan(0, 74)));
        return report;
    }

    // ---------- DualSense ----------

    /// <summary>
    /// DualSense-Eingabebericht zerlegen. USB: ID 0x01 mit dem vollen Bericht ab Byte 1.
    /// Bluetooth verbessert: ID 0x31 (CRC geprüft), Daten ab Byte 2. Bluetooth einfach: ID 0x01
    /// mit dem 9-Byte-Grundbericht ab Byte 1 (Sticks, Tasten, digitale Trigger – kein Gyro/Akku).
    /// </summary>
    public static bool TryParseDualSense(ReadOnlySpan<byte> report, out ControllerState state)
    {
        state = new ControllerState { Kind = ControllerKind.DualSense };
        ReadOnlySpan<byte> p;
        bool bluetooth = false;
        if (report.Length >= 34 && report[0] == 0x31)
        {
            if (!VerifyBluetoothCrc(report))
                return false;
            p = report[2..];
            bluetooth = true;
        }
        else if (report.Length >= 10 && report[0] == 0x01)
        {
            p = report[1..];
        }
        else
        {
            return false;
        }

        if (p.Length < 54)
        {
            // Grundbericht (Bluetooth vor dem verbesserten Modus): Sticks, 3 Tasten-Bytes, Trigger.
            if (p.Length < 9)
                return false;
            var buttons = Buttons(p[4], p[5], p[6], dualSense: true);
            byte lt = p[7], rt = p[8];
            if (lt == 0 && (p[5] & 0x04) != 0) lt = 255;
            if (rt == 0 && (p[5] & 0x08) != 0) rt = 255;
            state = new ControllerState
            {
                Kind = ControllerKind.DualSense,
                Buttons = buttons,
                LeftX = X(p[0]), LeftY = Y(p[1]), RightX = X(p[2]), RightY = Y(p[3]),
                LeftTrigger = lt, RightTrigger = rt,
            };
            return true;
        }

        // Voller Bericht: Trigger bei 4/5, Tasten bei 7–9 (DualSense Edge nutzt noch Byte 10).
        var full = Buttons(p[7], p[8], p[9], dualSense: true, p.Length > 10 ? p[10] : (byte)0);
        byte ltFull = p[4], rtFull = p[5];
        if (ltFull == 0 && (p[8] & 0x04) != 0) ltFull = 255;
        if (rtFull == 0 && (p[8] & 0x08) != 0) rtFull = 255;

        var motionFull = new Motion(
            (short)(BinaryPrimitives.ReadInt16LittleEndian(p[21..]) / 2),
            Neg((short)(BinaryPrimitives.ReadInt16LittleEndian(p[25..]) / 2)),
            (short)(BinaryPrimitives.ReadInt16LittleEndian(p[23..]) / 2),
            BinaryPrimitives.ReadInt16LittleEndian(p[15..]),
            Neg(BinaryPrimitives.ReadInt16LittleEndian(p[19..])),
            BinaryPrimitives.ReadInt16LittleEndian(p[17..]));

        // Akku-Byte 52: untere 4 Bit = Stufe 0–10, obere 4 Bit = Ladestatus (0x1/0x2/0xA/0xB = lädt, 0xC = voll).
        byte battery = p[52];
        int levelFull = battery & 0x0F;
        int chargeStatus = battery >> 4;
        bool chargingFull = chargeStatus is 0x1 or 0x2 or 0xA or 0xB or 0xC;
        int percentFull = levelFull >= 10 || chargeStatus == 0xC ? 100 : Math.Min(levelFull * 10 + 5, 100);
        if (!bluetooth)
            chargingFull = (battery & 0x20) != 0 || chargingFull; // per USB lädt der Controller

        state = new ControllerState
        {
            Kind = ControllerKind.DualSense,
            Buttons = full,
            LeftX = X(p[0]), LeftY = Y(p[1]), RightX = X(p[2]), RightY = Y(p[3]),
            LeftTrigger = ltFull, RightTrigger = rtFull,
            Motion = motionFull,
            BatteryPercent = percentFull,
            Charging = chargingFull,
        };
        return true;
    }

    /// <summary>
    /// DualSense-Ausgabebericht (DS5EffectsState_t, 47 Byte Effekte).
    /// USB: 48 Byte, ID 0x02, Effekte ab Byte 1. Bluetooth: 78 Byte, ID 0x31, Byte 1 = 0x02,
    /// Effekte ab Byte 2, CRC32 über 0xA2+Daten am Ende.
    /// Rumble: Firmware ≥ 2.24 braucht EnableBits3 0x04, ältere EnableBits1 0x01 mit halben Werten –
    /// wir kennen die Firmware ggf. nicht → beide Bits setzen schadet nichts? Nein: nur eines. Wir setzen
    /// das moderne Bit und fallen auf die einfache Emulation zurück, wenn firmware &lt; 0x0224.
    /// </summary>
    public static byte[] BuildDualSenseOutput(bool bluetooth, byte small, byte large,
        (byte R, byte G, byte B)? lightbar, int playerIndex, bool firmwareHasImprovedRumble)
    {
        var report = new byte[bluetooth ? 78 : 48];
        int offset;
        if (bluetooth)
        {
            report[0] = 0x31;
            report[1] = 0x02;
            offset = 2;
        }
        else
        {
            report[0] = 0x02;
            offset = 1;
        }
        // Effekt-Bytes (Offset relativ zum Effektblock, siehe DS5EffectsState_t):
        if (small != 0 || large != 0)
        {
            if (firmwareHasImprovedRumble)
            {
                report[offset + 38] |= 0x04; // EnableBits3: verbesserte Vibration (Firmware ≥ 2.24)
                report[offset + 3] = large;
                report[offset + 2] = small;
            }
            else
            {
                report[offset + 0] |= 0x01; // EnableBits1: Vibration-Emulation
                report[offset + 3] = (byte)(large >> 1);
                report[offset + 2] = (byte)(small >> 1);
            }
            report[offset + 0] |= 0x02; // Audio-Haptik aus, solange vibriert wird
        }
        if (lightbar is { } color)
        {
            report[offset + 1] |= 0x04; // EnableBits2: Lichtleiste
            report[offset + 44] = color.R;
            report[offset + 45] = color.G;
            report[offset + 46] = color.B;
        }
        if (playerIndex >= 0)
        {
            report[offset + 1] |= 0x10; // EnableBits2: Spieler-LEDs (unter dem Touchpad)
            byte[] lights = [0x04, 0x0A, 0x15, 0x1B, 0x1F];
            report[offset + 43] = (byte)(lights[playerIndex % lights.Length] | 0x20); // 0x20 = sofort statt weiches Einblenden
        }
        if (bluetooth)
            BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(74), Crc32(0xA2, report.AsSpan(0, 74)));
        return report;
    }

    // ---------- Feature-Berichte ----------

    /// <summary>Seriennummer (eigene Bluetooth-Adresse) aus Feature-Bericht 0x09: Bytes 1–6, umgedreht.</summary>
    public static string? DualSenseSerial(ReadOnlySpan<byte> report)
    {
        if (report.Length < 7 || report[0] != 0x09 || report.Slice(1, 6).IndexOfAnyExcept((byte)0) < 0)
            return null;
        return string.Join(':', report.Slice(1, 6).ToArray().Reverse().Select(b => $"{b:X2}"));
    }

    /// <summary>Firmware-Version aus Feature-Bericht 0x20: Bytes 44/45 (little-endian), z. B. 0x0224.</summary>
    public static ushort? DualSenseFirmware(ReadOnlySpan<byte> report)
    {
        if (report.Length < 46 || report[0] != 0x20)
            return null;
        return BinaryPrimitives.ReadUInt16LittleEndian(report[44..]);
    }

    /// <summary>Seriennummer (Bluetooth-Adresse) des DualShock 4 aus Feature-Bericht 0x12: Bytes 1–6 umgedreht.</summary>
    public static string? DualShock4Serial(ReadOnlySpan<byte> report)
    {
        if (report.Length < 7 || report[0] != 0x12 || report.Slice(1, 6).IndexOfAnyExcept((byte)0) < 0)
            return null;
        return string.Join(':', report.Slice(1, 6).ToArray().Reverse().Select(b => $"{b:X2}"));
    }

    /// <summary>Lichtleistenfarben je Spielernummer (wie PS4/PS5: Blau, Rot, Grün, Pink …).</summary>
    public static (byte R, byte G, byte B) LightbarColor(int playerIndex)
    {
        (byte, byte, byte)[] colors = [(0, 0, 0x40), (0x40, 0, 0), (0, 0x40, 0), (0x20, 0, 0x20), (0x20, 0x10, 0), (0, 0x10, 0x10), (0x10, 0x10, 0x10)];
        return colors[(playerIndex < 0 ? 0 : playerIndex) % colors.Length];
    }

    private static short Neg(short v) => v == short.MinValue ? short.MaxValue : (short)-v;

    private static int X(byte v) => v * 4095 / 255;
    private static int Y(byte v) => (255 - v) * 4095 / 255; // Sony: 0 = oben → nach oben wachsend
}
