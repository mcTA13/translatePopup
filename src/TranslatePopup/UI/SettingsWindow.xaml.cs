using System.Windows;
using TranslatePopup.Models;
using TranslatePopup.Services;

namespace TranslatePopup.UI;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly TranslationService _translationService;
    private AppSettings _settings;

    public SettingsWindow(SettingsService settingsService, TranslationService translationService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _translationService = translationService;
        _settings = _settingsService.Load();

        ApiKeyTextBox.Text = _settings.ApiKey;
        RegionTextBox.Text = _settings.Region;

        Loaded += SettingsWindow_Loaded;
    }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        StatusTextBlock.Text = "言語一覧を取得中...";
        var languages = await _translationService.GetSupportedLanguagesAsync();
        DefaultLanguageComboBox.ItemsSource = languages;
        DefaultLanguageComboBox.SelectedItem = languages.FirstOrDefault(l => l.Code == _settings.DefaultTargetLanguage)
            ?? languages.FirstOrDefault();
        StatusTextBlock.Text = string.Empty;
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        TestConnectionButton.IsEnabled = false;
        StatusTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
        StatusTextBlock.Text = "接続確認中...";

        try
        {
            var targetCode = (DefaultLanguageComboBox.SelectedItem as TranslationLanguage)?.Code ?? "en";
            var result = await _translationService.TranslateAsync(
                "Hello", targetCode, ApiKeyTextBox.Text.Trim(), RegionTextBox.Text.Trim());

            StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
            StatusTextBlock.Text = $"接続に成功しました。テスト結果: {result.TranslatedText}";
        }
        catch (TranslationException ex)
        {
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            StatusTextBlock.Text = ex.Message;
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Reload rather than saving the possibly-stale `_settings` captured when this window
        // opened - otherwise fields another window wrote in the meantime (e.g. the translation
        // window's remembered size) would be silently reverted by this save.
        var current = _settingsService.Load();
        current.ApiKey = ApiKeyTextBox.Text.Trim();
        current.Region = RegionTextBox.Text.Trim();
        current.DefaultTargetLanguage = (DefaultLanguageComboBox.SelectedItem as TranslationLanguage)?.Code
            ?? current.DefaultTargetLanguage;

        _settingsService.Save(current);
        _settings = current;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
