using System.Reflection;
using SkzConnect.Android.Profiles;
using Switch2Pro.Protocol;

namespace SkzConnect.Android.Tests;

/// <summary>
/// Texte, die erst zur Laufzeit durch L.T laufen (Tastennamen aus der gemeinsamen Bibliothek, Ziele im Belegungs-Editor),
/// sieht android/i18n/generate.py nicht – hier wird geprüft, dass sie in allen 11 Übersetzungstabellen stehen.
/// </summary>
public class TranslationTests
{
    private static readonly string[] Langs = ["En", "Es", "Fr", "It", "Pt", "Nl", "Pl", "Ru", "Ja", "Zh", "Ko"];

    private static Dictionary<string, string> Table(string lang) =>
        (Dictionary<string, string>)typeof(L).GetField(lang, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    /// <summary>Reine Kürzel wie „A“, „ZL“, „HOME“, „−“ bleiben in allen Sprachen gleich.</summary>
    private static bool LanguageNeutral(string text) => !text.Any(char.IsLower);

    private static void AssertTranslated(IEnumerable<string> texts)
    {
        var missing = new List<string>();
        foreach (var text in texts.Distinct().Where(t => !LanguageNeutral(t)))
        {
            foreach (var lang in Langs)
            {
                if (!Table(lang).ContainsKey(text))
                    missing.Add($"{lang}: {text}");
            }
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void Tabellen_SindVollstaendig() =>
        Assert.All(Langs, lang => Assert.Equal(Table("En").Count, Table(lang).Count));

    [Fact]
    public void Tastennamen_DerAndroidController_SindUebersetzt() =>
        AssertTranslated(ProfileEdit.Kinds.SelectMany(kind => ControllerButtons.For(kind).Select(b => ControllerButtons.Label(b, kind))));

    [Fact]
    public void Ziele_ImBelegungsEditor_SindUebersetzt() => AssertTranslated(ProfileEdit.Targets.Select(t => t.Label));
}
