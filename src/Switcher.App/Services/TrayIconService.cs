using System.Windows;
using Forms = System.Windows.Forms;

namespace Switcher.App.Services;

/// <summary>
/// Keeps the app resident in the notification area (docs/tasks/agent-A-004-app-integration.md step 3:
/// "常駐（トレイ）動作"). Closing the main window hides it instead of exiting; the tray menu is the
/// only way to actually quit, so the web host/tally/ATEM connection keep running while the operator's
/// window is minimized.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayIconService(Window mainWindow, Action onExitRequested)
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Switcher.App",
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowWindow(mainWindow));
        menu.Items.Add("Exit", null, (_, _) => onExitRequested());
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowWindow(mainWindow);
    }

    private static void ShowWindow(Window window)
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
