using System.Windows;
using TranslatePopup.Interop;
using TranslatePopup.Models;
using TranslatePopup.Services;

namespace TranslatePopup.UI;

public partial class TranslationWindow : Window
{
    private readonly TranslationService _translationService;
    private readonly SettingsService _settingsService;
    private readonly string _sourceText;
    private bool _isReady;
    private CancellationTokenSource? _translateCts;

    public event Action? SettingsRequested;

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

        if (settings.TranslationWindowWidth is > 0 && settings.TranslationWindowHeight is > 0)
        {
            Width = settings.TranslationWindowWidth.Value;
            Height = settings.TranslationWindowHeight.Value;
        }

        Loaded += (_, _) => _ = TranslateAsync();
        Closing += (_, _) => SaveWindowSize();
    }

    private void SaveWindowSize()
    {
        double width, height;
        if (WindowState == WindowState.Normal)
        {
            width = ActualWidth;
            height = ActualHeight;
        }
        else
        {
            width = RestoreBounds.Width;
            height = RestoreBounds.Height;
        }

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var current = _settingsService.Load();
        current.TranslationWindowWidth = width;
        current.TranslationWindowHeight = height;
        _settingsService.Save(current);
    }

    /// <summary>Positions the window near the given raw (physical-pixel) screen point, clamping
    /// its remembered size and position to fit the screen it will actually appear on - a size
    /// saved on a larger external monitor should not leave the window partly off-screen when
    /// reopened on a smaller display.</summary>
    public void SetScreenPosition(int rawX, int rawY)
    {
        var dip = DpiHelper.PhysicalToDip(rawX, rawY);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(rawX, rawY));
        var workArea = screen.WorkingArea;
        var topLeftDip = DpiHelper.PhysicalToDip(workArea.Left, workArea.Top);
        var bottomRightDip = DpiHelper.PhysicalToDip(workArea.Right, workArea.Bottom);

        Width = Math.Min(Width, Math.Max(MinWidth, bottomRightDip.X - topLeftDip.X));
        Height = Math.Min(Height, Math.Max(MinHeight, bottomRightDip.Y - topLeftDip.Y));

        var maxLeft = Math.Max(topLeftDip.X, bottomRightDip.X - Width);
        var maxTop = Math.Max(topLeftDip.Y, bottomRightDip.Y - Height);
        Left = Math.Clamp(dip.X, topLeftDip.X, maxLeft);
        Top = Math.Clamp(dip.Y, topLeftDip.Y, maxTop);
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

        // Switching languages again before a request finishes must not let that older response
        // land after the newer one and overwrite it - cancel it rather than just racing them.
        _translateCts?.Cancel();
        var cts = new CancellationTokenSource();
        _translateCts = cts;

        ResultTextBlock.Text = string.Empty;
        StatusTextBlock.Text = "翻訳中...";

        var settings = _settingsService.Load();
        try
        {
            var result = await _translationService.TranslateAsync(
                _sourceText, language.Code, settings.ApiKey, settings.Region, cts.Token);

            StatusTextBlock.Text = string.Empty;
            ResultTextBlock.Text = result.TranslatedText;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer language selection; that call owns the UI update instead.
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

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke();
    }
}
