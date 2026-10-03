using System.Text.Json;
using System.Text.Json.Serialization;

namespace Switch2Pro.Protocol;

public enum OutputMode
{
    /// <summary>Virtueller Xbox-360-Controller (XInput) – funktioniert mit praktisch allen Spielen.</summary>
    Xbox360,
    /// <summary>Virtueller DualShock 4 – zusätzlich mit Gyro (Steam, Emulatoren).</summary>
    DualShock4,
}

public enum FaceButtonLayout
{
    /// <summary>Xbox-Belegung (nach Position): Nintendo B (unten) wird Xbox A (unten) usw.
    /// Spiele zeigen „A“ für die untere Taste – wie auf einem Xbox-Controller.</summary>
    Xbox,
    /// <summary>Switch-2-Pro-Belegung (nach Beschriftung): Nintendo A bleibt A (rechts),
    /// B bleibt B (unten) – wie auf der Switch.</summary>
    Switch2,
}

/// <summary>Ziel einer Taste beim freien Umbelegen.</summary>
public enum ExtraButtonTarget
{
    None, A, B, X, Y, LB, RB, LT, RT, Back, Start, Guide, LS, RS, Up, Down, Left, Right,
    /// <summary>Nur DualShock 4: Touchpad-Klick.</summary>
    Touchpad,
}

/// <summary>
/// Einstellungen. Wird vom Bluetooth-Thread gelesen und vom Fenster geändert: Listen und
/// Wörterbücher deshalb nie verändern, sondern immer durch eine neue Kopie ersetzen.
/// </summary>
public sealed class Settings
{
    public OutputMode OutputMode { get; set; } = OutputMode.Xbox360;
    public FaceButtonLayout Layout { get; set; } = FaceButtonLayout.Xbox;
    public bool RumbleEnabled { get; set; } = true;
    /// <summary>Vibrationsstärke 0.0–1.0.</summary>
    public float RumbleStrength { get; set; } = 0.8f;
    /// <summary>Radiale Totzone der Sticks 0.0–0.5.</summary>
    public float StickDeadzone { get; set; } = 0.06f;
    /// <summary>
    /// Freie Umbelegung: Controller-Taste → Ziel. Überschreibt die Belegung aus <see cref="Layout"/>.
    /// Tasten: A, B, X, Y, L, R, ZL, ZR, Minus, Plus, LeftStick, RightStick, Home, Capture, C, GL, GR,
    /// Up, Down, Left, Right. Ziele: siehe <see cref="ExtraButtonTarget"/> (None = Taste aus).
    /// Beispiel: "GL": "LS" legt die linke Rücktaste auf den linken Stick-Klick.
    /// </summary>
    public Dictionary<ProButtons, ExtraButtonTarget> Remap { get; set; } = new()
    {
        [ProButtons.Capture] = ExtraButtonTarget.Touchpad,
        [ProButtons.C] = ExtraButtonTarget.None,
        [ProButtons.GL] = ExtraButtonTarget.None,
        [ProButtons.GR] = ExtraButtonTarget.None,
    };
    /// <summary>Kurz „Klick“ vibrieren, wenn der Controller verbunden ist.</summary>
    public bool ConnectFeedback { get; set; } = true;
    /// <summary>
    /// Bekannte Controller per Tastendruck wieder verbinden (ohne SYNC). Achtung: Läuft das Programm,
    /// verbindet sich ein bekannter Controller dann mit dem PC statt mit der Switch 2.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;
    /// <summary>Controller, die schon einmal verbunden waren (wird automatisch gefüllt).</summary>
    public List<string> KnownControllers { get; set; } = [];
    /// <summary>Nur diese Controller annehmen (Bluetooth-Adressen, z. B. "AA:BB:CC:DD:EE:FF"). Leer = alle.</summary>
    public List<string> AllowedControllers { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Settings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return (JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions) ?? new Settings()).Sanitized();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Kaputte Datei: mit Standardwerten weiterlaufen, Datei nicht überschreiben.
            return new Settings { LoadError = e.Message };
        }
        var fresh = new Settings();
        fresh.Save(path);
        return fresh;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Übernimmt alle Werte aus <paramref name="other"/>.</summary>
    public void CopyFrom(Settings other)
    {
        OutputMode = other.OutputMode;
        Layout = other.Layout;
        RumbleEnabled = other.RumbleEnabled;
        RumbleStrength = other.RumbleStrength;
        StickDeadzone = other.StickDeadzone;
        Remap = new Dictionary<ProButtons, ExtraButtonTarget>(other.Remap);
        ConnectFeedback = other.ConnectFeedback;
        AutoReconnect = other.AutoReconnect;
        KnownControllers = [.. other.KnownControllers];
        AllowedControllers = [.. other.AllowedControllers];
    }

    [JsonIgnore]
    public string? LoadError { get; private set; }

    public bool IsKnown(string address) =>
        KnownControllers.Any(a => string.Equals(a.Trim(), address, StringComparison.OrdinalIgnoreCase));

    public bool IsAllowed(string address) =>
        AllowedControllers.Count == 0
        || AllowedControllers.Any(a => string.Equals(a.Trim(), address, StringComparison.OrdinalIgnoreCase));

    private Settings Sanitized()
    {
        RumbleStrength = float.IsFinite(RumbleStrength) ? Math.Clamp(RumbleStrength, 0f, 1f) : 0.8f;
        StickDeadzone = float.IsFinite(StickDeadzone) ? Math.Clamp(StickDeadzone, 0f, 0.5f) : 0.06f;
        AllowedControllers ??= [];
        KnownControllers ??= [];
        Remap ??= [];
        return this;
    }
}
