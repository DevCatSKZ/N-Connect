using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>
/// Gemerkte Controller-Art, Art aus dem Windows-Bluetooth-Namen und Host-Adresse in der Werbung (früher Teil der
/// ESP32-Export-Tests; der Export selbst ist entfernt). Nur ausgedachte Beispieldaten.
/// </summary>
public class KnownControllerTests
{
    private const string Host = "98:B6:E9:01:02:03";

    [Fact]
    public void RememberKind_MerktUndMeldetNurAenderungen()
    {
        var s = new Settings();
        Assert.True(s.RememberKind("aa-bb-cc-00-11-22", ControllerKind.Pro2));
        Assert.False(s.RememberKind("AA:BB:CC:00:11:22", ControllerKind.Pro2));
        Assert.False(s.RememberKind("AA:BB:CC:00:11:23", ControllerKind.Unknown));
        Assert.Equal(ControllerKind.Pro2, s.ControllerKinds["AA:BB:CC:00:11:22"]);
    }

    [Fact]
    public void Werbung_HostBytesPassenZurLeTabelle()
    {
        // Byte 10–15 der Herstellerdaten (ohne Kennung) = Host-Adresse niedrigstes Byte zuerst.
        byte[] adv = new byte[16];
        new byte[] { 0x01, 0x00, 0x03, 0x7E, 0x05, 0x69, 0x20 }.CopyTo(adv, 0);
        new byte[] { 0x03, 0x02, 0x01, 0xE9, 0xB6, 0x98 }.CopyTo(adv, 10);
        Assert.Equal(Host, BtAddress.Format(Advertisement.HostAddress(adv)));
    }

    [Theory]
    [InlineData("Pro Controller", ControllerKind.Pro1)]
    [InlineData("Joy-Con (L)", ControllerKind.JoyCon1Left)]
    [InlineData("Joy-Con 2 (R)", ControllerKind.JoyCon2Right)]
    [InlineData("Nintendo RVL-CNT-01", ControllerKind.WiiRemote)]
    [InlineData("Nintendo RVL-CNT-01-UC", ControllerKind.WiiUPro)]
    [InlineData("Wireless Controller", ControllerKind.DualShock4)]
    [InlineData("Xbox Wireless Controller", ControllerKind.XboxController)]
    [InlineData("Galaxy Buds3 Pro", ControllerKind.Unknown)]
    [InlineData("", ControllerKind.Unknown)]
    public void KindFromName_ErkenntController(string name, ControllerKind expected) =>
        Assert.Equal(expected, BtDeviceNames.KindFromName(name));
}
