using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>Lizenzierte Kabel-Gamepads (HORI, PowerA, PDP): Erkennung am Gerätepfad und Bericht zerlegen.</summary>
public class WiredSwitchPadTests
{
    [Theory]
    [InlineData(@"\\?\hid#vid_0f0d&pid_00c1#7&2a&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", "HORIPAD für Nintendo Switch")]
    [InlineData(@"\\?\HID#VID_20D6&PID_A711#7&1&0&0000#{4d1e55b2}", "PowerA Switch-Controller (Kabel)")]
    [InlineData(@"\\?\hid#vid_0e6f&pid_0180#x", "PDP Switch-Controller (Kabel)")]
    public void Erkennung_BekannteGeraete(string path, string name) => Assert.Equal(name, WiredSwitchPad.NameFromHidPath(path));

    [Theory]
    [InlineData(@"\\?\hid#vid_057e&pid_2009#x")] // echter Pro Controller: eigenes Protokoll
    [InlineData(@"\\?\hid#vid_045e&pid_02ea#x")] // Xbox-Controller
    public void Erkennung_AndereGeraete(string path) => Assert.Null(WiredSwitchPad.NameFromHidPath(path));

    [Fact]
    public void Ruhezustand_MitteUndKeineTasten()
    {
        Assert.True(WiredSwitchPad.TryParse([0, 0, 0x0F, 0x80, 0x80, 0x80, 0x80, 0], out var s));
        Assert.Equal(ProButtons.None, s.Buttons);
        Assert.Equal(ControllerKind.Pro1, s.Kind);
        var cal = WiredSwitchPad.Calibration;
        Assert.Equal(0f, cal.Left.X.Normalize(s.LeftX), 1);
        Assert.Equal(0f, cal.Left.Y.Normalize(s.LeftY), 1);
    }

    [Fact]
    public void Tasten_NachBeschriftung()
    {
        Assert.True(WiredSwitchPad.TryParse([0x01 | 0x04 | 0x80, 0x02 | 0x10, 0x08, 0x80, 0x80, 0x80, 0x80], out var s));
        Assert.Equal(ProButtons.Y | ProButtons.A | ProButtons.ZR | ProButtons.Plus | ProButtons.Home, s.Buttons);
    }

    [Theory]
    [InlineData(0, ProButtons.Up)]
    [InlineData(1, ProButtons.Up | ProButtons.Right)]
    [InlineData(4, ProButtons.Down)]
    [InlineData(6, ProButtons.Left)]
    [InlineData(8, ProButtons.None)]
    public void Steuerkreuz(byte hat, ProButtons expected)
    {
        Assert.True(WiredSwitchPad.TryParse([0, 0, hat, 0x80, 0x80, 0x80, 0x80], out var s));
        Assert.Equal(expected, s.Buttons);
    }

    [Fact]
    public void Sticks_ObenIstPositiv_VollerAusschlag()
    {
        // Linker Stick ganz nach oben (Y = 0), rechter ganz nach rechts (X = 255).
        Assert.True(WiredSwitchPad.TryParse([0, 0, 0x0F, 0x80, 0x00, 0xFF, 0x80], out var s));
        var cal = WiredSwitchPad.Calibration;
        Assert.Equal(1f, cal.Left.Y.Normalize(s.LeftY), 2);
        Assert.Equal(1f, cal.Right.X.Normalize(s.RightX), 2);
    }

    [Fact]
    public void ZuKurzOderUngueltigesSteuerkreuz_Abgelehnt()
    {
        Assert.False(WiredSwitchPad.TryParse([0, 0, 0x0F], out _));
        Assert.False(WiredSwitchPad.TryParse([0, 0, 0x0A, 0x80, 0x80, 0x80, 0x80], out _));
    }
}
