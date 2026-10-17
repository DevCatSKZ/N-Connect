namespace Switch2Pro.Bridge.Tests;

/// <summary>
/// „Schon 8 Controller verbunden“: einmal je Versuch – die stillen Wiederholungen alle ~10 s melden nicht erneut, ein
/// späterer neuer Versuch (Gerät war aus bzw. abgezogen) aber schon. Galt die Meldung je Gerät nur einmal je Sitzung,
/// wirkte ein später erneut abgewiesener Controller einfach tot.
/// </summary>
public sealed class RefusalNoticesTests
{
    private const string Pro = @"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002057e_pid&2009#9&1&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    [Fact]
    public void ErsteAbweisungMeldet_WiederholungenNicht()
    {
        var notices = new RefusalNotices();
        long t = 1_000;
        Assert.True(notices.ShouldNotify(Pro, t));
        // Wiederholung nach Wartezeit (10 s) + Suchtakt + Verbindungsaufbau – gut vier Minuten lang.
        for (int i = 0; i < 20; i++)
        {
            t += 13_000;
            Assert.False(notices.ShouldNotify(Pro, t));
        }
        // Längster Abstand ohne Pause: Wartezeit 10 s + Bluetooth-LE-Verbindungsaufbau bis 30 s.
        t += 42_000;
        Assert.False(notices.ShouldNotify(Pro, t));
    }

    [Fact]
    public void SpaetererNeuerVersuch_MeldetWieder()
    {
        var notices = new RefusalNotices();
        Assert.True(notices.ShouldNotify(Pro, 0));
        Assert.False(notices.ShouldNotify(Pro, 12_000));
        // Controller aus, Minuten später wieder an – alle Plätze noch belegt.
        Assert.True(notices.ShouldNotify(Pro, 12_000 + 5 * 60_000));
        Assert.False(notices.ShouldNotify(Pro, 12_000 + 5 * 60_000 + 12_000));
        Assert.True(notices.ShouldNotify(Pro, 12_000 + 5 * 60_000 + 12_000 + RefusalNotices.PauseMs + 1));
    }

    [Fact]
    public void NachAnnahme_IstDieNaechsteAbweisungEinNeuerVersuch()
    {
        var notices = new RefusalNotices();
        Assert.True(notices.ShouldNotify("XINPUT:0", 0));
        notices.Forget("XINPUT:0"); // Platz frei geworden, Controller angenommen
        Assert.True(notices.ShouldNotify("XINPUT:0", 5_000));
    }

    [Fact]
    public void JedesGeraetFuerSich_GrossschreibungEgal()
    {
        var notices = new RefusalNotices();
        Assert.True(notices.ShouldNotify("A4:AE:12:34:56:78", 0));
        Assert.True(notices.ShouldNotify("XINPUT:1", 1_000));
        Assert.False(notices.ShouldNotify("a4:ae:12:34:56:78", 2_000));
        Assert.False(notices.ShouldNotify("XINPUT:1", 3_000));
    }
}
