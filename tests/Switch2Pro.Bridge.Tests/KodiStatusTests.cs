namespace Switch2Pro.Bridge.Tests;

/// <summary>Vertrag der status.json mit dem Kodi-Add-on: "bluetooth" ist genau einer von on | off | missing | no_le.</summary>
public class KodiStatusTests
{
    [Theory]
    [InlineData(nameof(BluetoothAdapterState.On), "on")]
    [InlineData(nameof(BluetoothAdapterState.Off), "off")]
    [InlineData(nameof(BluetoothAdapterState.Missing), "missing")]
    [InlineData(nameof(BluetoothAdapterState.NoLe), "no_le")]
    public void BluetoothWert_NachVertrag(string state, string expected) =>
        Assert.Equal(expected, KodiStatusWriter.BluetoothValue(Enum.Parse<BluetoothAdapterState>(state)));

    [Fact]
    public void JederZustandHatEinenVertragswert()
    {
        string[] allowed = ["on", "off", "missing", "no_le"];
        var values = Enum.GetValues<BluetoothAdapterState>().Select(KodiStatusWriter.BluetoothValue).ToList();
        Assert.All(values, v => Assert.Contains(v, allowed));
        Assert.Equal(values.Count, values.Distinct().Count()); // kein Zustand fällt mit einem anderen zusammen
    }
}
