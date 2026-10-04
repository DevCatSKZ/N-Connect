using Switch2Pro.Bridge.Links;
using Switch2Pro.Protocol;

namespace Switch2Pro.Bridge;

/// <summary>
/// Ein Spieler = ein virtueller Controller in Windows. Gespeist von einem Controller oder von zwei
/// Joy-Con (links + rechts). Kümmert sich außerdem um den Mausmodus der Joy-Con 2, um Tastatur-Hotkeys,
/// den DSU-Server und um die Gesten zum Trennen/Zusammenfügen von Joy-Con (wie an der Switch).
/// Zwei Sperren: <see cref="_gate"/> schützt die Zuordnung (Links, Zustände), <see cref="_output"/> die
/// Ausgabe (virtueller Controller, Tasten, DSU) – so kann nach dem Freigeben nichts mehr gedrückt werden.
/// </summary>
internal sealed class Player : IDisposable
{
    /// <summary>So lange SL + SR halten, um einen Joy-Con aus dem Paar zu lösen.</summary>
    private const int SplitHoldMs = 1000;

    private readonly Func<Settings> _settings;
    private readonly PadFactory _factory;
    private readonly object _gate = new();
    private readonly object _output = new();
    private readonly List<IControllerLink> _links = [];
    private readonly Dictionary<IControllerLink, JoyConMouse> _mice = [];
    /// <summary>Letzter Zustand je Joy-Con nach dem Mausmodus (Maustasten entfernt).</summary>
    private readonly Dictionary<IControllerLink, ControllerState> _effective = [];
    /// <summary>Seit wann SL + SR an einem Joy-Con gehalten werden (je Joy-Con getrennt).</summary>
    private readonly Dictionary<IControllerLink, long> _splitSince = [];
    /// <summary>Gerade gedrückte Tastatur-Tasten (Vereinigung aller aktiven Hotkeys).</summary>
    private readonly List<ushort> _heldKeys = [];
    private IVirtualPad? _pad;
    private bool _disposed;
    private bool _rumbling;
    private long _updates;
    private ulong? _mac;

    /// <summary>Spielernummer (0–7): bestimmt LEDs und DSU-Slot. Wird nur vom Manager vergeben.</summary>
    public int Index { get; }

    /// <summary>Wird ausgelöst, wenn sich Akku, Ladezustand o. Ä. sichtbar ändert.</summary>
    public event Action? Changed;
    /// <summary>Ein Joy-Con soll aus dem Paar gelöst werden (SL + SR gehalten).</summary>
    public event Action<Player, IControllerLink>? SplitRequested;
    /// <summary>Ein einzelner Joy-Con möchte sich mit einem anderen verbinden (Schultertaste gedrückt).</summary>
    public event Action<Player>? PairRequested;

    public Player(int index, IControllerLink first, Func<Settings> settings, PadFactory factory)
    {
        Index = index;
        _settings = settings;
        _factory = factory;
        CreatePad(settings().OutputMode); // wirft bei ViGEm-Fehlern – der Manager fängt das ab
        Add(first);
    }

    public IReadOnlyList<IControllerLink> Links
    {
        get { lock (_gate) return _links.ToList(); }
    }

    public ControllerKind Kind
    {
        get
        {
            lock (_gate)
                return _links.Count == 2 ? ControllerKind.JoyConPair : _links.FirstOrDefault()?.Kind ?? ControllerKind.Unknown;
        }
    }

    public bool IsEmpty
    {
        get { lock (_gate) return _links.Count == 0; }
    }

    public bool IsPair
    {
        get { lock (_gate) return _links.Count == 2; }
    }

    /// <summary>Ist ein Joy-Con dieses Spielers gerade im Mausmodus?</summary>
    public bool MouseActive(IControllerLink link)
    {
        lock (_gate)
            return _mice.TryGetValue(link, out var m) && m.Active;
    }

    /// <summary>Zustand eines Joy-Con, wie er ans Spiel geht (für die Anzeige).</summary>
    public ControllerState? EffectiveState(IControllerLink link)
    {
        lock (_gate)
            return _effective.TryGetValue(link, out var s) ? s : link.LastState;
    }

    /// <summary>Seit wann ein einzelner Joy-Con auf einen Partner wartet (Schultertaste gedrückt), sonst −1.</summary>
    public long PairRequestTicks { get; private set; } = -1;

    /// <summary>Ein einzelner Joy-Con, der noch einen Partner der anderen Seite aufnehmen kann.</summary>
    public bool CanPairWith(IControllerLink link)
    {
        lock (_gate)
        {
            return _links.Count == 1 && _links[0].Kind.IsJoyCon() && link.Kind.IsJoyCon()
                   && _links[0].Kind.IsLeftJoyCon() != link.Kind.IsLeftJoyCon();
        }
    }

    public void Add(IControllerLink link)
    {
        lock (_gate)
        {
            _links.Add(link);
            if (link.Kind is ControllerKind.JoyCon2Left or ControllerKind.JoyCon2Right)
                _mice[link] = new JoyConMouse(link.Kind.IsLeftJoyCon());
            _mac = null; // DSU-Adresse neu bestimmen
        }
        link.StateReceived += OnState;
        link.SetPlayerAsync(Index).Forget($"{link.Id}: Spieler-LED");
        Changed?.Invoke();
    }

    public void Remove(IControllerLink link)
    {
        link.StateReceived -= OnState;
        link.SetRumble(0, 0, 0); // Vibration nicht an einen anderen Spieler „vererben“
        lock (_gate)
        {
            _links.Remove(link);
            _effective.Remove(link);
            _splitSince.Remove(link);
            if (_mice.Remove(link, out var mouse))
                mouse.Stop();
            _mac = null;
        }
        Changed?.Invoke();
    }

    // ---------- Eingaben → virtueller Controller ----------

    private int _lastBatteryBucket = -1;
    private bool _lastCharging;

    /// <summary>Kalibrierung eines Controllers mit dem vom Benutzer gemessenen Gyro-Nullpunkt (falls vorhanden).</summary>
    public static DeviceCalibration CalibrationFor(IControllerLink link, Settings settings) =>
        settings.GyroCalibration.Count == 0 ? link.Calibration
            : link.Calibration with { Gyro = settings.GyroBiasFor(link.Address, link.Calibration.Gyro) };

    /// <summary>
    /// Einheitliche Eingabe aus den letzten Zuständen (auch für die Anzeige): Paar zusammengefügt,
    /// einzelner Joy-Con quer oder – wenn so gewählt – hochkant.
    /// </summary>
    public static PadInput Combine(IReadOnlyList<IControllerLink> links, Func<IControllerLink, ControllerState?> stateOf, Settings settings)
    {
        if (links.Count == 2)
        {
            var left = links.First(l => l.Kind.IsLeftJoyCon());
            var right = links.First(l => !l.Kind.IsLeftJoyCon());
            return Mapping.Merge(stateOf(left)!, CalibrationFor(left, settings), stateOf(right)!, CalibrationFor(right, settings),
                settings.PairGyroSource);
        }
        var link = links[0];
        if (link.Kind.IsJoyCon() && settings.IsUprightJoyCon(link.Address))
        {
            // Hochkant: Tasten unter ihren echten Namen (wie eine Hälfte des Paars) – deshalb gilt die Belegung
            // „Joy-Con-Paar“; die Belegung des quer gehaltenen Joy-Con nutzt gedrehte Namen und passt hier nicht.
            return Mapping.Normalize(stateOf(link)!, CalibrationFor(link, settings), sideways: false) with { Kind = ControllerKind.JoyConPair };
        }
        return Mapping.Normalize(stateOf(link)!, CalibrationFor(link, settings));
    }

    /// <summary>Zeitpunkt der letzten echten Eingabe (Taste, Stick, Trigger, Bewegung, Maus) – für das Trennen bei Inaktivität.</summary>
    public long LastActivity { get; private set; } = Environment.TickCount64;

    /// <summary>Benutzt gerade jemand den Controller? Ruhig liegend (nur Sensorrauschen) zählt nicht.</summary>
    private static bool IsActive(PadInput p)
    {
        const short GyroActive = 330; // ≈ 20 °/s (2000 °/s ≙ 32767)
        return p.Buttons != ProButtons.None
               || MathF.Abs(p.LeftX) > 0.3f || MathF.Abs(p.LeftY) > 0.3f || MathF.Abs(p.RightX) > 0.3f || MathF.Abs(p.RightY) > 0.3f
               || p.LeftTrigger > 0.3f || p.RightTrigger > 0.3f
               || p.Motion is { } m && (Math.Abs((int)m.GyroX) > GyroActive || Math.Abs((int)m.GyroY) > GyroActive || Math.Abs((int)m.GyroZ) > GyroActive);
    }

    private void OnState(IControllerLink source, ControllerState state)
    {
        var settings = _settings();
        PadInput input;
        ulong mac;
        bool motionFresh = true; // beim Paar nur bei Berichten des Joy-Con, der die Bewegungsdaten liefert
        lock (_gate)
        {
            // Ein Bericht, der beim Trennen/Zusammenfügen schon unterwegs war, gehört nicht mehr zu diesem Spieler.
            if (_disposed || !_links.Contains(source))
                return;
            if (_gyroSamples is { } samples && state.Motion is { } raw && samples.TryGetValue(source, out var list))
                list.Add(raw);
            // Mausmodus zuerst: die Maustasten gehen dann nicht ans Spiel.
            bool mouseMoved = false;
            if (_mice.TryGetValue(source, out var mouse))
            {
                var stick = source.Kind.IsLeftJoyCon() ? source.Calibration.Left : source.Calibration.Right;
                state = mouse.Process(state, settings, stick);
                mouseMoved = mouse.Moved;
            }
            _effective[source] = state;

            if (_links.Count == 2)
            {
                if (_links.Any(l => !_effective.ContainsKey(l)))
                    return;
                input = Combine(_links, l => _effective[l], settings);
                // Gyro-Maus nur im Takt des Joy-Con rechnen, von dem die Bewegungsdaten wirklich stammen
                // (der gewählte; fehlen dessen Daten, der andere – wie in Mapping.Merge).
                var chosen = _links.First(l => l.Kind.IsLeftJoyCon() == (settings.PairGyroSource == GyroSource.Left));
                var motionLink = _effective[chosen].Motion is not null ? chosen : _links.First(l => l != chosen);
                motionFresh = source == motionLink;
                CheckSplitGesture(source, state);
            }
            else
            {
                input = Combine(_links, _ => state, settings);
                CheckPairGesture(source, state);
            }
            mac = _mac ??= MacOf(_links);
            if (IsActive(input) || mouseMoved)
                LastActivity = Environment.TickCount64;
        }

        var output = Mapping.Evaluate(input, settings);
        var gamepad = output.Gamepad;
        lock (_output)
        {
            if (_disposed)
                return;
            if (++_updates == 1)
                Log.Info($"Spieler {Index + 1}: erste Eingabe am virtuellen Controller ({Kind.DisplayName()})");
            var keys = output.Keys;
            gamepad = ApplyMacros(gamepad, output.Macros, keys);
            UpdateSpecials(output.Specials, motionFresh ? input.Motion : null, settings);
            if (settings.WiiPointerMouse && state.Pointer is { } pointer)
                MovePointer(pointer);
            // Gyro als rechter Stick: immer, beim Zielen (linker Trigger) oder per Taste (halten/ein-aus).
            bool gyroStick = settings.GyroStick == GyroStickMode.Always
                             || settings.GyroStick == GyroStickMode.WhileAiming && gamepad.LeftTrigger > 40
                             || output.Specials.HasFlag(SpecialAction.GyroStick) || _gyroStickToggled;
            if (gyroStick != GyroStickActive)
            {
                GyroStickActive = gyroStick;
                ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke());
            }
            if (gyroStick && input.Motion is { } motion)
            {
                var (gx, gy) = Mapping.GyroToStick(motion, settings, gamepad.RightX, gamepad.RightY);
                gamepad = gamepad with { RightX = gx, RightY = gy };
            }
            try
            {
                _pad?.Update(gamepad, input);
            }
            catch (Exception e)
            {
                Log.Error($"Spieler {Index + 1}: virtueller Controller", e);
            }
            UpdateHotkeys(keys);
            DsuServer.Instance?.Publish(Index, mac, gamepad, input);
            // Vibration abgeschaltet, während ein Spiel vibrieren lässt: sofort stoppen.
            if (_rumbling && !settings.RumbleEnabled)
                StopRumble();
        }

        CheckBattery(source, state);

        int bucket = input.BatteryPercent / 5;
        if (bucket != _lastBatteryBucket || input.Charging != _lastCharging)
        {
            _lastBatteryBucket = bucket;
            _lastCharging = input.Charging;
            Changed?.Invoke();
        }
    }

    // ---------- Akku-Warnung ----------

    /// <summary>Warnstufen in Prozent: einmal bei 15 %, noch einmal bei 5 %.</summary>
    private static readonly int[] BatteryWarnLevels = [15, 5];
    /// <summary>Je Controller die zuletzt gemeldete Warnstufe (100 = noch keine).</summary>
    private readonly Dictionary<IControllerLink, int> _batteryWarned = [];

    /// <summary>Hinweis, z. B. „Akku fast leer“ (zum Anzeigen als Einblendung).</summary>
    public event Action<string>? Warning;

    private void CheckBattery(IControllerLink source, ControllerState state)
    {
        int percent = state.BatteryPercent;
        if (percent < 0)
            return;
        string? message = null;
        lock (_gate)
        {
            int warned = _batteryWarned.GetValueOrDefault(source, 100);
            // Beim Laden oder deutlich höherem Stand wieder scharf schalten (Hysterese gegen Schwanken der Messung).
            if (state.Charging || percent >= 25)
            {
                _batteryWarned[source] = 100;
                return;
            }
            foreach (int level in BatteryWarnLevels)
            {
                if (percent <= level && warned > level)
                {
                    _batteryWarned[source] = level;
                    message = $"Spieler {Index + 1}: Akku {source.Kind.DisplayName()} fast leer ({percent} %) – bitte aufladen";
                }
            }
        }
        if (message is not null)
        {
            Log.Info(message);
            Warning?.Invoke(message);
        }
    }

    /// <summary>Bluetooth-Adresse des (ersten) Controllers als Zahl, für den DSU-Server.</summary>
    private static ulong MacOf(IReadOnlyList<IControllerLink> links)
    {
        var text = links.FirstOrDefault()?.Address?.Replace(":", "");
        return text is { Length: 12 } && ulong.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out ulong mac) ? mac : 0;
    }

    /// <summary>Wie an der Switch: SL + SR an einem Joy-Con halten löst ihn aus dem Paar (Zeit je Joy-Con).</summary>
    private void CheckSplitGesture(IControllerLink source, ControllerState state)
    {
        var rail = source.Kind.IsLeftJoyCon() ? ProButtons.SLLeft | ProButtons.SRLeft : ProButtons.SLRight | ProButtons.SRRight;
        if ((state.Buttons & rail) != rail)
        {
            _splitSince.Remove(source);
            return;
        }
        long now = Environment.TickCount64;
        if (!_splitSince.TryGetValue(source, out long since))
        {
            _splitSince[source] = now;
        }
        else if (now - since >= SplitHoldMs)
        {
            _splitSince.Remove(source);
            ThreadPool.QueueUserWorkItem(_ => SplitRequested?.Invoke(this, source));
        }
    }

    /// <summary>Wie an der Switch: L am linken und R am rechten einzelnen Joy-Con gleichzeitig = Paar.</summary>
    private void CheckPairGesture(IControllerLink source, ControllerState state)
    {
        if (!source.Kind.IsJoyCon())
            return;
        var shoulder = source.Kind.IsLeftJoyCon() ? ProButtons.L : ProButtons.R;
        if (state.Has(shoulder))
        {
            if (PairRequestTicks < 0)
            {
                PairRequestTicks = Environment.TickCount64;
                ThreadPool.QueueUserWorkItem(_ => PairRequested?.Invoke(this));
            }
        }
        else
        {
            PairRequestTicks = -1;
        }
    }

    // ---------- Sonderaktionen: Maustasten, Gyro-Maus ----------

    private SpecialAction _heldSpecials;
    private bool _gyroMouseToggled;
    private long _lastGyroTicks;
    private readonly SmoothMouse _gyroMouse = new();

    /// <summary>Ist die Gyro-Maus gerade aktiv (gehalten oder eingeschaltet)?</summary>
    public bool GyroMouseActive { get; private set; }

    private bool _gyroStickToggled;

    /// <summary>Steuert der Gyro gerade den rechten Stick?</summary>
    public bool GyroStickActive { get; private set; }

    // ---------- Makros ----------

    /// <summary>Laufende Makros (Skript, Startzeit) und die Makros, deren Taste gerade gehalten wird.</summary>
    private readonly List<(string Text, MacroScript Script, long Start)> _macros = [];
    private HashSet<string> _macroButtons = [];

    /// <summary>
    /// Startet Makros beim Drücken ihrer Taste (nicht erneut, solange sie läuft) und fügt den aktuellen Schritt
    /// aller laufenden Makros zum Gamepad bzw. zu den Tastaturtasten hinzu. Aufruf unter <see cref="_output"/>.
    /// </summary>
    private GamepadState ApplyMacros(GamepadState g, IReadOnlyList<string> pressed, HashSet<string> keys)
    {
        long now = Environment.TickCount64;
        foreach (var text in pressed)
        {
            if (_macroButtons.Contains(text) || _macros.Any(m => m.Text == text) || !MacroScript.TryParse(text, out var script))
                continue;
            _macros.Add((text, script, now));
        }
        _macroButtons = [.. pressed];
        for (int i = _macros.Count - 1; i >= 0; i--)
        {
            var (_, script, start) = _macros[i];
            if (script.StepAt(now - start) is not { } step)
            {
                _macros.RemoveAt(i);
                continue;
            }
            if (step.Keys is { } combo)
                keys.Add(combo);
            foreach (var target in step.Buttons)
                g = Mapping.Press(g, target);
        }
        return g;
    }

    /// <summary>Maustasten nach Flanken drücken/lösen, Gyro-Maus bewegen. Aufruf unter <see cref="_output"/>.</summary>
    private void UpdateSpecials(SpecialAction wanted, Motion? motion, Settings settings)
    {
        foreach (var (flag, button) in new[]
                 {
                     (SpecialAction.MouseLeft, WindowsInput.MouseButton.Left),
                     (SpecialAction.MouseRight, WindowsInput.MouseButton.Right),
                     (SpecialAction.MouseMiddle, WindowsInput.MouseButton.Middle),
                 })
        {
            bool down = wanted.HasFlag(flag), before = _heldSpecials.HasFlag(flag);
            if (down != before)
                WindowsInput.MouseButtonState(button, down);
        }
        if (wanted.HasFlag(SpecialAction.GyroMouseToggle) && !_heldSpecials.HasFlag(SpecialAction.GyroMouseToggle))
            _gyroMouseToggled = !_gyroMouseToggled;
        if (wanted.HasFlag(SpecialAction.GyroStickToggle) && !_heldSpecials.HasFlag(SpecialAction.GyroStickToggle))
            _gyroStickToggled = !_gyroStickToggled;
        _heldSpecials = wanted;

        bool active = wanted.HasFlag(SpecialAction.GyroMouse) || _gyroMouseToggled;
        if (active != GyroMouseActive)
        {
            GyroMouseActive = active;
            _lastGyroTicks = 0;
            if (!active)
                _gyroMouse.Reset();
            ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke());
        }
        if (!active || motion is not { } m)
            return;

        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lastGyroTicks != 0)
        {
            double dt = Math.Min(0.05, System.Diagnostics.Stopwatch.GetElapsedTime(_lastGyroTicks, now).TotalSeconds);
            // Gierachse (Drehen nach links/rechts) = GyroZ, Nickachse (kippen) = GyroX; 2000 °/s ≙ 32767.
            const float DegPerRaw = 2000f / 32767f;
            float yaw = m.GyroZ * DegPerRaw, pitch = m.GyroX * DegPerRaw;
            // Kleines Rauschen unterdrücken (weicher Übergang statt harter Schwelle).
            static float Soft(float v) => MathF.Abs(v) < 1.5f ? v * MathF.Abs(v) / 1.5f : v;
            float dx = -Soft(yaw) * (float)dt * settings.GyroMouseSpeed;
            float dy = -Soft(pitch) * (float)dt * settings.GyroMouseSpeed;
            _gyroMouse.Add(settings.MouseInvertX ? -dx : dx, settings.MouseInvertY ? -dy : dy);
        }
        _lastGyroTicks = now;
    }

    private (float X, float Y)? _pointer;

    /// <summary>
    /// Wii-Zeiger → Mauszeiger (absolut auf dem Hauptbildschirm). Geglättet gegen das Zittern der IR-Punkte; der
    /// mittlere Bereich des Kamerabilds wird auf den ganzen Bildschirm gestreckt, damit man nicht weit zielen muss.
    /// Aufruf unter <see cref="_output"/>.
    /// </summary>
    private void MovePointer((float X, float Y) target)
    {
        const float Smoothing = 0.35f, Stretch = 1.5f;
        var (x, y) = _pointer is { } p ? (p.X + (target.X - p.X) * Smoothing, p.Y + (target.Y - p.Y) * Smoothing) : target;
        _pointer = (x, y);
        WindowsInput.MoveMouseAbsolute(0.5f + (x - 0.5f) * Stretch, 0.5f + (y - 0.5f) * Stretch);
    }

    /// <summary>Gehaltene Maustasten lösen (beim Freigeben). Aufruf unter <see cref="_output"/>.</summary>
    private void ReleaseSpecials()
    {
        UpdateSpecials(SpecialAction.None, null, _settings());
        _gyroMouseToggled = false;
        _gyroStickToggled = false;
        _macros.Clear();
        GyroMouseActive = false;
        _gyroMouse.Reset();
    }

    // ---------- Gyro kalibrieren ----------

    private Dictionary<IControllerLink, List<Motion>>? _gyroSamples;

    /// <summary>
    /// Misst den Gyro-Nullpunkt jedes Controllers dieses Spielers (Controller muss ruhig liegen).
    /// Ergebnis: Adresse → Nullpunkt; null, wenn der Controller bewegt wurde oder keine Daten lieferte.
    /// </summary>
    public async Task<Dictionary<string, GyroBias>?> CalibrateGyroAsync(TimeSpan duration)
    {
        lock (_gate)
        {
            if (_gyroSamples is not null)
                return null; // läuft schon (z. B. aus einem zweiten Fenster gestartet)
            _gyroSamples = _links.ToDictionary(l => l, _ => new List<Motion>());
        }
        await Task.Delay(duration);
        Dictionary<IControllerLink, List<Motion>> samples;
        lock (_gate)
        {
            samples = _gyroSamples!;
            _gyroSamples = null;
        }

        var result = new Dictionary<string, GyroBias>(StringComparer.OrdinalIgnoreCase);
        foreach (var (link, list) in samples)
        {
            if (link.Address is null || list.Count < 20)
                return null;
            // Bewegt? Abweichung vom Mittelwert darf ~3 °/s (≈ 50 Rohwerte) nicht übersteigen.
            float mx = (float)list.Average(m => m.GyroX), my = (float)list.Average(m => m.GyroY), mz = (float)list.Average(m => m.GyroZ);
            float spread = list.Max(m => Math.Max(Math.Abs(m.GyroX - mx), Math.Max(Math.Abs(m.GyroY - my), Math.Abs(m.GyroZ - mz))));
            if (spread > 50)
            {
                Log.Info($"{link.Id}: Gyro-Kalibrierung verworfen – Controller bewegt (Abweichung {spread:F0})");
                return null;
            }
            result[link.Address] = new GyroBias(mx, my, mz);
            Log.Info($"{link.Id}: Gyro-Nullpunkt gemessen ({mx:F1}, {my:F1}, {mz:F1}) aus {list.Count} Werten, " +
                     $"Werk ({link.Calibration.Gyro.X:F1}, {link.Calibration.Gyro.Y:F1}, {link.Calibration.Gyro.Z:F1})");
        }
        return result.Count > 0 ? result : null;
    }

    // ---------- Tastatur-Hotkeys ----------

    private static bool IsModifier(ushort vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;

    /// <summary>
    /// Gewünschte Kombinationen umsetzen. Gedrückt wird die Vereinigung aller Tasten: eine Zusatztaste (z. B. Strg),
    /// die zwei Kombinationen gemeinsam haben, bleibt gedrückt, bis keine sie mehr braucht. Aufruf unter <see cref="_output"/>.
    /// </summary>
    private void UpdateHotkeys(HashSet<string> combos)
    {
        var wanted = new List<ushort>();
        foreach (var combo in combos)
        {
            if (WindowsInput.ParseCombo(combo) is not { } keys)
                continue;
            foreach (var k in keys)
                if (!wanted.Contains(k))
                    wanted.Add(k);
        }
        // Erst lösen (normale Tasten vor Zusatztasten), dann drücken (Zusatztasten zuerst).
        var release = _heldKeys.Where(k => !wanted.Contains(k)).OrderBy(IsModifier).ToList();
        var press = wanted.Where(k => !_heldKeys.Contains(k)).OrderByDescending(IsModifier).ToList();
        if (release.Count > 0)
        {
            WindowsInput.Combo(release, down: false);
            _heldKeys.RemoveAll(release.Contains);
        }
        if (press.Count > 0)
        {
            WindowsInput.Combo(press, down: true);
            _heldKeys.AddRange(press);
        }
    }

    // ---------- virtueller Controller ----------

    private void CreatePad(OutputMode mode)
    {
        var pad = _factory.Create(mode);
        pad.Rumble += OnGameRumble;
        IVirtualPad? old;
        lock (_output)
        {
            if (_disposed)
            {
                old = pad; // inzwischen freigegeben: neuen Controller gleich wieder entfernen
            }
            else
            {
                old = _pad;
                _pad = pad;
            }
            if (old is not null)
                StopRumble(); // altes Pad weg – dessen Vibration darf nicht weiterlaufen
        }
        if (old is not null)
            DisposePad(old);
    }

    private void DisposePad(IVirtualPad pad)
    {
        pad.Rumble -= OnGameRumble;
        pad.Dispose();
    }

    /// <summary>Ausgabeart wechseln (Xbox 360 ↔ DualShock 4) ohne neu zu verbinden.</summary>
    public void SwitchOutput(OutputMode mode) => CreatePad(mode);

    private void OnGameRumble(byte large, byte small)
    {
        var settings = _settings();
        float strength = settings.RumbleEnabled ? settings.RumbleStrength : 0f;
        if (strength <= 0f)
            large = small = 0;
        _rumbling = (large | small) != 0;
        foreach (var link in Links)
            link.SetRumble(large, small, strength);
    }

    private void StopRumble()
    {
        _rumbling = false;
        foreach (var link in Links)
            link.SetRumble(0, 0, 0);
    }

    /// <summary>Controller zur Erkennung kurz vibrieren lassen („Welcher ist Spieler 2?“).</summary>
    public async Task IdentifyAsync()
    {
        foreach (var link in Links)
            link.SetRumble(180, 180, 1f);
        await Task.Delay(400);
        if (!_rumbling)
            foreach (var link in Links)
                link.SetRumble(0, 0, 0);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var mouse in _mice.Values)
                mouse.Stop();
        }
        foreach (var link in Links)
            link.StateReceived -= OnState;
        IVirtualPad? pad;
        lock (_output)
        {
            UpdateHotkeys([]);          // keine Taste darf gedrückt bleiben
            ReleaseSpecials();          // auch keine Maustaste
            StopRumble();
            DsuServer.Instance?.Clear(Index);
            pad = _pad;
            _pad = null;
        }
        if (pad is not null)
            DisposePad(pad);
    }
}
