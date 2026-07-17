using System.Windows;
using System.Windows.Media.Imaging;
using Switcher.App.Rendering;
using Switcher.App.Services;
using Switcher.Contracts;

namespace Switcher.App;

/// <summary>
/// Sub-operator source projector: a borderless, topmost, full-screen window mirroring the PGM output
/// (docs/specs/pc-switcher-app.md §2.3/§5). Fed from <see cref="FramePumpService.ProgramFrameReady"/>
/// rather than a dedicated Direct3D swap chain - <c>Switcher.VirtualCam</c>'s
/// <c>Direct3DSwapChainOutput</c> is internal to that assembly with no public factory (see README.md),
/// so App renders the physical full-screen output itself via WPF/<see cref="WriteableBitmap"/>.
/// </summary>
public partial class ProjectorWindow : Window
{
    private readonly FramePumpService _framePump;
    private WriteableBitmap? _bitmap;

    public ProjectorWindow(FramePumpService framePump)
    {
        InitializeComponent();
        _framePump = framePump;
        _framePump.ProgramFrameReady += OnProgramFrameReady;
        Closed += (_, _) => _framePump.ProgramFrameReady -= OnProgramFrameReady;
    }

    /// <summary>Positions this window full-screen on the given monitor (0-based, matching
    /// <see cref="System.Windows.Forms.Screen.AllScreens"/> order) and shows it.</summary>
    public void ShowOnDisplay(int displayIndex)
    {
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

    private void OnProgramFrameReady(object? sender, FrameData frame)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _bitmap = FrameBitmapWriter.Write(_bitmap, frame);
            ProgramImage.Source = _bitmap;
        });
    }
}
