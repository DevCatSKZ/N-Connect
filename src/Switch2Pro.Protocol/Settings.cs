using System.Text.Json;
using System.Text.Json.Serialization;

namespace Switch2Pro.Protocol;

/// <summary>Eigene Werte einer Controller-Art; null = allgemeiner Wert gilt.</summary>
public sealed record ControllerTuning
{
    public float? StickCurve { get; init; }
    public float? TriggerDeadzone { get; init; }
    public float? TriggerFullAt { get; init; }
    public float? RumbleStrength { get; init; }
    public float? GyroMouseSpeed { get; init; }

    [JsonIgnore]
    public bool IsEmpty => StickCurve is null && TriggerDeadzone is null && TriggerFullAt is null && RumbleStrength is null && GyroMouseSpeed is null;

    internal ControllerTuning Sanitized()
    {
        static float? Fin(float? v, float min, float max) => v is { } x && float.IsFinite(x) ? Math.Clamp(x, min, max) : null;
        return new ControllerTuning
        {
            StickCurve = Fin(StickCurve, 0.3f, 3f),
            TriggerDeadzone = Fin(TriggerDeadzone, 0f, 0.5f),
            TriggerFullAt = Fin(TriggerFullAt, 0.5f, 1f),
            RumbleStrength = Fin(RumbleStrength, 0f, 1f),
            GyroMouseSpeed = Fin(GyroMouseSpeed, 1f, 100f),
        };
    }
}

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

/// <summary>Welcher Joy-Con eines Paars die Bewegungsdaten liefert.</summary>
public enum GyroSource { Right, Left }

/// <summary>Wann der Gyro den rechten Stick steuert (zusätzlich zu den Tasten-Aktionen „Gyro-Stick“).</summary>
public enum GyroStickMode
{
    /// <summary>Nur über eine Taste mit „Gyro-Stick (halten)“ bzw. „ein/aus“.</summary>
    Off,
    /// <summary>Immer.</summary>
    Always,
    /// <summary>Solange der linke Trigger (Zielen, ZL) gedrückt ist – wie in vielen Shootern üblich.</summary>
    WhileAiming,
}

/// <summary>
/// Benanntes Tastenprofil, z. B. für ein bestimmtes Spiel. Wird automatisch aktiv, solange eines der
/// <see cref="Programs"/> (EXE-Name, z. B. "Cemu.exe") im Vordergrund ist. Unveränderlich behandeln.
/// </summary>
public sealed record NamedProfile
{
    public string Name { get; init; } = "Profil";
    public List<string> Programs { get; init; } = [];
    /// <summary>Belegung je Controller-Art (wie <see cref="Settings.Profiles"/>).</summary>
    public Dictionary<ControllerKind, Dictionary<ProButtons, string>> Buttons { get; init; } = [];
    /// <summary>Shift-Ebene: gilt, solange eine Taste mit der Aktion „Shift“ gehalten wird.</summary>
    public Dictionary<ControllerKind, Dictionary<ProButtons, string>> ShiftButtons { get; init; } = [];

    public bool MatchesProgram(string exe) =>
        Programs.Any(p => string.Equals(p.Trim(), exe, StringComparison.OrdinalIgnoreCase));

    private static readonly JsonSerializerOptions FileOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Als Datei zum Weitergeben (lesbares JSON).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, FileOptions);

    /// <summary>Profil aus einer Datei lesen; null, wenn die Datei kein gültiges Profil enthält.</summary>
    public static NamedProfile? FromJson(string json)
    {
        try
        {
            var p = JsonSerializer.Deserialize<NamedProfile>(json, FileOptions);
            if (p is null || string.IsNullOrWhiteSpace(p.Name))
                return null;
            // Nur gültige Einträge übernehmen (fremde Dateien können alles enthalten).
            static Dictionary<ControllerKind, Dictionary<ProButtons, string>> Clean(Dictionary<ControllerKind, Dictionary<ProButtons, string>>? maps) =>
                maps?.Where(m => m.Value is not null && Enum.IsDefined(m.Key))
                    .ToDictionary(m => m.Key, m => m.Value.Where(e => e.Value is not null && Enum.IsDefined(e.Key)).ToDictionary(e => e.Key, e => e.Value)) ?? [];
            return p with
            {
                Name = p.Name.Trim(),
                Programs = p.Programs?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [],
                Buttons = Clean(p.Buttons),
                ShiftButtons = Clean(p.ShiftButtons),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
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
    /// <summary>
    /// Belegung je Controller-Art: Taste → Aktion als Text (siehe <see cref="ButtonAction"/>), z. B.
    /// "Pro2": { "GL": "Key:Ctrl+Shift+S", "C": "Guide" }. Hat Vorrang vor <see cref="Remap"/>.
    /// </summary>
    public Dictionary<ControllerKind, Dictionary<ProButtons, string>> Profiles { get; set; } = [];
    /// <summary>Shift-Ebene des Standardprofils (solange eine Taste mit der Aktion „Shift“ gehalten wird).</summary>
    public Dictionary<ControllerKind, Dictionary<ProButtons, string>> ShiftProfiles { get; set; } = [];
    /// <summary>Weitere Profile, z. B. je Spiel – automatisch aktiv, wenn ihr Programm im Vordergrund ist.</summary>
    public List<NamedProfile> NamedProfiles { get; set; } = [];
    /// <summary>Von Hand gewähltes Profil (Name); null = automatisch nach Programm im Vordergrund.</summary>
    public string? ForcedProfile { get; set; }
    /// <summary>Automatisch erkanntes Profil (vom Programm im Vordergrund); wird nicht gespeichert.</summary>
    [JsonIgnore]
    public string? DetectedProfile { get; set; }

    /// <summary>Stick-Totzone je Controller-Art (fehlt eine Art, gilt <see cref="StickDeadzone"/>).</summary>
    public Dictionary<ControllerKind, float> Deadzones { get; set; } = [];

    /// <summary>Eigene Werte je Controller-Art (Kennlinie, Trigger, Vibration, Gyro-Maus); fehlende gelten allgemein.</summary>
    public Dictionary<ControllerKind, ControllerTuning> Tuning { get; set; } = [];

    /// <summary>Gyro-Nullpunkt je Controller (Bluetooth-Adresse), vom Benutzer kalibriert (Rohwerte).</summary>
    public Dictionary<string, GyroBias> GyroCalibration { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Welcher Joy-Con eines Paars die Bewegungsdaten liefert (an der Switch: rechts).</summary>
    public GyroSource PairGyroSource { get; set; } = GyroSource.Right;
    /// <summary>Gyro-Maus: Bildpunkte je Grad Drehung.</summary>
    public float GyroMouseSpeed { get; set; } = 20f;
    /// <summary>Nach so vielen Minuten ohne Eingabe trennen (spart Akku); 0 = nie.</summary>
    public int InactivityMinutes { get; set; }

    /// <summary>Gyro als rechter Stick (Zielen per Bewegung in Spielen ohne Mausunterstützung).</summary>
    public GyroStickMode GyroStick { get; set; } = GyroStickMode.Off;
    /// <summary>Drehgeschwindigkeit (°/s), bei der der Stick voll ausschlägt.</summary>
    public float GyroStickFullSpeed { get; set; } = 150f;
    /// <summary>Mindestausschlag, sobald sich der Controller dreht (überwindet die Totzone des Spiels), 0–0,4.</summary>
    public float GyroStickAntiDeadzone { get; set; } = 0.12f;
    public bool GyroStickInvertY { get; set; }

    /// <summary>
    /// Gyro-Beschleunigung (wie JoyShockMapper MIN/MAX_GYRO_SENS): bei langsamer Drehung (≤ <see cref="GyroAccelSlow"/>
    /// °/s) normale Empfindlichkeit, bei schneller (≥ <see cref="GyroAccelFast"/> °/s) das <see cref="GyroAcceleration"/>-
    /// fache, dazwischen linear. 1 = aus. Gilt für Gyro-Stick und Gyro-Maus.
    /// </summary>
    public float GyroAcceleration { get; set; } = 1f;
    public float GyroAccelSlow { get; set; } = 20f;
    public float GyroAccelFast { get; set; } = 120f;

    /// <summary>
    /// Flick-Stick (wie JoyShockMapper FLICK_STICK): Der rechte Stick dreht die Kamera per Maus sofort in die Richtung,
    /// in die er zeigt; am Rand gedreht dreht sie mit. Für Spiele mit Maussteuerung (Zielen per Gyro, Drehen per Stick).
    /// </summary>
    public bool FlickStick { get; set; }
    /// <summary>Mausbewegung (Bildpunkte/Counts) für eine volle Drehung im Spiel – je Spiel einmal einstellen.</summary>
    public int FlickCountsPer360 { get; set; } = 3600;
    /// <summary>Dauer eines Flicks in Sekunden (JoyShockMapper FLICK_TIME).</summary>
    public float FlickTime { get; set; } = 0.1f;

    /// <summary>
    /// Spieler-LED und Lichtleiste aus dem Spiel übernehmen: Xbox-Platz, den Windows vergibt, als Spieler-LED;
    /// DualShock-4-Lichtleiste als HOME-LED (Switch 1 Pro, Joy-Con R) und in der Übersicht.
    /// </summary>
    public bool GameLeds { get; set; } = true;

    /// <summary>Stick-Kennlinie: 1 = linear, &gt; 1 = feiner in der Mitte, &lt; 1 = schneller.</summary>
    public float StickCurve { get; set; } = 1f;
    /// <summary>Analoge Trigger (GameCube): Totzone am Anfang, 0–0,5.</summary>
    public float TriggerDeadzone { get; set; } = 0.05f;
    /// <summary>Analoge Trigger: ab diesem Anteil gilt der Trigger als voll gedrückt, 0,5–1.</summary>
    public float TriggerFullAt { get; set; } = 1f;

    /// <summary>Dauerfeuer: Wechsel pro Sekunde für Tasten mit „Turbo“.</summary>
    public float TurboRate { get; set; } = 12f;

    /// <summary>Beim Start auf GitHub nach einer neuen Version suchen.</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Beim Start auf GitHub nach einer neuen HidHide-Version suchen (Installation nur nach Rückfrage).</summary>
    public bool CheckHidHideUpdates { get; set; } = true;
    /// <summary>
    /// Zuletzt gesehene HidHide-Version. Ändert sie sich (Update = Neuinstallation, dabei geht HidHides Geräteliste
    /// verloren), werden die Controller neu versteckt.
    /// </summary>
    public string? HidHideVersion { get; set; }
    /// <summary>Wii-Fernbedienung: Zeiger über die Sensorleiste (IR-Kamera) steuert den Mauszeiger.</summary>
    public bool WiiPointerMouse { get; set; }
    /// <summary>Hinweis „an der Switch 2 neu koppeln“ wurde schon einmal gezeigt.</summary>
    public bool ConsoleHintShown { get; set; }
    /// <summary>Autostart wurde beim ersten Start eingerichtet (danach entscheidet nur noch der Benutzer).</summary>
    public bool AutostartConfigured { get; set; }
    /// <summary>Darstellung: "dark" (Standard), "light" oder "system" (wie Windows).</summary>
    public string? Theme { get; set; }
    /// <summary>Durchscheinender Fensterhintergrund (Mica, ab Windows 11).</summary>
    public bool Transparency { get; set; } = true;
    /// <summary>Sprache der Oberfläche: null = wie Windows, sonst "de" oder "en".</summary>
    public string? Language { get; set; }

    /// <summary>Joy-Con 2 als Maus, sobald er auf einer Fläche liegt (wie an der Switch 2).</summary>
    public bool JoyConMouse { get; set; } = true;
    /// <summary>Mausgeschwindigkeit (Bildpunkte je Sensorschritt).</summary>
    public float MouseSpeed { get; set; } = 2.5f; // Sensor ~160 DPI (gemessen) → wie eine 400-DPI-Maus
    public bool MouseInvertX { get; set; }
    public bool MouseInvertY { get; set; }
    /// <summary>Zwei Joy-Con (links + rechts) automatisch zu einem Controller zusammenfassen.</summary>
    public bool CombineJoyCons { get; set; } = true;

    /// <summary>Bewegungsdaten für Emulatoren bereitstellen (Cemuhook/DSU, 127.0.0.1:26760).</summary>
    public bool DsuServer { get; set; } = true;

    /// <summary>Kurz „Klick“ vibrieren, wenn der Controller verbunden ist.</summary>
    public bool ConnectFeedback { get; set; } = true;
    /// <summary>
    /// Bekannte Controller per Tastendruck wieder verbinden (ohne SYNC). Achtung: Läuft das Programm,
    /// verbindet sich ein bekannter Controller dann mit dem PC statt mit der Switch 2.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;
    /// <summary>
    /// Switch-1-, Nintendo-Switch-Online- und Wii-Controller im Kopplungsmodus (SYNC) im Hintergrund selbst mit Windows
    /// koppeln – ohne Umweg über die Windows-Bluetooth-Einstellungen. Sucht nur, solange niemand spielt.
    /// </summary>
    public bool AutoPair { get; set; } = true;
    /// <summary>Controller, die schon einmal verbunden waren (wird automatisch gefüllt).</summary>
    public List<string> KnownControllers { get; set; } = [];
    /// <summary>Nur diese Controller annehmen (Bluetooth-Adressen, z. B. "AA:BB:CC:DD:EE:FF"). Leer = alle.</summary>
    public List<string> AllowedControllers { get; set; } = [];
    /// <summary>
    /// Joy-Con, die einzeln verwendet werden sollen (Bluetooth-Adressen). Wird automatisch gepflegt:
    /// Trennen (quer + SL/SR oder Knopf) trägt ein, Zusammenfügen (L + R oder Knopf) trägt aus.
    /// </summary>
    public List<string> SingleJoyCons { get; set; } = [];
    /// <summary>Per HidHide versteckte Controller (HID-Instanz-IDs) – damit nicht erneut gefragt wird.</summary>
    public List<string> HiddenDevices { get; set; } = [];

    public bool IsHidden(string instanceId) => Contains(HiddenDevices, instanceId);

    /// <summary>
    /// Original-Controller automatisch per HidHide vor Spielen und Steam verstecken (Switch 1, NSO, USB, Kabel-Pads) –
    /// sonst sehen Steam und viele Spiele sie doppelt (Original + virtueller Controller).
    /// </summary>
    public bool HideFromGames { get; set; } = true;

    /// <summary>Ausgabeart je Controller (Adresse → Xbox 360 / DualShock 4); fehlt der Eintrag, gilt <see cref="OutputMode"/>.</summary>
    public Dictionary<string, OutputMode> ControllerOutputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Eigene Ausgabeart eines Controllers (null = wie allgemein eingestellt).</summary>
    public OutputMode? OutputFor(string? address) =>
        address is not null && ControllerOutputs.TryGetValue(address, out var mode) ? mode : null;

    /// <summary>Ausgabeart eines Controllers setzen; null = allgemeine Einstellung. Neue Kopie (andere Threads lesen).</summary>
    public void SetOutput(string address, OutputMode? mode)
    {
        var copy = new Dictionary<string, OutputMode>(ControllerOutputs, StringComparer.OrdinalIgnoreCase);
        if (mode is { } m)
            copy[address] = m;
        else
            copy.Remove(address);
        ControllerOutputs = copy;
    }

    /// <summary>Einzelne Joy-Con, die hochkant statt quer gehalten werden (Bluetooth-Adressen).</summary>
    public List<string> UprightJoyCons { get; set; } = [];

    /// <summary>
    /// Übernommene Kopplungsdaten (von der Switch-SD-Karte oder einem anderen PC): welcher Controller an welchen
    /// Host gebunden ist. Nur zur Anzeige und für den Export – Schlüssel werden hier nie gespeichert.
    /// </summary>
    public List<PairingNote> ImportedPairings { get; set; } = [];

    /// <summary>Beim Einstecken einer Switch-SD-Karte anbieten, die Kopplungsdaten zu übernehmen.</summary>
    public bool SwitchCardHint { get; set; } = true;

    /// <summary>Eigene Namen je Controller (Adresse → Name), z. B. „Lenas Joy-Con“.</summary>
    public Dictionary<string, string> ControllerNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Eigene Stick-Kalibrierung (geführt gemessen) je Controller und Stick – Schlüssel „Adresse|L“ bzw. „Adresse|R“.
    /// Ersetzt die Werkswerte des Controllers; in den Controller selbst wird nichts geschrieben.
    /// </summary>
    public Dictionary<string, StickCalibration> StickCalibrations { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string StickKey(string address, bool left) => $"{address}|{(left ? "L" : "R")}";

    /// <summary>Eigene Kalibrierung eines Sticks (null = Werkswerte).</summary>
    public StickCalibration? StickCalibrationFor(string? address, bool left) =>
        address is not null && StickCalibrations.TryGetValue(StickKey(address, left), out var c) ? c : null;

    /// <summary>Eigene Kalibrierung setzen bzw. mit null entfernen. Neue Kopie, weil andere Threads gleichzeitig lesen.</summary>
    public void SetStickCalibration(string address, bool left, StickCalibration? calibration)
    {
        var copy = new Dictionary<string, StickCalibration>(StickCalibrations, StringComparer.OrdinalIgnoreCase);
        if (calibration is { } c)
            copy[StickKey(address, left)] = c;
        else
            copy.Remove(StickKey(address, left));
        StickCalibrations = copy;
    }

    /// <summary>Gewünschter Spielerplatz je Controller (Adresse → 0–7); wird beim Verbinden bevorzugt.</summary>
    public Dictionary<string, int> PlayerSlots { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Eigener Name eines Controllers (null = keiner).</summary>
    public string? NameFor(string? address) =>
        address is not null && ControllerNames.TryGetValue(address, out var name) && !string.IsNullOrWhiteSpace(name) ? name : null;

    /// <summary>Namen setzen; leer = entfernen. Neue Kopie, weil andere Threads gleichzeitig lesen.</summary>
    public void SetName(string address, string? name)
    {
        var copy = new Dictionary<string, string>(ControllerNames, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(name))
            copy.Remove(address);
        else
            copy[address] = name.Trim().Length > 40 ? name.Trim()[..40] : name.Trim();
        ControllerNames = copy;
    }

    /// <summary>Gewünschter Spielerplatz eines Controllers (null = keiner).</summary>
    public int? SlotFor(string? address) =>
        address is not null && PlayerSlots.TryGetValue(address, out int slot) && slot is >= 0 and < 8 ? slot : null;

    /// <summary>Spielerplatz merken. Neue Kopie, weil andere Threads gleichzeitig lesen.</summary>
    public void SetSlot(string address, int slot) =>
        PlayerSlots = new Dictionary<string, int>(PlayerSlots, StringComparer.OrdinalIgnoreCase) { [address] = Math.Clamp(slot, 0, 7) };

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
        try
        {
            fresh.Save(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nicht beschreibbar: trotzdem mit Standardwerten starten.
        }
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
        Profiles = other.Profiles.ToDictionary(p => p.Key, p => new Dictionary<ProButtons, string>(p.Value));
        ShiftProfiles = other.ShiftProfiles.ToDictionary(p => p.Key, p => new Dictionary<ProButtons, string>(p.Value));
        NamedProfiles = [.. other.NamedProfiles];
        ForcedProfile = other.ForcedProfile;
        Deadzones = new Dictionary<ControllerKind, float>(other.Deadzones);
        Tuning = new Dictionary<ControllerKind, ControllerTuning>(other.Tuning);
        GyroCalibration = new Dictionary<string, GyroBias>(other.GyroCalibration, StringComparer.OrdinalIgnoreCase);
        PairGyroSource = other.PairGyroSource;
        GyroMouseSpeed = other.GyroMouseSpeed;
        InactivityMinutes = other.InactivityMinutes;
        GyroStick = other.GyroStick;
        GyroStickFullSpeed = other.GyroStickFullSpeed;
        GyroStickAntiDeadzone = other.GyroStickAntiDeadzone;
        GyroStickInvertY = other.GyroStickInvertY;
        GyroAcceleration = other.GyroAcceleration;
        GyroAccelSlow = other.GyroAccelSlow;
        GyroAccelFast = other.GyroAccelFast;
        FlickStick = other.FlickStick;
        FlickCountsPer360 = other.FlickCountsPer360;
        FlickTime = other.FlickTime;
        GameLeds = other.GameLeds;
        StickCurve = other.StickCurve;
        TriggerDeadzone = other.TriggerDeadzone;
        TriggerFullAt = other.TriggerFullAt;
        TurboRate = other.TurboRate;
        CheckForUpdates = other.CheckForUpdates;
        CheckHidHideUpdates = other.CheckHidHideUpdates;
        HidHideVersion = other.HidHideVersion;
        WiiPointerMouse = other.WiiPointerMouse;
        ConsoleHintShown = other.ConsoleHintShown;
        AutostartConfigured = other.AutostartConfigured;
        Theme = other.Theme;
        Transparency = other.Transparency;
        Language = other.Language;
        UprightJoyCons = [.. other.UprightJoyCons];
        HiddenDevices = [.. other.HiddenDevices];
        HideFromGames = other.HideFromGames;
        ControllerOutputs = new Dictionary<string, OutputMode>(other.ControllerOutputs, StringComparer.OrdinalIgnoreCase);
        JoyConMouse = other.JoyConMouse;
        MouseSpeed = other.MouseSpeed;
        MouseInvertX = other.MouseInvertX;
        MouseInvertY = other.MouseInvertY;
        CombineJoyCons = other.CombineJoyCons;
        DsuServer = other.DsuServer;
        ConnectFeedback = other.ConnectFeedback;
        AutoReconnect = other.AutoReconnect;
        AutoPair = other.AutoPair;
        KnownControllers = [.. other.KnownControllers];
        AllowedControllers = [.. other.AllowedControllers];
        SingleJoyCons = [.. other.SingleJoyCons];
        ImportedPairings = [.. other.ImportedPairings];
        SwitchCardHint = other.SwitchCardHint;
        ControllerNames = new Dictionary<string, string>(other.ControllerNames, StringComparer.OrdinalIgnoreCase);
        PlayerSlots = new Dictionary<string, int>(other.PlayerSlots, StringComparer.OrdinalIgnoreCase);
        StickCalibrations = new Dictionary<string, StickCalibration>(other.StickCalibrations, StringComparer.OrdinalIgnoreCase);
    }

    [JsonIgnore]
    public string? LoadError { get; private set; }

    public bool IsKnown(string address) =>
        KnownControllers.Any(a => string.Equals(a.Trim(), address, StringComparison.OrdinalIgnoreCase));

    public bool IsAllowed(string address) =>
        AllowedControllers.Count == 0
        || AllowedControllers.Any(a => string.Equals(a.Trim(), address, StringComparison.OrdinalIgnoreCase));

    public bool IsSingleJoyCon(string? address) => Contains(SingleJoyCons, address);

    /// <summary>Merkt sich, ob ein Joy-Con einzeln verwendet wird. Gibt true zurück, wenn sich etwas geändert hat.</summary>
    public bool SetSingleJoyCon(string address, bool single)
    {
        if (single == IsSingleJoyCon(address))
            return false;
        SingleJoyCons = Toggled(SingleJoyCons, address, single);
        return true;
    }

    public bool IsUprightJoyCon(string? address) => Contains(UprightJoyCons, address);

    public void SetUprightJoyCon(string address, bool upright)
    {
        if (upright != IsUprightJoyCon(address))
            UprightJoyCons = Toggled(UprightJoyCons, address, upright);
    }

    private static bool Contains(List<string> list, string? address) =>
        address is not null && list.Any(a => string.Equals(a.Trim(), address, StringComparison.OrdinalIgnoreCase));

    private static List<string> Toggled(List<string> list, string address, bool add) => add
        ? [.. list, address]
        : [.. list.Where(a => !string.Equals(a.Trim(), address, StringComparison.OrdinalIgnoreCase))];

    /// <summary>Totzone für eine Controller-Art (eigene oder die allgemeine).</summary>
    public float DeadzoneFor(ControllerKind kind) => Deadzones.TryGetValue(kind, out float d) ? d : StickDeadzone;

    private ControllerTuning? TuningFor(ControllerKind kind) => Tuning.GetValueOrDefault(kind);
    public float StickCurveFor(ControllerKind kind) => TuningFor(kind)?.StickCurve ?? StickCurve;
    public float TriggerDeadzoneFor(ControllerKind kind) => TuningFor(kind)?.TriggerDeadzone ?? TriggerDeadzone;
    public float TriggerFullAtFor(ControllerKind kind) => TuningFor(kind)?.TriggerFullAt ?? TriggerFullAt;
    public float RumbleStrengthFor(ControllerKind kind) => TuningFor(kind)?.RumbleStrength ?? RumbleStrength;
    public float GyroMouseSpeedFor(ControllerKind kind) => TuningFor(kind)?.GyroMouseSpeed ?? GyroMouseSpeed;

    /// <summary>Eigene Werte einer Controller-Art ändern; ohne eigene Werte wird der Eintrag entfernt.</summary>
    public void SetTuning(ControllerKind kind, Func<ControllerTuning, ControllerTuning> change)
    {
        var updated = change(TuningFor(kind) ?? new ControllerTuning());
        // Neue Kopie statt Änderung: der Bluetooth-Thread liest gleichzeitig.
        var copy = new Dictionary<ControllerKind, ControllerTuning>(Tuning);
        if (updated.IsEmpty)
            copy.Remove(kind);
        else
            copy[kind] = updated;
        Tuning = copy;
    }

    /// <summary>Gerade geltendes benanntes Profil (von Hand gewählt oder per Programm erkannt); null = Standard.</summary>
    public NamedProfile? CurrentProfile()
    {
        string? name = ForcedProfile ?? DetectedProfile;
        return name is null ? null : NamedProfiles.FirstOrDefault(p => p.Name == name);
    }

    /// <summary>Gyro-Nullpunkt eines Controllers: vom Benutzer kalibriert, sonst der Werkswert.</summary>
    public GyroBias GyroBiasFor(string? address, GyroBias factory) =>
        address is not null && GyroCalibration.TryGetValue(address, out var bias) ? bias : factory;

    /// <summary>Belegungen ohne null-Einträge (von Hand bearbeitete Dateien).</summary>
    private static Dictionary<ControllerKind, Dictionary<ProButtons, string>> Clean(Dictionary<ControllerKind, Dictionary<ProButtons, string>>? maps) =>
        maps?.Where(m => m.Value is not null)
            .ToDictionary(m => m.Key, m => m.Value.Where(e => e.Value is not null).ToDictionary(e => e.Key, e => e.Value)) ?? [];

    private Settings Sanitized()
    {
        RumbleStrength = float.IsFinite(RumbleStrength) ? Math.Clamp(RumbleStrength, 0f, 1f) : 0.8f;
        StickDeadzone = float.IsFinite(StickDeadzone) ? Math.Clamp(StickDeadzone, 0f, 0.5f) : 0.06f;
        MouseSpeed = float.IsFinite(MouseSpeed) ? Math.Clamp(MouseSpeed, 0.1f, 10f) : 2.5f;
        AllowedControllers ??= [];
        KnownControllers ??= [];
        SingleJoyCons ??= [];
        UprightJoyCons ??= [];
        HiddenDevices ??= [];
        ImportedPairings = ImportedPairings?.Where(p => p?.Address is not null).ToList() ?? [];
        Remap ??= [];
        Profiles ??= [];
        ShiftProfiles ??= [];
        NamedProfiles ??= [];
        Deadzones ??= [];
        GyroCalibration = GyroCalibration is null ? new(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, GyroBias>(GyroCalibration, StringComparer.OrdinalIgnoreCase);
        GyroMouseSpeed = float.IsFinite(GyroMouseSpeed) ? Math.Clamp(GyroMouseSpeed, 1f, 100f) : 20f;
        InactivityMinutes = Math.Clamp(InactivityMinutes, 0, 240);
        static float Fin(float v, float min, float max, float fallback) => float.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
        GyroStickFullSpeed = Fin(GyroStickFullSpeed, 20f, 1000f, 150f);
        GyroStickAntiDeadzone = Fin(GyroStickAntiDeadzone, 0f, 0.4f, 0.12f);
        GyroAcceleration = Fin(GyroAcceleration, 1f, 4f, 1f);
        GyroAccelSlow = Fin(GyroAccelSlow, 0f, 500f, 20f);
        GyroAccelFast = Fin(GyroAccelFast, GyroAccelSlow + 1f, 1000f, Math.Max(GyroAccelSlow + 1f, 120f));
        FlickCountsPer360 = Math.Clamp(FlickCountsPer360, 100, 100_000);
        FlickTime = Fin(FlickTime, 0f, 0.5f, 0.1f);
        StickCurve = Fin(StickCurve, 0.3f, 3f, 1f);
        TriggerDeadzone = Fin(TriggerDeadzone, 0f, 0.5f, 0.05f);
        TriggerFullAt = Fin(TriggerFullAt, 0.5f, 1f, 1f);
        TurboRate = Fin(TurboRate, 2f, 30f, 12f);
        if (Theme is not (null or "dark" or "light" or "system"))
            Theme = null;
        if (Language is not (null or "de" or "en"))
            Language = null;
        if (!Enum.IsDefined(GyroStick))
            GyroStick = GyroStickMode.Off;
        UprightJoyCons.RemoveAll(a => a is null);
        NamedProfiles.RemoveAll(p => p is null || string.IsNullOrWhiteSpace(p.Name));
        NamedProfiles = NamedProfiles.Select(p => p with
        {
            Programs = p.Programs?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [],
            Buttons = Clean(p.Buttons),
            ShiftButtons = Clean(p.ShiftButtons),
        }).ToList();
        ShiftProfiles = Clean(ShiftProfiles);
        Tuning = Tuning?.Where(t => t.Value is not null && Enum.IsDefined(t.Key))
            .ToDictionary(t => t.Key, t => t.Value.Sanitized()) ?? [];
        foreach (var kind in Deadzones.Keys.ToList())
            Deadzones[kind] = float.IsFinite(Deadzones[kind]) ? Math.Clamp(Deadzones[kind], 0f, 0.5f) : StickDeadzone;
        // Von Hand bearbeitete Dateien können null-Einträge enthalten ("…": null).
        AllowedControllers.RemoveAll(a => a is null);
        KnownControllers.RemoveAll(a => a is null);
        SingleJoyCons.RemoveAll(a => a is null);
        Profiles = Clean(Profiles);
        ControllerNames = ControllerNames?.Where(n => n.Key is not null && !string.IsNullOrWhiteSpace(n.Value))
            .ToDictionary(n => n.Key, n => n.Value.Trim(), StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
        PlayerSlots = PlayerSlots?.Where(s => s.Key is not null && s.Value is >= 0 and < 8)
            .ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
        ControllerOutputs = ControllerOutputs?.Where(o => o.Key is not null && Enum.IsDefined(o.Value))
            .ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
        if (!Enum.IsDefined(OutputMode))
            OutputMode = OutputMode.Xbox360;
        // Nur plausible Kalibrierungen (Mitte im 12-Bit-Bereich, Ausschlag 200–2047) – sonst lieber die Werkswerte.
        static bool Plausible(AxisCalibration a) => a.Neutral is > 0 and < 4095 && a.Max is >= 200 and <= 2047 && a.Min is >= 200 and <= 2047;
        StickCalibrations = StickCalibrations?.Where(c => c.Key is not null && Plausible(c.Value.X) && Plausible(c.Value.Y))
            .ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
        return this;
    }
}

/// <summary>Ein übernommener Kopplungseintrag: Controller-Adresse, Name, Host-Adresse, Herkunft ("Switch"/"N-Connect").</summary>
public sealed record PairingNote(string Address, string? Name, string? HostAddress, string? Source);
