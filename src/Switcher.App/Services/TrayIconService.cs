using System.Windows;
using Forms = System.Windows.Forms;

namespace Switcher.App.Services;

/// <summary>
/// Keeps the app resident in the notification area (docs/tasks/agent-A-004-app-integration.md step 3:
/// "常駐（トレイ）動作"). Closing the main window hides it instead of exiting; the tray menu is the
/// only way to actually quit, so the web host/tally/ATEM connection keep running while the operator's
/// window is minimized. The right-click menu also carries the operator-display move (requirement 2) and
/// multiview full-screen (requirement 4) sub-menus, each dynamically generated from
/// <see cref="Forms.Screen.AllScreens"/>.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayIconService(
        Window mainWindow,
        Action onExitRequested,
        Action<int> onMoveOperatorDisplay,
        Action<int> onMultiviewFullscreen)
    {
        ArgumentNullException.ThrowIfNull(onMoveOperatorDisplay);
        ArgumentNullException.ThrowIfNull(onMultiviewFullscreen);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Switcher.App",
        };

        var moveOperatorMenu = new Forms.ToolStripMenuItem("Move operator to");
        var multiviewMenu = new Forms.ToolStripMenuItem("Multiview full-screen");

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowWindow(mainWindow));
        menu.Items.Add(moveOperatorMenu);
        menu.Items.Add(multiviewMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => onExitRequested());

        // Repopulate the display sub-menus each time the menu opens so hot-plugged monitors are reflected.
        menu.Opening += (_, _) =>
        {
            PopulateDisplaySubmenu(moveOperatorMenu, onMoveOperatorDisplay);
            PopulateDisplaySubmenu(multiviewMenu, onMultiviewFullscreen);
        };

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowWindow(mainWindow);
    }

    private static void PopulateDisplaySubmenu(Forms.ToolStripMenuItem parent, Action<int> onDisplaySelected)
    {
        parent.DropDownItems.Clear();
        var screens = Forms.Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
        {
            var index = i;
            var bounds = screens[i].Bounds;
            var primary = screens[i].Primary ? " (Primary)" : string.Empty;
            var text = $"Display {index} — {bounds.Width}x{bounds.Height}{primary}";
            parent.DropDownItems.Add(text, null, (_, _) => onDisplaySelected(index));
        }
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
