namespace TranslatePopup.Models;

public sealed class AppSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string DefaultTargetLanguage { get; set; } = "ja";
    public double? TranslationWindowWidth { get; set; }
    public double? TranslationWindowHeight { get; set; }
}
