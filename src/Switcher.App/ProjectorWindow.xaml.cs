using System.Windows;
using System.Windows.Interop;
using Switcher.App.Configuration;
using Switcher.Contracts;
using Switcher.VirtualCam;
using Switcher.VirtualCam.Display;

namespace Switcher.App;

/// <summary>
/// Sub-operator source projector: a borderless, topmost, full-screen window mirroring whichever PGM
/// bus the operator assigned to the HDMI output sink (docs/specs/pc-switcher-app.md §2.3/§5). Presents
/// via <see cref="IHdmiFullscreenOutput"/>, attaching this window's native HWND to it on
/// <see cref="OnSourceInitialized"/> - <see cref="FramePumpService"/>'s <c>OutputRouter.RouteFrame</c>
/// call then drives <see cref="IHdmiFullscreenOutput.Present"/> directly, so this window has no frame
/// handling of its own beyond owning the window/monitor placement and mouse-cursor policy
/// (docs/tasks/agent-A2-006-app-integration-v2.md step 5).
/// </summary>
public partial class ProjectorWindow : Window
{
    private readonly IHdmiFullscreenOutput _hdmiOutput;
    private readonly OutputRouter _outputRouter;
    private readonly AppConfig _config;

    public ProjectorWindow(IHdmiFullscreenOutput hdmiOutput, OutputRouter outputRouter, AppConfig config)
    {
        InitializeComponent();
        _hdmiOutput = hdmiOutput;
        _outputRouter = outputRouter;
        _config = config;

        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => _hdmiOutput.Detach();
    }

    /// <summary>Positions this window full-screen on the display currently assigned to the HDMI output
    /// sink (falling back to <see cref="AppConfig.ProjectorDisplayIndex"/> if none has been configured
    /// yet) and shows it. The actual DXGI attach happens once the native HWND exists, in
    /// <see cref="OnSourceInitialized"/>.</summary>
    public void ShowOnConfiguredDisplay()
    {
        var assignment = _outputRouter.CurrentAssignments.FirstOrDefault(a => a.Sink == OutputSink.Hdmi);
        var displayIndex = assignment?.DisplayId ?? _config.ProjectorDisplayIndex;

        var screens = System.Windows.Forms.Screen.AllScreens;
        var screen = displayIndex >= 0 && displayIndex < screens.Length ? screens[displayIndex] : screens[0];

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = screen.Bounds.Left;
        Top = screen.Bounds.Top;
        Width = screen.Bounds.Width;
        Height = screen.Bounds.Height;
        WindowState = WindowState.Normal;
        Show();
        WindowState = WindowState.Maximized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var assignment = _outputRouter.CurrentAssignments.FirstOrDefault(a => a.Sink == OutputSink.Hdmi);
        var displayIndex = assignment?.DisplayId ?? _config.ProjectorDisplayIndex;
        var hideCursor = assignment?.HideCursor ?? true;
        var fullscreen = assignment?.Fullscreen ?? true;

        var hwnd = new WindowInteropHelper(this).Handle;
        _hdmiOutput.Attach(displayIndex, hwnd, hideCursor, fullscreen);
    }
}
