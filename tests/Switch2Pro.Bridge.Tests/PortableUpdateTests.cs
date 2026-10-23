using System.Text.RegularExpressions;

namespace Switch2Pro.Bridge.Tests;

/// <summary>
/// Ein-Klick-Update der portablen Variante: Das Hilfsskript ersetzt die EXE und danach die Begleitdateien aus dem ZIP
/// (Lizenz, Drittanbieter-Hinweise – „Lizenzen Dritter“ zeigt die Datei neben der EXE), nie portable.txt oder „data“.
/// Das Skript wird nur gebaut, nicht ausgeführt (es startet am Ende SKZ Connect).
/// </summary>
public sealed class PortableUpdateTests
{
    private const string NewDir = @"C:\Users\Jürgen\AppData\Local\Temp\SKZ-Connect-Update\portable";
    private const string AppDir = @"D:\Tools\SKZ Connect 100%";

    private static string Script() => TrayApp.PortableUpdateScript(4242, $@"{NewDir}\SKZ-Connect.exe", $@"{AppDir}\SKZ-Connect.exe",
        TrayApp.PortableCompanionFiles.Select(name => ($@"{NewDir}\{name}", $@"{AppDir}\{name}")));

    [Fact]
    public void BegleitdateienErstNachDerExe_DannNeustart()
    {
        var lines = Script().Split("\r\n");
        int exe = Array.FindIndex(lines, l => l.StartsWith("copy /y \"C:\\Users\\Jürgen\\AppData\\Local\\Temp\\SKZ-Connect-Update\\portable\\SKZ-Connect.exe\" \"D:\\Tools\\SKZ Connect 100%%\\SKZ-Connect.exe\"", StringComparison.Ordinal));
        int copied = Array.IndexOf(lines, ":copied");
        int run = Array.IndexOf(lines, ":run");
        Assert.True(exe >= 0 && copied > exe && run > copied, Script());
        Assert.EndsWith("&& goto copied", lines[exe]);
        // Scheitert das Ersetzen der EXE, nicht die neuen Hinweise neben die alte EXE legen.
        Assert.Equal("goto run", lines[copied - 1]);

        var companions = lines[(copied + 1)..run];
        Assert.Equal(TrayApp.PortableCompanionFiles.Length, companions.Length);
        foreach (var (line, name) in companions.Zip(TrayApp.PortableCompanionFiles))
            Assert.Equal($"copy /y \"{NewDir}\\{name}\" \"{AppDir.Replace("%", "%%")}\\{name}\" >nul 2>nul", line);
        Assert.Equal("start \"\" \"D:\\Tools\\SKZ Connect 100%%\\SKZ-Connect.exe\"", lines[run + 1]);
    }

    [Fact]
    public void EinstellungenUndPortableTxtBleibenUnberuehrt()
    {
        Assert.Equal(["LICENSE", SettingsForm.NoticesFile], TrayApp.PortableCompanionFiles);
        string script = Script();
        Assert.DoesNotContain("portable.txt", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\data", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("del /", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OhneBegleitdateien_NurDieExe()
    {
        var lines = TrayApp.PortableUpdateScript(1, @"C:\n\SKZ-Connect.exe", @"C:\a\SKZ-Connect.exe", []).Split("\r\n");
        Assert.Equal(Array.IndexOf(lines, ":copied") + 1, Array.IndexOf(lines, ":run"));
    }

    /// <summary>Was das Update mitkopiert, packt der Build auch ins Portable-ZIP (build.yml).</summary>
    [Fact]
    public void BuildPacktDieBegleitdateienInsZip()
    {
        string build = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", "build.yml"));
        string copy = Regex.Match(build, @"Copy-Item out/publish/win-x64/SKZ-Connect\.exe,[^\n]*out/portable/").Value;
        Assert.NotEqual("", copy);
        foreach (var name in TrayApp.PortableCompanionFiles)
            Assert.Contains(name, copy);
    }
}
