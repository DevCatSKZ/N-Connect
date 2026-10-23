using Android.Content;
using Switch2Pro.Protocol;

namespace SkzConnect.Android;

/// <summary>Wie die Controller-Eingaben bei den Spielen ankommen.</summary>
public enum OutputPath
{
    /// <summary>Den besten verfügbaren Weg wählen: Root, dann Shizuku, dann Touch.</summary>
    Auto,
    /// <summary>Virtueller Xbox-Controller über Root (su).</summary>
    Root,
    /// <summary>Virtueller Xbox-Controller über Shizuku (Shell-Rechte ohne Root).</summary>
    Shizuku,
    /// <summary>Nur Bildschirmberührungen über die Bedienungshilfe.</summary>
    Touch,
}

/// <summary>
/// Einstellungen der Android-App (SharedPreferences). Belegung, Sticks, Gyro usw. stehen wie unter Windows in
/// <see cref="Settings"/> (JSON-Datei, gleiche Klasse wie in SKZ Connect für Windows).
/// </summary>
public static class AppPrefs
{
    private static ISharedPreferences? _prefs;
    private static ISharedPreferences P => _prefs ??= OpenPrefs();

    /// <summary>Fruehere Versionen speicherten unter „nconnect" – einmalig uebernehmen, dann loeschen.</summary>
    private static ISharedPreferences OpenPrefs()
    {
        var ctx = global::Android.App.Application.Context;
        var p = ctx.GetSharedPreferences("skzconnect", FileCreationMode.Private)!;
        var old = ctx.GetSharedPreferences("nconnect", FileCreationMode.Private)!;
        if (old.All.Count == 0)
            return p;
        var e = p.Edit()!;
        foreach (var kv in old.All)
        {
            switch (kv.Value)
            {
                case Java.Lang.Boolean b: e.PutBoolean(kv.Key, b.BooleanValue()); break;
                case Java.Lang.Integer i: e.PutInt(kv.Key, i.IntValue()); break;
                case Java.Lang.Long l: e.PutLong(kv.Key, l.LongValue()); break;
                case Java.Lang.Float f: e.PutFloat(kv.Key, f.FloatValue()); break;
                case Java.Lang.String str: e.PutString(kv.Key, str.ToString()); break;
            }
        }
        e.Apply();
        ctx.DeleteSharedPreferences("nconnect");
        return p;
    }

    /// <summary>Einrichtungs-Assistent abgeschlossen.</summary>
    public static bool SetupDone
    {
        get => P.GetBoolean("setup_done", false);
        set => P.Edit()!.PutBoolean("setup_done", value)!.Apply();
    }

    /// <summary>Zuletzt geöffneter Schritt des Assistenten (nach Neustart dort weitermachen statt von vorn).</summary>
    public static int WizardStep
    {
        get => P.GetInt("wizard_step", 0);
        set => P.Edit()!.PutInt("wizard_step", value)!.Apply();
    }

    public static OutputPath Mode
    {
        get => (OutputPath)P.GetInt("output_mode", (int)OutputPath.Auto);
        set => P.Edit()!.PutInt("output_mode", (int)value)!.Apply();
    }

    /// <summary>Touch-Steuerung zusätzlich zum virtuellen Controller (für Spiele ohne Controller-Unterstützung).</summary>
    public static bool TouchEnabled
    {
        get => P.GetBoolean("touch_enabled", false);
        set => P.Edit()!.PutBoolean("touch_enabled", value)!.Apply();
    }

    /// <summary>
    /// Bluetooth-Adresse dieses Geräts (z. B. „04:C8:B0:F5:B2:27“). Normale Apps dürfen sie nicht lesen; mit Root/Shizuku
    /// liest SKZ Connect sie einmal aus, oder der Nutzer trägt sie ein. Nötig, damit Controller sich ohne SYNC wiederverbinden.
    /// </summary>
    public static string? HostAddress
    {
        get => P.GetString("host_address", null);
        set => P.Edit()!.PutString("host_address", value)!.Apply();
    }

    /// <summary>Beim Öffnen der App automatisch nach Controllern suchen.</summary>
    public static bool AutoStart
    {
        get => P.GetBoolean("auto_start", true);
        set => P.Edit()!.PutBoolean("auto_start", value)!.Apply();
    }

    /// <summary>Controller-Dienst nach dem Einschalten des Geräts wieder starten (BootReceiver).</summary>
    public static bool StartOnBoot
    {
        get => P.GetBoolean("start_on_boot", false);
        set => P.Edit()!.PutBoolean("start_on_boot", value)!.Apply();
    }

    /// <summary>DSU-Server (Cemuhook, 127.0.0.1:26760): Gyro/Tasten an Emulatoren auf diesem Gerät liefern.</summary>
    public static bool DsuEnabled
    {
        get => P.GetBoolean("dsu_enabled", false);
        set => P.Edit()!.PutBoolean("dsu_enabled", value)!.Apply();
    }

    public static string SettingsPath =>
        Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "settings.json");

    private static Settings? _settings;

    /// <summary>Belegung und Feineinstellungen (geteilte Klasse mit der Windows-Version).</summary>
    public static Settings Settings => _settings ??= LoadSettings();

    /// <summary>
    /// Laden. War settings.json unlesbar, merkt sich die App das für einen einmaligen Hinweis beim nächsten Öffnen
    /// (<see cref="TakeSettingsNotice"/>) – geladen wird womöglich schon beim Gerätestart im Dienst, ohne Fenster.
    /// </summary>
    private static Settings LoadSettings()
    {
        var settings = Settings.Load(SettingsPath);
        if (settings.LoadError is { } error)
        {
            Log.Warn($"settings.json nicht lesbar ({error}) – Standardwerte");
            P.Edit()!.PutString("settings_notice", NoticeUnreadable)!.Apply();
        }
        else if (settings.RestoredFromBackup)
        {
            Log.Warn("settings.json nicht lesbar – Sicherung geladen");
            P.Edit()!.PutString("settings_notice", NoticeRestored)!.Apply();
        }
        return settings;
    }

    public const string NoticeUnreadable = "unreadable", NoticeRestored = "restored";

    /// <summary>
    /// Offener Hinweis zu den Einstellungen (<see cref="NoticeUnreadable"/>: Standardwerte, <see cref="NoticeRestored"/>:
    /// aus der Sicherung) oder null – nach dem Abholen gelöscht, damit er nur einmal erscheint.
    /// </summary>
    public static string? TakeSettingsNotice()
    {
        _ = Settings; // spätestens jetzt laden – das merkt sich auch den Hinweis
        string? notice = P.GetString("settings_notice", null);
        if (notice is not null)
            P.Edit()!.Remove("settings_notice")!.Apply();
        return notice;
    }

    public static void SaveSettings()
    {
        try
        {
            Settings.Save(SettingsPath);
        }
        catch (Exception e)
        {
            Log.Error("Einstellungen speichern", e);
        }
    }

    /// <summary>„04:C8:B0:F5:B2:27“ → 0x04C8B0F5B227 (0 bei ungültiger Eingabe).</summary>
    public static ulong ParseAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        string hex = text.Replace(":", "").Replace("-", "").Trim();
        return hex.Length == 12 && ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out ulong v) ? v : 0;
    }
}
