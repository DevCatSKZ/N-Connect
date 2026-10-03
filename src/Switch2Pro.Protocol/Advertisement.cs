namespace Switch2Pro.Protocol;

public static class Advertisement
{
    /// <summary>
    /// Prüft die Herstellerdaten einer BLE-Werbung. <paramref name="data"/> ist der
    /// Inhalt OHNE die zwei Bytes der Herstellerkennung (so liefert ihn Windows).
    /// Aufbau: 01 00 03 | VID (LE) | PID (LE) | ...
    /// </summary>
    public static bool IsProController2(ushort companyId, ReadOnlySpan<byte> data)
    {
        if (companyId != Gatt.NintendoCompanyId || data.Length < 7)
            return false;
        // Byte 4 (VID-High) variiert laut anderen Bridges zwischen Firmware-Ständen und wird
        // daher nicht geprüft; VID-Low und PID müssen passen.
        if (data[0] != 0x01 || data[1] != 0x00 || data[2] != 0x03 || data[3] != 0x7E)
            return false;
        return (data[5] | (data[6] << 8)) == Gatt.ProController2ProductId;
    }

    /// <summary>
    /// SYNC-Modus: In der normalen Kopplungs-Werbung ist die Host-Adresse (Byte 10–15 ohne
    /// Herstellerkennung) leer. Bei der Wiederverbindungs-/Weck-Werbung steht dort die Adresse
    /// der Konsole, mit der der Controller gekoppelt ist.
    /// </summary>
    public static bool IsSyncMode(ReadOnlySpan<byte> data) =>
        data.Length < 16 || data[10..16].IndexOfAnyExcept((byte)0) < 0;
}
