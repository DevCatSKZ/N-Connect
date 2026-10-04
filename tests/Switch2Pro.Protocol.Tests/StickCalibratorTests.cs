using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>Geführte Stick-Kalibrierung: Mitte, Rand im Kreis, Ergebnis, Rundheit, Fehlerfälle.</summary>
public class StickCalibratorTests
{
    /// <summary>Stick mit verschobener Mitte (Drift) und ungleichem Ausschlag simulieren.</summary>
    private static (int X, int Y) Stick(double angle, double radius, int cx = 2100, int cy = 1990, int rx = 1500, int ry = 1400) =>
        ((int)(cx + Math.Cos(angle) * radius * rx), (int)(cy + Math.Sin(angle) * radius * ry));

    private static StickCalibrator Measure(double edgeRadius = 1.0)
    {
        var c = new StickCalibrator();
        long t = 0;
        for (int i = 0; i < 120; i++, t += 16)
        {
            var (x, y) = Stick(0, 0);
            c.Add(x + i % 3 - 1, y, t); // leichtes Rauschen
        }
        Assert.Equal(StickCalibrator.Phase.Rotate, c.Current);
        for (int i = 0; i < 400 && c.Current == StickCalibrator.Phase.Rotate; i++, t += 16)
        {
            var (x, y) = Stick(i * 0.05, edgeRadius);
            c.Add(x, y, t);
        }
        return c;
    }

    [Fact]
    public void MisstMitteUndAusschlag()
    {
        var c = Measure();
        Assert.Equal(StickCalibrator.Phase.Done, c.Current);
        var r = c.Result!.Value;
        Assert.InRange(r.X.Neutral, 2099, 2101);
        Assert.InRange(r.Y.Neutral, 1989, 1991);
        Assert.InRange(r.X.Max, 1400, 1500);
        Assert.InRange(r.Y.Min, 1300, 1400);
    }

    [Fact]
    public void NeueKalibrierung_RandWirdVollerAusschlag_MitteNull()
    {
        var c = Measure();
        var r = c.Result!.Value;
        Assert.Equal(0f, r.X.Normalize(2100), 2);
        var (x, _) = Stick(0, 1.0);
        Assert.Equal(1f, r.X.Normalize(x), 2);
        Assert.True(c.RoundnessError(r) < 10);
    }

    [Fact]
    public void BewegterStickBeimLoslassen_MisstMitteNeu()
    {
        var c = new StickCalibrator();
        c.Add(2100, 2000, 0);
        c.Add(2100, 2000, 1000);
        c.Add(2600, 2000, 1400); // bewegt
        c.Add(2100, 2000, 1600);
        Assert.Equal(StickCalibrator.Phase.Center, c.Current);
        Assert.True(c.Progress < 0.5f);
    }

    [Fact]
    public void NichtBisZumRand_KeinErgebnis()
    {
        // Nur knapp über der Erfassungsschwelle gekreist: Ausschlag zu klein, aber alle Richtungen erfasst.
        var c = new StickCalibrator();
        long t = 0;
        for (int i = 0; i < 120; i++, t += 16)
            c.Add(2048, 2048, t);
        for (int i = 0; i < 400 && c.Current == StickCalibrator.Phase.Rotate; i++, t += 16)
            c.Add((int)(2048 + Math.Cos(i * 0.05) * 380), (int)(2048 + Math.Sin(i * 0.05) * 380), t);
        Assert.NotEqual(StickCalibrator.Phase.Done, c.Current); // 380 < Randschwelle – nie fertig
        Assert.Null(c.Result);
    }

    [Fact]
    public void Einstellungen_StickKalibrierungSetzenEntfernen()
    {
        var s = new Settings();
        var cal = new StickCalibration(new AxisCalibration(2100, 1400, 1450), new AxisCalibration(1990, 1300, 1350));
        s.SetStickCalibration("AA:BB", left: true, cal);
        Assert.Equal(cal, s.StickCalibrationFor("aa:bb", left: true));
        Assert.Null(s.StickCalibrationFor("AA:BB", left: false));
        s.SetStickCalibration("AA:BB", left: true, null);
        Assert.Null(s.StickCalibrationFor("AA:BB", left: true));
    }

    [Fact]
    public void Einstellungen_KalibrierungUeberlebtSpeichern()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nconnect-test-{Guid.NewGuid():N}.json");
        try
        {
            var s = new Settings();
            var cal = new StickCalibration(new AxisCalibration(2100, 1400, 1450), new AxisCalibration(1990, 1300, 1350));
            s.SetStickCalibration("AA:BB", left: false, cal);
            s.Save(path);
            Assert.Equal(cal, Settings.Load(path).StickCalibrationFor("AA:BB", left: false));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
