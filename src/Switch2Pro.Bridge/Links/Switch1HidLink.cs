using Switch2Pro.Bridge.Usb;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge.Links;

/// <summary>
/// Switch-1-Controller (Pro Controller, Joy-Con L/R), in Windows per Bluetooth gekoppelt.
/// Windows stellt ihn als HID-Gerät bereit; wir schalten ihn in den Vollbericht 0x30 (60 Hz, mit Gyro),
/// lesen die Kalibrierung aus dem SPI-Flash und senden HD-Rumble. Ablauf wie SDL (SDL_hidapi_switch.c).
/// </summary>
internal sealed class Switch1HidLink : IControllerLink
{
    private const int ReplyTimeoutMs = 300;
    private const int InputTimeoutMs = 3000;
    private const int RumbleIntervalMs = 30;   // SDL: höchstens alle 30 ms schreiben
    private const int RumbleRefreshMs = 50;    // und laufende Vibration alle 50 ms auffrischen

    private readonly HidChannel _hid;
    private readonly CancellationTokenSource _cts = new();
    private readonly RateMeter _rate = new();
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly object _replyGate = new();
    private (byte Subcommand, uint Address, TaskCompletionSource<byte[]> Reply)? _pending;
    private int _counter;
    private ImuCalibration _imu = ImuCalibration.Default;
    private long _lastInputTicks = Environment.TickCount64;
    private volatile int _rumbleLarge, _rumbleSmall;
    private float _rumbleStrength = 1f;
    private int _closed, _lostRaised;
    private Task? _reader;

    public ControllerKind Kind { get; private set; }
    public Transport Transport { get; }
    public string Id { get; }
    public string? Address { get; private set; }
    public DeviceCalibration Calibration { get; private set; } = DeviceCalibration.Default;
    public ControllerInfo Info { get; private set; } = new();
    public ControllerState? LastState { get; private set; }
    public double ReportRate => _rate.Rate;
    public bool IsLost => Volatile.Read(ref _lostRaised) == 1;

    public event Action<IControllerLink, ControllerState>? StateReceived;
    public event Action<IControllerLink>? Lost;

    private Switch1HidLink(ControllerKind kind, string path, HidChannel hid, Transport transport)
    {
        Kind = kind;
        Id = path;
        _hid = hid;
        Transport = transport;
    }

    public static async Task<Switch1HidLink> ConnectAsync(string hidPath, ControllerKind kind, CancellationToken ct)
    {
        var hid = new HidChannel(hidPath);
        bool usb = hidPath.Contains("vid_057e&pid", StringComparison.OrdinalIgnoreCase);
        var link = new Switch1HidLink(kind, hidPath, hid, usb ? Transport.Usb : Transport.Bluetooth);
        try
        {
            await link.StartAsync(usb, ct);
            return link;
        }
        catch
        {
            await link.DisposeAsync();
            throw;
        }
    }

    private async Task StartAsync(bool usb, CancellationToken ct)
    {
        _reader = Task.Run(() => ReadLoopAsync(_cts.Token));
        if (usb)
        {
            // USB: Handshake, hohe Geschwindigkeit, Handshake, nur noch über USB senden (wie SDL).
            foreach (byte cmd in new byte[] { 0x02, 0x03, 0x02, 0x04 })
            {
                await _hid.WriteAsync([0x80, cmd], ct);
                await Task.Delay(30, ct);
            }
        }

        if (await SubcommandAsync(Switch1.SubDeviceInfo, [], ct) is { Length: >= 10 } info)
        {
            // Byte 4–9: MAC (big-endian).
            Address = string.Join(':', info.Skip(4).Take(6).Select(b => b.ToString("X2")));
            Info = Info with { Firmware = $"{info[0]}.{info[1]:D2}" };
            // Byte 2: Gerätetyp – NES-Controller geben sich per Produkt-ID als Joy-Con aus.
            var detected = ControllerKinds.FromSwitch1DeviceType(info[2], Kind);
            if (detected != Kind)
            {
                Log.Info($"{Id}: Gerätetyp 0x{info[2]:X2} → {detected.DisplayName()}");
                Kind = detected;
            }
        }
        else
        {
            // Keine Antwort: kein (bereiter) Switch-1-Controller – später erneut versuchen.
            throw new IOException($"{Id}: keine Geräte-Antwort");
        }

        await ReadCalibrationAsync(ct);
        await SubcommandAsync(Switch1.SubSetInputMode, [Switch1.InputFull], ct);
        await SubcommandAsync(Switch1.SubEnableImu, [0x01], ct);
        await SubcommandAsync(Switch1.SubEnableVibration, [0x01], ct);
        _lastInputTicks = Environment.TickCount64;
        _ = Task.Run(() => WatchdogAsync(_cts.Token));
        _ = Task.Run(() => RumbleLoopAsync(_cts.Token));
        Log.Info($"{Id}: {Kind.DisplayName()} bereit ({Address ?? "?"})");
    }

    private async Task ReadCalibrationAsync(CancellationToken ct)
    {
        var cal = new DeviceCalibration();
        async Task<StickCalibration?> Stick(uint user, uint factory, bool left)
        {
            if (await SpiReadAsync(user, 11, ct) is { } u && Switch1.HasUserMagic(u) && Switch1.TryParseStick(u.AsSpan(2), left, out var uc))
                return uc;
            if (await SpiReadAsync(factory, 9, ct) is { } f && Switch1.TryParseStick(f, left, out var fc))
                return fc;
            return null;
        }
        if (Kind != ControllerKind.JoyCon1Right && await Stick(Switch1.SpiUserStickLeft, Switch1.SpiFactoryStickLeft, true) is { } l)
            cal = cal with { Left = l };
        if (Kind != ControllerKind.JoyCon1Left && await Stick(Switch1.SpiUserStickRight, Switch1.SpiFactoryStickRight, false) is { } r)
            cal = cal with { Right = r };

        if (await SpiReadAsync(Switch1.SpiUserImu, 26, ct) is { } ui && Switch1.HasUserMagic(ui) && Switch1.TryParseImu(ui.AsSpan(2), out var uimu))
            _imu = uimu;
        else if (await SpiReadAsync(Switch1.SpiFactoryImu, 24, ct) is { } fi && Switch1.TryParseImu(fi, out var fimu))
            _imu = fimu;

        if (await SpiReadAsync(Switch1.SpiBodyColor, 12, ct) is { Length: 12 } color)
        {
            int C(int o) => color[o] << 16 | color[o + 1] << 8 | color[o + 2];
            // Gehäuse, Tasten, Griff links, Griff rechts (Griffe nur beim Pro Controller belegt).
            Info = Info with { BodyColor = C(0), ButtonColor = C(3), GripColor = Kind == ControllerKind.Pro1 ? C(6) : null };
        }
        Calibration = cal;
        Log.Info($"{Id}: Kalibrierung L={cal.Left} R={cal.Right} IMU={_imu}");
    }

    private Task<byte[]?> SpiReadAsync(uint address, byte length, CancellationToken ct) =>
        SubcommandAsync(Switch1.SubSpiRead, Switch1.SpiReadArgs(address, length), ct, address);

    /// <summary>Unterbefehl senden (bis zu 3 Versuche) und Antwort 0x21 abwarten.</summary>
    private async Task<byte[]?> SubcommandAsync(byte subcommand, byte[] data, CancellationToken ct, uint spiAddress = 0)
    {
        await _commandLock.WaitAsync(ct);
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var reply = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_replyGate)
                    _pending = (subcommand, spiAddress, reply);
                var rumble = CurrentRumble();
                await _hid.WriteAsync(Switch1.Subcommand(Interlocked.Increment(ref _counter), subcommand, data, rumble), ct);
                if (await Task.WhenAny(reply.Task, Task.Delay(ReplyTimeoutMs, ct)) == reply.Task)
                    return await reply.Task;
            }
            Log.Warn($"{Id}: keine Antwort auf Unterbefehl {subcommand:X2}");
            return null;
        }
        finally
        {
            lock (_replyGate)
                _pending = null;
            _commandLock.Release();
        }
    }

    public Task SetPlayerAsync(int playerIndex) =>
        Volatile.Read(ref _closed) == 1
            ? Task.CompletedTask
            : SubcommandAsync(Switch1.SubPlayerLights, [Commands.PlayerLedMask(playerIndex)], _cts.Token);

    /// <summary>Unterbefehl 0x06 (HCI-Zustand) mit 0x00: Controller trennt die Verbindung und schläft.</summary>
    public Task SleepAsync() =>
        Volatile.Read(ref _closed) == 1 ? Task.CompletedTask : SubcommandAsync(Switch1.SubSetHciState, [0x00], _cts.Token);

    // ---------- amiibo (NFC) ----------

    /// <summary>Hat dieser Controller einen NFC-Leser? (rechter Joy-Con und Pro Controller der Switch 1)</summary>
    public bool HasNfc => Kind is ControllerKind.JoyCon1Right or ControllerKind.Pro1;

    private TaskCompletionSource<(byte Kind, byte[] Data)>? _mcuWaiter;
    private readonly SemaphoreSlim _nfcLock = new(1, 1);

    /// <summary>MCU-Anfrage (Bericht 0x11) senden und die nächste MCU-Antwort aus Bericht 0x31 abwarten.</summary>
    private async Task<(byte Kind, byte[] Data)?> McuAsync(byte mcuSubcommand, byte[] data, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var waiter = new TaskCompletionSource<(byte, byte[])>(TaskCreationOptions.RunContinuationsAsynchronously);
            _mcuWaiter = waiter;
            await _hid.WriteAsync(Nfc.McuReport(Interlocked.Increment(ref _counter), mcuSubcommand, data, CurrentRumble()), ct);
            if (await Task.WhenAny(waiter.Task, Task.Delay(250, ct)) == waiter.Task)
                return await waiter.Task;
        }
        return null;
    }

    /// <summary>Solange MCU-Anfragen senden, bis die Antwort passt (oder die Versuche aufgebraucht sind).</summary>
    private async Task<(byte Kind, byte[] Data)?> McuUntilAsync(byte sub, Func<byte[]> request, Func<byte, byte[], bool> done,
        int tries, CancellationToken ct)
    {
        for (int i = 0; i < tries; i++)
        {
            if (await McuAsync(sub, request(), ct) is { } r && done(r.Kind, r.Data))
                return r;
        }
        return null;
    }

    /// <summary>
    /// Wartet bis zu <paramref name="timeout"/> auf ein amiibo am NFC-Leser und liest es (540 Byte). null bei
    /// Zeitablauf oder Fehler. Währenddessen sendet der Controller größere Berichte (0x31), die Eingaben laufen weiter.
    /// </summary>
    public async Task<(byte[] Uid, byte[] Data)?> ReadAmiiboAsync(TimeSpan timeout, Action<string> progress, CancellationToken ct)
    {
        if (_ir is not null || _ringActive)
            throw new IOException("Erst IR-Kamera bzw. Ring-Con ausschalten (sie nutzen denselben Zusatzprozessor wie der NFC-Leser).");
        if (!HasNfc)
            return null;
        await _nfcLock.WaitAsync(ct);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var token = linked.Token;
        try
        {
            progress("NFC-Leser wird eingeschaltet …");
            await SubcommandAsync(Switch1.SubSetInputMode, [Nfc.InputMcu], token);
            await SubcommandAsync(Nfc.SubMcuState, [0x01], token);
            if (await McuUntilAsync(Nfc.McuSetDeviceMode, () => [], (k, d) => Nfc.IsMcuMode(k, d, Nfc.ModeStandby), 16, token) is null)
                throw new IOException("MCU startet nicht");
            await SubcommandAsync(Nfc.SubMcuConfig, Nfc.McuConfig(Nfc.ModeNfc), token);
            if (await McuUntilAsync(Nfc.McuSetDeviceMode, () => [], (k, d) => Nfc.IsMcuMode(k, d, Nfc.ModeNfc), 16, token) is null)
                throw new IOException("NFC-Modus nicht aktiv");
            bool Status(byte k, byte[] d, byte s) => Nfc.TryGetNfcStatus(k, d, out byte st) && st == s;
            await McuUntilAsync(Nfc.McuReadDeviceMode, () => Nfc.NextPacket(), (k, d) => Status(k, d, Nfc.StatusReady), 10, token);
            await McuAsync(Nfc.McuReadDeviceMode, Nfc.StopPolling(), token);
            await McuUntilAsync(Nfc.McuReadDeviceMode, () => Nfc.NextPacket(), (k, d) => Status(k, d, Nfc.StatusReady), 10, token);

            await McuAsync(Nfc.McuReadDeviceMode, Nfc.StartPolling(), token);
            progress("amiibo an den Leser halten (rechter Stick des Joy-Con bzw. NFC-Logo des Pro Controllers) …");
            byte[]? uid = null;
            var until = DateTime.UtcNow + timeout;
            while (uid is null && DateTime.UtcNow < until)
            {
                if (await McuAsync(Nfc.McuReadDeviceMode, Nfc.NextPacket(), token) is { } r && Nfc.TryGetTag(r.Kind, r.Data, out var found))
                    uid = found;
                else
                    await Task.Delay(40, token);
            }
            if (uid is null)
                return null;

            progress($"amiibo erkannt ({Convert.ToHexString(uid)}) – lese …");
            var assembler = new AmiiboAssembler();
            // Schon die Antwort auf die Leseanfrage kann das erste Datenpaket enthalten.
            if (await McuAsync(Nfc.McuReadDeviceMode, Nfc.ReadNtag215(), token) is { } first)
                assembler.Add(first.Kind, first.Data);
            byte packet = (byte)assembler.PacketCount;
            for (int i = 0; i < 60 && !assembler.Complete; i++)
            {
                if (await McuAsync(Nfc.McuReadDeviceMode, Nfc.NextPacket(packet), token) is not { } r)
                    continue;
                if (Nfc.TryGetNfcStatus(r.Kind, r.Data, out byte st) && st == Nfc.StatusTagLost)
                    throw new IOException("amiibo zu früh entfernt");
                if (assembler.Add(r.Kind, r.Data))
                    packet++;
                else if (st == Nfc.StatusLastPacket && r.Kind == Nfc.ReportNfcState && assembler.PacketCount > 0)
                    break;
            }
            if (!assembler.Complete)
                throw new IOException($"amiibo unvollständig gelesen ({assembler.ToArray().Length} von {Nfc.AmiiboSize} Byte)");
            Log.Info($"{Id}: amiibo {Convert.ToHexString(uid)} gelesen ({assembler.PacketCount} Pakete)");
            return (uid, assembler.ToArray());
        }
        finally
        {
            // Aufräumen: Abfrage stoppen, MCU schlafen legen, zurück zum normalen Vollbericht.
            try
            {
                await McuAsync(Nfc.McuReadDeviceMode, Nfc.StopPolling(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
                await SubcommandAsync(Nfc.SubMcuState, [0x00], CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
                await SubcommandAsync(Switch1.SubSetInputMode, [Switch1.InputFull], CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception)
            {
                // Controller getrennt o. Ä.
            }
            _mcuWaiter = null;
            _nfcLock.Release();
        }
    }

    // ---------- Ring-Con ----------

    private volatile bool _ringActive;
    private RingFlexCalibration _ring = new();

    /// <summary>Ring-Con am rechten Joy-Con eingeschaltet?</summary>
    public bool RingConActive => _ringActive;

    /// <summary>
    /// Ring-Con einschalten: MCU in Bereitschaft, externes Gerät erkennen (Kennung 0x2000), Format setzen und
    /// Abfrage starten. Den Ring dabei nicht berühren (Ruhelage wird gemessen). false, wenn kein Ring-Con steckt.
    /// </summary>
    public async Task<bool> EnableRingConAsync(CancellationToken ct)
    {
        if (_ir is not null)
            throw new IOException("Erst die IR-Kamera schließen (Ring-Con und IR-Kamera nutzen denselben Zusatzprozessor).");
        if (Kind != ControllerKind.JoyCon1Right)
            return false;
        await _nfcLock.WaitAsync(ct);
        try
        {
            await SubcommandAsync(Nfc.SubMcuState, [0x01], ct);
            var config = new byte[38];
            config[0] = 0x21;   // MCU konfigurieren
            config[1] = 0x01;   // Gerätemodus
            config[2] = Nfc.ModeStandby;
            config[37] = Nfc.Crc8(config.AsSpan(1, 36));
            await SubcommandAsync(Nfc.SubMcuConfig, config, ct);
            bool found = false;
            for (int i = 0; i < 42 && !found; i++)
            {
                if (await SubcommandAsync(Switch1.SubExternalDeviceInfo, [], ct) is { Length: >= 2 } info
                    && (info[0] | info[1] << 8) == Switch1.ExternalRingCon)
                    found = true;
                else
                    await Task.Delay(50, ct);
            }
            if (!found)
            {
                await SubcommandAsync(Nfc.SubMcuState, [0x00], ct);
                return false;
            }
            await SubcommandAsync(Switch1.SubExternalFormat, Switch1.RingConFormat.ToArray(), ct);
            await SubcommandAsync(Switch1.SubEnableExternalPolling, Switch1.RingConPolling.ToArray(), ct);
            _ring = new RingFlexCalibration();
            _ringActive = true;
            Log.Info($"{Id}: Ring-Con eingeschaltet");
            return true;
        }
        finally
        {
            _nfcLock.Release();
        }
    }

    public async Task DisableRingConAsync()
    {
        _ringActive = false;
        try
        {
            await SubcommandAsync(Nfc.SubMcuState, [0x00], _cts.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
        }
        Log.Info($"{Id}: Ring-Con ausgeschaltet");
    }

    // ---------- IR-Kamera ----------

    private IrFrameAssembler? _ir;
    private Action<byte[], int, int>? _irFrame;
    private int _irWriting;

    /// <summary>Hat dieser Controller eine IR-Kamera? (nur der rechte Joy-Con der Switch 1)</summary>
    public bool HasIrCamera => Kind == ControllerKind.JoyCon1Right;

    /// <summary>
    /// IR-Kamera einschalten und Bilder liefern (Graustufen, Breite × Höhe) bis <see cref="StopIrAsync"/>.
    /// false, wenn die Kamera nicht startet.
    /// </summary>
    public async Task<bool> StartIrAsync(IrResolution resolution, Action<byte[], int, int> onFrame, CancellationToken ct)
    {
        if (_ringActive)
            throw new IOException("Erst den Ring-Con ausschalten (Ring-Con und IR-Kamera nutzen denselben Zusatzprozessor).");
        if (!HasIrCamera)
            return false;
        await _nfcLock.WaitAsync(ct);
        try
        {
            await SubcommandAsync(Switch1.SubSetInputMode, [Nfc.InputMcu], ct);
            await SubcommandAsync(Nfc.SubMcuState, [0x01], ct);
            if (await McuUntilAsync(Nfc.McuSetDeviceMode, () => [], (k, d) => Nfc.IsMcuMode(k, d, Nfc.ModeStandby), 16, ct) is null)
                throw new IOException("MCU startet nicht");
            await SubcommandAsync(Nfc.SubMcuConfig, Nfc.McuConfig(IrCamera.ModeIr), ct);
            if (await McuUntilAsync(Nfc.McuSetDeviceMode, () => [], (k, d) => Nfc.IsMcuMode(k, d, IrCamera.ModeIr), 16, ct) is null)
                throw new IOException("IR-Modus nicht aktiv");

            bool configured = false;
            for (int i = 0; i < 28 && !configured; i++)
                configured = await SubcommandAsync(Nfc.SubMcuConfig, IrCamera.Configure(resolution), ct) is [0x0B, ..];
            if (!configured)
                throw new IOException("IR-Bildübertragung nicht einstellbar");

            bool step1 = false;
            for (int i = 0; i < 28 && !step1; i++)
            {
                var reply = await SubcommandAsync(Nfc.SubMcuConfig, IrCamera.RegistersStep1(resolution), ct);
                if (i == 0)
                    await _hid.WriteAsync(Nfc.McuReport(Interlocked.Increment(ref _counter), Switch1.SubSetInputMode,
                        IrCamera.Acknowledge(0, start: true), CurrentRumble()), ct);
                step1 = reply is [0x13, _, 0x07, ..] or [0x23, ..];
            }
            bool step2 = false;
            for (int i = 0; i < 28 && !step2; i++)
                step2 = await SubcommandAsync(Nfc.SubMcuConfig, IrCamera.RegistersStep2(), ct) is [0x13, ..] or [0x23, ..];
            if (!step1 || !step2)
                throw new IOException("IR-Kameraregister nicht gesetzt");

            _irFrame = onFrame;
            _ir = new IrFrameAssembler(resolution);
            Log.Info($"{Id}: IR-Kamera an ({resolution})");
            return true;
        }
        catch (IOException e)
        {
            Log.Warn($"{Id}: IR-Kamera: {e.Message}");
            await StopIrCoreAsync();
            return false;
        }
        finally
        {
            _nfcLock.Release();
        }
    }

    public async Task StopIrAsync()
    {
        await _nfcLock.WaitAsync();
        try
        {
            await StopIrCoreAsync();
        }
        finally
        {
            _nfcLock.Release();
        }
    }

    private async Task StopIrCoreAsync()
    {
        _ir = null;
        _irFrame = null;
        try
        {
            await SubcommandAsync(Nfc.SubMcuState, [0x00], CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
            await SubcommandAsync(Switch1.SubSetInputMode, [Switch1.InputFull], CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (Exception)
        {
            // Controller getrennt.
        }
        Log.Info($"{Id}: IR-Kamera aus");
    }

    /// <summary>Bericht 0x31 während der IR-Übertragung: Stück übernehmen, Quittung senden (nicht blockierend).</summary>
    private void HandleIr(ReadOnlySpan<byte> report)
    {
        if (_ir is not { } assembler)
            return;
        var (request, image) = assembler.Handle(report);
        if (image is not null)
        {
            try { _irFrame?.Invoke(image, assembler.Width, assembler.Height); }
            catch (Exception e) { Log.Error($"{Id}: IR-Bild anzeigen", e); }
        }
        // Höchstens eine Quittung gleichzeitig unterwegs (Schreiben ist asynchron).
        if (Interlocked.Exchange(ref _irWriting, 1) == 1)
            return;
        _hid.WriteAsync(Nfc.McuReport(Interlocked.Increment(ref _counter), Switch1.SubSetInputMode, request, CurrentRumble()), _cts.Token)
            .ContinueWith(t =>
            {
                Volatile.Write(ref _irWriting, 0);
                _ = t.Exception; // beobachtet
            }, TaskScheduler.Default);
    }

    // ---------- Eingaben ----------

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[Math.Max(64, _hid.InputLength)];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await _hid.ReadAsync(buffer, ct);
                if (n == 0)
                {
                    RaiseLost();
                    return;
                }
                HandleReport(buffer.AsSpan(0, n));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (Volatile.Read(ref _closed) == 0)
                Log.Warn($"{Id}: Lesen beendet: {e.Message}");
            RaiseLost();
        }
    }

    private void HandleReport(ReadOnlySpan<byte> report)
    {
        if (report[0] == Switch1.InputSubcommandReply)
        {
            lock (_replyGate)
            {
                if (_pending is { } p && Switch1.TryParseReply(report, p.Subcommand, out var data, p.Address))
                    p.Reply.TrySetResult(data);
            }
        }
        if (report[0] == Nfc.InputMcu && _mcuWaiter is { } waiter && Nfc.TryGetMcu(report, out byte mcuKind, out var mcuData))
            waiter.TrySetResult((mcuKind, mcuData.ToArray()));
        if (report[0] == Nfc.InputMcu && _ir is not null)
            HandleIr(report);
        bool ring = _ringActive && report[0] == Switch1.InputFull;
        if (report[0] is Switch1.InputFull or Nfc.InputMcu && Switch1.TryParseFull(report, Kind, _imu, out var state, ring))
        {
            if (ring)
                state = state with { RingFlex = _ring.Update(Switch1.RingRaw(report)) };
            _lastInputTicks = Environment.TickCount64;
            _rate.Tick();
            LastState = state;
            try
            {
                StateReceived?.Invoke(this, state);
            }
            catch (Exception e)
            {
                Log.Error($"{Id}: Eingabe verarbeiten", e);
            }
        }
    }

    private async Task WatchdogAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);
                if (Environment.TickCount64 - _lastInputTicks > InputTimeoutMs)
                {
                    Log.Warn($"{Id}: keine Eingaben mehr – getrennt");
                    RaiseLost();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ---------- Vibration ----------

    public void SetRumble(byte large, byte small, float strength)
    {
        _rumbleLarge = large;
        _rumbleSmall = small;
        _rumbleStrength = strength;
    }

    private byte[] CurrentRumble()
    {
        var frame = Switch1Rumble.Frame((byte)_rumbleLarge, (byte)_rumbleSmall, _rumbleStrength);
        return [.. frame, .. frame];
    }

    private async Task RumbleLoopAsync(CancellationToken ct)
    {
        bool wasActive = false;
        long lastSent = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                bool active = (_rumbleLarge | _rumbleSmall) != 0;
                long now = Environment.TickCount64;
                if (active || wasActive)
                {
                    if (active != wasActive || now - lastSent >= RumbleRefreshMs || active)
                    {
                        await _hid.WriteAsync(Switch1.RumbleReport(Interlocked.Increment(ref _counter), CurrentRumble()), ct);
                        lastSent = now;
                    }
                }
                wasActive = active;
                await Task.Delay(RumbleIntervalMs, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.Warn($"{Id}: Vibration abgeschaltet: {e.Message}");
        }
    }

    private void RaiseLost()
    {
        if (Interlocked.Exchange(ref _lostRaised, 1) == 0)
            Lost?.Invoke(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return;
        try
        {
            _rumbleLarge = _rumbleSmall = 0;
            await _hid.WriteAsync(Switch1.RumbleReport(Interlocked.Increment(ref _counter), CurrentRumble()), CancellationToken.None)
                .WaitAsync(TimeSpan.FromMilliseconds(200));
        }
        catch (Exception)
        {
            // Controller schon weg.
        }
        _cts.Cancel();
        _hid.Dispose();
        if (_reader is not null)
        {
            try { await _reader.WaitAsync(TimeSpan.FromSeconds(1)); } catch (Exception) { /* beendet */ }
        }
    }
}
