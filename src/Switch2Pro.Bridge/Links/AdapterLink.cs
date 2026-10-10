using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge.Links;

/// <summary>
/// Ein Switch-2-Controller, der nicht direkt am PC, sondern an einem Funkadapter (ESP32/nRF52840) hängt und dessen
/// Eingaben über USB kommen (siehe docs/ADAPTER-PROTOKOLL.md). Der Adapter reicht den rohen Bericht 0x05 durch;
/// N-Connect parst ihn mit <see cref="InputReports.TryParseReport05"/> – genau wie bei einer direkten BLE-Verbindung.
/// Vibration und Spieler-LED gehen als kurze Befehle an den Adapter zurück (<paramref name="send"/>).
/// </summary>
internal sealed class AdapterLink(byte slot, ControllerKind kind, string address, Action<byte[]> send) : IControllerLink
{
    private readonly RateMeter _rate = new();
    private int _lostRaised;

    public byte Slot { get; } = slot;
    public ControllerKind Kind { get; } = kind;
    // Es ist ein BLE-Controller, nur über den Adapter weitergereicht – als Transport „Bluetooth LE“ anzeigen.
    public Transport Transport => Transport.BluetoothLE;
    public string Id { get; } = $"ADAPTER:{slot}:{address}";
    public string? Address { get; } = BtAddress.TryNormalize(address, out var a) ? a : address;
    public DeviceCalibration Calibration => DeviceCalibration.Default;
    public ControllerInfo Info { get; } = new();
    public ControllerState? LastState { get; private set; }
    public double ReportRate => _rate.Rate;
    public bool IsLost => Volatile.Read(ref _lostRaised) == 1;

    public event Action<IControllerLink, ControllerState>? StateReceived;
    public event Action<IControllerLink>? Lost;

    /// <summary>Rohen Bericht 0x05 vom Adapter verarbeiten (vom Lese-Task aufgerufen, wie bei der direkten BLE-Verbindung).</summary>
    public void Feed(ReadOnlySpan<byte> report)
    {
        if (IsLost || !InputReports.TryParseReport05(report, out var state, Kind))
            return;
        _rate.Tick();
        LastState = state;
        try { StateReceived?.Invoke(this, state); }
        catch (Exception e) { Log.Error($"{Id}: Eingabe verarbeiten", e); }
    }

    /// <summary>Verbindung als beendet melden (genau einmal).</summary>
    public void MarkLost()
    {
        if (Interlocked.Exchange(ref _lostRaised, 1) == 0)
            Lost?.Invoke(this);
    }

    public void SetRumble(byte large, byte small, float strength)
    {
        byte Scale(byte v) => (byte)Math.Clamp((int)MathF.Round(v * Math.Clamp(strength, 0f, 1f)), 0, 255);
        try { send(AdapterProtocol.Encode(AdapterMessage.Rumble, Slot, [Scale(large), Scale(small)])); }
        catch (Exception e) { Log.Warn($"{Id}: Vibration an Adapter: {Log.Reason(e)}"); }
    }

    public Task SetPlayerAsync(int playerIndex)
    {
        // Muster wie an der Switch: Spieler 1–8 → 01 03 07 0F 09 05 0D 06.
        byte[] patterns = [0x01, 0x03, 0x07, 0x0F, 0x09, 0x05, 0x0D, 0x06];
        byte pattern = patterns[Math.Clamp(playerIndex, 0, patterns.Length - 1)];
        try { send(AdapterProtocol.Encode(AdapterMessage.PlayerLed, Slot, [pattern])); }
        catch (Exception e) { Log.Warn($"{Id}: Spieler-LED an Adapter: {Log.Reason(e)}"); }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        MarkLost();
        return ValueTask.CompletedTask;
    }
}
