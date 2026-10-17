using System.Runtime.InteropServices;
using System.Text;
using Switch2Pro.Protocol;

namespace NConnect.Linux.Output;

/// <summary>
/// Virtueller Xbox-360-Controller über /dev/uinput (braucht root oder die Gruppe „input“; unter LibreELEC läuft der
/// Dienst als root). Name, Kennung und Belegung wie der Linux-Treiber xpad („Microsoft X-Box 360 pad“, 045E:028E), damit
/// Kodi (peripheral.joystick), RetroArch und SDL ihn mit ihren fertigen Belegungen erkennen.
/// Vibration: Spiele laden FF_RUMBLE-Effekte hoch (UI_BEGIN/END_FF_UPLOAD) und spielen sie mit EV_FF ab.
/// </summary>
public sealed unsafe class UInputPad : IDisposable
{
    // ---- Linux-Konstanten (linux/input-event-codes.h, linux/uinput.h) ----
    private const ushort EV_SYN = 0x00, EV_KEY = 0x01, EV_ABS = 0x03, EV_FF = 0x15, EV_UINPUT = 0x0101;
    private const ushort SYN_REPORT = 0;
    private const ushort BTN_A = 0x130, BTN_B = 0x131, BTN_X = 0x133, BTN_Y = 0x134, BTN_TL = 0x136, BTN_TR = 0x137,
        BTN_SELECT = 0x13a, BTN_START = 0x13b, BTN_MODE = 0x13c, BTN_THUMBL = 0x13d, BTN_THUMBR = 0x13e;
    private const ushort ABS_X = 0x00, ABS_Y = 0x01, ABS_Z = 0x02, ABS_RX = 0x03, ABS_RY = 0x04, ABS_RZ = 0x05,
        ABS_HAT0X = 0x10, ABS_HAT0Y = 0x11;
    private const ushort FF_RUMBLE = 0x50;
    private const ushort UI_FF_UPLOAD = 1, UI_FF_ERASE = 2;
    private const int O_RDWR = 2, O_CLOEXEC = 0x80000;

    private static readonly bool Is64 = IntPtr.Size == 8;
    private static readonly nuint UI_SET_EVBIT = 0x40045564, UI_SET_KEYBIT = 0x40045565, UI_SET_ABSBIT = 0x40045567,
        UI_SET_FFBIT = 0x4004556B, UI_DEV_CREATE = 0x5501, UI_DEV_DESTROY = 0x5502, UI_DEV_SETUP = 0x405C5503,
        UI_ABS_SETUP = 0x401C5504, UI_BEGIN_FF_ERASE = 0xC00C55CA, UI_END_FF_ERASE = 0x400C55CB;
    // _IOW('U', 108, char*): Größe = Zeigergröße
    private static readonly nuint UI_SET_PHYS = Is64 ? (nuint)0x4008556C : 0x4004556C;
    // struct uinput_ff_upload enthält zweimal struct ff_effect (48 Byte auf 64 Bit, 44 auf 32 Bit).
    private static readonly int FfUploadSize = Is64 ? 104 : 96;
    private static readonly nuint UI_BEGIN_FF_UPLOAD = (nuint)(0xC0000000u | ((uint)FfUploadSize << 16) | 0x55C8);
    private static readonly nuint UI_END_FF_UPLOAD = (nuint)(0x40000000u | ((uint)FfUploadSize << 16) | 0x55C9);
    private static readonly int EventSize = Is64 ? 24 : 16;

    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int ioctl(int fd, nuint request, nint arg);
    [DllImport("libc", SetLastError = true)] private static extern nint write(int fd, byte* buf, nint count);
    [DllImport("libc", SetLastError = true)] private static extern nint read(int fd, byte* buf, nint count);
    [DllImport("libc", SetLastError = true)] private static extern int poll(PollFd* fds, nuint nfds, int timeout);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    private const short POLLIN = 0x001;

    private readonly int _fd;
    private readonly object _writeLock = new();
    private readonly Dictionary<short, (ushort Strong, ushort Weak)> _effects = [];
    private readonly Thread _reader;
    private GamepadState _last;
    private bool _hasLast;
    private int _disposed;

    public string Name { get; }

    /// <summary>Spiel lässt vibrieren: große/kleine Amplitude 0…255.</summary>
    public event Action<byte, byte>? Rumble;

    private UInputPad(int fd, string name)
    {
        _fd = fd;
        Name = name;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "uinput-Leser" };
        _reader.Start();
    }

    /// <summary>Gerätename wie beim echten Xbox-360-Controller – Kodi wählt seine fertige Belegung nach Name und Tastenzahl.</summary>
    public const string XboxName = "Microsoft X-Box 360 pad";

    /// <summary>
    /// Legt den virtuellen Controller an. <paramref name="phys"/> unterscheidet die Spieler (z. B. „nconnect/player1“),
    /// der Name bleibt der eines echten Xbox-360-Controllers. Wirft, wenn /dev/uinput fehlt oder keine Rechte bestehen.
    /// </summary>
    public static UInputPad Create(string phys, string name = XboxName)
    {
        int fd = open("/dev/uinput", O_RDWR | O_CLOEXEC);
        if (fd < 0)
            throw new IOException($"/dev/uinput nicht nutzbar (errno {Marshal.GetLastPInvokeError()}) – läuft der Dienst als root, ist das Modul uinput geladen?");
        try
        {
            Check(ioctl(fd, UI_SET_EVBIT, EV_KEY), "EV_KEY");
            Check(ioctl(fd, UI_SET_EVBIT, EV_ABS), "EV_ABS");
            Check(ioctl(fd, UI_SET_EVBIT, EV_FF), "EV_FF");
            foreach (ushort key in (ushort[])[BTN_A, BTN_B, BTN_X, BTN_Y, BTN_TL, BTN_TR, BTN_SELECT, BTN_START, BTN_MODE, BTN_THUMBL, BTN_THUMBR])
                Check(ioctl(fd, UI_SET_KEYBIT, key), "Taste");
            foreach (ushort abs in (ushort[])[ABS_X, ABS_Y, ABS_Z, ABS_RX, ABS_RY, ABS_RZ, ABS_HAT0X, ABS_HAT0Y])
                Check(ioctl(fd, UI_SET_ABSBIT, abs), "Achse");
            Check(ioctl(fd, UI_SET_FFBIT, FF_RUMBLE), "FF_RUMBLE");

            // Achsen wie xpad: Sticks ±32767 (Rauschen 16, Totbereich 128), Trigger 0…255, Steuerkreuz −1…1.
            AbsSetup(fd, ABS_X, -32768, 32767, 16, 128);
            AbsSetup(fd, ABS_Y, -32768, 32767, 16, 128);
            AbsSetup(fd, ABS_RX, -32768, 32767, 16, 128);
            AbsSetup(fd, ABS_RY, -32768, 32767, 16, 128);
            AbsSetup(fd, ABS_Z, 0, 255, 0, 0);
            AbsSetup(fd, ABS_RZ, 0, 255, 0, 0);
            AbsSetup(fd, ABS_HAT0X, -1, 1, 0, 0);
            AbsSetup(fd, ABS_HAT0Y, -1, 1, 0, 0);

            // struct uinput_setup: input_id (bustype, vendor, product, version) | char name[80] | u32 ff_effects_max
            byte* setup = stackalloc byte[92];
            new Span<byte>(setup, 92).Clear();
            *(ushort*)(setup + 0) = 0x0003;  // BUS_USB
            *(ushort*)(setup + 2) = 0x045E;  // Microsoft
            *(ushort*)(setup + 4) = 0x028E;  // Xbox 360 Controller
            *(ushort*)(setup + 6) = 0x0114;
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            for (int i = 0; i < Math.Min(nameBytes.Length, 79); i++)
                setup[8 + i] = nameBytes[i];
            *(uint*)(setup + 88) = 16;
            Check(ioctl(fd, UI_DEV_SETUP, (nint)setup), "UI_DEV_SETUP");
            byte[] physBytes = Encoding.ASCII.GetBytes(phys + "\0");
            fixed (byte* p = physBytes)
                ioctl(fd, UI_SET_PHYS, (nint)p); // nur Kennzeichnung – Fehler egal
            Check(ioctl(fd, UI_DEV_CREATE, 0), "UI_DEV_CREATE");
        }
        catch
        {
            close(fd);
            throw;
        }
        var pad = new UInputPad(fd, phys);
        pad.Update(default);
        Log.Info($"Virtueller Xbox-360-Controller angelegt ({phys})");
        return pad;
    }

    private static void AbsSetup(int fd, ushort code, int min, int max, int fuzz, int flat)
    {
        // struct uinput_abs_setup: u16 code | 2 Byte Füllung | input_absinfo (value, min, max, fuzz, flat, resolution)
        byte* s = stackalloc byte[28];
        new Span<byte>(s, 28).Clear();
        *(ushort*)s = code;
        *(int*)(s + 8) = min;
        *(int*)(s + 12) = max;
        *(int*)(s + 16) = fuzz;
        *(int*)(s + 20) = flat;
        Check(ioctl(fd, UI_ABS_SETUP, (nint)s), "UI_ABS_SETUP");
    }

    private static void Check(int result, string what)
    {
        if (result < 0)
            throw new IOException($"uinput {what}: errno {Marshal.GetLastPInvokeError()}");
    }

    /// <summary>Neuen Gamepad-Zustand senden (nur geänderte Werte, dann SYN_REPORT).</summary>
    public void Update(GamepadState g)
    {
        lock (_writeLock)
        {
            // Prüfung unter der Sperre: Dispose schließt den fd ebenfalls unter ihr – sonst könnte ein Schreiber, der
            // die Prüfung gerade passiert hat, in einen schon geschlossenen (und womöglich neu vergebenen) fd schreiben.
            if (Volatile.Read(ref _disposed) != 0)
                return;
            var b = g.Buttons;
            var old = _last.Buttons;
            bool first = !_hasLast;
            void Key(XButtons flag, ushort code)
            {
                if (first || (b & flag) != (old & flag))
                    Emit(EV_KEY, code, (b & flag) != 0 ? 1 : 0);
            }
            void Abs(ushort code, int value, int previous)
            {
                if (first || value != previous)
                    Emit(EV_ABS, code, value);
            }
            Key(XButtons.A, BTN_A);
            Key(XButtons.B, BTN_B);
            Key(XButtons.X, BTN_X);
            Key(XButtons.Y, BTN_Y);
            Key(XButtons.LB, BTN_TL);
            Key(XButtons.RB, BTN_TR);
            Key(XButtons.Back, BTN_SELECT);
            Key(XButtons.Start, BTN_START);
            Key(XButtons.Guide, BTN_MODE);
            Key(XButtons.LS, BTN_THUMBL);
            Key(XButtons.RS, BTN_THUMBR);
            // Linux-Konvention: Y nach unten positiv (GamepadState: oben positiv).
            Abs(ABS_X, g.LeftX, _last.LeftX);
            Abs(ABS_Y, Math.Clamp(-g.LeftY, -32768, 32767), Math.Clamp(-_last.LeftY, -32768, 32767));
            Abs(ABS_RX, g.RightX, _last.RightX);
            Abs(ABS_RY, Math.Clamp(-g.RightY, -32768, 32767), Math.Clamp(-_last.RightY, -32768, 32767));
            Abs(ABS_Z, g.LeftTrigger, _last.LeftTrigger);
            Abs(ABS_RZ, g.RightTrigger, _last.RightTrigger);
            Abs(ABS_HAT0X, HatX(b), HatX(old));
            Abs(ABS_HAT0Y, HatY(b), HatY(old));
            Emit(EV_SYN, SYN_REPORT, 0);
            _last = g;
            _hasLast = true;
        }
    }

    private static int HatX(XButtons b) => (b.HasFlag(XButtons.Right) ? 1 : 0) - (b.HasFlag(XButtons.Left) ? 1 : 0);
    private static int HatY(XButtons b) => (b.HasFlag(XButtons.Down) ? 1 : 0) - (b.HasFlag(XButtons.Up) ? 1 : 0);

    private void Emit(ushort type, ushort code, int value)
    {
        // struct input_event: timeval (2× long) | u16 type | u16 code | s32 value – Zeit setzt der Kernel selbst.
        byte* ev = stackalloc byte[24];
        new Span<byte>(ev, EventSize).Clear();
        int o = Is64 ? 16 : 8;
        *(ushort*)(ev + o) = type;
        *(ushort*)(ev + o + 2) = code;
        *(int*)(ev + o + 4) = value;
        write(_fd, ev, EventSize);
    }

    /// <summary>
    /// Liest Ereignisse vom Kernel: Effekte hochladen/löschen (Vibration) und EV_FF (abspielen/stoppen).
    /// Gewartet wird mit poll (höchstens 250 ms), gelesen erst danach unter der Schreibsperre samt Prüfung auf Dispose –
    /// so liest der Leser nie aus einem fd, den Dispose schon geschlossen hat (die Nummer kann neu vergeben sein), und er
    /// endet spätestens 250 ms nach Dispose.
    /// </summary>
    private void ReadLoop()
    {
        byte* ev = stackalloc byte[24];
        int o = Is64 ? 16 : 8;
        var pfd = new PollFd { Fd = _fd, Events = POLLIN };
        while (Volatile.Read(ref _disposed) == 0)
        {
            pfd.Revents = 0;
            int ready = poll(&pfd, 1, 250);
            if (ready == 0)
                continue; // nichts angekommen
            nint n;
            lock (_writeLock)
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return;
                n = ready > 0 && (pfd.Revents & POLLIN) != 0 ? read(_fd, ev, EventSize) : -1;
            }
            if (n != EventSize)
            {
                Thread.Sleep(50); // Fehler oder Unterbrechung – kurz warten, nicht im Kreis drehen
                continue;
            }
            ushort type = *(ushort*)(ev + o);
            ushort code = *(ushort*)(ev + o + 2);
            int value = *(int*)(ev + o + 4);
            try
            {
                if (type == EV_UINPUT && code == UI_FF_UPLOAD)
                    HandleUpload((uint)value);
                else if (type == EV_UINPUT && code == UI_FF_ERASE)
                    HandleErase((uint)value);
                else if (type == EV_FF)
                    Play((short)code, value != 0);
            }
            catch (Exception e)
            {
                Log.Warn($"{Name}: Vibration: {Log.Reason(e)}");
            }
        }
    }

    private void HandleUpload(uint requestId)
    {
        byte* up = stackalloc byte[104];
        new Span<byte>(up, FfUploadSize).Clear();
        *(uint*)up = requestId;
        lock (_writeLock) // ioctl auf den fd nur, solange er offen ist (siehe ReadLoop)
        {
            if (Volatile.Read(ref _disposed) != 0 || ioctl(_fd, UI_BEGIN_FF_UPLOAD, (nint)up) < 0)
                return;
            // effect beginnt bei Byte 8: type (u16), id (s16) … Union ab Effekt-Byte 16: strong (u16), weak (u16).
            ushort effectType = *(ushort*)(up + 8);
            short id = *(short*)(up + 10);
            if (effectType == FF_RUMBLE)
                lock (_effects)
                    _effects[id] = (*(ushort*)(up + 24), *(ushort*)(up + 26));
            *(int*)(up + 4) = 0; // retval
            ioctl(_fd, UI_END_FF_UPLOAD, (nint)up);
        }
    }

    private void HandleErase(uint requestId)
    {
        byte* er = stackalloc byte[12];
        new Span<byte>(er, 12).Clear();
        *(uint*)er = requestId;
        lock (_writeLock)
        {
            if (Volatile.Read(ref _disposed) != 0 || ioctl(_fd, UI_BEGIN_FF_ERASE, (nint)er) < 0)
                return;
            short id = (short)*(uint*)(er + 8);
            lock (_effects)
                _effects.Remove(id);
            *(int*)(er + 4) = 0;
            ioctl(_fd, UI_END_FF_ERASE, (nint)er);
        }
        Rumble?.Invoke(0, 0);
    }

    private void Play(short id, bool on)
    {
        (ushort Strong, ushort Weak) e;
        lock (_effects)
            if (!_effects.TryGetValue(id, out e))
                return;
        Rumble?.Invoke(on ? (byte)(e.Strong >> 8) : (byte)0, on ? (byte)(e.Weak >> 8) : (byte)0);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        lock (_writeLock)
        {
            ioctl(_fd, UI_DEV_DESTROY, 0);
            close(_fd); // der Leser sieht _disposed spätestens nach seinem poll-Zeitlimit und endet
        }
        Log.Info($"Virtueller Controller „{Name}“ entfernt");
    }
}
