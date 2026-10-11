using System.Text.Json;
using Switch2Pro.Protocol;

namespace NConnect.Android.Touch;

/// <summary>Bedienelement, das auf eine Bildschirmposition gelegt werden kann.</summary>
public enum TouchControl
{
    A, B, X, Y, LB, RB, LT, RT, Start, Back, Up, Down, Left, Right, LS, RS,
    /// <summary>Linker Stick als ziehender Finger um einen Mittelpunkt.</summary>
    LeftStick,
    /// <summary>Rechter Stick als ziehender Finger (z. B. Kamera in Shootern).</summary>
    RightStick,
}

/// <summary>Position eines Bedienelements in Bruchteilen der Bildschirmgröße (0…1, unabhängig von der Auflösung).</summary>
public sealed class TouchBinding
{
    public TouchControl Control { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
}

/// <summary>Touch-Belegung: welche Taste tippt wohin. Gespeichert als JSON im App-Speicher.</summary>
public sealed class TouchLayout
{
    public List<TouchBinding> Bindings { get; set; } = [];

    /// <summary>Wie weit der „Stick-Finger“ vom Mittelpunkt wegzieht (Anteil der Bildschirmbreite).</summary>
    public float StickRadius { get; set; } = 0.07f;

    private static string FilePath =>
        Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "touch-layout.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static TouchLayout Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<TouchLayout>(File.ReadAllText(FilePath), Json) ?? new TouchLayout();
        }
        catch (Exception e)
        {
            Log.Error("Touch-Belegung laden", e);
        }
        return new TouchLayout();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception e)
        {
            Log.Error("Touch-Belegung speichern", e);
        }
    }

    /// <summary>Ist das Bedienelement im Gamepad-Zustand gerade gedrückt?</summary>
    public static bool IsPressed(TouchControl c, GamepadState g) => c switch
    {
        TouchControl.A => g.Buttons.HasFlag(XButtons.A),
        TouchControl.B => g.Buttons.HasFlag(XButtons.B),
        TouchControl.X => g.Buttons.HasFlag(XButtons.X),
        TouchControl.Y => g.Buttons.HasFlag(XButtons.Y),
        TouchControl.LB => g.Buttons.HasFlag(XButtons.LB),
        TouchControl.RB => g.Buttons.HasFlag(XButtons.RB),
        TouchControl.LT => g.LeftTrigger > 100,
        TouchControl.RT => g.RightTrigger > 100,
        TouchControl.Start => g.Buttons.HasFlag(XButtons.Start),
        TouchControl.Back => g.Buttons.HasFlag(XButtons.Back),
        TouchControl.Up => g.Buttons.HasFlag(XButtons.Up),
        TouchControl.Down => g.Buttons.HasFlag(XButtons.Down),
        TouchControl.Left => g.Buttons.HasFlag(XButtons.Left),
        TouchControl.Right => g.Buttons.HasFlag(XButtons.Right),
        TouchControl.LS => g.Buttons.HasFlag(XButtons.LS),
        TouchControl.RS => g.Buttons.HasFlag(XButtons.RS),
        _ => false,
    };

    /// <summary>Beschriftung im Editor.</summary>
    public static string Label(TouchControl c) => c switch
    {
        TouchControl.LeftStick => "L-Stick",
        TouchControl.RightStick => "R-Stick",
        TouchControl.Up => "↑",
        TouchControl.Down => "↓",
        TouchControl.Left => "←",
        TouchControl.Right => "→",
        TouchControl.Start => "+",
        TouchControl.Back => "−",
        _ => c.ToString(),
    };
}
