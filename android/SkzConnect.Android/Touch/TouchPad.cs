using Android.AccessibilityServices;
using Android.Graphics;
using SkzConnect.Android.Output;
using Switch2Pro.Protocol;
using Path = Android.Graphics.Path;

namespace SkzConnect.Android.Touch;

/// <summary>
/// Touch-Ausgabe: gedrückte Tasten werden zu gehaltenen Fingern, Sticks zu ziehenden Fingern um ihren Mittelpunkt.
/// Android bricht eine laufende Geste ab, sobald eine neue kommt – deshalb werden alle aktiven Finger in EINER Geste
/// fortgesetzt (StrokeDescription.ContinueStroke) und nie mehr als eine Geste gleichzeitig verschickt.
/// </summary>
public sealed class TouchPad : IVirtualPad
{
    private const long StepMs = 16;
    private const float StickDeadzone = 0.2f;

    private static TouchLayout _layout = TouchLayout.Load();
    private readonly Dictionary<TouchControl, Finger> _fingers = [];
    private readonly object _lock = new();
    private Dictionary<TouchControl, (float X, float Y)>? _pending;
    private bool _inFlight;

    private sealed class Finger(GestureDescription.StrokeDescription stroke, float x, float y)
    {
        public GestureDescription.StrokeDescription Stroke = stroke;
        public float X = x, Y = y;
    }

    /// <summary>Der Editor hat gespeichert: neue Belegung übernehmen.</summary>
    public static void ReloadLayout() => _layout = TouchLayout.Load();

    public event Action<byte, byte>? Rumble { add { } remove { } }

    public void Update(GamepadState g)
    {
        var service = TouchService.Instance;
        if (service is null)
            return;
        var (w, h) = service.ScreenSize();
        var desired = new Dictionary<TouchControl, (float X, float Y)>();
        foreach (var b in _layout.Bindings)
        {
            float x = b.X * w, y = b.Y * h;
            if (b.Control is TouchControl.LeftStick or TouchControl.RightStick)
            {
                var (sx, sy) = b.Control == TouchControl.LeftStick ? (g.LeftX, g.LeftY) : (g.RightX, g.RightY);
                float fx = sx / 32767f, fy = -sy / 32767f; // Bildschirm: Y nach unten
                if (MathF.Sqrt(fx * fx + fy * fy) > StickDeadzone)
                {
                    float r = _layout.StickRadius * w;
                    desired[b.Control] = (x + Math.Clamp(fx, -1, 1) * r, y + Math.Clamp(fy, -1, 1) * r);
                }
            }
            else if (TouchLayout.IsPressed(b.Control, g))
            {
                desired[b.Control] = (x, y);
            }
        }
        lock (_lock)
        {
            if (desired.Count == 0 && _fingers.Count == 0)
                return;
            _pending = desired;
            if (!_inFlight)
                service.RunOnMain(DispatchPending);
        }
    }

    private void DispatchPending()
    {
        var service = TouchService.Instance;
        GestureDescription? gesture;
        lock (_lock)
        {
            if (service is null || _pending is null || _inFlight)
                return;
            gesture = Build(_pending);
            _pending = null;
            if (gesture is null)
                return;
            _inFlight = true;
        }
        bool sent = service.DispatchGesture(gesture, new Done(this), null);
        if (!sent)
            Finished(cancelled: true);
    }

    /// <summary>Eine Geste mit allen Fingern: gehaltene fortsetzen, neue aufsetzen, losgelassene anheben.</summary>
    private GestureDescription? Build(Dictionary<TouchControl, (float X, float Y)> desired)
    {
        var builder = new GestureDescription.Builder();
        int strokes = 0;
        foreach (var (control, finger) in _fingers.ToList())
        {
            bool keep = desired.TryGetValue(control, out var to);
            if (!keep)
                to = (finger.X, finger.Y);
            var path = Line(finger.X, finger.Y, to.X, to.Y);
            finger.Stroke = finger.Stroke.ContinueStroke(path, 0, StepMs, keep)!;
            finger.X = to.X;
            finger.Y = to.Y;
            builder.AddStroke(finger.Stroke);
            strokes++;
            if (!keep)
                _fingers.Remove(control);
        }
        foreach (var (control, to) in desired)
        {
            if (_fingers.ContainsKey(control) || strokes >= GestureDescription.MaxStrokeCount)
                continue;
            var stroke = new GestureDescription.StrokeDescription(Line(to.X, to.Y, to.X, to.Y), 0, StepMs, true);
            _fingers[control] = new Finger(stroke, to.X, to.Y);
            builder.AddStroke(stroke);
            strokes++;
        }
        return strokes == 0 ? null : builder.Build();
    }

    private static Path Line(float x0, float y0, float x1, float y1)
    {
        var path = new Path();
        path.MoveTo(Math.Max(0, x0), Math.Max(0, y0));
        if (x0 != x1 || y0 != y1)
            path.LineTo(Math.Max(0, x1), Math.Max(0, y1));
        return path;
    }

    private void Finished(bool cancelled)
    {
        lock (_lock)
        {
            _inFlight = false;
            if (cancelled)
                _fingers.Clear(); // System hat abgebrochen: Finger gelten als angehoben, beim nächsten Mal neu aufsetzen
            if (_pending is not null)
                TouchService.Instance?.RunOnMain(DispatchPending);
        }
    }

    private sealed class Done(TouchPad pad) : AccessibilityService.GestureResultCallback
    {
        public override void OnCompleted(GestureDescription? gestureDescription) => pad.Finished(cancelled: false);
        public override void OnCancelled(GestureDescription? gestureDescription) => pad.Finished(cancelled: true);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_fingers.Count == 0)
                return;
            _pending = [];
        }
        TouchService.Instance?.RunOnMain(DispatchPending); // alle Finger anheben
    }
}
