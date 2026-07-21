using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TranslatePopup.Interop;
using TranslatePopup.Services;

namespace TranslatePopup.UI;

public partial class SelectionButtonWindow : Window
{
    private const double OffsetDip = 8;

    private nint _hwnd;

    public event Action? TranslateRequested;

    public bool IsCaptured => TranslateButton.IsMouseCaptured;

    public SelectionButtonWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            WindowStyleHelper.MakeToolWindowNonActivating(_hwnd);
        };

        // Diagnostic only: if capture is lost without our own ReleaseMouseCapture() having run
        // first (logged separately in the Up handler), something external stole/cancelled it.
        TranslateButton.LostMouseCapture += (_, _) =>
            DiagnosticLog.Write("Button LostMouseCapture");
    }

    /// <summary>Shows (or repositions, if already visible) the button near the given raw screen point.</summary>
    public void ShowNear(int rawX, int rawY)
    {
        var dip = DpiHelper.PhysicalToDip(rawX, rawY);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(rawX, rawY));
        var workArea = screen.WorkingArea;
        var topLeftDip = DpiHelper.PhysicalToDip(workArea.Left, workArea.Top);
        var bottomRightDip = DpiHelper.PhysicalToDip(workArea.Right, workArea.Bottom);

        var left = Math.Clamp(dip.X + OffsetDip, topLeftDip.X, bottomRightDip.X - Width);
        var top = Math.Clamp(dip.Y + OffsetDip, topLeftDip.Y, bottomRightDip.Y - Height);

        Left = left;
        Top = top;

        if (!IsVisible)
        {
            Show();
        }

        // WPF's Topmost property can fail to re-assert HWND_TOPMOST z-order after another
        // topmost window (the translation window) opened and closed in between: the button then
        // renders on top visually but is not actually foremost for hit-testing, so clicks on it
        // get swallowed by whatever is really on top. Force it explicitly on every show.
        if (_hwnd != nint.Zero)
        {
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        DiagnosticLog.Write($"ShowNear raw=({rawX},{rawY}) dip=({dip.X:F1},{dip.Y:F1}) hwnd={_hwnd}");
    }

    public void HideButton()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    // A NOACTIVATE window's Button.Click (which relies on WPF's own mouse-capture bookkeeping)
    // does not fire reliably when the window never becomes active, so the down/up pair is handled
    // explicitly here instead, with our own capture and hit-test.
    private void TranslateButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        TranslateButton.CaptureMouse();
        DiagnosticLog.Write($"Button PreviewMouseLeftButtonDown captured={TranslateButton.IsMouseCaptured}");
        e.Handled = true;
    }

    private void TranslateButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (TranslateButton.IsMouseCaptured)
        {
            TranslateButton.ReleaseMouseCapture();
        }

        var position = e.GetPosition(TranslateButton);
        var isOverButton = position.X >= 0 && position.X <= TranslateButton.ActualWidth
            && position.Y >= 0 && position.Y <= TranslateButton.ActualHeight;

        DiagnosticLog.Write($"Button PreviewMouseLeftButtonUp pos=({position.X:F1},{position.Y:F1}) isOverButton={isOverButton}");

        e.Handled = true;

        if (isOverButton)
        {
            TranslateRequested?.Invoke();
        }
    }
}
