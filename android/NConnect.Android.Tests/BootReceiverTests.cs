using NConnect.Android.Core;

namespace NConnect.Android.Tests;

/// <summary>
/// Autostart: Nur die beiden System-Broadcasts starten den Controller-Dienst. Vorher war der Receiver exportiert und wertete
/// die Action nicht aus – jede App konnte den Dienst mit einem expliziten Intent (auch ohne Action) starten.
/// </summary>
public class BootReceiverTests
{
    [Theory]
    [InlineData("android.intent.action.BOOT_COMPLETED", true)]
    [InlineData("android.intent.action.MY_PACKAGE_REPLACED", true)]
    [InlineData(null, false)] // expliziter Intent ohne Action
    [InlineData("", false)]
    [InlineData("android.intent.action.LOCKED_BOOT_COMPLETED", false)] // vor dem Entsperren – Einstellungen noch nicht lesbar
    [InlineData("android.intent.action.PACKAGE_REPLACED", false)]
    [InlineData("android.intent.action.boot_completed", false)]
    [InlineData("com.example.START_NCONNECT", false)]
    public void NurSystemBroadcasts_StartenDenDienst(string? action, bool expected) =>
        Assert.Equal(expected, BootReceiver.StartsService(action));
}
