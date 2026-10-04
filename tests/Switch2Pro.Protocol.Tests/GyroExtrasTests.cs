using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>Gyro-Extras nach JoyShockMapper: Flick-Stick, Gyro-Beschleunigung, „Gyro anhalten“.</summary>
public class GyroExtrasTests
{
    private static float Run(FlickStick flick, float x, float y, int steps, float dt = 0.02f, float time = 0.1f)
    {
        float sum = 0f;
        for (int i = 0; i < steps; i++)
            sum += flick.Update(x, y, dt, time);
        return sum;
    }

    [Fact]
    public void Flick_DrehtZurStickRichtung_UeberDieFlickDauer()
    {
        var flick = new FlickStick();
        float first = flick.Update(1f, 0f, 0f, 0.1f); // rechts → +90°, beginnt gerade
        Assert.True(first < 90f);
        float total = first + Run(flick, 1f, 0f, 10);
        Assert.Equal(90f, total, 3);
        Assert.Equal(0f, Run(flick, 1f, 0f, 5), 3); // bleibt der Stick dort: keine weitere Drehung
    }

    [Fact]
    public void Flick_Sofort_WennDauerNull()
    {
        var flick = new FlickStick();
        Assert.Equal(-90f, flick.Update(-1f, 0f, 0.016f, 0f), 3); // links
        flick.Update(0f, 0f, 0.016f, 0f); // loslassen
        Assert.Equal(180f, MathF.Abs(flick.Update(0f, -1f, 0.016f, 0f)), 3); // nach hinten
    }

    [Fact]
    public void Flick_StickAmRandDrehen_DrehtMit_UeberDieRueckseite()
    {
        var flick = new FlickStick();
        flick.Update(0f, -1f, 0f, 0f); // nach hinten: ±180°
        // Am Rand weiter von „hinten links“ nach „hinten rechts“ = kleine Drehung, nicht fast 360°.
        float a = flick.Update(-0.1f, -0.995f, 0.016f, 0f);
        float b = flick.Update(0.1f, -0.995f, 0.016f, 0f);
        Assert.True(MathF.Abs(b) < 20f, $"Sprung über die Rückseite: {b}");
        Assert.True(MathF.Abs(a) < 20f);
    }

    [Fact]
    public void Flick_KleinerAusschlag_LoestNichtsAus()
    {
        var flick = new FlickStick();
        Assert.Equal(0f, Run(flick, 0.6f, 0.3f, 10));
        Assert.False(flick.Active);
    }

    [Fact]
    public void Beschleunigung_LinearZwischenDenSchwellen()
    {
        var s = new Settings { GyroAcceleration = 2f, GyroAccelSlow = 20f, GyroAccelFast = 120f };
        Assert.Equal(1f, Mapping.GyroAccelFactor(10f, s));
        Assert.Equal(1.5f, Mapping.GyroAccelFactor(70f, s), 3);
        Assert.Equal(2f, Mapping.GyroAccelFactor(500f, s));
        Assert.Equal(1f, Mapping.GyroAccelFactor(500f, new Settings())); // Standard: aus
    }

    [Fact]
    public void Beschleunigung_WirktAufGyroStick()
    {
        // 60 °/s gieren: ohne Beschleunigung schwächer als mit.
        short raw = (short)(-60f * 32767f / 2000f);
        var motion = new Motion(0, 0, 0, 0, 0, raw);
        var plain = new Settings { GyroStickAntiDeadzone = 0f, GyroStickFullSpeed = 200f };
        var fast = new Settings { GyroStickAntiDeadzone = 0f, GyroStickFullSpeed = 200f, GyroAcceleration = 3f };
        Assert.True(Mapping.GyroToStick(motion, fast, 0, 0).X > Mapping.GyroToStick(motion, plain, 0, 0).X);
    }

    [Fact]
    public void GyroAnhalten_IstAlsTastenaktionSpeicherbar()
    {
        var action = ButtonAction.Parse("GyroPause");
        Assert.Equal(SpecialAction.GyroPause, action.Special);
        Assert.Equal("GyroPause", action.ToString());
    }

    [Fact]
    public void Einstellungen_WerdenAufgeraeumt()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nconnect-test-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "GyroAcceleration": 99, "FlickCountsPer360": 5, "FlickTime": -1, "FlickStick": true, "GameLeds": false }""");
            var s = Settings.Load(path);
            Assert.Equal(4f, s.GyroAcceleration);
            Assert.Equal(100, s.FlickCountsPer360);
            Assert.Equal(0f, s.FlickTime);
            Assert.True(s.FlickStick);
            Assert.False(s.GameLeds);
            var copy = new Settings();
            copy.CopyFrom(s);
            Assert.True(copy.FlickStick);
            Assert.Equal(100, copy.FlickCountsPer360);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
