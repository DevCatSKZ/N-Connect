namespace NConnect.Android.Core;

/// <summary>Welche Broadcasts den Autostart auslösen – ohne Android-Abhängigkeiten (testbar in android/NConnect.Android.Tests).</summary>
public sealed partial class BootReceiver
{
    /// <summary>Intent.ActionBootCompleted – kommt erst nach dem ersten Entsperren (der Receiver ist nicht DirectBootAware).</summary>
    internal const string BootCompleted = "android.intent.action.BOOT_COMPLETED";

    /// <summary>Intent.ActionMyPackageReplaced – nach einem Update der App.</summary>
    internal const string PackageReplaced = "android.intent.action.MY_PACKAGE_REPLACED";

    /// <summary>
    /// Nur die beiden System-Broadcasts starten den Dienst. Der Receiver ist nicht exportiert, fremde Apps erreichen ihn
    /// also nicht; die Prüfung sichert zusätzlich gegen einen expliziten Intent ohne oder mit anderer Action ab.
    /// </summary>
    internal static bool StartsService(string? action) => action is BootCompleted or PackageReplaced;
}
