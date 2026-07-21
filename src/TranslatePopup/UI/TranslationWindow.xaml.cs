using System.Windows;
using TranslatePopup.Models;
using TranslatePopup.Services;

namespace TranslatePopup.UI;

public partial class TranslationWindow : Window
{
    private readonly TranslationService _translationService;
    private readonly SettingsService _settingsService;
    private readonly string _sourceText;
    private bool _isReady;

    public TranslationWindow(
        TranslationService translationService,
        SettingsService settingsService,
        IReadOnlyList<TranslationLanguage> languages,
        string sourceText)
    {
        InitializeComponent();
        _translationService = translationService;
        _settingsService = settingsService;
        _sourceText = sourceText;

        var settings = _settingsService.Load();
        TargetLanguageComboBox.ItemsSource = languages;
        TargetLanguageComboBox.SelectedItem =
            languages.FirstOrDefault(l => l.Code == settings.DefaultTargetLanguage)
            ?? languages.FirstOrDefault();
        _isReady = true;

        Loaded += (_, _) => _ = TranslateAsync();
    }

    public void SetScreenPosition(double left, double top)
    {
        Left = left;
        Top = top;
    }

    private async void TargetLanguageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isReady)
        {
            await TranslateAsync();
        }
    }

    private async Task TranslateAsync()
    {
        var language = TargetLanguageComboBox.SelectedItem as TranslationLanguage;
        if (language is null)
        {
            return;
        }

        ResultTextBlock.Text = string.Empty;
        StatusTextBlock.Text = "翻訳中...";

        var settings = _settingsService.Load();
        try
        {
            var result = await _translationService.TranslateAsync(
                _sourceText, language.Code, settings.ApiKey, settings.Region);

            StatusTextBlock.Text = string.Empty;
            ResultTextBlock.Text = result.TranslatedText;
        }
        catch (TranslationException ex)
        {
            StatusTextBlock.Text = ex.Message;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
