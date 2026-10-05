using System.Drawing;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>Was eine Controller-Art kann (bestimmt, welche Einstellungen angeboten werden).</summary>
internal static class KindInfo
{
    public static bool HasSticks(ControllerKind k) =>
        k is not (ControllerKind.NesController or ControllerKind.SnesController or ControllerKind.MegaDrive);

    public static bool HasAnalogTriggers(ControllerKind k) =>
        k is ControllerKind.GameCube2 or ControllerKind.DualShock4 or ControllerKind.DualSense or ControllerKind.XboxController;

    public static bool HasRumble(ControllerKind k) =>
        k is not (ControllerKind.NesController or ControllerKind.SnesController or ControllerKind.MegaDrive);

    public static bool HasMotion(ControllerKind k) =>
        k is ControllerKind.Pro2 or ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right or ControllerKind.JoyConPair
            or ControllerKind.Pro1 or ControllerKind.JoyCon1Left or ControllerKind.JoyCon1Right or ControllerKind.WiiRemote
            or ControllerKind.DualShock4 or ControllerKind.DualSense;

    /// <summary>Alle Controller-Arten, für die sich eigene Belegungen und Werte festlegen lassen.</summary>
    public static readonly ControllerKind[] Configurable =
    [
        ControllerKind.Pro2, ControllerKind.JoyConPair, ControllerKind.JoyCon2Left, ControllerKind.JoyCon2Right,
        ControllerKind.GameCube2, ControllerKind.Pro1, ControllerKind.JoyCon1Left, ControllerKind.JoyCon1Right,
        ControllerKind.NesController, ControllerKind.SnesController, ControllerKind.N64Controller, ControllerKind.MegaDrive,
        ControllerKind.WiiRemote, ControllerKind.WiiUPro,
        ControllerKind.DualShock4, ControllerKind.DualSense, ControllerKind.XboxController,
    ];
}

[Flags]
internal enum TuningParts
{
    Sticks = 1,
    Triggers = 2,
    Rumble = 4,
    GyroMouse = 8,
    All = Sticks | Triggers | Rumble | GyroMouse,
}

/// <summary>
/// Eigene Werte einer Controller-Art (Totzone, Kennlinie, Trigger, Vibration, Gyro-Maus). Ohne eigenen Wert gilt der
/// allgemeine; ↺ setzt zurück. Nur Einstellungen, die der Controller auch hat, werden gezeigt.
/// </summary>
internal sealed class TuningEditor : SettingsGroup
{
    private readonly Func<Settings> _settings;
    private readonly Action _save;
    private readonly List<Entry> _entries = [];
    private readonly ToolTip _tips = Theme.CreateToolTip();
    private ControllerKind _kind;
    private bool _loading;

    private sealed record Entry(
        SettingRow Row, Slider Slider, GlyphButton Reset, TuningParts Part,
        Func<Settings, ControllerKind, float?> Own, Func<Settings, float> General,
        Action<Settings, ControllerKind, float?> Set, float Scale, Func<int, string> Format);

    public TuningEditor(Func<Settings> settings, Action save, ControllerKind kind, TuningParts parts = TuningParts.All)
    {
        _settings = settings;
        _save = save;
        _kind = kind;
        static string Percent(int v) => $"{v} %";
        if (parts.HasFlag(TuningParts.Sticks))
        {
            Add("Stick-Totzone", Glyph.Stick, TuningParts.Sticks, 0, 30, 1, 100f, Percent,
                (s, k) => s.Deadzones.TryGetValue(k, out var d) ? d : null, s => s.StickDeadzone,
                (s, k, v) =>
                {
                    var map = new Dictionary<ControllerKind, float>(s.Deadzones);
                    if (v is { } x) map[k] = x; else map.Remove(k);
                    s.Deadzones = map;
                });
            Add("Stick-Kennlinie", Glyph.Sliders, TuningParts.Sticks, 50, 250, 5, 100f, v => v == 100 ? "linear" : $"{v / 100f:0.00}",
                (s, k) => s.Tuning.GetValueOrDefault(k)?.StickCurve, s => s.StickCurve,
                (s, k, v) => s.SetTuning(k, t => t with { StickCurve = v }));
        }
        if (parts.HasFlag(TuningParts.Triggers))
        {
            Add("Trigger-Totzone", Glyph.Gauge, TuningParts.Triggers, 0, 50, 1, 100f, Percent,
                (s, k) => s.Tuning.GetValueOrDefault(k)?.TriggerDeadzone, s => s.TriggerDeadzone,
                (s, k, v) => s.SetTuning(k, t => t with { TriggerDeadzone = v }));
            Add("Trigger voll gedrückt ab", Glyph.Gauge, TuningParts.Triggers, 50, 100, 1, 100f, Percent,
                (s, k) => s.Tuning.GetValueOrDefault(k)?.TriggerFullAt, s => s.TriggerFullAt,
                (s, k, v) => s.SetTuning(k, t => t with { TriggerFullAt = v }));
        }
        if (parts.HasFlag(TuningParts.Rumble))
            Add("Vibrationsstärke", Glyph.Vibrate, TuningParts.Rumble, 0, 100, 5, 100f, Percent,
                (s, k) => s.Tuning.GetValueOrDefault(k)?.RumbleStrength, s => s.RumbleStrength,
                (s, k, v) => s.SetTuning(k, t => t with { RumbleStrength = v }));
        if (parts.HasFlag(TuningParts.GyroMouse))
            Add("Gyro-Maus-Geschwindigkeit", Glyph.Mouse, TuningParts.GyroMouse, 2, 80, 1, 1f, v => $"{v}",
                (s, k) => s.Tuning.GetValueOrDefault(k)?.GyroMouseSpeed, s => s.GyroMouseSpeed,
                (s, k, v) => s.SetTuning(k, t => t with { GyroMouseSpeed = v }));
        Reload();
    }

    public ControllerKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            Reload();
        }
    }

    /// <summary>Gibt es für diesen Controller überhaupt eine Einstellung?</summary>
    public bool HasAny => _entries.Any(e => Applies(e.Part));

    private bool Applies(TuningParts part) => part switch
    {
        TuningParts.Sticks => KindInfo.HasSticks(_kind),
        TuningParts.Triggers => KindInfo.HasAnalogTriggers(_kind),
        TuningParts.Rumble => KindInfo.HasRumble(_kind),
        TuningParts.GyroMouse => KindInfo.HasMotion(_kind),
        _ => true,
    };

    private void Add(string title, string glyph, TuningParts part, int min, int max, int step, float scale, Func<int, string> format,
        Func<Settings, ControllerKind, float?> own, Func<Settings, float> general, Action<Settings, ControllerKind, float?> set)
    {
        var slider = new Slider { Minimum = min, Maximum = max, SmallChange = step, Format = format, Width = 280 };
        var reset = new GlyphButton("", Glyph.Undo) { Width = 36, Margin = Padding.Empty };
        var box = new Panel { Size = new Size(slider.Width + 8 + 36, 32) };
        slider.Location = new Point(0, 0);
        reset.Location = new Point(slider.Width + 8, 0);
        box.Controls.AddRange([slider, reset]);
        var row = new SettingRow(title, null, box, glyph);
        slider.BackColor = reset.BackColor = Theme.Current.Surface;
        _tips.SetToolTip(reset, Tr.T("Eigenen Wert entfernen – es gilt wieder der allgemeine Wert"));
        var entry = new Entry(row, slider, reset, part, own, general, set, scale, format);
        _entries.Add(entry);
        slider.ValueChanged += (_, _) =>
        {
            if (_loading)
                return;
            entry.Set(_settings(), _kind, slider.Value / scale);
            _save();
            Describe(entry);
        };
        reset.Click += (_, _) =>
        {
            entry.Set(_settings(), _kind, null);
            _save();
            Reload();
        };
        Controls.Add(row);
    }

    private static int ToSlider(Entry e, float value) => Math.Clamp((int)MathF.Round(value * e.Scale), e.Slider.Minimum, e.Slider.Maximum);

    private void Describe(Entry e)
    {
        var s = _settings();
        bool own = e.Own(s, _kind) is not null;
        string general = e.Format(ToSlider(e, e.General(s)));
        e.Row.Description = own ? $"Eigener Wert · allgemein: {general}" : $"Allgemeiner Wert ({general}) – Regler verschieben für einen eigenen";
        e.Reset.Enabled = own;
    }

    /// <summary>Werte neu einlesen (anderer Controller, Einstellungen neu geladen).</summary>
    public void Reload()
    {
        var s = _settings();
        _loading = true;
        foreach (var e in _entries)
        {
            SetShown(e.Row, Applies(e.Part));
            e.Slider.Value = ToSlider(e, e.Own(s, _kind) ?? e.General(s));
            Describe(e);
        }
        _loading = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _tips.Dispose();
        base.Dispose(disposing);
    }
}
