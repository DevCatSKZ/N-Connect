using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>Gemeinsame Farbschemata von Windows und Android.</summary>
public class ColorSchemesTests
{
    [Fact]
    public void FuenfSchemata_WindowsIstStandard()
    {
        Assert.Equal(["windows", "neon", "aurora", "sunset", "joycon"], ColorSchemes.All.Select(s => s.Id));
        Assert.Equal("windows", ColorSchemes.ById(null).Id);
        Assert.Equal("windows", ColorSchemes.ById("gibt-es-nicht").Id);
        Assert.Equal("aurora", ColorSchemes.ById("aurora").Id);
        Assert.False(ColorSchemes.ById("windows").Neon);
    }

    [Fact]
    public void Paletten_DunkelUndHellRichtigGekennzeichnet()
    {
        foreach (var s in ColorSchemes.All)
        {
            Assert.True(s.DarkPalette.Dark);
            Assert.False(s.LightPalette.Dark);
            Assert.True(ColorSchemes.Luminance(s.DarkPalette.Window) < ColorSchemes.Luminance(s.LightPalette.Window));
        }
    }

    [Fact]
    public void SchriftAufAkzent_SchwarzAufHellblauSonstWeiss()
    {
        Assert.Equal(0x000000, ColorSchemes.OnAccent(0x4CC2FF)); // Windows dunkel
        Assert.Equal(0xFFFFFF, ColorSchemes.OnAccent(0x005FB8)); // Windows hell
        Assert.Equal(0xFFFFFF, ColorSchemes.OnAccent(0xD7263D)); // Joy-Con
    }

    [Fact]
    public void Mischen()
    {
        Assert.Equal(0x000000, ColorSchemes.Blend(0x000000, 0xFFFFFF, 0f));
        Assert.Equal(0xFFFFFF, ColorSchemes.Blend(0x000000, 0xFFFFFF, 1f));
        Assert.Equal(0x7F7F7F, ColorSchemes.Blend(0x000000, 0xFFFFFF, 0.5f));
    }
}
