using TranslatePopup.Interop;
using TranslatePopup.Services;
using TranslatePopup.UI;

namespace TranslatePopup.Selection;

/// <summary>
/// Orchestrates global selection detection: interprets raw mouse-hook events as a "text selection" gesture,
/// copies the selection via a synthesized Ctrl+C, restores the clipboard, and drives the floating translate button.
/// </summary>
public sealed class SelectionWatcher : IDisposable
{
    private const double DragThresholdPx = 6;
    private const double DoubleClickDistancePx = 10;
    private static readonly TimeSpan ClipboardChangeTimeout = TimeSpan.FromMilliseconds(300);

    private readonly MouseHook _mouseHook = new();
    private readonly ForegroundWindowWatcher _foregroundWatcher = new();
    private readonly SelectionButtonWindow _buttonWindow;

    private MouseHookEvent? _downEvt;
    private bool _downShift;
    private bool _downIsDoubleClick;
    private MouseHookEvent? _lastDown;
    private DateTime _lastDownTimeUtc;

    private string? _pendingText;
    private (int X, int Y) _pendingPoint;

    /// <summary>Raised when the user clicks the floating translate button. Carries the selected text and the
    /// screen point (physical pixels) where the selection was made.</summary>
    public event Action<string, int, int>? SelectionButtonClicked;

    public SelectionWatcher(SelectionButtonWindow buttonWindow)
    {
        _buttonWindow = buttonWindow;
        _buttonWindow.TranslateRequested += OnTranslateRequested;

        _mouseHook.LeftButtonDown += OnLeftButtonDown;
        _mouseHook.LeftButtonUp += OnLeftButtonUp;
        _foregroundWatcher.ForegroundChanged += OnForegroundChanged;
    }

    private void OnForegroundChanged()
    {
        DiagnosticLog.Write($"[hook] ForegroundChanged buttonVisible={_buttonWindow.IsVisible} captured={_buttonWindow.IsCaptured}");

        // Hiding the button while it currently owns mouse capture (i.e. mid-click) would silently
        // cancel the pending click - WPF never delivers the matching Up event to a hidden window's
        // element, so the user's click just vanishes. Only hide when nothing is in-flight.
        if (!_buttonWindow.IsCaptured)
        {
            _buttonWindow.HideButton();
        }
    }

    public void Start()
    {
        _mouseHook.Start();
        _foregroundWatcher.Start();
    }

    private bool IsWithinButtonBounds(System.Windows.Point dip)
    {
        if (!_buttonWindow.IsVisible)
        {
            return false;
        }

        return dip.X >= _buttonWindow.Left && dip.X <= _buttonWindow.Left + _buttonWindow.Width
            && dip.Y >= _buttonWindow.Top && dip.Y <= _buttonWindow.Top + _buttonWindow.Height;
    }

    private void OnLeftButtonDown(MouseHookEvent evt, bool shiftPressed)
    {
        _downIsDoubleClick = _lastDown.HasValue
            && (DateTime.UtcNow - _lastDownTimeUtc).TotalMilliseconds <= NativeMethods.GetDoubleClickTime()
            && Distance(_lastDown.Value, evt) <= DoubleClickDistancePx;

        _downEvt = evt;
        _downShift = shiftPressed;
        _lastDown = evt;
        _lastDownTimeUtc = DateTime.UtcNow;

        var withinBounds = IsWithinButtonBounds(evt.DipPoint);
        if (_buttonWindow.IsVisible)
        {
            DiagnosticLog.Write($"[hook] Down dip=({evt.DipPoint.X:F1},{evt.DipPoint.Y:F1}) withinBounds={withinBounds}");
        }

        // A click that isn't on the translate button clears any previously shown selection immediately.
        if (!withinBounds)
        {
            _buttonWindow.HideButton();
        }
    }

    private void OnLeftButtonUp(MouseHookEvent evt)
    {
        // Always reset regardless of outcome below, so a click on the button never leaves stale
        // down-state around to skew the next unrelated gesture's drag-distance calculation.
        var down = _downEvt;
        _downEvt = null;

        var withinBounds = IsWithinButtonBounds(evt.DipPoint);
        if (_buttonWindow.IsVisible)
        {
            DiagnosticLog.Write($"[hook] Up dip=({evt.DipPoint.X:F1},{evt.DipPoint.Y:F1}) withinBounds={withinBounds}");
        }

        if (withinBounds)
        {
            // Let the button's own handler deal with this; not a selection gesture.
            return;
        }

        if (down is not { } downValue)
        {
            return;
        }

        var isDrag = Distance(downValue, evt) > DragThresholdPx;
        var looksLikeSelection = isDrag || _downShift || _downIsDoubleClick;
        if (!looksLikeSelection)
        {
            return;
        }

        // Defer to a fresh dispatcher tick: SendInput must not be issued while still inside the
        // low-level hook's own call stack (before CallNextHookEx returns control), otherwise the
        // synthesized Ctrl+C races the real button-up message and can silently fail to copy anything.
        _buttonWindow.Dispatcher.BeginInvoke(new Action(() => _ = HandleSelectionAsync(downValue, evt)));
    }

    private async Task HandleSelectionAsync(MouseHookEvent downEvt, MouseHookEvent upEvt)
    {
        try
        {
            var seqBefore = ClipboardHelper.GetSequenceNumber();
            var snapshot = await ClipboardHelper.TrySnapshotAsync();

            InputSimulator.SendCtrlC();
            var changed = await ClipboardHelper.WaitForChangeAsync(seqBefore, ClipboardChangeTimeout);

            string? text = null;
            if (changed)
            {
                text = await ClipboardHelper.TryGetTextAsync();
            }

            await ClipboardHelper.RestoreAsync(snapshot);

            text = text?.Trim();
            DiagnosticLog.Write($"HandleSelectionAsync changed={changed} text.len={text?.Length ?? -1}");
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            // Anchor on the bottom-right corner of the drag rectangle rather than wherever the
            // mouse happened to be released, so the button consistently lands just past the end
            // of the selected text regardless of drag direction (word double-click/shift-click,
            // where down and up are effectively the same point, just anchor on that point).
            var anchorX = Math.Max(downEvt.RawX, upEvt.RawX);
            var anchorY = Math.Max(downEvt.RawY, upEvt.RawY);

            _pendingText = text;
            _pendingPoint = (anchorX, anchorY);
            _buttonWindow.ShowNear(anchorX, anchorY);
        }
        catch
        {
            // Best-effort: selection detection failing should never crash the app.
        }
    }

    private void OnTranslateRequested()
    {
        DiagnosticLog.Write($"OnTranslateRequested pendingText.len={_pendingText?.Length ?? -1} pendingPoint={_pendingPoint}");

        if (!string.IsNullOrEmpty(_pendingText))
        {
            SelectionButtonClicked?.Invoke(_pendingText, _pendingPoint.X, _pendingPoint.Y);
        }

        _buttonWindow.HideButton();
    }

    private static double Distance(MouseHookEvent a, MouseHookEvent b)
    {
        var dx = a.RawX - b.RawX;
        var dy = a.RawY - b.RawY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public void Dispose()
    {
        _mouseHook.Dispose();
        _foregroundWatcher.Dispose();
    }
}
