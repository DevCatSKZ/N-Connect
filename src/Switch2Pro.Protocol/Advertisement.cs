namespace Switch2Pro.Protocol;

public static class Advertisement
{
    /// <summary>
    /// Welcher Switch-2-Controller wirbt hier? <paramref name="data"/> ist der Inhalt der Herstellerdaten
    /// OHNE die zwei Bytes der Herstellerkennung (so liefert ihn Windows).
    /// Aufbau: 01 00 03 | VID (LE) | PID (LE) | ... (aufgezeichnet: 01 00 03 7E 05 69 20 …)
    /// </summary>
    public static ControllerKind Kind(ushort companyId, ReadOnlySpan<byte> data)
    {
        if (companyId != Gatt.NintendoCompanyId || data.Length < 7)
            return ControllerKind.Unknown;
        // Byte 4 (VID-High) variiert laut anderen Bridges zwischen Firmware-Ständen und wird
        // daher nicht geprüft; VID-Low und PID müssen passen.
        if (data[0] != 0x01 || data[1] != 0x00 || data[2] != 0x03 || data[3] != 0x7E)
            return ControllerKind.Unknown;
        return ControllerKinds.FromSwitch2ProductId(data[5] | (data[6] << 8));
    }

    public static bool IsProController2(ushort companyId, ReadOnlySpan<byte> data) =>
        Kind(companyId, data) == ControllerKind.Pro2;

    /// <summary>
    /// Host, für den der Controller wirbt (Byte 10–15, niedrigstes Byte zuerst); 0 = SYNC-Modus (für alle).
    /// Nach einer Kopplung mit dem PC steht hier dessen Bluetooth-Adresse.
    /// </summary>
    public static ulong HostAddress(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16)
            return 0;
        ulong address = 0;
        for (int i = 5; i >= 0; i--)
            address = address << 8 | data[10 + i];
        return address;
    }

    /// <summary>
    /// SYNC-Modus: In der normalen Kopplungs-Werbung ist die Host-Adresse (Byte 10–15 ohne
    /// Herstellerkennung) leer. Bei der Wiederverbindungs-/Weck-Werbung steht dort die Adresse
    /// der Konsole, mit der der Controller gekoppelt ist.
    /// </summary>
    public static bool IsSyncMode(ReadOnlySpan<byte> data) =>
        data.Length < 16 || data[10..16].IndexOfAnyExcept((byte)0) < 0;
}
