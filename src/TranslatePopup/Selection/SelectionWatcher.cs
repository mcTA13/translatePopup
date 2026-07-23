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
        if (!_mouseHook.Start())
        {
            DiagnosticLog.Write("SelectionWatcher.Start: failed to install the global mouse hook - selection detection will not work.");
        }

        if (!_foregroundWatcher.Start())
        {
            DiagnosticLog.Write("SelectionWatcher.Start: failed to install the foreground-window watcher.");
        }
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

        // A click that isn't on the translate button clears any previously shown selection immediately.
        if (!IsWithinButtonBounds(evt.DipPoint))
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

        if (IsWithinButtonBounds(evt.DipPoint))
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
            // Checked against where the gesture started (mouse-down), not where it ended: a
            // normal drag-select routinely finishes with the mouse released past the source
            // window's edge (near a border, or dragging onto another monitor), and that must
            // still count as a real selection. What actually distinguishes a genuine selection
            // from e.g. triple-clicking empty desktop space while some background editor still
            // holds an old, never-cleared selection is whether the click *began* inside the
            // window that currently has keyboard focus - that's where our synthesized Ctrl+C
            // will actually go.
            if (!IsClickInsideFocusedWindow(downEvt))
            {
                return;
            }

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
        if (!string.IsNullOrEmpty(_pendingText))
        {
            SelectionButtonClicked?.Invoke(_pendingText, _pendingPoint.X, _pendingPoint.Y);
        }

        _buttonWindow.HideButton();
    }

    private static bool IsClickInsideFocusedWindow(MouseHookEvent evt)
    {
        var point = new POINT { X = evt.RawX, Y = evt.RawY };
        var hwndAtPoint = NativeMethods.WindowFromPoint(point);
        if (hwndAtPoint == nint.Zero)
        {
            return false;
        }

        var rootAtPoint = NativeMethods.GetAncestor(hwndAtPoint, NativeMethods.GA_ROOT);
        var foreground = NativeMethods.GetForegroundWindow();
        return rootAtPoint != nint.Zero && rootAtPoint == foreground;
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
