namespace Switch2Pro.Protocol;

/// <summary>
/// Xbox-Controller über XInput (Bluetooth, USB, Microsoft Xbox Wireless Adapter; Xbox 360, One, Series,
/// Elite usw. – Windows erkennt sie alle selbst). Diese Klasse bildet den XInput-Zustand auf das
/// einheitliche <see cref="ControllerState"/> ab – positionsgetreu: XInput-A (unten) = B, B (rechts) = A,
/// X (links) = Y, Y (oben) = X. Dasselbe Schema wie die virtuelle Ausgabe, also 1:1 weitergebbar.
/// </summary>
public static class XboxPad
{
    // Tastenbits von XINPUT_GAMEPAD.wButtons.
    public const ushort XButtonUp = 0x0001;
    public const ushort XButtonDown = 0x0002;
    public const ushort XButtonLeft = 0x0004;
    public const ushort XButtonRight = 0x0008;
    public const ushort XButtonStart = 0x0010;
    public const ushort XButtonBack = 0x0020;
    public const ushort XButtonLeftThumb = 0x0040;
    public const ushort XButtonRightThumb = 0x0080;
    public const ushort XButtonLeftShoulder = 0x0100;
    public const ushort XButtonRightShoulder = 0x0200;
    /// <summary>Xbox-/Guide-Taste – nur mit XInputGetStateEx (1.4) lesbar, XInputGetState liefert sie nicht.</summary>
    public const ushort XButtonGuide = 0x0400;
    public const ushort XButtonA = 0x1000;
    public const ushort XButtonB = 0x2000;
    public const ushort XButtonX = 0x4000;
    public const ushort XButtonY = 0x8000;

    /// <summary>Akkutypen von XInputGetBatteryInformation (BATTERY_DEVTYPE_*).</summary>
    public const int BatteryAlkaline = 0, BatteryNiMh = 1, BatteryUnknown = 2, BatteryWired = 255;

    /// <summary>16-Bit-Sticks auf den 12-Bit-Bereich (Mitte 2048, Vollausschlag ±2048).</summary>
    public static DeviceCalibration Calibration { get; } = new()
    {
        Left = new StickCalibration(new AxisCalibration(2048, 2047, 2048), new AxisCalibration(2048, 2047, 2048)),
        Right = new StickCalibration(new AxisCalibration(2048, 2047, 2048), new AxisCalibration(2048, 2047, 2048)),
    };

    /// <summary>XInput-Akkustufe (0 = leer, 1 = niedrig, 2 = mittel, 3 = voll) → Prozent; −1 bei Kabel.</summary>
    public static int BatteryPercent(byte deviceType, byte level) => deviceType switch
    {
        BatteryWired => -1,
        _ => level switch
        {
            0 => 5,
            1 => 20,
            2 => 60,
            _ => 100,
        },
    };

    /// <summary>
    /// XInput-Zustand → einheitlicher Zustand. <paramref name="buttons"/> aus wButtons (Guide nur über die
    /// erweiterte Abfrage), Trigger 0–255, Sticks −32768…32767 (Y positiv = oben, wie unser Modell).
    /// </summary>
    public static ControllerState FromXInput(ushort buttons, byte leftTrigger, byte rightTrigger,
        short leftX, short leftY, short rightX, short rightY, int batteryPercent = -1)
    {
        var b = ProButtons.None;
        if ((buttons & XButtonA) != 0) b |= ProButtons.B;      // unten
        if ((buttons & XButtonB) != 0) b |= ProButtons.A;      // rechts
        if ((buttons & XButtonX) != 0) b |= ProButtons.Y;      // links
        if ((buttons & XButtonY) != 0) b |= ProButtons.X;      // oben
        if ((buttons & XButtonLeftShoulder) != 0) b |= ProButtons.L;
        if ((buttons & XButtonRightShoulder) != 0) b |= ProButtons.R;
        if ((buttons & XButtonBack) != 0) b |= ProButtons.Minus;
        if ((buttons & XButtonStart) != 0) b |= ProButtons.Plus;
        if ((buttons & XButtonLeftThumb) != 0) b |= ProButtons.LeftStick;
        if ((buttons & XButtonRightThumb) != 0) b |= ProButtons.RightStick;
        if ((buttons & XButtonGuide) != 0) b |= ProButtons.Home;
        if ((buttons & XButtonUp) != 0) b |= ProButtons.Up;
        if ((buttons & XButtonDown) != 0) b |= ProButtons.Down;
        if ((buttons & XButtonLeft) != 0) b |= ProButtons.Left;
        if ((buttons & XButtonRight) != 0) b |= ProButtons.Right;

        static int Axis(short v) => Math.Clamp((v + 32768) * 4095 / 65535, 0, 4095);
        return new ControllerState
        {
            Kind = ControllerKind.XboxController,
            Buttons = b,
            LeftX = Axis(leftX), LeftY = Axis(leftY), RightX = Axis(rightX), RightY = Axis(rightY),
            LeftTrigger = leftTrigger, RightTrigger = rightTrigger,
            BatteryPercent = batteryPercent,
        };
    }
}
