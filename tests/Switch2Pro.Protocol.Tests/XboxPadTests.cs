using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>Xbox-Controller (XInput): Abbildung der Tasten/Achsen/Trigger auf das einheitliche Modell und Akkustufen.</summary>
public class XboxPadTests
{
    [Fact]
    public void Ruhezustand_MitteUndKeineTasten()
    {
        var s = XboxPad.FromXInput(0, 0, 0, 0, 0, 0, 0);
        Assert.Equal(ControllerKind.XboxController, s.Kind);
        Assert.Equal(ProButtons.None, s.Buttons);
        Assert.InRange(s.LeftX, 2008, 2088);
        Assert.InRange(s.LeftY, 2008, 2088);
        Assert.Equal(0, s.LeftTrigger);
        Assert.Equal(0, s.RightTrigger);
        Assert.Equal(-1, s.BatteryPercent);
    }

    [Fact]
    public void Tasten_Positionsgetreu()
    {
        // XInput A (unten) → B, B (rechts) → A, X (links) → Y, Y (oben) → X.
        var s = XboxPad.FromXInput(
            XboxPad.XButtonA | XboxPad.XButtonB | XboxPad.XButtonX | XboxPad.XButtonY
            | XboxPad.XButtonStart | XboxPad.XButtonBack | XboxPad.XButtonGuide
            | XboxPad.XButtonLeftShoulder | XboxPad.XButtonRightShoulder
            | XboxPad.XButtonLeftThumb | XboxPad.XButtonRightThumb
            | XboxPad.XButtonUp | XboxPad.XButtonDown | XboxPad.XButtonLeft | XboxPad.XButtonRight,
            0, 0, 0, 0, 0, 0);
        Assert.Equal(
            ProButtons.B | ProButtons.A | ProButtons.Y | ProButtons.X
            | ProButtons.Plus | ProButtons.Minus | ProButtons.Home
            | ProButtons.L | ProButtons.R | ProButtons.LeftStick | ProButtons.RightStick
            | ProButtons.Up | ProButtons.Down | ProButtons.Left | ProButtons.Right,
            s.Buttons);
    }

    [Fact]
    public void Sticks_VollerAusschlag()
    {
        var s = XboxPad.FromXInput(0, 0, 0, short.MinValue, short.MaxValue, 32767, -32768);
        Assert.Equal(0, s.LeftX);
        Assert.Equal(4095, s.LeftY);
        Assert.Equal(4095, s.RightX);
        Assert.Equal(0, s.RightY);
    }

    [Fact]
    public void Trigger_0Bis255()
    {
        var s = XboxPad.FromXInput(0, 200, 255, 0, 0, 0, 0);
        Assert.Equal(200, s.LeftTrigger);
        Assert.Equal(255, s.RightTrigger);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 20)]
    [InlineData(2, 60)]
    [InlineData(3, 100)]
    public void Akku_Stufen(byte level, int percent) =>
        Assert.Equal(percent, XboxPad.BatteryPercent(XboxPad.BatteryAlkaline, level));

    [Fact]
    public void Akku_Kabel_IstUnbekannt() =>
        Assert.Equal(-1, XboxPad.BatteryPercent(XboxPad.BatteryWired, 3));
}
