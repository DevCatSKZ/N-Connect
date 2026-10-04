using System.Runtime.InteropServices;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Meldet neu eingesteckte Laufwerke, die wie eine Switch-SD-Karte aussehen (Kartenleser oder Switch als
/// USB-Massenspeicher, z. B. hekate UMS). Hört auf WM_DEVICECHANGE in einem unsichtbaren Fenster – Windows schickt
/// die Laufwerksmeldung nur an Fenster der obersten Ebene, nicht an reine Nachrichtenfenster. Liest nur.
/// </summary>
internal sealed class SwitchCardWatcher : NativeWindow, IDisposable
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVTYP_VOLUME = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_VOLUME
    {
        public int dbcv_size;
        public int dbcv_devicetype;
        public int dbcv_reserved;
        public uint dbcv_unitmask;
        public ushort dbcv_flags;
    }

    private readonly SynchronizationContext _ui;

    /// <summary>Switch-SD-Karte gefunden (im UI-Thread).</summary>
    public event Action<SwitchCardInfo>? CardArrived;

    public SwitchCardWatcher()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        // Unsichtbares Fenster der obersten Ebene (ohne WS_VISIBLE, nicht in der Taskleiste).
        CreateHandle(new CreateParams { Caption = "N-Connect SD", ExStyle = 0x80 /* WS_EX_TOOLWINDOW */ });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DEVICECHANGE && (int)m.WParam == DBT_DEVICEARRIVAL && m.LParam != IntPtr.Zero
            && Marshal.ReadInt32(m.LParam, 4) == DBT_DEVTYP_VOLUME)
        {
            var volume = Marshal.PtrToStructure<DEV_BROADCAST_VOLUME>(m.LParam);
            for (int i = 0; i < 26; i++)
            {
                if ((volume.dbcv_unitmask & (1u << i)) != 0)
                    Check($"{(char)('A' + i)}:\\");
            }
        }
        base.WndProc(ref m);
    }

    private void Check(string root)
    {
        // Karte im Hintergrund prüfen: ein frisch eingestecktes Laufwerk antwortet manchmal erst nach Sekunden.
        Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (new DriveInfo(root).IsReady)
                        break;
                }
                catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
                {
                }
                await Task.Delay(1000);
            }
            var info = SwitchCard.Inspect(root);
            if (info.IsSwitchCard)
            {
                Log.Info($"Switch-SD-Karte erkannt: {info.Summary}");
                _ui.Post(_ => CardArrived?.Invoke(info), null);
            }
        }).Forget("SD-Karte prüfen");
    }

    /// <summary>Alle bereiten Laufwerke nach Switch-SD-Karten durchsuchen (Karten mit Kopplungsdaten zuerst).</summary>
    public static List<SwitchCardInfo> FindCards()
    {
        var result = new List<SwitchCardInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is not (DriveType.Removable or DriveType.Fixed) || !drive.IsReady)
                    continue;
                // Systemlaufwerk überspringen.
                if (string.Equals(drive.RootDirectory.FullName, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))
                    continue;
                var info = SwitchCard.Inspect(drive.RootDirectory.FullName);
                if (info.IsSwitchCard)
                    result.Add(info);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return [.. result.OrderByDescending(c => c.HasPairingExport)];
    }

    public void Dispose() => DestroyHandle();
}
