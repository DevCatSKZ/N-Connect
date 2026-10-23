using System.Text;
using System.Text.RegularExpressions;
using Switch2Pro.Protocol;
using Exception = System.Exception;
using Process = Java.Lang.Process;

namespace SkzConnect.Android.Output;

/// <summary>Ein Ausgabeweg für einen Spieler (virtueller Controller und/oder Touch).</summary>
public interface IVirtualPad : IDisposable
{
    void Update(GamepadState state);

    /// <summary>Spiel lässt vibrieren: große/kleine Amplitude 0…255.</summary>
    event Action<byte, byte>? Rumble;
}

/// <summary>
/// Virtueller Xbox-One-S-Controller über den Android-Shell-Befehl <c>hid</c> (eine Instanz je Spieler; beendet
/// man den Prozess, verschwindet das Gerät). Spiele sehen einen ganz normalen Bluetooth-Xbox-Controller.
/// </summary>
public sealed partial class UhidPad : IVirtualPad
{
    private readonly Process _process;
    private readonly Stream _stdin;
    private readonly object _writeLock = new();
    private readonly int _id;
    private readonly byte[] _report = new byte[XboxBtReport.Length];
    private readonly byte[] _line;
    private bool _sent;
    private int _disposed;

    public event Action<byte, byte>? Rumble;
    public string Name { get; }

    private UhidPad(Process process, int id, string name)
    {
        _process = process;
        _stdin = process.OutputStream!;
        _id = id;
        _line = new byte[UhidCommand.ReportLineCapacity(XboxBtReport.Length)];
        Name = name;
        StartReader(process.InputStream!, ReadOutputLine);
        StartReader(process.ErrorStream!, line => Log.Warn($"hid ({Name}): {line}"));
    }

    /// <summary>Gerät anlegen. Wirft, wenn der Befehl nicht startet (keine Rechte, kein <c>hid</c>).</summary>
    public static UhidPad Create(IPrivilegedShell shell, int playerIndex, string name)
    {
        var process = shell.Start("hid", "-");
        var pad = new UhidPad(process, playerIndex + 1, name);
        pad.Write(UhidCommand.RegisterXbox(pad._id, name));
        pad.Update(default);
        Log.Info($"Virtueller Xbox-Controller „{name}“ angelegt ({shell.Name})");
        return pad;
    }

    public void Update(GamepadState state)
    {
        lock (_writeLock)
        {
            // Bericht und JSON-Zeile laufen über feste Puffer – ~90 Berichte/s je Controller ohne neue Objekte.
            var previous = _report.AsSpan();
            Span<byte> current = stackalloc byte[XboxBtReport.Length];
            XboxBtReport.Build(state, current);
            if (_sent && current.SequenceEqual(previous))
                return; // Android meldet nur Änderungen – gleiche Berichte sparen Akku
            current.CopyTo(_report);
            _sent = true;
            Write(UhidCommand.WriteReportLine(_id, _report, _line));
        }
    }

    private void Write(string line)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
        lock (_writeLock)
            WriteCore(bytes, bytes.Length);
    }

    private void Write(int length)
    {
        WriteCore(_line, length);
    }

    private void WriteCore(byte[] bytes, int length)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        try
        {
            _stdin.Write(bytes, 0, length);
            _stdin.Flush();
        }
        catch (Exception e)
        {
            Log.Error($"Virtueller Controller „{Name}“", e);
        }
    }

    /// <summary>
    /// <c>hid</c> meldet Ausgabeberichte (vom Spiel) als JSON-Zeile mit <c>reportData</c>. Xbox One S Vibration:
    /// Report 0x03 = [0x03, Motoren, LT, RT, stark, schwach, Dauer, Verzögerung, Wiederholungen], Stärke 0…100.
    /// </summary>
    private void ReadOutputLine(string line)
    {
        var m = ReportData().Match(line);
        if (!m.Success)
            return;
        var data = m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out int v) ? v : 0).ToArray();
        if (data.Length >= 6 && data[0] == 0x03)
        {
            byte strong = (byte)Math.Clamp(data[4] * 255 / 100, 0, 255);
            byte weak = (byte)Math.Clamp(data[5] * 255 / 100, 0, 255);
            if ((data[1] & 0x03) == 0)
                strong = weak = 0; // Hauptmotoren nicht freigegeben = aus
            Rumble?.Invoke(strong, weak);
        }
    }

    private static void StartReader(Stream stream, Action<string> onLine)
    {
        var t = new Thread(() =>
        {
            try
            {
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line)
                    onLine(line);
            }
            catch (Exception)
            {
                // Prozess beendet
            }
        }) { IsBackground = true, Name = "hid-Leser" };
        t.Start();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            _stdin.Close();
        }
        catch (Exception)
        {
        }
        _process.Destroy();
        Log.Info($"Virtueller Controller „{Name}“ entfernt");
    }

    [GeneratedRegex("\"reportData\"\\s*:\\s*\\[([^\\]]*)\\]")]
    private static partial Regex ReportData();
}

/// <summary>Mehrere Ausgabewege gleichzeitig (z. B. virtueller Controller und Touch).</summary>
public sealed class CompositePad(IReadOnlyList<IVirtualPad> pads) : IVirtualPad
{
    public event Action<byte, byte>? Rumble
    {
        add { foreach (var p in pads) p.Rumble += value; }
        remove { foreach (var p in pads) p.Rumble -= value; }
    }

    public void Update(GamepadState state)
    {
        foreach (var p in pads)
            p.Update(state);
    }

    public void Dispose()
    {
        foreach (var p in pads)
            p.Dispose();
    }
}
