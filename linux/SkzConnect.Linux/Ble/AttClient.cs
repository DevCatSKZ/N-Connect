using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SkzConnect.Linux.Ble;

/// <summary>
/// GATT-Client direkt über einen L2CAP-ATT-Socket (Kanal 4) – an BlueZs GATT-Dienst vorbei.
/// Grund: BlueZ fragt nach dem Verbinden auch „Secondary Services“ ab; darauf antworten Switch-2-Controller nie,
/// die Diensteabfrage hängt bis zum ATT-Timeout und die Verbindung ist verloren. Ein eigener ATT-Client-Socket hat
/// beim Kernel Vorrang vor dem von bluetoothd (wie früher gatttool) und fragt nur ab, was SKZ Connect braucht.
/// Außerdem kann so das Verbindungsintervall per HCI-Befehl verkürzt werden (BlueZ wählt ~50 ms).
/// </summary>
public sealed class AttClient : IDisposable
{
    private const int AF_BLUETOOTH = 31, SOCK_SEQPACKET = 5, SOCK_RAW = 3, BTPROTO_L2CAP = 0, BTPROTO_HCI = 1;
    private const int SOL_L2CAP = 6, L2CAP_CONNINFO = 2, SOL_BLUETOOTH = 274, BT_SECURITY = 4;
    private const ushort ATT_CID = 4;
    public const byte AddressPublic = 1, AddressRandom = 2;

    // ATT-Opcodes
    private const byte ErrorRsp = 0x01, MtuReq = 0x02, MtuRsp = 0x03, FindInfoReq = 0x04, FindInfoRsp = 0x05,
        FindByTypeReq = 0x06, FindByTypeRsp = 0x07, ReadByTypeReq = 0x08, ReadByTypeRsp = 0x09,
        WriteReq = 0x12, WriteRsp = 0x13, Notification = 0x1B, Indication = 0x1D, Confirmation = 0x1E, WriteCmd = 0x52;

    [DllImport("libc", SetLastError = true)] private static extern int socket(int domain, int type, int protocol);
    [DllImport("libc", SetLastError = true)] private static extern unsafe int bind(int fd, byte* addr, int len);
    [DllImport("libc", SetLastError = true)] private static extern unsafe int connect(int fd, byte* addr, int len);
    [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int shutdown(int fd, int how);
    [DllImport("libc", SetLastError = true)] private static extern unsafe nint read(int fd, byte* buf, nint count);
    [DllImport("libc", SetLastError = true)] private static extern unsafe nint write(int fd, byte* buf, nint count);
    [DllImport("libc", SetLastError = true)] private static extern unsafe int getsockopt(int fd, int level, int name, byte* val, int* len);
    [DllImport("libc", SetLastError = true)] private static extern unsafe int setsockopt(int fd, int level, int name, byte* val, int len);

    private readonly int _fd;
    private readonly string _address;
    private readonly object _writeLock = new();
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly Thread _reader;
    private volatile TaskCompletionSource<byte[]>? _pending;
    private volatile byte _pendingOpcode;
    private int _closed, _lostRaised;
    // Wer den Socket noch benutzt: Besitzer (bis Dispose) und – solange er läuft – der Leser. Geschlossen wird erst,
    // wenn beide fertig sind: Ein close() unter dem laufenden read() gäbe die fd-Nummer frei, und der Leser läse
    // danach womöglich aus einem neu geöffneten fremden Socket oder einer Datei.
    private int _users = 1;

    public int Mtu { get; private set; } = 23;
    public ushort HciHandle { get; private set; }

    /// <summary>Benachrichtigung: (Handle, Wert).</summary>
    public event Action<ushort, byte[]>? Notified;
    /// <summary>Verbindung beendet (Controller weg, Bluetooth aus, Fehler).</summary>
    public event Action? Lost;

    private AttClient(int fd, string address)
    {
        _fd = fd;
        _address = address;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "ATT " + address };
    }

    /// <summary>Baut die LE-Verbindung auf (Zeitlimit <paramref name="timeout"/>).</summary>
    public static async Task<AttClient> ConnectAsync(string adapterAddress, string deviceAddress, byte deviceAddressType,
        TimeSpan timeout, CancellationToken ct)
    {
        int fd = OpenSocket(adapterAddress);
        var client = new AttClient(fd, deviceAddress);
        // connect() blockiert bis zur Verbindung; bei Zeitüberschreitung schließt das Schließen des Sockets es ab.
        var connectTask = Task.Run(() => ConnectSocket(fd, deviceAddress, deviceAddressType));
        var done = await Task.WhenAny(connectTask, Task.Delay(timeout, ct)).ConfigureAwait(false);
        if (done != connectTask)
        {
            client.Dispose();
            await connectTask.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException("Controller hat die Verbindung nicht angenommen.");
        }
        int err = await connectTask.ConfigureAwait(false);
        if (err != 0)
        {
            client.Dispose();
            throw new IOException($"Verbindung abgelehnt (errno {err}).");
        }

        client.HciHandle = ReadHciHandle(fd);
        Interlocked.Increment(ref client._users);
        client._reader.Start();
        return client;
    }

    private static unsafe int OpenSocket(string adapterAddress)
    {
        int fd = socket(AF_BLUETOOTH, SOCK_SEQPACKET, BTPROTO_L2CAP);
        if (fd < 0)
            throw new IOException($"Bluetooth-Socket: errno {Marshal.GetLastPInvokeError()}");
        byte* src = stackalloc byte[14];
        FillAddress(src, adapterAddress, AddressPublic);
        if (bind(fd, src, 14) < 0)
        {
            int err = Marshal.GetLastPInvokeError();
            close(fd);
            throw new IOException($"Bluetooth-Socket binden: errno {err}");
        }
        // Niedrigste Sicherheitsstufe: Switch-2-Controller verlangen keine Verschlüsselung (wie Windows/Android).
        byte* level = stackalloc byte[2];
        *(ushort*)level = 1;
        setsockopt(fd, SOL_BLUETOOTH, BT_SECURITY, level, 2);
        return fd;
    }

    private static unsafe int ConnectSocket(int fd, string deviceAddress, byte type)
    {
        byte* dst = stackalloc byte[14];
        FillAddress(dst, deviceAddress, type);
        return connect(fd, dst, 14) < 0 ? Marshal.GetLastPInvokeError() : 0;
    }

    private static unsafe ushort ReadHciHandle(int fd)
    {
        byte* info = stackalloc byte[8];
        int len = 8;
        return getsockopt(fd, SOL_L2CAP, L2CAP_CONNINFO, info, &len) == 0 ? *(ushort*)info : (ushort)0;
    }

    /// <summary>sockaddr_l2: family | psm | bdaddr (umgekehrt) | cid | bdaddr_type.</summary>
    private static unsafe void FillAddress(byte* a, string address, byte type)
    {
        new Span<byte>(a, 14).Clear();
        *(ushort*)a = AF_BLUETOOTH;
        string[] parts = address.Split(':');
        for (int i = 0; i < 6; i++)
            a[4 + i] = Convert.ToByte(parts[5 - i], 16);
        *(ushort*)(a + 10) = ATT_CID;
        a[12] = type;
    }

    // ---------- Senden/Empfangen ----------

    private unsafe void Send(ReadOnlySpan<byte> pdu)
    {
        lock (_writeLock)
        {
            // Prüfung unter der Sperre: Release schließt den fd ebenfalls unter ihr – nie in einen schon geschlossenen
            // (und womöglich neu vergebenen) fd schreiben.
            if (Volatile.Read(ref _closed) != 0)
                throw new IOException("Verbindung getrennt.");
            fixed (byte* p = pdu)
                if (write(_fd, p, pdu.Length) != pdu.Length)
                    throw new IOException($"Senden fehlgeschlagen (errno {Marshal.GetLastPInvokeError()}).");
        }
    }

    /// <summary>Anfrage senden und auf die Antwort warten (ATT erlaubt nur eine offene Anfrage).</summary>
    private async Task<byte[]> RequestAsync(byte[] pdu, byte responseOpcode, CancellationToken ct, int timeoutMs = 5000)
    {
        await _requestLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingOpcode = responseOpcode;
            _pending = tcs;
            Send(pdu);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
            if (done != tcs.Task)
                throw new TimeoutException($"Keine Antwort auf ATT-Anfrage {pdu[0]:X2}.");
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending = null;
            _requestLock.Release();
        }
    }

    private unsafe void ReadLoop()
    {
        byte* buf = stackalloc byte[1024];
        while (Volatile.Read(ref _closed) == 0)
        {
            nint n = read(_fd, buf, 1024);
            if (n <= 0)
                break;
            var pdu = new ReadOnlySpan<byte>(buf, (int)n);
            byte op = pdu[0];
            try
            {
                switch (op)
                {
                    case Notification when pdu.Length >= 3:
                        Notified?.Invoke(BinaryPrimitives.ReadUInt16LittleEndian(pdu[1..]), pdu[3..].ToArray());
                        break;
                    case Indication when pdu.Length >= 3:
                        Send([Confirmation]);
                        Notified?.Invoke(BinaryPrimitives.ReadUInt16LittleEndian(pdu[1..]), pdu[3..].ToArray());
                        break;
                    default:
                        if (_pending is { } p && (op == _pendingOpcode || op == ErrorRsp))
                            p.TrySetResult(pdu.ToArray());
                        break;
                }
            }
            catch (Exception e)
            {
                Log.Warn($"{_address}: ATT: {Log.Reason(e)}");
            }
        }
        _pending?.TrySetException(new IOException("Verbindung getrennt."));
        try
        {
            if (Interlocked.Exchange(ref _lostRaised, 1) == 0 && Volatile.Read(ref _closed) == 0)
                Lost?.Invoke();
        }
        finally
        {
            Release();
        }
    }

    private static void ThrowIfError(byte[] rsp, string what)
    {
        if (rsp[0] == ErrorRsp)
            throw new AttErrorException(what, rsp.Length >= 5 ? rsp[4] : (byte)0);
    }

    // ---------- GATT-Operationen ----------

    public async Task ExchangeMtuAsync(ushort mtu, CancellationToken ct)
    {
        byte[] rsp = await RequestAsync([MtuReq, (byte)mtu, (byte)(mtu >> 8)], MtuRsp, ct).ConfigureAwait(false);
        if (rsp[0] == MtuRsp && rsp.Length >= 3)
            Mtu = Math.Min(mtu, BinaryPrimitives.ReadUInt16LittleEndian(rsp.AsSpan(1)));
    }

    /// <summary>Handle-Bereich eines primären Dienstes (Find By Type Value auf 0x2800).</summary>
    public async Task<(ushort Start, ushort End)?> FindServiceAsync(Guid uuid, CancellationToken ct)
    {
        var pdu = new byte[7 + 16];
        pdu[0] = FindByTypeReq;
        BinaryPrimitives.WriteUInt16LittleEndian(pdu.AsSpan(1), 0x0001);
        BinaryPrimitives.WriteUInt16LittleEndian(pdu.AsSpan(3), 0xFFFF);
        BinaryPrimitives.WriteUInt16LittleEndian(pdu.AsSpan(5), 0x2800);
        WriteUuid128(pdu.AsSpan(7), uuid);
        byte[] rsp = await RequestAsync(pdu, FindByTypeRsp, ct).ConfigureAwait(false);
        if (rsp[0] == ErrorRsp || rsp.Length < 5)
            return null;
        return (BinaryPrimitives.ReadUInt16LittleEndian(rsp.AsSpan(1)), BinaryPrimitives.ReadUInt16LittleEndian(rsp.AsSpan(3)));
    }

    public sealed record Characteristic(Guid Uuid, byte Properties, ushort Declaration, ushort Value);

    /// <summary>Alle Merkmale eines Dienstes (Read By Type auf 0x2803).</summary>
    public async Task<List<Characteristic>> DiscoverCharacteristicsAsync(ushort start, ushort end, CancellationToken ct)
    {
        var result = new List<Characteristic>();
        ushort from = start;
        while (from <= end)
        {
            var pdu = new byte[7];
            pdu[0] = ReadByTypeReq;
            BinaryPrimitives.WriteUInt16LittleEndian(pdu.AsSpan(1), from);
            BinaryPrimitives.WriteUInt16LittleEndian(pdu.AsSpan(3), end);
            BinaryPrimitives.WriteUInt16LittleEndian(pdu.AsSpan(5), 0x2803);
            byte[] rsp = await RequestAsync(pdu, ReadByTypeRsp, ct).ConfigureAwait(false);
            if (rsp[0] == ErrorRsp)
                break; // Attribute Not Found = fertig
            int len = rsp[1];
            ushort last = from;
            for (int i = 2; i + len <= rsp.Length; i += len)
            {
                var c = ParseCharacteristic(rsp, i, len);
                result.Add(c);
                last = c.Declaration;
            }
            if (last == ushort.MaxValue)
                break;
            from = (ushort)(last + 1);
        }
        return result;
    }

    /// <summary>Eintrag einer Read-By-Type-Antwort: Deklaration | Eigenschaften | Wert-Handle | UUID (16 oder 128 Bit).</summary>
    private static Characteristic ParseCharacteristic(byte[] rsp, int offset, int len)
    {
        var e = rsp.AsSpan(offset, len);
        ushort decl = BinaryPrimitives.ReadUInt16LittleEndian(e);
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(e[3..]);
        Guid uuid = len == 21 ? ReadUuid128(e[5..]) : Uuid16(BinaryPrimitives.ReadUInt16LittleEndian(e[5..]));
        return new Characteristic(uuid, e[2], decl, value);
    }

    /// <summary>Handle des CCCD (0x2902) zwischen <paramref name="from"/> und <paramref name="to"/>.</summary>
    public async Task<ushort?> FindCccdAsync(ushort from, ushort to, CancellationToken ct)
    {
        while (from <= to)
        {
            var pdu = new byte[5];
            pdu[0] = FindInfoReq;
            BinaryPrimitives.WriteUInt16LittleEndian(pdu.AsSpan(1), from);
            BinaryPrimitives.WriteUInt16LittleEndian(pdu.AsSpan(3), to);
            byte[] rsp = await RequestAsync(pdu, FindInfoRsp, ct).ConfigureAwait(false);
            if (rsp[0] == ErrorRsp)
                return null;
            int entry = rsp[1] == 1 ? 4 : 18;
            ushort last = from;
            for (int i = 2; i + entry <= rsp.Length; i += entry)
            {
                ushort h = BinaryPrimitives.ReadUInt16LittleEndian(rsp.AsSpan(i));
                if (entry == 4 && BinaryPrimitives.ReadUInt16LittleEndian(rsp.AsSpan(i + 2)) == 0x2902)
                    return h;
                last = h;
            }
            if (last == ushort.MaxValue)
                return null;
            from = (ushort)(last + 1);
        }
        return null;
    }

    /// <summary>Schreiben mit Bestätigung (z. B. CCCD).</summary>
    public async Task WriteRequestAsync(ushort handle, byte[] value, CancellationToken ct)
    {
        var pdu = new byte[3 + value.Length];
        pdu[0] = WriteReq;
        BinaryPrimitives.WriteUInt16LittleEndian(pdu.AsSpan(1), handle);
        value.CopyTo(pdu, 3);
        ThrowIfError(await RequestAsync(pdu, WriteRsp, ct).ConfigureAwait(false), $"Schreiben {handle:X4}");
    }

    /// <summary>Schreiben ohne Antwort (Befehle, Vibration).</summary>
    public bool WriteCommand(ushort handle, ReadOnlySpan<byte> value)
    {
        if (Volatile.Read(ref _closed) != 0)
            return false; // schneller Ausstieg; die verbindliche Prüfung macht Send unter der Sperre
        Span<byte> pdu = stackalloc byte[3 + value.Length];
        pdu[0] = WriteCmd;
        BinaryPrimitives.WriteUInt16LittleEndian(pdu[1..], handle);
        value.CopyTo(pdu[3..]);
        try
        {
            Send(pdu);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // ---------- Verbindungsintervall ----------

    /// <summary>
    /// Kurzes Verbindungsintervall beim Bluetooth-Chip anfordern (HCI LE Connection Update, Einheiten 1,25 ms).
    /// Braucht root (raw HCI-Socket). false = nicht möglich, die Verbindung läuft dann mit dem bisherigen Intervall.
    /// </summary>
    public unsafe bool RequestConnectionInterval(int adapterIndex, ushort minUnits = 6, ushort maxUnits = 9, ushort timeout10ms = 200)
    {
        if (HciHandle == 0)
            return false;
        int fd = socket(AF_BLUETOOTH, SOCK_RAW, BTPROTO_HCI);
        if (fd < 0)
            return false;
        try
        {
            byte* addr = stackalloc byte[6];
            *(ushort*)addr = AF_BLUETOOTH;
            *(ushort*)(addr + 2) = (ushort)adapterIndex;
            *(ushort*)(addr + 4) = 0; // HCI_CHANNEL_RAW
            if (bind(fd, addr, 6) < 0)
                return false;
            byte* cmd = stackalloc byte[4 + 14];
            cmd[0] = 0x01; // HCI-Befehlspaket
            *(ushort*)(cmd + 1) = 0x2013; // LE Connection Update
            cmd[3] = 14;
            ushort* p = (ushort*)(cmd + 4);
            p[0] = HciHandle;
            p[1] = minUnits;
            p[2] = maxUnits;
            p[3] = 0;          // Latenz
            p[4] = timeout10ms;
            p[5] = 0;
            p[6] = 0;
            return write(fd, cmd, 18) == 18;
        }
        finally
        {
            close(fd);
        }
    }

    // ---------- UUID-Hilfen (ATT: Little Endian) ----------

    private static void WriteUuid128(Span<byte> dst, Guid uuid)
    {
        Span<byte> be = stackalloc byte[16];
        uuid.TryWriteBytes(be, bigEndian: true, out _);
        for (int i = 0; i < 16; i++)
            dst[i] = be[15 - i];
    }

    private static Guid ReadUuid128(ReadOnlySpan<byte> src)
    {
        Span<byte> be = stackalloc byte[16];
        for (int i = 0; i < 16; i++)
            be[i] = src[15 - i];
        return new Guid(be, bigEndian: true);
    }

    private static Guid Uuid16(ushort u) => new($"0000{u:x4}-0000-1000-8000-00805f9b34fb");

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        // Bewusst ohne Schreibsperre: Ein Sender, der in write() hängt (Sendepuffer voll, Controller quittiert nicht),
        // hält sie – shutdown beendet sein write sofort (EPIPE), weckt den Leser (read liefert 0) und beendet die
        // Verbindung. Der fd ist dabei sicher noch offen: Der Besitzer gibt seinen Anteil erst unten ab. close läuft in
        // Release unter der Sperre, und Send prüft _closed unter ihr – nach dem close schreibt niemand mehr in den fd.
        shutdown(_fd, 2);
        Release();
    }

    /// <summary>Besitzer oder Leser ist fertig – der Letzte schließt den fd (unter der Schreibsperre).</summary>
    private void Release()
    {
        if (Interlocked.Decrement(ref _users) != 0)
            return;
        lock (_writeLock)
            close(_fd);
    }
}

public sealed class AttErrorException(string what, byte code) : IOException($"{what}: ATT-Fehler 0x{code:X2}")
{
    public byte Code { get; } = code;
}
