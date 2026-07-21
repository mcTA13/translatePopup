using Point = System.Windows.Point;

namespace TranslatePopup.Interop;

internal static class DpiHelper
{
    /// <summary>Converts a physical-pixel screen point (as reported by WH_MOUSE_LL) into WPF DIP coordinates.</summary>
    public static Point PhysicalToDip(int x, int y)
    {
        var point = new POINT { X = x, Y = y };
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);

        double scale = 1.0;
        if (monitor != nint.Zero &&
            NativeMethods.GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dpiX, out _) == 0)
        {
            scale = dpiX / 96.0;
        }

        return new Point(x / scale, y / scale);
    }
}
