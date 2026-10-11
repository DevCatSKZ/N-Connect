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
}
