using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>Virtueller Xbox-One-S-Controller für Android (UHID): Bericht gegen die CTS-Referenzberichte, hid-JSON.</summary>
public class XboxBtReportTests
{
    private static byte[] Build(XButtons buttons = XButtons.None, short lx = 0, short ly = 0, short rx = 0, short ry = 0,
        byte lt = 0, byte rt = 0) =>
        XboxBtReport.Build(new GamepadState(buttons, lt, rt, lx, ly, rx, ry, false));

    [Fact]
    public void Neutral_EntsprichtCtsRuhebericht()
    {
        byte[] cts = [0x01, 0xff, 0x7f, 0xff, 0x7f, 0xff, 0x7f, 0xff, 0x7f, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(cts, XboxBtReport.Neutral());
    }

    [Theory]
    [InlineData(XButtons.A, 14, 0x01)]
    [InlineData(XButtons.B, 14, 0x02)]
    [InlineData(XButtons.X, 14, 0x08)]
    [InlineData(XButtons.Y, 14, 0x10)]
    [InlineData(XButtons.LB, 14, 0x40)]
    [InlineData(XButtons.RB, 14, 0x80)]
    [InlineData(XButtons.Start, 15, 0x08)]
    [InlineData(XButtons.Guide, 15, 0x10)]
    [InlineData(XButtons.LS, 15, 0x20)]
    [InlineData(XButtons.RS, 15, 0x40)]
    [InlineData(XButtons.Back, 16, 0x01)]
    public void Tasten_SetzenDasCtsBit(XButtons button, int index, int bit)
    {
        byte[] r = Build(button);
        Assert.Equal(bit, r[index]);
        for (int i = 13; i < XboxBtReport.Length; i++)
            if (i != index)
                Assert.Equal(0, r[i]);
    }

    [Theory]
    [InlineData(XButtons.Up, 1)]
    [InlineData(XButtons.Up | XButtons.Right, 2)]
    [InlineData(XButtons.Right, 3)]
    [InlineData(XButtons.Down | XButtons.Right, 4)]
    [InlineData(XButtons.Down, 5)]
    [InlineData(XButtons.Down | XButtons.Left, 6)]
    [InlineData(XButtons.Left, 7)]
    [InlineData(XButtons.Up | XButtons.Left, 8)]
    [InlineData(XButtons.Up | XButtons.Down, 0)]
    public void Steuerkreuz_AlsHat(XButtons dpad, int hat) => Assert.Equal(hat, Build(dpad)[13]);

    [Fact]
    public void Sticks_YObenPositivWirdBerichtObenNull()
    {
        byte[] r = Build(lx: short.MinValue, ly: short.MaxValue, rx: short.MaxValue, ry: short.MinValue);
        Assert.Equal(0x0000, r[1] | r[2] << 8);   // LX ganz links
        Assert.Equal(0x0000, r[3] | r[4] << 8);   // LY ganz oben
        Assert.True((r[5] | r[6] << 8) >= 0xFFFE); // RX ganz rechts
        Assert.Equal(0xFFFF, r[7] | r[8] << 8);   // RY ganz unten
    }

    [Fact]
    public void Trigger_ZehnBit()
    {
        byte[] r = Build(lt: 255, rt: 128);
        Assert.Equal(0x3FF, r[9] | r[10] << 8);
        int half = r[11] | r[12] << 8;
        Assert.InRange(half, 0x1F0, 0x210);  // CTS: 0x1FF = halb gedrückt
    }

    [Fact]
    public void Deskriptor_IstDerXboxOneSDeskriptor()
    {
        Assert.Equal(334, XboxBtReport.Descriptor.Length);
        Assert.Equal(new byte[] { 0x05, 0x01, 0x09, 0x05, 0xA1, 0x01, 0x85, 0x01 }, XboxBtReport.Descriptor[..8].ToArray());
    }

    [Fact]
    public void HidJson_RegisterUndReport()
    {
        string reg = UhidCommand.RegisterXbox(1, "N-Connect \"Pro\"");
        Assert.StartsWith("{\"id\":1,\"command\":\"register\",\"name\":\"N-Connect \\\"Pro\\\"\"", reg);
        Assert.Contains("\"vid\":1118,\"pid\":765,\"bus\":\"bluetooth\",\"descriptor\":[5,1,9,5,", reg);
        Assert.EndsWith("]}", reg);
        Assert.Equal("{\"id\":1,\"command\":\"report\",\"report\":[1,255,127,255,127,255,127,255,127,0,0,0,0,0,0,0,0]}",
            UhidCommand.Report(1, XboxBtReport.Neutral()));
    }

    [Fact]
    public void Build_InPuffer_GleicheBytesWieNeuesArray()
    {
        Span<byte> buffer = stackalloc byte[XboxBtReport.Length + 8];
        foreach (var g in new[]
                 {
                     new GamepadState(XButtons.A | XButtons.Left, 255, 128, -1000, 2000, 30000, -31000, true),
                     new GamepadState(XButtons.None, 0, 0, 0, 0, 0, 0, false),
                     new GamepadState(XButtons.Guide | XButtons.RS, 1, 1, short.MaxValue, short.MinValue, short.MinValue, short.MaxValue, false),
                 })
        {
            byte[] expected = XboxBtReport.Build(g);
            XboxBtReport.Build(g, buffer);
            Assert.Equal(expected, buffer[..XboxBtReport.Length].ToArray());
        }
    }

    [Fact]
    public void WriteReportLine_EntsperichtReportPlusZeilenumbruch()
    {
        byte[] report = XboxBtReport.Build(new GamepadState(XButtons.Y | XButtons.RB, 0, 0, 1234, -5678, 0, 0, false));
        Span<byte> buffer = stackalloc byte[UhidCommand.ReportLineCapacity(report.Length)];
        int n = UhidCommand.WriteReportLine(3, report, buffer);
        Assert.Equal(UhidCommand.Report(3, report) + "\n", System.Text.Encoding.ASCII.GetString(buffer[..n]));
        // Puffer mit Lückenfüller muss sauber enden (nichts wird hinter das Zeilenende geschrieben)
        Assert.True(n <= buffer.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(999)]
    [InlineData(1000)]
    [InlineData(1234)]                // früher als „234“ geschrieben
    [InlineData(int.MaxValue)]
    [InlineData(-1)]                  // früher ein falsches Zeichen
    [InlineData(int.MinValue)]
    public void WriteReportLine_schreibt_jede_ID_wie_Report(int id)
    {
        byte[] report = [0, 9, 10, 99, 100, 255, 1, 128];
        Span<byte> buffer = stackalloc byte[UhidCommand.ReportLineCapacity(report.Length)];
        int n = UhidCommand.WriteReportLine(id, report, buffer);
        Assert.Equal(UhidCommand.Report(id, report) + "\n", System.Text.Encoding.ASCII.GetString(buffer[..n]));
    }

    [Fact]
    public void ReportLineCapacity_reicht_fuer_den_laengsten_Fall()
    {
        // Längste ID, jedes Byte dreistellig, und der leere Bericht.
        byte[] full = Enumerable.Repeat((byte)255, XboxBtReport.Length).ToArray();
        foreach (var report in new byte[][] { full, [] })
        {
            var buffer = new byte[UhidCommand.ReportLineCapacity(report.Length)];
            int n = UhidCommand.WriteReportLine(int.MinValue, report, buffer);
            Assert.Equal(UhidCommand.Report(int.MinValue, report) + "\n", System.Text.Encoding.ASCII.GetString(buffer, 0, n));
            Assert.InRange(buffer.Length - n, 0, 1); // höchstens ein Byte Reserve: die Schätzung ist knapp, aber sicher
        }
    }

    [Fact]
    public void WriteReportLine_meldet_zu_kleinen_Puffer()
    {
        byte[] report = XboxBtReport.Neutral();
        var buffer = new byte[UhidCommand.ReportLineCapacity(report.Length) - 1];
        Assert.Throws<ArgumentException>(() => UhidCommand.WriteReportLine(1, report, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => UhidCommand.ReportLineCapacity(-1));
    }

    [Fact]
    public void HidJson_negative_ID_unabhaengig_von_der_Sprache()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // Schwedisch schreibt negative Zahlen mit Unicode-Minus (U+2212) – im JSON wäre das ungültig.
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
            Assert.StartsWith("{\"id\":-1,", UhidCommand.Report(-1, [1]));
            Assert.StartsWith("{\"id\":-1,", UhidCommand.RegisterXbox(-1, "x"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }
}
