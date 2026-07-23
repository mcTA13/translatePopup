namespace TranslatePopup.Interop;

/// <summary>Notifies when the foreground window changes (e.g. Alt+Tab), so callers can dismiss transient UI.</summary>
internal sealed class ForegroundWindowWatcher : IDisposable
{
    // Field, not local: SetWinEventHook does not root the delegate either.
    private readonly WinEventDelegate _proc;
    private nint _hookId;

    public event Action? ForegroundChanged;

    public ForegroundWindowWatcher()
    {
        _proc = OnWinEvent;
    }

    /// <summary>Installs the hook. Returns false if installation failed.</summary>
    public bool Start()
    {
        _hookId = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            nint.Zero,
            _proc,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);
        return _hookId != nint.Zero;
    }

    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        ForegroundChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_hookId != nint.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookId);
            _hookId = nint.Zero;
        }
    }
}
