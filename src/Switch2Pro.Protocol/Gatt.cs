namespace Switch2Pro.Protocol;

/// <summary>
/// GATT-Attribute des Switch 2 Pro Controllers.
/// Quelle: ndeadly/switch2_controller_research, bluetooth_interface.md.
/// Der Controller spricht kein HID-over-GATT und kein SMP-Pairing, daher
/// erkennt Windows ihn nicht von selbst als Gamepad.
/// </summary>
public static class Gatt
{
    /// <summary>Herstellerkennung in der BLE-Werbung (Nintendo).</summary>
    public const ushort NintendoCompanyId = 0x0553;

    public const ushort NintendoVendorId = 0x057E;
    public const ushort ProController2ProductId = 0x2069;

    /// <summary>Dienst, unter dem alle HID-ähnlichen Berichte liegen.</summary>
    public static readonly Guid HidService = new("ab7de9be-89fe-49ad-828f-118f09df7fd0");

    /// <summary>Eingabebericht 0x05 (für alle Controller gleich, enthält Gyro im Klartext).</summary>
    public static readonly Guid InputReportCommon = new("ab7de9be-89fe-49ad-828f-118f09df7fd2");

    /// <summary>Eingabebericht 0x09 (nur Pro Controller 2). Nur Ausweichweg.</summary>
    public static readonly Guid InputReportPro = new("7492866c-ec3e-4619-8258-32755ffcc0f8");

    /// <summary>Ausgabebericht 0x02: HD-Rumble links + rechts (Schreiben ohne Antwort).</summary>
    public static readonly Guid ProRumbleOutput = new("cc483f51-9258-427d-a939-630c31f72b05");

    /// <summary>Vibration linker Joy-Con 2.</summary>
    public static readonly Guid JoyConLeftRumbleOutput = new("289326cb-a471-485d-a8f4-240c14f18241");

    /// <summary>Vibration rechter Joy-Con 2.</summary>
    public static readonly Guid JoyConRightRumbleOutput = new("fa19b0fb-cd1f-46a7-84a1-bbb09e00c149");

    /// <summary>Vibrationskanal je Controller-Art (GameCube: wie Pro, falls vorhanden).</summary>
    public static Guid RumbleOutput(ControllerKind kind) => kind switch
    {
        ControllerKind.JoyCon2Left => JoyConLeftRumbleOutput,
        ControllerKind.JoyCon2Right => JoyConRightRumbleOutput,
        _ => ProRumbleOutput,
    };

    /// <summary>Befehlskanal (Schreiben ohne Antwort).</summary>
    public static readonly Guid CommandOutput = new("649d4ac9-8eb7-4e6c-af44-1ea54fe5f005");

    /// <summary>Antworten auf Befehle aus <see cref="CommandOutput"/> (Benachrichtigung).</summary>
    public static readonly Guid CommandResponse = new("c765a961-d9d8-4d36-a20a-5315b111836a");
}
