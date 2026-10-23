using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>Gemeinsame Farbschemata von Windows und Android.</summary>
public class ColorSchemesTests
{
    [Fact]
    public void VierzehnSchemata_HoloGlasIstStandard()
    {
        Assert.Equal(
            ["hologlas", "mitternacht", "tageslicht", "nova", "abendrot", "tiefsee", "sakura", "graphit",
             "samtpfoten", "windows", "neon", "aurora", "sunset", "joycon"],
            ColorSchemes.All.Select(s => s.Id));
        Assert.Equal("hologlas", ColorSchemes.ById(null).Id);
        Assert.Equal("hologlas", ColorSchemes.ById("gibt-es-nicht").Id);
        Assert.Equal("aurora", ColorSchemes.ById("aurora").Id);
        Assert.False(ColorSchemes.ById("windows").Neon);
        Assert.False(ColorSchemes.ById("graphit").Neon);
        Assert.True(ColorSchemes.ById("hologlas").Neon);
    }

    [Fact]
    public void Paletten_DunkelUndHellRichtigGekennzeichnet()
    {
        foreach (var s in ColorSchemes.All)
        {
            Assert.False(s.LightPalette.Dark);
            if (s.Id == "tageslicht")
            {
                // helles Schema ohne dunkle Variante: beide Paletten gleich (wie in SKZ/Kartarium)
                Assert.False(s.DarkPalette.Dark);
                Assert.Equal(s.DarkPalette.Window, s.LightPalette.Window);
                continue;
            }
            Assert.True(s.DarkPalette.Dark);
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
