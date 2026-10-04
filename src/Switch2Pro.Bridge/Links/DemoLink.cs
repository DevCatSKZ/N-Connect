using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge.Links;

/// <summary>
/// Simulierter Controller für den Demo-/Prüfmodus (Start mit <c>--demo</c>): erzeugt kreisende Sticks und
/// wechselnde Tasten, damit sich mehrere Spieler, Joy-Con-Paare und die Anzeige ohne zweite Hardware prüfen lassen.
/// </summary>
internal sealed class DemoLink : IControllerLink
{
    private readonly CancellationTokenSource _cts = new();
    private readonly RateMeter _rate = new();
    private int _lost;

    public ControllerKind Kind { get; }
    public Transport Transport => Transport.BluetoothLE;
    public string Id { get; }
    public string? Address { get; }
    public DeviceCalibration Calibration { get; } = DeviceCalibration.Default;
    public ControllerInfo Info { get; }
    public ControllerState? LastState { get; private set; }
    public double ReportRate => _rate.Rate;
    public bool IsLost => Volatile.Read(ref _lost) == 1;
    public bool InGrip => Kind is ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right; // Demo: Paar im Griff
    public int PlayerIndex { get; private set; } = -1;
    public (byte Large, byte Small) Rumble { get; private set; }

    public event Action<IControllerLink, ControllerState>? StateReceived;
    public event Action<IControllerLink>? Lost;

    public DemoLink(ControllerKind kind, int number)
    {
        Kind = kind;
        Id = $"DEMO-{number}";
        Address = $"DE:M0:00:00:00:{number:X2}";
        // Pro Controller 2 in seinen Originalfarben, Joy-Con in Blau/Rot.
        Info = kind switch
        {
            ControllerKind.Pro2 => new ControllerInfo { SerialNumber = "DEMO", Firmware = "0.0.0", BodyColor = 0x232323, ButtonColor = 0xA0A0A0, GripColor = 0xE6E6E6 },
            ControllerKind.JoyCon2Left => new ControllerInfo { SerialNumber = "DEMO", Firmware = "0.0.0", BodyColor = 0x2D2E33, ButtonColor = 0xC8C8C8, GripColor = 0x1CB4DC },
            ControllerKind.JoyCon2Right => new ControllerInfo { SerialNumber = "DEMO", Firmware = "0.0.0", BodyColor = 0x2D2E33, ButtonColor = 0xC8C8C8, GripColor = 0xF2593C },
            _ => new ControllerInfo { SerialNumber = "DEMO", Firmware = "0.0.0", BodyColor = 0xE33B3B, ButtonColor = 0x1A1A1A, GripColor = 0xE33B3B },
        };
        _ = Task.Run(() => RunAsync(number, _cts.Token));
    }

    private async Task RunAsync(int seed, CancellationToken ct)
    {
        var buttons = Enum.GetValues<ProButtons>().Where(b => b != ProButtons.None).ToArray();
        long tick = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                double t = tick++ / 60.0 + seed;
                int x = 2048 + (int)(1200 * Math.Cos(t)), y = 2048 + (int)(1200 * Math.Sin(t));
                var state = new ControllerState
                {
                    Kind = Kind,
                    Buttons = buttons[(int)(t * 2) % buttons.Length],
                    LeftX = x, LeftY = y, RightX = 4096 - x, RightY = y,
                    LeftTrigger = Kind == ControllerKind.GameCube2 ? (int)(127 + 100 * Math.Sin(t)) : -1,
                    RightTrigger = Kind == ControllerKind.GameCube2 ? (int)(127 + 100 * Math.Cos(t)) : -1,
                    Motion = new Motion(0, 0, 4096, (short)(3000 * Math.Sin(t)), 0, 0),
                    BatteryPercent = 20 + seed * 37 % 80,
                };
                LastState = state;
                _rate.Tick();
                StateReceived?.Invoke(this, state);
                await Task.Delay(16, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void SetRumble(byte large, byte small, float strength) => Rumble = (large, small);

    public Task SetPlayerAsync(int playerIndex)
    {
        PlayerIndex = playerIndex;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (Interlocked.Exchange(ref _lost, 1) == 0)
            Lost?.Invoke(this);
        return ValueTask.CompletedTask;
    }
}
