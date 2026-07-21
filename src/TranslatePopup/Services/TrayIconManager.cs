using System.Drawing;
using System.Windows.Forms;

namespace TranslatePopup.Services;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIconManager()
    {
        var settingsItem = new ToolStripMenuItem("設定");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        var exitItem = new ToolStripMenuItem("終了");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "TranslatePopup",
            Visible = true,
            ContextMenuStrip = contextMenu,
        };

        _notifyIcon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
