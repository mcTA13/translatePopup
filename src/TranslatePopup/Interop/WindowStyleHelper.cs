namespace TranslatePopup.Interop;

internal static class WindowStyleHelper
{
    /// <summary>Marks a window as non-activating and excluded from the taskbar/Alt+Tab at the Win32 level,
    /// as a backup to WPF's ShowInTaskbar (which alone does not prevent activation-on-show).</summary>
    public static void MakeToolWindowNonActivating(nint hwnd)
    {
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        var newStyle = exStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, newStyle);
    }
}
