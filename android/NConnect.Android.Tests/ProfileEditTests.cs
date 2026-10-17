using NConnect.Android.Profiles;
using Switch2Pro.Protocol;

namespace NConnect.Android.Tests;

/// <summary>
/// Profile je Spiel (Befund AN-2): neue Profile übernehmen die aktuelle Belegung, Änderungen wirken in der gemeinsamen
/// Belegung (<see cref="Mapping.Evaluate"/>), Umbenennen/Löschen halten fest gewähltes und erkanntes Profil stimmig.
/// </summary>
public class ProfileEditTests
{
    private static PadInput Press(ControllerKind kind, ProButtons buttons) => new() { Kind = kind, Buttons = buttons };

    [Fact]
    public void KopieDerStandardbelegung_GiltFuerJedeTasteUndJedeArt()
    {
        var settings = new Settings { Layout = FaceButtonLayout.Switch2 };
        settings.Remap = new Dictionary<ProButtons, ExtraButtonTarget>(settings.Remap) { [ProButtons.GL] = ExtraButtonTarget.LS };
        var profile = ProfileEdit.CopyOfStandard(settings, "Spiel", "com.example.game");
        Assert.Equal(new[] { "com.example.game" }, profile.Programs);
        foreach (var kind in ProfileEdit.Kinds)
        {
            foreach (var button in ControllerButtons.For(kind))
            {
                Assert.Equal(Mapping.ActionFor(button, kind, settings, profile: null, shift: false).ToString(),
                    Mapping.ActionFor(button, kind, settings, profile, shift: false).ToString());
            }
        }
    }

    [Fact]
    public void Kopie_BleibtBeiSpaeterenStandardAenderungen()
    {
        var settings = new Settings { Layout = FaceButtonLayout.Xbox };
        var profile = ProfileEdit.CopyOfStandard(settings, "Spiel", "com.example.game");
        settings.Layout = FaceButtonLayout.Switch2;
        // Xbox-Belegung: Nintendo A (rechts) = Xbox B – auch nachdem der Standard umgestellt wurde.
        Assert.Equal(ButtonAction.Gamepad(ExtraButtonTarget.B),
            Mapping.ActionFor(ProButtons.A, ControllerKind.Pro2, settings, profile, shift: false));
        Assert.Equal(ButtonAction.Gamepad(ExtraButtonTarget.A),
            Mapping.ActionFor(ProButtons.A, ControllerKind.Pro2, settings, profile: null, shift: false));
    }

    [Fact]
    public void GeaenderteTaste_WirktImAktivenProfil()
    {
        var settings = new Settings();
        var profile = ProfileEdit.WithAction(ProfileEdit.CopyOfStandard(settings, "Spiel", "com.example.game"),
            ControllerKind.Pro2, ProButtons.A, "X");
        settings.NamedProfiles = [profile];
        settings.DetectedProfile = "Spiel";
        var output = Mapping.Evaluate(Press(ControllerKind.Pro2, ProButtons.A), settings);
        Assert.Equal(XButtons.X, output.Gamepad.Buttons);
        // Ohne Profil gilt wieder der Standard (Xbox-Belegung: A rechts = B).
        settings.DetectedProfile = null;
        Assert.Equal(XButtons.B, Mapping.Evaluate(Press(ControllerKind.Pro2, ProButtons.A), settings).Gamepad.Buttons);
    }

    [Fact]
    public void KeineFunktion_GyroUndMakro_KommenAn()
    {
        var settings = new Settings();
        var profile = ProfileEdit.CopyOfStandard(settings, "Spiel", "com.example.game");
        profile = ProfileEdit.WithAction(profile, ControllerKind.Pro2, ProButtons.B, "None");
        profile = ProfileEdit.WithAction(profile, ControllerKind.Pro2, ProButtons.GL, "GyroStick");
        profile = ProfileEdit.WithAction(profile, ControllerKind.Pro2, ProButtons.GR, ButtonAction.Play("A 80, Pause 40, A 80").ToString());
        settings.NamedProfiles = [profile];
        settings.ForcedProfile = "Spiel";
        var output = Mapping.Evaluate(Press(ControllerKind.Pro2, ProButtons.B | ProButtons.GL | ProButtons.GR), settings);
        Assert.Equal(XButtons.None, output.Gamepad.Buttons);
        Assert.True(output.Specials.HasFlag(SpecialAction.GyroStick));
        Assert.Equal(new[] { "A 80, Pause 40, A 80" }, output.Macros);
    }

    [Fact]
    public void Standard_EntferntDenEintrag()
    {
        var profile = ProfileEdit.WithAction(new NamedProfile { Name = "P" }, ControllerKind.GameCube2, ProButtons.ZR, "LT");
        Assert.Equal("LT", profile.Buttons[ControllerKind.GameCube2][ProButtons.ZR]);
        var reset = ProfileEdit.WithAction(profile, ControllerKind.GameCube2, ProButtons.ZR, null);
        Assert.False(reset.Buttons.ContainsKey(ControllerKind.GameCube2)); // leere Art fällt ganz weg
        Assert.Equal("LT", profile.Buttons[ControllerKind.GameCube2][ProButtons.ZR]); // Original unverändert (Kopie)
    }

    [Fact]
    public void AllesAufStandard_NurFuerDieseArt()
    {
        var settings = new Settings();
        var profile = ProfileEdit.CopyOfStandard(settings, "Spiel", "com.example.game");
        var reset = ProfileEdit.WithoutKind(profile, ControllerKind.JoyCon2Left);
        Assert.False(reset.Buttons.ContainsKey(ControllerKind.JoyCon2Left));
        Assert.True(reset.Buttons.ContainsKey(ControllerKind.Pro2));
        Assert.True(profile.Buttons.ContainsKey(ControllerKind.JoyCon2Left));
    }

    [Theory]
    [InlineData("A 80, Pause 60, A 80", "A 80, Pause 60, A 80")]
    [InlineData("Down+B 150\r\nA", "Down+B 150,A")]
    [InlineData("  LT 100  ", "LT 100")]
    public void Makro_Gueltig(string text, string expected) => Assert.Equal(expected, ProfileEdit.ValidMacro(text));

    [Theory]
    [InlineData("Key:Ctrl+C 50")] // Tastatur gibt es unter Android nicht
    [InlineData("A 80, Key:Space")]
    [InlineData("Sprung 80")]
    [InlineData("A 99999")]
    [InlineData("")]
    [InlineData(null)]
    public void Makro_Ungueltig(string? text) => Assert.Null(ProfileEdit.ValidMacro(text));

    [Fact]
    public void Namen_SindEindeutig_OhneGrossKlein()
    {
        NamedProfile[] profiles = [new() { Name = "Minecraft" }, new() { Name = "Dolphin" }];
        Assert.False(ProfileEdit.NameFree(profiles, "minecraft"));
        Assert.False(ProfileEdit.NameFree(profiles, ""));
        Assert.True(ProfileEdit.NameFree(profiles, "Minecraft", except: "Minecraft"));
        Assert.True(ProfileEdit.NameFree(profiles, "MINECRAFT", except: "Minecraft"));
        Assert.False(ProfileEdit.NameFree(profiles, "dolphin", except: "Minecraft"));
        Assert.True(ProfileEdit.NameFree(profiles, "Eden"));
    }

    [Fact]
    public void Umbenennen_ZiehtFestesUndErkanntesProfilMit()
    {
        var settings = new Settings { NamedProfiles = [new() { Name = "Alt", Programs = ["a"] }, new() { Name = "B" }] };
        settings.ForcedProfile = "Alt";
        settings.DetectedProfile = "Alt";
        ProfileEdit.Rename(settings, "Alt", "Neu");
        Assert.Equal(new[] { "Neu", "B" }, settings.NamedProfiles.Select(p => p.Name));
        Assert.Equal(new[] { "a" }, settings.NamedProfiles[0].Programs);
        Assert.Equal("Neu", settings.ForcedProfile);
        Assert.Equal("Neu", settings.DetectedProfile);
        Assert.Equal("Neu", settings.CurrentProfile()?.Name);
    }

    [Fact]
    public void Loeschen_SetztAufAutomatischZurueck()
    {
        var settings = new Settings { NamedProfiles = [new() { Name = "A" }, new() { Name = "B" }] };
        settings.ForcedProfile = "A";
        settings.DetectedProfile = "A";
        ProfileEdit.Delete(settings, "A");
        Assert.Equal(new[] { "B" }, settings.NamedProfiles.Select(p => p.Name));
        Assert.Null(settings.ForcedProfile);
        Assert.Null(settings.DetectedProfile);
    }

    [Fact]
    public void Ersetzen_TauschtNurDasGleichnamigeProfil()
    {
        var settings = new Settings { NamedProfiles = [new() { Name = "A" }, new() { Name = "B" }] };
        var before = settings.NamedProfiles;
        ProfileEdit.Replace(settings, settings.NamedProfiles[1] with { Programs = ["com.b"] });
        Assert.NotSame(before, settings.NamedProfiles); // neue Liste – der Bluetooth-Thread liest die alte weiter
        Assert.Equal(new[] { "com.b" }, settings.NamedProfiles[1].Programs);
        Assert.Empty(settings.NamedProfiles[0].Programs);
    }

    [Fact]
    public void Ziele_SindGueltigeAktionen()
    {
        foreach (var (action, _) in ProfileEdit.Targets)
        {
            var parsed = ButtonAction.Parse(action);
            Assert.Equal(action, parsed.ToString());
            Assert.False(parsed.IsKeyboard);
        }
    }

    [Fact]
    public void GespeichertesProfil_UeberstehtSpeichernUndLaden()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nconnect-profile-test-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new Settings();
            var profile = ProfileEdit.WithAction(ProfileEdit.CopyOfStandard(settings, "Spiel", "com.example.game"),
                ControllerKind.JoyConPair, ProButtons.ZR, ButtonAction.Play("RT 50, Pause 50, RT 50").ToString());
            settings.NamedProfiles = [profile];
            settings.Save(path);
            var loaded = Settings.Load(path).NamedProfiles.Single();
            Assert.Equal("Spiel", loaded.Name);
            Assert.Equal(new[] { "com.example.game" }, loaded.Programs);
            Assert.Equal("Macro:RT 50, Pause 50, RT 50", loaded.Buttons[ControllerKind.JoyConPair][ProButtons.ZR]);
            Assert.Equal(profile.Buttons[ControllerKind.Pro2], loaded.Buttons[ControllerKind.Pro2]);
        }
        finally
        {
            File.Delete(path);
            File.Delete(Settings.BackupPath(path));
        }
    }
}
