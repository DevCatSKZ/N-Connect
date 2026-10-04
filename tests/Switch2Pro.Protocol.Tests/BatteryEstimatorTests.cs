using Switch2Pro.Protocol;

namespace Switch2Pro.Protocol.Tests;

/// <summary>Akkuschätzung aus der Spannung: Glättung, Laden (Ladespannung herausgerechnet, nur steigend), Entladen.</summary>
public class BatteryEstimatorTests
{
    private const ControllerKind Pro = ControllerKind.Pro2;

    [Fact]
    public void ErsterWert_WieKennlinie()
    {
        var e = new BatteryEstimator();
        Assert.Equal(InputReports.BatteryPercentFromMillivolts(3800, Pro), e.Update(3800, false, Pro, 1000));
    }

    [Fact]
    public void OhneSpannung_BleibtUnbekannt()
    {
        Assert.Equal(-1, new BatteryEstimator().Update(0, false, Pro, 1000));
    }

    [Fact]
    public void Anstecken_ZeigtNichtSofortVoll()
    {
        var e = new BatteryEstimator();
        int before = e.Update(3700, false, Pro, 1000);
        // Am Kabel springt die Spannung um die Ladespannung nach oben.
        int plugged = e.Update(3700 + BatteryEstimator.DefaultChargeOffsetMillivolts(Pro), true, Pro, 1100);
        Assert.Equal(before, plugged);
        Assert.True(plugged < 50);
    }

    [Fact]
    public void Laden_StandSteigtLaufendMit()
    {
        var e = new BatteryEstimator();
        e.Update(3700, false, Pro, 1000);
        int offset = BatteryEstimator.DefaultChargeOffsetMillivolts(Pro);
        long t = 2000;
        int last = e.Update(3700 + offset, true, Pro, t);
        int first = last;
        for (int mv = 3700; mv <= 4000; mv += 10)
        {
            t += 10_000;
            int now = e.Update(mv + offset, true, Pro, t);
            Assert.True(now >= last); // nie sinkend beim Laden
            last = now;
        }
        Assert.True(last > first + 20);
    }

    [Fact]
    public void Laden_KleinerSpannungsabfallLässtAnzeigeStehen()
    {
        var e = new BatteryEstimator();
        int offset = BatteryEstimator.DefaultChargeOffsetMillivolts(Pro);
        int a = e.Update(3850 + offset, true, Pro, 1000);
        int b = e.Update(3800 + offset, true, Pro, 30_000);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Entladen_Schwankungen_ZeigenKeinenAnstieg()
    {
        var e = new BatteryEstimator();
        int a = e.Update(3800, false, Pro, 1000);
        int b = e.Update(3815, false, Pro, 30_000); // Messrauschen nach oben
        Assert.Equal(a, b);
        int c = e.Update(3700, false, Pro, 120_000);
        Assert.True(c < a);
    }

    [Fact]
    public void Glättung_EinzelnerAusreißerSpringtNicht()
    {
        var e = new BatteryEstimator();
        int a = e.Update(3800, false, Pro, 1000);
        int b = e.Update(3500, false, Pro, 1016); // ein einzelner Bericht 16 ms später
        Assert.True(a - b <= 1);
    }

    [Fact]
    public void Anstecken_MisstSpannungssprung_GemessenAmProController()
    {
        // Gemessen: ohne Kabel 3704 mV (≈ 26 %), mit Kabel 3724 mV.
        var e = new BatteryEstimator();
        int? measured = null;
        e.ChargeOffsetMeasured += o => measured = o;
        int before = e.Update(3704, false, Pro, 1000);
        e.Update(3724, true, Pro, 2000);
        int after = e.Update(3724, true, Pro, 6000);
        Assert.Equal(20, measured);
        Assert.Equal(before, after);
    }

    [Fact]
    public void GespeicherterSprung_StimmtAuchNachNeustartMitKabel()
    {
        var e = new BatteryEstimator(chargeOffsetMillivolts: 20);
        Assert.Equal(InputReports.BatteryPercentFromMillivolts(3704, Pro), e.Update(3724, true, Pro, 1000));
    }

    [Fact]
    public void LadenEnde_ÜbernimmtHöherenEchtenStand()
    {
        // Gemessen: mit Kabel 3724 mV (Anzeige 24 % bei zu großem Abzug), danach ohne Laden 3705 mV ≈ 26 %.
        var e = new BatteryEstimator(chargeOffsetMillivolts: 30);
        int charging = e.Update(3724, true, Pro, 1000);
        int after = e.Update(3705, false, Pro, 31_000);
        Assert.True(after > charging);
        Assert.Equal(InputReports.BatteryPercentFromMillivolts(3705, Pro), after);
    }

    [Fact]
    public void Abstecken_ZeigtEchtenStand()
    {
        var e = new BatteryEstimator();
        int offset = BatteryEstimator.DefaultChargeOffsetMillivolts(Pro);
        e.Update(3900 + offset, true, Pro, 1000);
        int unplugged = e.Update(3900, false, Pro, 2000);
        Assert.Equal(InputReports.BatteryPercentFromMillivolts(3900, Pro), unplugged);
    }
}
