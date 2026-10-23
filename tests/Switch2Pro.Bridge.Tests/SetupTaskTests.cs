using System.Text.RegularExpressions;

namespace Switch2Pro.Bridge.Tests;

/// <summary>
/// Geplante Aufgabe „SKZ Connect HidHide“ im Setup: läuft als SYSTEM und darf von jedem angemeldeten Benutzer gestartet
/// werden – deshalb nur für eine EXE, die Benutzer ohne Adminrechte nicht austauschen können.
/// </summary>
public sealed class SetupTaskTests
{
    [Fact]
    public void Aufgabenname_WieImSetup()
    {
        string iss = Iss();
        Assert.Contains($"#define HidHideTask \"{HidHide.TaskName}\"", iss);
        Assert.Contains("<Arguments>--hidhide-helper \"$(Arg0)\"</Arguments>", iss);
    }

    /// <summary>
    /// Die SYSTEM-Aufgabe startet {app}\SKZ-Connect.exe, und jeder Benutzer darf sie starten. In einem frei gewählten Ordner
    /// (z. B. C:\SKZ Connect erbt „Ändern“ für alle) könnte jeder die EXE tauschen – deshalb nur unter „Programme“, und eine
    /// Aufgabe einer früheren Installation an anderem Ort wird entfernt.
    /// </summary>
    [Fact]
    public void Setup_LegtDieSystemAufgabeNurUnterProgrammeAn()
    {
        string iss = Iss();
        string check = Function(iss, "function AppDirIsProtected: Boolean;");
        Assert.Contains("ExpandFileName(ExpandConstant('{app}'))", check);
        Assert.Contains("AddBackslash(ExpandConstant('{commonpf64}'))", check);
        Assert.Contains("AddBackslash(ExpandConstant('{commonpf32}'))", check);

        Assert.Single(Regex.Matches(iss, @"\.RegisterTask\("));
        string register = Function(iss, "procedure RegisterHidHideTask;");
        var guarded = Regex.Match(register,
            @"if AppDirIsProtected then\s*\n(\s*//[^\n]*\n)*\s*Folder\.RegisterTask\('\{#HidHideTask\}'[^\n]*\)\s*\n\s*else\s*\n\s*begin(?<else>.*?)\n    end;",
            RegexOptions.Singleline);
        Assert.True(guarded.Success, "RegisterTask muss allein im then-Zweig von AppDirIsProtected stehen");
        Assert.Contains("Folder.DeleteTask('{#HidHideTask}', 0);", guarded.Groups["else"].Value);
    }

    private static string Iss() =>
        File.ReadAllText(Path.Combine(Repo.Root(), "installer", "SKZ-Connect.iss")).Replace("\r\n", "\n");

    private static string Function(string iss, string header)
    {
        var m = Regex.Match(iss, Regex.Escape(header) + @".*?\nend;", RegexOptions.Singleline);
        Assert.True(m.Success, $"{header} fehlt im Setup-Skript");
        return m.Value;
    }
}
