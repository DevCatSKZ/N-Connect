namespace Switch2Pro.Bridge.Tests;

/// <summary>
/// Autostart-Eintrag: Zeigt er auf eine EXE, die es nicht mehr gibt (Ordner umbenannt oder verschoben), stellt die App
/// ihn beim Start auf sich selbst um. Dafür muss der EXE-Pfad aus dem Run-Befehl sauber gelesen werden.
/// </summary>
public sealed class AutostartTests
{
    [Theory]
    [InlineData("\"C:\\Programme\\SKZ Connect\\SKZ-Connect.exe\" --autostart", "C:\\Programme\\SKZ Connect\\SKZ-Connect.exe")]
    [InlineData("  \"C:\\A B\\SKZ-Connect.exe\"", "C:\\A B\\SKZ-Connect.exe")]
    [InlineData("C:\\Tools\\SKZ-Connect.exe --autostart", "C:\\Tools\\SKZ-Connect.exe")]
    [InlineData("C:\\Tools\\SKZ-Connect.exe", "C:\\Tools\\SKZ-Connect.exe")]
    public void ExePath_LiestDenPfad(string command, string expected) =>
        Assert.Equal(expected, Autostart.ExePath(command));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"")]
    [InlineData("\"\" --autostart")]
    public void ExePath_UnlesbarIstNull(string command) => Assert.Null(Autostart.ExePath(command));
}
