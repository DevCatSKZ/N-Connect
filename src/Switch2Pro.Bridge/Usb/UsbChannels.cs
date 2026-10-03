using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Switch2Pro.Protocol;
using static Switch2Pro.Bridge.Usb.UsbNative;

namespace Switch2Pro.Bridge.Usb;

/// <summary>
/// Ein per USB angeschlossener Switch-2-Controller: WinUSB-Pfad (Befehle), HID-Pfad (Eingaben) und
/// Instanz-ID der HID-Schnittstelle (zum Verstecken per HidHide).
/// </summary>
internal sealed record UsbControllerInfo(string DeviceId, string WinUsbPath, string HidPath, ControllerKind Kind, string? HidInstanceId);

internal static class UsbEnumerator
{
    /// <summary>Per USB unterstützt: Pro Controller 2 und GameCube-Controller (Joy-Con 2 haben keinen USB-Anschluss).</summary>
    private static readonly ushort[] ProductIds = [0x2069, 0x2073];

    /// <summary>Findet alle per USB angeschlossenen Switch-2-Controller.</summary>
    public static List<UsbControllerInfo> Find()
    {
        var winUsb = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var guid in new[] { ControllerWinUsbInterface, GenericWinUsbInterface })
        {
            foreach (var path in GetInterfacePaths(guid))
            {
                if (ProductOf(path, "mi_01") is null)
                    continue;
                if (CompositeId(path) is { } id)
                    winUsb.TryAdd(id, path);
            }
        }
        var result = new List<UsbControllerInfo>();
        if (winUsb.Count == 0)
            return result;
        foreach (var path in GetInterfacePaths(HidInterface))
        {
            if (ProductOf(path, "mi_00") is not { } pid)
                continue;
            if (CompositeId(path) is { } id && winUsb.TryGetValue(id, out var usbPath))
                result.Add(new UsbControllerInfo(id, usbPath, path, ControllerKinds.FromSwitch2ProductId(pid), GetInstanceId(path)));
        }
        return result;
    }

    private static ushort? ProductOf(string path, string iface)
    {
        if (!path.Contains(iface, StringComparison.OrdinalIgnoreCase))
            return null;
        foreach (var pid in ProductIds)
            if (path.Contains($"vid_{Gatt.NintendoVendorId:x4}&pid_{pid:x4}", StringComparison.OrdinalIgnoreCase))
                return pid;
        return null;
    }

    /// <summary>Instanz-ID des USB-Verbundgeräts, zu dem die Schnittstelle gehört.</summary>
    private static string? CompositeId(string interfacePath)
    {
        var id = GetInstanceId(interfacePath);
        for (int depth = 0; id is not null && depth < 4; depth++)
        {
            if (ProductIds.Any(pid => id.StartsWith($@"USB\VID_{Gatt.NintendoVendorId:X4}&PID_{pid:X4}", StringComparison.OrdinalIgnoreCase))
                && !id.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
                return id;
            id = GetParentInstanceId(id);
        }
        return null;
    }
}

/// <summary>
/// Befehlskanal über die Bulk-Endpunkte von Interface 1 (WinUSB, ohne eigenen Treiber:
/// Windows bindet WinUSB automatisch über den MS-OS-Deskriptor des Controllers).
/// Aufrufe sind synchron und laufen nacheinander (eine Anfrage, eine Antwort).
/// </summary>
internal sealed class WinUsbChannel : IDisposable
{
    private readonly SafeFileHandle _file;
    private readonly IntPtr _usb;
    private readonly byte _outPipe, _inPipe;
    private readonly object _gate = new();
    private bool _disposed;

    public WinUsbChannel(string path)
    {
        _file = Open(path, FILE_FLAG_OVERLAPPED);
        if (!WinUsb_Initialize(_file, out _usb))
        {
            int error = Marshal.GetLastWin32Error();
            _file.Dispose();
            throw new Win32Exception(error, "WinUSB nicht initialisierbar");
        }
        try
        {
            if (!WinUsb_QueryInterfaceSettings(_usb, 0, out var descriptor))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "USB-Schnittstelle nicht lesbar");
            for (byte i = 0; i < descriptor.bNumEndpoints; i++)
            {
                if (!WinUsb_QueryPipe(_usb, 0, i, out var pipe) || pipe.PipeType != UsbdPipeTypeBulk)
                    continue;
                if ((pipe.PipeId & 0x80) != 0) _inPipe = pipe.PipeId;
                else _outPipe = pipe.PipeId;
            }
            if (_inPipe == 0 || _outPipe == 0)
                throw new InvalidOperationException("Bulk-Endpunkte des Controllers nicht gefunden");
            SetTimeout(_outPipe, 1000);
            SetTimeout(_inPipe, 300);
        }
        catch
        {
            WinUsb_Free(_usb);
            _file.Dispose();
            throw;
        }
    }

    private void SetTimeout(byte pipe, uint ms) =>
        WinUsb_SetPipePolicy(_usb, pipe, PIPE_TRANSFER_TIMEOUT, sizeof(uint), ref ms);

    /// <summary>Sendet einen Befehl und liest die Antwort (null = keine Antwort / Fehler).</summary>
    public byte[]? Transact(byte[] command)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!WinUsb_WritePipe(_usb, _outPipe, command, (uint)command.Length, out _, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"USB-Befehl {command[0]:X2}/{command[3]:X2} nicht gesendet");
            var buffer = new byte[256];
            if (!WinUsb_ReadPipe(_usb, _inPipe, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                const int ERROR_SEM_TIMEOUT = 121;
                if (error == ERROR_SEM_TIMEOUT)
                    return null;
                throw new Win32Exception(error, $"USB-Antwort auf {command[0]:X2}/{command[3]:X2} nicht lesbar");
            }
            return buffer[..(int)read];
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            WinUsb_Free(_usb);
            _file.Dispose();
        }
    }
}

/// <summary>HID-Schnittstelle (Interface 0): Eingabeberichte lesen, Vibrationsberichte schreiben.</summary>
internal sealed class HidChannel : IDisposable
{
    private readonly SafeFileHandle _file;
    private readonly FileStream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public int InputLength { get; }
    public int OutputLength { get; }

    public HidChannel(string path)
    {
        _file = Open(path, FILE_FLAG_OVERLAPPED);
        try
        {
            var caps = GetHidCaps(_file);
            InputLength = caps.InputReportByteLength;
            OutputLength = caps.OutputReportByteLength;
            _stream = new FileStream(_file, FileAccess.ReadWrite, 0, isAsync: true);
        }
        catch
        {
            _file.Dispose();
            throw;
        }
    }

    /// <summary>Liest einen Eingabebericht (Byte 0 = Report-ID). 0 = Gerät getrennt.</summary>
    public async Task<int> ReadAsync(byte[] buffer, CancellationToken ct) =>
        await _stream.ReadAsync(buffer.AsMemory(0, InputLength), ct);

    /// <summary>Schreibt einen Ausgabebericht (Byte 0 = Report-ID), auf die HID-Länge aufgefüllt.</summary>
    public async Task WriteAsync(byte[] report, CancellationToken ct)
    {
        var buffer = report;
        if (OutputLength > 0 && report.Length != OutputLength)
        {
            buffer = new byte[OutputLength];
            report.AsSpan(0, Math.Min(report.Length, OutputLength)).CopyTo(buffer);
        }
        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(buffer, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Ausgabebericht über den Steuerkanal statt über WriteFile (synchron). Ausweg für Bluetooth-Geräte, deren
    /// Treiber WriteFile ablehnt. false bei Fehler.
    /// </summary>
    public bool SetOutputReport(byte[] report)
    {
        var buffer = report;
        if (OutputLength > 0 && report.Length != OutputLength)
        {
            buffer = new byte[OutputLength];
            report.AsSpan(0, Math.Min(report.Length, OutputLength)).CopyTo(buffer);
        }
        return HidD_SetOutputReport(_file, buffer, buffer.Length);
    }

    public void Dispose()
    {
        _stream.Dispose();
        _file.Dispose();
    }
}
