using System.Threading;
using System.Windows;
using TranslatePopup.Models;
using TranslatePopup.Selection;
using TranslatePopup.Services;
using TranslatePopup.UI;
using Application = System.Windows.Application;

namespace TranslatePopup;

public partial class App : Application
{
    // Fixed GUID so the name can't collide with anything else on the machine.
    private const string SingleInstanceMutexName = @"Global\TranslatePopup-9F3B2C7A-4E1D-4A6B-9C3D-2F8E7A1B5C4D";

    private Mutex? _singleInstanceMutex;
    private SettingsService _settingsService = null!;
    private TranslationService _translationService = null!;
    private TrayIconManager _trayIconManager = null!;
    private SelectionButtonWindow _buttonWindow = null!;
    private SelectionWatcher _selectionWatcher = null!;
    private SettingsWindow? _settingsWindow;
    private TranslationWindow? _translationWindow;
    private IReadOnlyList<TranslationLanguage>? _languagesCache;
    private bool _isHandlingSelectionClick;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "TranslatePopup は既に起動しています。タスクトレイのアイコンをご確認ください。",
                "TranslatePopup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

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
        // The only await below (the first-ever language-list fetch) creates a window where a
        // second selection+click could re-enter this method before the first call has finished
        // setting up _translationWindow, letting two windows race for that field. Once cached,
        // everything past that point is synchronous, so this guard only ever matters once.
        if (_isHandlingSelectionClick)
        {
            return;
        }

        _isHandlingSelectionClick = true;
        try
        {
            var settings = _settingsService.Load();
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                OpenSettings();
                return;
            }

            _languagesCache ??= await _translationService.GetSupportedLanguagesAsync();

            // Only one translation window at a time: the previous one is superseded, not stacked.
            _translationWindow?.Close();

            var window = new TranslationWindow(_translationService, _settingsService, _languagesCache, text);
            window.SettingsRequested += OpenSettings;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_translationWindow, window))
                {
                    _translationWindow = null;
                }
            };
            _translationWindow = window;
            window.SetScreenPosition(rawX, rawY);
            window.Show();
            window.Activate();
        }
        finally
        {
            _isHandlingSelectionClick = false;
        }
    }

    private void ExitApplication()
    {
        _selectionWatcher.Dispose();
        _trayIconManager.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Shutdown();
    }
}

