namespace TranslatePopup.Models;

public sealed record TranslationLanguage(string Code, string Name)
{
    public override string ToString() => Name;
}
