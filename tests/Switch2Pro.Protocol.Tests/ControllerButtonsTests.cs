using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>Welche Tasten ein Controller hat und wie sie beschriftet sind (Belegungsfenster).</summary>
public class ControllerButtonsTests
{
    private static readonly ProButtons[] Dpad = [ProButtons.Up, ProButtons.Down, ProButtons.Left, ProButtons.Right];

    [Fact]
    public void Jede_Art_hat_eindeutige_einzelne_Tasten_mit_Beschriftung()
    {
        foreach (var kind in Enum.GetValues<ControllerKind>())
        {
            var buttons = ControllerButtons.For(kind);
            Assert.NotEmpty(buttons);
            Assert.Equal(buttons.Count, buttons.Distinct().Count());
            Assert.All(buttons, b =>
            {
                Assert.NotEqual(ProButtons.None, b);
                Assert.True(Enum.IsDefined(b), $"{kind}: {b} ist keine einzelne Taste");
                Assert.False(string.IsNullOrWhiteSpace(ControllerButtons.Label(b, kind)));
            });
        }
    }

    [Fact]
    public void Unbekannte_Art_bietet_alle_Tasten()
    {
        var all = Enum.GetValues<ProButtons>().Where(b => b != ProButtons.None).ToArray();
        Assert.Equal(all, ControllerButtons.For(ControllerKind.Unknown));
    }

    [Fact]
    public void Tasten_je_Controller()
    {
        Assert.Contains(ProButtons.GL, ControllerButtons.For(ControllerKind.Pro2));
        Assert.Contains(ProButtons.C, ControllerButtons.For(ControllerKind.Pro2));
        Assert.DoesNotContain(ProButtons.C, ControllerButtons.For(ControllerKind.Pro1));
        Assert.DoesNotContain(ProButtons.GL, ControllerButtons.For(ControllerKind.Pro1));
        // Einzelner Joy-Con quer: kein Steuerkreuz (seine Richtungstasten sind jetzt A/B/X/Y), kein rechter Stick.
        foreach (var kind in new[] { ControllerKind.JoyCon1Left, ControllerKind.JoyCon1Right, ControllerKind.JoyCon2Left, ControllerKind.JoyCon2Right })
        {
            Assert.Empty(ControllerButtons.For(kind).Intersect(Dpad));
            Assert.DoesNotContain(ProButtons.RightStick, ControllerButtons.For(kind));
        }
        Assert.Contains(ProButtons.C, ControllerButtons.For(ControllerKind.JoyCon2Right)); // C-Taste nur am Joy-Con 2 (R)
        Assert.DoesNotContain(ProButtons.C, ControllerButtons.For(ControllerKind.JoyCon1Right));
        // Xbox: Trigger sind analog (LT/RT kommen nicht als Taste), keine Aufnahme-Taste.
        Assert.DoesNotContain(ProButtons.ZL, ControllerButtons.For(ControllerKind.XboxController));
        Assert.DoesNotContain(ProButtons.Capture, ControllerButtons.For(ControllerKind.XboxController));
        Assert.Contains(ProButtons.Headset, ControllerButtons.For(ControllerKind.DualSense));
        Assert.DoesNotContain(ProButtons.Headset, ControllerButtons.For(ControllerKind.DualShock4));
        // NES: nur A, B, L, R, SELECT, START und Steuerkreuz.
        Assert.Equal([ProButtons.A, ProButtons.B, ProButtons.L, ProButtons.R, ProButtons.Minus, ProButtons.Plus, .. Dpad],
            ControllerButtons.For(ControllerKind.NesController));
    }

    [Theory]
    [InlineData(ProButtons.B, ControllerKind.GameCube2, "A (groß, unten)")]
    [InlineData(ProButtons.ZR, ControllerKind.GameCube2, "Z")]
    [InlineData(ProButtons.Plus, ControllerKind.GameCube2, "START/PAUSE")]
    [InlineData(ProButtons.Up, ControllerKind.GameCube2, "Steuerkreuz hoch")]       // nicht eigens benannt: allgemein
    [InlineData(ProButtons.Minus, ControllerKind.NesController, "SELECT")]
    [InlineData(ProButtons.B, ControllerKind.N64Controller, "A")]
    [InlineData(ProButtons.ZL, ControllerKind.N64Controller, "Z")]
    [InlineData(ProButtons.A, ControllerKind.MegaDrive, "C")]
    [InlineData(ProButtons.Minus, ControllerKind.MegaDrive, "MODE")]
    [InlineData(ProButtons.Y, ControllerKind.WiiRemote, "1 (quer) / Classic Y")]
    [InlineData(ProButtons.Minus, ControllerKind.DualShock4, "Share")]
    [InlineData(ProButtons.Minus, ControllerKind.DualSense, "Create")]
    [InlineData(ProButtons.B, ControllerKind.DualSense, "Kreuz (unten)")]
    [InlineData(ProButtons.GL, ControllerKind.DualSense, "Rücktaste links (Edge)")]
    [InlineData(ProButtons.B, ControllerKind.XboxController, "A")]
    [InlineData(ProButtons.ZL, ControllerKind.XboxController, "LT")]
    [InlineData(ProButtons.Home, ControllerKind.XboxController, "Xbox-Taste")]
    [InlineData(ProButtons.ZL, ControllerKind.JoyCon2Left, "L")]
    [InlineData(ProButtons.ZL, ControllerKind.JoyCon1Right, "R")]
    [InlineData(ProButtons.Plus, ControllerKind.JoyCon1Left, "−")]
    [InlineData(ProButtons.Home, ControllerKind.JoyCon1Left, "Aufnahme")]
    [InlineData(ProButtons.Home, ControllerKind.JoyCon2Right, "HOME")]
    [InlineData(ProButtons.L, ControllerKind.JoyCon1Right, "SL")]
    [InlineData(ProButtons.L, ControllerKind.JoyConPair, "L")]                      // Paar: wie ein Pro Controller
    [InlineData(ProButtons.Minus, ControllerKind.Pro2, "− (Minus)")]
    [InlineData(ProButtons.GR, ControllerKind.Pro2, "GR (Rücktaste rechts)")]
    [InlineData(ProButtons.SLLeft, ControllerKind.Unknown, "SLLeft")]
    public void Beschriftung_wie_auf_dem_Controller(ProButtons button, ControllerKind kind, string expected) =>
        Assert.Equal(expected, ControllerButtons.Label(button, kind));
}
