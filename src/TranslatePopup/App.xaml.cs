using System.Windows;
using TranslatePopup.Interop;
using TranslatePopup.Models;
using TranslatePopup.Selection;
using TranslatePopup.Services;
using TranslatePopup.UI;
using Application = System.Windows.Application;

namespace TranslatePopup;

public partial class App : Application
{
    private SettingsService _settingsService = null!;
    private TranslationService _translationService = null!;
    private TrayIconManager _trayIconManager = null!;
    private SelectionButtonWindow _buttonWindow = null!;
    private SelectionWatcher _selectionWatcher = null!;
    private SettingsWindow? _settingsWindow;
    private IReadOnlyList<TranslationLanguage>? _languagesCache;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Without this, an exception anywhere in an async-void handler (selection click, settings
        // load, etc.) would otherwise tear the whole process down silently - there's no visible
        // main window, so a crash would just look like "the app stopped responding to clicks".
        DispatcherUnhandledException += (_, args) =>
        {
            Services.DiagnosticLog.Write($"UNHANDLED EXCEPTION: {args.Exception}");
            System.Diagnostics.Debug.WriteLine($"[TranslatePopup] Unhandled exception: {args.Exception}");
            args.Handled = true;
        };

        // No StartupUri and no visible MainWindow: the app lives entirely in the tray until a
        // selection/settings window is opened, so it must not shut down when a window closes.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settingsService = new SettingsService();
        _translationService = new TranslationService();

        _trayIconManager = new TrayIconManager();
        _trayIconManager.SettingsRequested += OpenSettings;
        _trayIconManager.ExitRequested += ExitApplication;

        _buttonWindow = new SelectionButtonWindow();
        _selectionWatcher = new SelectionWatcher(_buttonWindow);
        _selectionWatcher.SelectionButtonClicked += OnSelectionButtonClicked;
        _selectionWatcher.Start();

        var settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            OpenSettings();
        }
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settingsService, _translationService);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private async void OnSelectionButtonClicked(string text, int rawX, int rawY)
    {
        Services.DiagnosticLog.Write($"OnSelectionButtonClicked text.len={text.Length} point=({rawX},{rawY})");

        var settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            OpenSettings();
            return;
        }

        _languagesCache ??= await _translationService.GetSupportedLanguagesAsync();

        var dip = DpiHelper.PhysicalToDip(rawX, rawY);
        var window = new TranslationWindow(_translationService, _settingsService, _languagesCache, text);
        window.SetScreenPosition(dip.X, dip.Y);
        window.Show();
        window.Activate();

        Services.DiagnosticLog.Write("OnSelectionButtonClicked window shown");
    }

    private void ExitApplication()
    {
        _selectionWatcher.Dispose();
        _trayIconManager.Dispose();
        Shutdown();
    }
}

