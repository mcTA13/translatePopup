using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using TranslatePopup.Interop;
using Icon = System.Drawing.Icon;
using SystemIcons = System.Drawing.SystemIcons;

namespace TranslatePopup.Services;

public sealed class TrayIconManager : IDisposable
{
    private const nuint CommandSettings = 1;
    private const nuint CommandExit = 2;

    private readonly NotifyIcon _notifyIcon;
    private readonly Window _hiddenOwner;

    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIconManager()
    {
        // A real (if invisible) HWND is required so SetForegroundWindow has something to hand
        // activation to - without it, the native popup menu can fail to take focus and won't
        // close on an outside click, since NotifyIcon itself has no window of its own.
        _hiddenOwner = new Window
        {
            Width = 0,
            Height = 0,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Left = -10000,
            Top = -10000,
        };
        new WindowInteropHelper(_hiddenOwner).EnsureHandle();

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "TranslatePopup",
            Visible = true,
        };

        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                ShowTrayContextMenu();
            }
        };
        _notifyIcon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
    }

    // Uses the native Win32 popup menu (TrackPopupMenuEx) rather than a WinForms ContextMenuStrip
    // or a hand-styled WPF Popup, so the menu is rendered by the OS itself - the same standard
    // rounded/Mica-styled menu every other tray-icon app on Windows 11 gets, with no re-styling.
    private void ShowTrayContextMenu()
    {
        var hMenu = NativeMethods.CreatePopupMenu();
        try
        {
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, CommandSettings, "設定");
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, CommandExit, "終了");

            var hwnd = new WindowInteropHelper(_hiddenOwner).Handle;
            NativeMethods.SetForegroundWindow(hwnd);

            var cursor = System.Windows.Forms.Cursor.Position;
            var command = (nuint)NativeMethods.TrackPopupMenuEx(
                hMenu,
                NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON,
                cursor.X, cursor.Y,
                hwnd,
                nint.Zero);

            if (command == CommandSettings)
            {
                SettingsRequested?.Invoke();
            }
            else if (command == CommandExit)
            {
                ExitRequested?.Invoke();
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(hMenu);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _hiddenOwner.Close();
    }

    // Extracted from the exe itself (the same .ico embedded via <ApplicationIcon>), so the tray
    // icon and the exe's own icon are guaranteed to always be identical.
    private static Icon LoadAppIcon()
    {
        try
        {
            // ProcessPath points at the apphost .exe (which carries the embedded icon); the
            // assembly Location would instead point at the managed .dll, which has none.
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            }

            return Icon.ExtractAssociatedIcon(exePath) ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }
}
