using System.Text.Json.Nodes;
using SkzConnect.Android.Kodi;
using SkzConnect.Android.Profiles;

namespace SkzConnect.Android.Tests;

/// <summary>App im Vordergrund (ohne SKZ Connect und Startbildschirm) und status.json-Vertrag mit dem Kodi-Add-on.</summary>
public class ForegroundAndStatusTests
{
    private const string Own = "com.devcatskz.skzconnect";
    private static readonly HashSet<string> Launchers = ["com.google.android.apps.nexuslauncher", "com.android.settings"];

    [Fact]
    public void Vordergrund_IgnoriertEigeneAppUndStartbildschirm()
    {
        // Spiel → SKZ Connect → Startbildschirm: das Spiel bleibt die erkannte App (vorher sprang das Profil um).
        (long, string)[] events = [(1000, "com.mojang.minecraftpe"), (2000, Own), (3000, "com.google.android.apps.nexuslauncher")];
        Assert.Equal("com.mojang.minecraftpe", GameProfileWatcher.Pick(events, Own, Launchers));
    }

    [Fact]
    public void Vordergrund_NeuesteAppGewinnt()
    {
        (long, string)[] events = [(3000, "org.dolphinemu.dolphinemu"), (1000, "com.mojang.minecraftpe"), (2000, "com.android.settings")];
        Assert.Equal("org.dolphinemu.dolphinemu", GameProfileWatcher.Pick(events, Own, Launchers));
    }

    [Fact]
    public void Vordergrund_NurEigeneAppOderNichts_Null()
    {
        Assert.Null(GameProfileWatcher.Pick([(1000, Own)], Own, Launchers));
        Assert.Null(GameProfileWatcher.Pick([], Own, Launchers));
    }

    [Theory]
    [InlineData(false, true, true, "missing")]
    [InlineData(false, false, false, "missing")]
    [InlineData(true, false, true, "no_le")]
    [InlineData(true, false, false, "no_le")]
    [InlineData(true, true, true, "on")]
    [InlineData(true, true, false, "off")]
    public void Bluetooth_WerteWieImVertrag(bool adapter, bool lowEnergy, bool enabled, string expected) =>
        Assert.Equal(expected, KodiStatusWriter.BluetoothState(adapter, lowEnergy, enabled));

    [Fact]
    public void Plattform_IstAndroid() => Assert.Equal("android", KodiStatusWriter.Platform);

    private static JsonObject Status(double rate, int battery) => new()
    {
        ["state"] = "running",
        ["platform"] = "android",
        ["bluetooth"] = "on",
        ["players"] = new JsonArray(new JsonObject
        {
            ["player"] = 1,
            ["devices"] = new JsonArray(new JsonObject { ["address"] = "AA", ["battery"] = battery, ["rate"] = rate }),
        }),
    };

    [Fact]
    public void Aenderungspruefung_IgnoriertNurDieBerichtsrate()
    {
        Assert.Equal(KodiStatusWriter.ChangeKey(Status(118, 80)), KodiStatusWriter.ChangeKey(Status(121, 80)));
        Assert.NotEqual(KodiStatusWriter.ChangeKey(Status(118, 80)), KodiStatusWriter.ChangeKey(Status(118, 75)));
        // Die geschriebene Datei behält die Rate (nur der Vergleich lässt sie weg).
        var status = Status(118, 80);
        KodiStatusWriter.ChangeKey(status);
        Assert.Equal(118, status["players"]![0]!["devices"]![0]!["rate"]!.GetValue<double>());
    }
}
