using System.Text.Json;
using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>ESP32-Export: Host-Adresse, Controllerauswahl, JSON und C-Header. Nur ausgedachte Beispieldaten.</summary>
public class Esp32ExportTests
{
    private const string Host = "98:B6:E9:01:02:03";

    private static Settings Sample()
    {
        var s = new Settings
        {
            KnownControllers = ["aa:bb:cc:00:11:22", "AA:BB:CC:33:44:55", "11:22:33:44:55:66"],
            SingleJoyCons = ["AA:BB:CC:33:44:55"],
        };
        s.RememberKind("AA:BB:CC:00:11:22", ControllerKind.Pro2);
        s.RememberKind("AA:BB:CC:33:44:55", ControllerKind.JoyCon2Left);
        s.RememberKind("DD:EE:FF:00:11:22", ControllerKind.Pro1);
        s.SetName("AA:BB:CC:00:11:22", "Lenas \"Pro\"");
        s.PlayerSlots["AA:BB:CC:00:11:22"] = 2;
        s.ControllerOutputs["AA:BB:CC:00:11:22"] = OutputMode.DualShock4;
        s.StickCalibrations["AA:BB:CC:00:11:22|L"] =
            new StickCalibration(new AxisCalibration(2000, 1400, 1300), new AxisCalibration(2100, 1350, 1250));
        return s;
    }

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
    public void Build_NurSwitch2Uebernehmbar_UnbekannteMitHinweis()
    {
        var p = Esp32Export.Build(Sample(), "98-b6-e9-01-02-03", "PC");
        Assert.Equal(Host, p.HostAddress);
        Assert.Equal(4, p.Controllers.Count);
        // Übernehmbare zuerst
        Assert.True(p.Controllers[0].SupportedOnEsp32S3);
        Assert.True(p.Controllers[1].SupportedOnEsp32S3);
        var pro = p.Controllers.Single(c => c.Address == "AA:BB:CC:00:11:22");
        Assert.Equal(0x2069, pro.ProductId);
        Assert.Equal("Lenas \"Pro\"", pro.Name);
        Assert.Equal(2, pro.PlayerSlot);
        Assert.Equal("DualShock4", pro.Output);
        Assert.NotNull(pro.StickLeft);
        Assert.Null(pro.StickRight);
        Assert.True(p.Controllers.Single(c => c.Address == "AA:BB:CC:33:44:55").SingleJoyCon);
        var classic = p.Controllers.Single(c => c.Address == "DD:EE:FF:00:11:22");
        Assert.False(classic.SupportedOnEsp32S3);
        Assert.NotNull(classic.Note);
        var unknown = p.Controllers.Single(c => c.Address == "11:22:33:44:55:66");
        Assert.Equal("Unknown", unknown.Kind);
        Assert.False(unknown.SupportedOnEsp32S3);
    }

    [Fact]
    public void Build_OhneAdapterAdresse_Fehler()
    {
        Assert.Throws<ArgumentException>(() => Esp32Export.Build(new Settings(), "00:00:00:00:00:00", null));
        Assert.Throws<ArgumentException>(() => Esp32Export.Build(new Settings(), "kaputt", null));
    }

    [Fact]
    public void Json_EnthaeltFormatUndKeineSchluessel()
    {
        string json = Esp32Export.ToJson(Esp32Export.Build(Sample(), Host, "PC"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(Esp32Export.FormatId, doc.RootElement.GetProperty("format").GetString());
        Assert.Equal(Host, doc.RootElement.GetProperty("hostAddress").GetString());
        Assert.DoesNotContain("linkKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ltk", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Header_AdresseInBeidenReihenfolgen_UndTabelle()
    {
        string h = Esp32Export.ToHeader(Esp32Export.Build(Sample(), Host, "PC"), "1.2.3");
        Assert.Contains("static const uint8_t NCONNECT_HOST_ADDR[6] = { 0x98, 0xB6, 0xE9, 0x01, 0x02, 0x03 };", h);
        Assert.Contains("static const uint8_t NCONNECT_HOST_ADDR_LE[6] = { 0x03, 0x02, 0x01, 0xE9, 0xB6, 0x98 };", h);
        Assert.Contains("#define NCONNECT_CONTROLLER_COUNT 2", h);
        Assert.Contains("\"Lenas \\\"Pro\\\"\"", h); // Anführungszeichen maskiert
        Assert.Contains("0x2069", h);
        Assert.Contains("{ 2000, 1400, 1300, 2100, 1350, 1250 }", h);
        Assert.DoesNotContain("DD:EE:FF:00:11:22", h); // klassisches Bluetooth nicht in der Tabelle
        Assert.Contains("N-Connect 1.2.3", h);
    }

    [Fact]
    public void Header_OhneControllerBleibtKompilierbar()
    {
        string h = Esp32Export.ToHeader(Esp32Export.Build(new Settings(), Host, null));
        Assert.Contains("#define NCONNECT_CONTROLLER_COUNT 0", h);
        Assert.Contains("NCONNECT_CONTROLLERS[1] = { 0 }", h);
    }

    [Fact]
    public void Werbung_HostBytesPassenZurLeTabelle()
    {
        // Byte 10–15 der Herstellerdaten (ohne Kennung) = Host-Adresse niedrigstes Byte zuerst.
        byte[] adv = new byte[16];
        new byte[] { 0x01, 0x00, 0x03, 0x7E, 0x05, 0x69, 0x20 }.CopyTo(adv, 0);
        Esp32Export.AddressBytes(Host).Reverse().ToArray().CopyTo(adv, 10);
        Assert.Equal(Host, BtAddress.Format(Advertisement.HostAddress(adv)));
    }

    [Fact]
    public void CString_MaskiertSonderzeichen()
    {
        Assert.Equal("NULL", Esp32Export.CString(null));
        Assert.Equal("\"a\\\\b\\n\"", Esp32Export.CString("a\\b\n"));
        Assert.Equal("\"Jöy\"", Esp32Export.CString("Jöy"));
    }
}
