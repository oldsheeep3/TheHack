using System.Windows;
using System.Windows.Interop;
using Switcher.App.Configuration;
using Switcher.Contracts;

namespace Switcher.App;

/// <summary>
/// Sub-operator source projector: a borderless, topmost, full-screen window mirroring whichever PGM
/// bus the operator assigned to the HDMI output sink (docs/specs/pc-switcher-app.md §2.3/§5). Since the
/// libobs migration the native engine renders directly into this window's HWND via
/// <see cref="IVideoEngine.StartDisplayOutput"/>, so this window only owns the window/monitor placement
/// and mouse-cursor policy - it does no frame handling of its own.
/// </summary>
public partial class ProjectorWindow : Window
{
    private readonly IVideoEngine _engine;
    private readonly AppConfig _config;
    private string? _startedTarget;

    public ProjectorWindow(IVideoEngine engine, AppConfig config)
    {
        InitializeComponent();
        _engine = engine;
        _config = config;

        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) =>
        {
            if (_startedTarget is { } target)
            {
                _engine.StopDisplayOutput(target);
            }
        };
    }

    /// <summary>Positions this window full-screen on the display currently assigned to the HDMI output
    /// sink (falling back to <see cref="AppConfig.ProjectorDisplayIndex"/> if none has been configured
    /// yet) and shows it. The native display attach happens once the HWND exists, in
    /// <see cref="OnSourceInitialized"/>.</summary>
    public void ShowOnConfiguredDisplay()
    {
        var assignment = _engine.CurrentAssignments.FirstOrDefault(a => a.Sink == OutputSink.Hdmi);
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
        var assignment = _engine.CurrentAssignments.FirstOrDefault(a => a.Sink == OutputSink.Hdmi);
        var displayIndex = assignment?.DisplayId ?? _config.ProjectorDisplayIndex;
        var target = assignment?.Source == OutputSource.Pgm2 ? "PGM2" : "PGM1";

        var hwnd = new WindowInteropHelper(this).Handle;
        _engine.StartDisplayOutput(target, hwnd, displayIndex);
        _startedTarget = target;
    }
}
