namespace Switch2Pro.Protocol;

/// <summary>Ein Schritt eines Makros: diese Gamepad-Tasten bzw. diese Tastenkombination so lange halten.</summary>
public sealed record MacroStep(IReadOnlyList<ExtraButtonTarget> Buttons, string? Keys, int DurationMs);

/// <summary>
/// Makro = Folge von Schritten, durch Komma getrennt. Jeder Schritt: was gehalten wird, dann die Dauer in ms.
/// Beispiele: „A 80, Pause 40, A 80“ (zweimal A), „Down+B 120“ (gleichzeitig), „Key:Ctrl+C 50, Key:Ctrl+V 50“.
/// Ohne Dauer gelten 60 ms. Tastennamen wie bei der Belegung (A, B, X, Y, LB, RB, LT, RT, Back, Start, Guide,
/// LS, RS, Up, Down, Left, Right); „Pause“ bzw. „Warten“ hält nichts.
/// </summary>
public sealed class MacroScript
{
    public const int DefaultStepMs = 60;
    public const int MaxSteps = 64;
    public const int MaxStepMs = 5000;

    public IReadOnlyList<MacroStep> Steps { get; }
    public int TotalMs { get; }

    private MacroScript(List<MacroStep> steps)
    {
        Steps = steps;
        TotalMs = steps.Sum(s => s.DurationMs);
    }

    public static bool TryParse(string? text, out MacroScript script)
    {
        script = new MacroScript([]);
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var steps = new List<MacroStep>();
        foreach (var raw in text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (steps.Count >= MaxSteps)
                return false;
            // Dauer = letztes Wort, wenn es eine Zahl ist (optional mit „ms“).
            string body = raw;
            int duration = DefaultStepMs;
            int space = raw.LastIndexOf(' ');
            if (space > 0)
            {
                var last = raw[(space + 1)..].Trim();
                if (last.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
                    last = last[..^2];
                if (int.TryParse(last, out int ms))
                {
                    duration = ms;
                    body = raw[..space].Trim();
                }
            }
            if (duration is <= 0 or > MaxStepMs)
                return false;

            if (body.StartsWith(ButtonAction.KeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var keys = body[ButtonAction.KeyPrefix.Length..].Trim();
                if (keys.Length == 0)
                    return false;
                steps.Add(new MacroStep([], keys, duration));
                continue;
            }
            if (body.Equals("Pause", StringComparison.OrdinalIgnoreCase) || body.Equals("Warten", StringComparison.OrdinalIgnoreCase)
                || body.Equals("Wait", StringComparison.OrdinalIgnoreCase))
            {
                steps.Add(new MacroStep([], null, duration));
                continue;
            }
            var buttons = new List<ExtraButtonTarget>();
            foreach (var name in body.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Enum.TryParse<ExtraButtonTarget>(name, ignoreCase: true, out var target) || !Enum.IsDefined(target)
                    || target == ExtraButtonTarget.None || int.TryParse(name, out _))
                    return false;
                buttons.Add(target);
            }
            if (buttons.Count == 0)
                return false;
            steps.Add(new MacroStep(buttons, null, duration));
        }
        if (steps.Count == 0)
            return false;
        script = new MacroScript(steps);
        return true;
    }

    /// <summary>Schritt zum Zeitpunkt <paramref name="elapsedMs"/> nach dem Start; null = Makro zu Ende.</summary>
    public MacroStep? StepAt(long elapsedMs)
    {
        if (elapsedMs < 0)
            return null;
        long t = 0;
        foreach (var step in Steps)
        {
            t += step.DurationMs;
            if (elapsedMs < t)
                return step;
        }
        return null;
    }
}
