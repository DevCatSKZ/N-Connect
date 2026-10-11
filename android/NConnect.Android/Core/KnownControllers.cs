using System.Text.Json;
using Switch2Pro.Protocol;

namespace NConnect.Android.Core;

/// <summary>Ein Controller, der schon einmal mit diesem Gerät verbunden war.</summary>
public sealed record KnownController(string Address, ControllerKind Kind, string? Serial, DateTime LastConnected);

/// <summary>
/// Merkt sich alle Controller, die sich schon einmal verbunden haben (Datei <c>files/known_controllers.json</c>) –
/// für die Liste „Gekoppelte Controller“ auf der Startseite, auch wenn sie gerade nicht verbunden sind.
/// </summary>
public static class KnownControllers
{
    private static readonly object Lock = new();
    private static List<KnownController>? _items;

    public static event Action? Changed;

    private static string FilePath =>
        Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "known_controllers.json");

    public static IReadOnlyList<KnownController> All
    {
        get
        {
            lock (Lock)
                return [.. Load().OrderByDescending(k => k.LastConnected)];
        }
    }

    /// <summary>Nach erfolgreicher Einrichtung aufrufen.</summary>
    public static void Remember(string address, ControllerKind kind, string? serial)
    {
        lock (Lock)
        {
            var list = Load();
            list.RemoveAll(k => string.Equals(k.Address, address, StringComparison.OrdinalIgnoreCase));
            list.Add(new KnownController(address, kind, serial, DateTime.Now));
            Save(list);
        }
        Changed?.Invoke();
    }

    public static void Forget(string address)
    {
        lock (Lock)
        {
            var list = Load();
            if (list.RemoveAll(k => string.Equals(k.Address, address, StringComparison.OrdinalIgnoreCase)) == 0)
                return;
            Save(list);
        }
        Changed?.Invoke();
    }

    private static List<KnownController> Load()
    {
        if (_items is not null)
            return _items;
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<List<KnownController>>(File.ReadAllText(FilePath)) is { } list)
                return _items = list;
        }
        catch (Exception e)
        {
            Log.Warn($"Gekoppelte Controller nicht lesbar: {e.Message}");
        }
        return _items = [];
    }

    private static void Save(List<KnownController> list)
    {
        _items = list;
        try
        {
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(list));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception e)
        {
            Log.Warn($"Gekoppelte Controller nicht gespeichert: {e.Message}");
        }
    }
}
