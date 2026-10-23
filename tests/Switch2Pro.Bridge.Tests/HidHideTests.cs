namespace Switch2Pro.Bridge.Tests;

/// <summary>
/// HidHide: eigene virtuelle Pads, die bis 1.0.21 versteckt werden konnten (Issue #1), wieder freigeben – und der Helfer
/// der SYSTEM-Aufgabe nimmt dafür nur, was nachweislich virtuell ist. Ohne HidHide-Aufruf und ohne Aufgabe: geprüft
/// werden die reinen Entscheidungen.
/// </summary>
public sealed class HidHideTests
{
    /// <summary>Die virtuellen DualShock 4 aus dem Issue-#1-Protokoll (eine Kaskade, ViGEm vergibt sie immer wieder).</summary>
    public static TheoryData<string> GhostIds() =>
    [
        @"HID\VID_054C&PID_05C4&REV_0100\2&be03103&0&0000",
        @"HID\VID_054C&PID_05C4&REV_0100\2&1e99e1a8&0&0000",
        @"HID\VID_054C&PID_05C4&REV_0100\2&3153924d&0&0000",
        @"HID\VID_054C&PID_05C4&REV_0100\2&87278eb&0&0000",
        @"HID\VID_054C&PID_05C4&REV_0100\2&3203785b&0&0000",
        @"HID\VID_054C&PID_05C4&REV_0100\2&1f49c7b6&0&0000",
        @"HID\VID_054C&PID_05C4&REV_0100\2&c901711&0&0000",
        @"hid\vid_054c&pid_05c4&rev_0100\2&BE03103&0&0000",
    ];

    [Theory]
    [MemberData(nameof(GhostIds))]
    public void ViGEmDs4_WirdAnDerIdErkannt_AuchOhneAngeschlossenesPad(string id)
    {
        Assert.True(HidHide.IsViGEmDs4Id(id));
        Assert.True(HidHide.IsVirtualPad(id)); // beim Start ist das Pad meist noch gar nicht angelegt
    }

    [Theory]
    // Echte DualShock 4 / DualSense: per USB Verbundgerät (MI_03), per Bluetooth über BTHENUM, Elternpräfix tief.
    [InlineData(@"HID\VID_054C&PID_05C4&MI_03\8&2a1b3c4d&0&0000")]
    [InlineData(@"HID\VID_054C&PID_09CC&MI_03\8&aa&0&0000")]
    [InlineData(@"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&05C4\9&3a&0&0000")]
    [InlineData(@"HID\VID_054C&PID_05C4&REV_0100\7&2a1b3c4d&0&0000")]
    [InlineData(@"HID\VID_054C&PID_0CE6&REV_0100\2&be03103&0&0000")]
    [InlineData(@"HID\VID_057E&PID_2009&REV_0200\2&be03103&0&0000")]
    // Eingeschleuste Argumente: Leerzeichen, Anführungszeichen, „\“ am Ende, Zeilenumbruch.
    [InlineData(@"HID\VID_054C&PID_05C4&REV_0100\2&be03103&0&0000 --app-reg C:\x.exe")]
    [InlineData("HID\\VID_054C&PID_05C4&REV_0100\\2&be03103&0&0000\"")]
    [InlineData(@"HID\VID_054C&PID_05C4&REV_0100\2&be03103&0&0000\")]
    [InlineData("HID\\VID_054C&PID_05C4&REV_0100\\2&be03103&0&0000\n")]
    [InlineData(@"HID\VID_054C&PID_05C4&REV_0100\2&be03103")]
    [InlineData(@"")]
    public void AndereIds_SindKeinViGEmDs4(string id) => Assert.False(HidHide.IsViGEmDs4Id(id));

    [Fact]
    public void HelferAnfrage_VersteckenOderFreigeben()
    {
        var hide = HidHide.ParseHelperRequest(@"HID\A\1| HID\B\2 |");
        Assert.False(hide.Unhide);
        Assert.Equal([@"HID\A\1", @"HID\B\2"], hide.Ids.Order());

        var unhide = HidHide.ParseHelperRequest(@"UNHIDE|HID\A\1|hid\a\1");
        Assert.True(unhide.Unhide);
        Assert.Equal([@"HID\A\1"], unhide.Ids); // Groß-/Kleinschreibung egal, das Wort selbst ist keine ID

        Assert.True(HidHide.ParseHelperRequest(HidHide.UnhideVerb).Unhide);
        Assert.Empty(HidHide.ParseHelperRequest(HidHide.UnhideVerb).Ids);
        Assert.Empty(HidHide.ParseHelperRequest("").Ids);
        // „unhide“ nur als erstes Feld: weiter hinten ist es eine (ungültige) ID, kein Freigeben.
        Assert.False(HidHide.ParseHelperRequest(@"HID\A\1|unhide").Unhide);

        string many = string.Join('|', Enumerable.Range(0, 40).Select(i => $@"HID\X\{i}"));
        Assert.Equal(HidHide.MaxIds, HidHide.ParseHelperRequest(many).Ids.Count);
        Assert.Equal(HidHide.MaxIds, HidHide.ParseHelperRequest(HidHide.UnhideVerb + "|" + many).Ids.Count);
    }

    [Fact]
    public void Freigeben_NimmtNurAusDerSperrliste()
    {
        string args = HidHide.UnhideArguments([@"HID\VID_054C&PID_05C4&REV_0100\2&be03103&0&0000", @"HID\VID_054C&PID_05C4&REV_0100\2&c901711&0&0000"]);
        Assert.Equal("--dev-unhide \"HID\\VID_054C&PID_05C4&REV_0100\\2&be03103&0&0000\" " +
                     "--dev-unhide \"HID\\VID_054C&PID_05C4&REV_0100\\2&c901711&0&0000\"", args);
        // Freigaben von Programmen und das Verstecken selbst bleiben unberührt.
        Assert.DoesNotContain("--app-", args);
        Assert.DoesNotContain("--cloak", args);
    }

    [Fact]
    public void Verstecken_GibtNurDieseExeFrei()
    {
        Assert.Equal("--app-reg \"C:\\Program Files\\SKZ-Connect\\SKZ-Connect.exe\" --dev-hide \"HID\\A\\1\" --cloak-on",
            HidHide.HideArguments(@"C:\Program Files\SKZ-Connect\SKZ-Connect.exe", [@"HID\A\1"]));
    }

    [Fact]
    public async Task Freigeben_OhneVirtuellesPad_RuftNichtsAuf()
    {
        // Echte bzw. nicht angeschlossene Geräte gibt SKZ Connect nie frei – ohne Aufgabe und ohne UAC-Abfrage.
        var result = await HidHide.UnhideAsync([@"HID\VID_057E&PID_2009&REV_0200\9&3a&0&0000", @"HID\VID_054C&PID_05C4&MI_03\8&2a1b3c4d&0&0000"]);
        Assert.Equal(HidHide.Outcome.Failed, result);
        Assert.Equal(HidHide.Outcome.Failed, await HidHide.UnhideAsync([]));
    }
}
