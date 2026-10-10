using System.Runtime.InteropServices;

namespace Switch2Pro.Bridge;

/// <summary>Maus- und Tastatureingaben an Windows senden (SendInput, wie ein echtes Gerät).</summary>
internal static class WindowsInput
{
    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
        MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_MIDDLEDOWN = 0x0020,
        MOUSEEVENTF_MIDDLEUP = 0x0040, MOUSEEVENTF_WHEEL = 0x0800;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    private static void Send(params INPUT[] inputs)
    {
        if (inputs.Length > 0)
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT Mouse(uint flags, int dx = 0, int dy = 0, int data = 0) =>
        new() { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = unchecked((uint)data), dwFlags = flags } } };

    public static void MoveMouse(int dx, int dy)
    {
        if (dx != 0 || dy != 0)
            Send(Mouse(MOUSEEVENTF_MOVE, dx, dy));
    }

    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    /// <summary>Mauszeiger auf eine Stelle des Hauptbildschirms setzen: x/y 0…1 (links oben = 0,0).</summary>
    public static void MoveMouseAbsolute(float x, float y)
    {
        int ax = (int)MathF.Round(Math.Clamp(x, 0f, 1f) * 65535f), ay = (int)MathF.Round(Math.Clamp(y, 0f, 1f) * 65535f);
        Send(Mouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE, ax, ay));
    }

    public enum MouseButton { Left, Right, Middle }

    public static void MouseButtonState(MouseButton button, bool down)
    {
        uint flags = (button, down) switch
        {
            (MouseButton.Left, true) => MOUSEEVENTF_LEFTDOWN,
            (MouseButton.Left, false) => MOUSEEVENTF_LEFTUP,
            (MouseButton.Right, true) => MOUSEEVENTF_RIGHTDOWN,
            (MouseButton.Right, false) => MOUSEEVENTF_RIGHTUP,
            (MouseButton.Middle, true) => MOUSEEVENTF_MIDDLEDOWN,
            _ => MOUSEEVENTF_MIDDLEUP,
        };
        Send(Mouse(flags));
    }

    /// <summary>Mausrad: 120 = eine Raste nach oben.</summary>
    public static void Wheel(int amount)
    {
        if (amount != 0)
            Send(Mouse(MOUSEEVENTF_WHEEL, data: amount));
    }

    private const uint MOUSEEVENTF_HWHEEL = 0x1000;

    /// <summary>Waagerechtes Mausrad: 120 = eine Raste nach rechts.</summary>
    public static void HorizontalWheel(int amount)
    {
        if (amount != 0)
            Send(Mouse(MOUSEEVENTF_HWHEEL, data: amount));
    }

    // ---------- Tastatur ----------

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0) } },
    };

    private static bool IsExtended(ushort vk) => vk is >= 0x21 and <= 0x2E or 0x5B or 0x5C or 0xA3 or 0xA5;

    /// <summary>Tastenkombination drücken (Zusatztasten zuerst) oder loslassen (umgekehrte Reihenfolge).</summary>
    public static void Combo(IReadOnlyList<ushort> keys, bool down)
    {
        var inputs = (down ? keys : keys.Reverse()).Select(k => Key(k, !down)).ToArray();
        Send(inputs);
    }

    /// <summary>
    /// „Ctrl+Shift+S“, „Alt+Tab“, „F5“, „Space“, „5“ … → virtuelle Tastencodes (Zusatztasten zuerst).
    /// null, wenn ein Teil unbekannt ist.
    /// </summary>
    public static IReadOnlyList<ushort>? ParseCombo(string text)
    {
        var result = new List<ushort>();
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ushort? vk = raw.ToUpperInvariant() switch
            {
                "CTRL" or "STRG" or "CONTROL" => 0x11,
                "SHIFT" or "UMSCHALT" => 0x10,
                "ALT" => 0x12,
                "WIN" or "WINDOWS" => 0x5B,
                "TAB" => 0x09,
                "ENTER" or "EINGABE" or "RETURN" => 0x0D,
                "ESC" or "ESCAPE" => 0x1B,
                "SPACE" or "LEERTASTE" => 0x20,
                "BACKSPACE" or "RÜCKTASTE" => 0x08,
                "DEL" or "DELETE" or "ENTF" => 0x2E,
                "INS" or "INSERT" or "EINFG" => 0x2D,
                "HOME" or "POS1" => 0x24,
                "END" or "ENDE" => 0x23,
                "PAGEUP" or "BILDAUF" => 0x21,
                "PAGEDOWN" or "BILDAB" => 0x22,
                "UP" or "HOCH" => 0x26,
                "DOWN" or "RUNTER" => 0x28,
                "LEFT" or "LINKS" => 0x25,
                "RIGHT" or "RECHTS" => 0x27,
                "PRINT" or "DRUCK" => 0x2C,
                "PAUSE" => 0x13,
                "VOLUP" or "LAUTER" => 0xAF,
                "VOLDOWN" or "LEISER" => 0xAE,
                "MUTE" or "STUMM" => 0xAD,
                "PLAYPAUSE" => 0xB3,
                "NEXTTRACK" => 0xB0,
                "PREVTRACK" => 0xB1,
                var s when s.Length == 1 && char.IsAsciiLetterOrDigit(s[0]) => s[0],
                var s when s.Length >= 2 && s[0] == 'F' && int.TryParse(s[1..], out int f) && f is >= 1 and <= 24 => (ushort)(0x70 + f - 1),
                var s when s.StartsWith("NUM") && s.Length == 4 && char.IsAsciiDigit(s[3]) => (ushort)(0x60 + s[3] - '0'),
                _ => null,
            };
            if (vk is null)
                return null;
            result.Add(vk.Value);
        }
        return result.Count == 0 ? null : result;
    }
}
