namespace Switch2Pro.Bridge.Tests;

/// <summary>Pfade im Repository (die Tests laufen in bin/…; Switch2Pro.sln liegt darüber).</summary>
internal static class Repo
{
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Switch2Pro.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Switch2Pro.sln nicht gefunden – Tests laufen außerhalb des Repositorys?");
    }
}
