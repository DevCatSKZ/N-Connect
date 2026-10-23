using Android.Content;
using AndroidX.Core.Content;

namespace NConnect.Android.Core;

/// <summary>
/// Sucht auf GitHub nach einer neueren Android-Version (Datei „SKZ-Connect-Android-…apk“ an einem Release, siehe
/// UpdateCheck.Releases.cs; Netzteil in UpdateCheck.Http.cs) – wie UpdateCheck in SKZ Connect für Windows, nur mit APK
/// statt Setup-EXE. Heruntergeladen wird nur nach Zustimmung, nur von GitHub, und die Datei wird geprüft (Größe,
/// SHA-256 falls GitHub sie angibt). Android fragt vor dem Installieren noch einmal nach.
/// </summary>
internal static partial class UpdateCheck
{
    public const string ProviderAuthority = "com.devcatskz.nconnect.fileprovider";

    /// <summary>Installierte Version (VersionName aus dem Paket, z. B. „0.9.1“).</summary>
    public static Version Current(Context ctx)
    {
        string? name = ctx.PackageManager!.GetPackageInfo(ctx.PackageName!, 0)!.VersionName;
        return Version.TryParse(name?.Split('-', '+')[0], out var v) ? v : new Version(0, 0, 0);
    }

    /// <summary>
    /// Neueste Android-APK unter den Releases (nach der Version im Dateinamen, nicht nach dem Tag) mit der installierten
    /// Version vergleichen. Wirft nicht: Netz- und API-Fehler kommen als <see cref="CheckStatus.Failed"/> zurück
    /// (UpdateCheck.Http.cs), damit die App dann nicht „auf dem neuesten Stand“ meldet.
    /// </summary>
    public static async Task<CheckResult> FindAsync(Context ctx, CancellationToken ct)
    {
        var result = await CheckAsync(Current(ctx), ct);
        if (result.Status == CheckStatus.Failed)
            Log.Warn($"Update-Prüfung nicht möglich: {result.Detail}");
        else
            Log.Info($"Update-Prüfung: {result.Detail}");
        return result;
    }

    private static Java.IO.File DownloadDir(Context ctx) =>
        ctx.GetExternalFilesDir(global::Android.OS.Environment.DirectoryDownloads) ?? ctx.FilesDir!;

    /// <summary>
    /// APK in den App-eigenen Download-Ordner laden und prüfen. Liegt sie dort schon vollständig (z. B. weil vorher noch
    /// „Unbekannte Apps installieren“ erlaubt werden musste), wird sie nicht erneut geladen. Andere APKs im Ordner
    /// werden gelöscht. Liefert die Datei oder null (Grund im Protokoll) – Netz- und Dateifehler werden nicht geworfen.
    /// </summary>
    public static async Task<Java.IO.File?> DownloadAsync(Context ctx, Apk apk, IProgress<int>? progress, CancellationToken ct)
    {
        var file = new Java.IO.File(DownloadDir(ctx), apk.Name);
        CleanUp(ctx, keep: apk.Name);
        var result = await DownloadToAsync(apk, file.AbsolutePath!, Current(ctx), progress, ct);
        if (result.Ok)
        {
            Log.Info($"Update: {result.Detail}");
            return file;
        }
        Log.Warn($"Update: {result.Detail}");
        file.Dispose();
        return null;
    }

    /// <summary>
    /// Alte Update-APKs im Download-Ordner löschen: beim App-Start alle, die nicht neuer als die installierte Version sind
    /// (also schon eingespielt oder überholt), vor einem Download alle außer der neuen (<paramref name="keep"/>).
    /// </summary>
    public static void CleanUp(Context ctx, string? keep = null)
    {
        try
        {
            var current = Current(ctx);
            foreach (var file in DownloadDir(ctx).ListFiles() ?? [])
            {
                using (file)
                {
                    string name = file.Name ?? "";
                    if (!name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) || name == keep)
                        continue;
                    if (keep is null && VersionOf(name) is { } version && IsNewer(version, current))
                        continue; // noch nicht installiert – bleibt für den nächsten Versuch
                    if (file.Delete())
                        Log.Info($"Update: alte Datei {name} gelöscht");
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"Update: Aufräumen nicht möglich: {Log.Reason(e)}");
        }
    }

    /// <summary>System-Installationsdialog für eine geladene APK öffnen (über den FileProvider).</summary>
    public static void Install(Context ctx, Java.IO.File apk)
    {
        var uri = FileProvider.GetUriForFile(ctx, ProviderAuthority, apk)!;
        var intent = new Intent(Intent.ActionView)
            .SetDataAndType(uri, "application/vnd.android.package-archive")!
            .AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission)!;
        ctx.StartActivity(intent);
    }

    /// <summary>Darf die App Pakete installieren? (Ab Android 8 einmalig pro Quelle erlauben.)</summary>
    public static bool CanInstall(Context ctx) =>
        !OperatingSystem.IsAndroidVersionAtLeast(26) || ctx.PackageManager!.CanRequestPackageInstalls();

    /// <summary>Systemseite „Unbekannte Apps installieren“ für SKZ Connect öffnen.</summary>
    public static void OpenInstallPermission(Context ctx) =>
        ctx.StartActivity(new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources,
                global::Android.Net.Uri.Parse($"package:{ctx.PackageName}"))!
            .AddFlags(ActivityFlags.NewTask));
}
