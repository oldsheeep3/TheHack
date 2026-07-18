using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Switcher.App.Services;
using Switcher.Contracts;
using Switcher.Media;
using Switcher.VirtualCam.Display;
using Forms = System.Windows.Forms;

namespace Switcher.App;

/// <summary>
/// Independent multiview full-screen window (requirement 4, docs/specs/multiview-output-revision.md §2.4):
/// presents the composited multiview onto a chosen display, borderless/top-most with the cursor hidden,
/// dismissed with <c>Esc</c>. Presentation reuses the exact HDMI stack via
/// <see cref="IFullscreenPresenterFactory"/> (no duplicated swap-chain/cursor code); this window only
/// owns the window/monitor placement and pulls a fresh <see cref="CompositorEngine.GetMultiviewFrame"/>
/// on every <see cref="FramePumpService.Tick"/> so the view stays live while full-screen. Multiview
/// full-screen is a separate system from <see cref="Switcher.VirtualCam.OutputRouter"/>'s routed sinks.
/// </summary>
public partial class MultiviewFullscreenWindow : Window
{
    private readonly IHdmiFullscreenOutput _presenter;
    private readonly CompositorEngine _compositor;
    private readonly FramePumpService _framePump;
    private readonly Func<MultiviewLayout> _layoutProvider;
    private readonly int _displayIndex;

    public MultiviewFullscreenWindow(
        IFullscreenPresenterFactory presenterFactory,
        CompositorEngine compositor,
        FramePumpService framePump,
        Func<MultiviewLayout> layoutProvider,
        int displayIndex)
    {
        InitializeComponent();

        _presenter = presenterFactory.Create();
        _compositor = compositor;
        _framePump = framePump;
        _layoutProvider = layoutProvider;
        _displayIndex = displayIndex;

        SourceInitialized += OnSourceInitialized;
        KeyDown += OnKeyDown;
        _framePump.Tick += OnTick;

        Closed += (_, _) =>
        {
            _framePump.Tick -= OnTick;
            _presenter.Detach();
            _presenter.Dispose();
        };
    }

    /// <summary>Positions this window full-screen on the configured display and shows it. The DXGI
    /// attach happens once the native HWND exists, in <see cref="OnSourceInitialized"/>.</summary>
    public void ShowFullscreen()
    {
        var screens = Forms.Screen.AllScreens;
        var screen = _displayIndex >= 0 && _displayIndex < screens.Length ? screens[_displayIndex] : screens[0];

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
        var hwnd = new WindowInteropHelper(this).Handle;
        _presenter.Attach(_displayIndex, hwnd, hideCursor: true, fullscreen: true);
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void OnTick(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (!_presenter.IsAttached)
            {
                return;
            }

            try
            {
                _presenter.Present(_compositor.GetMultiviewFrame(_layoutProvider()));
            }
            catch (Exception)
            {
                // Presentation depends on a Direct3D 11 runtime and the swap chain currently attached;
                // a transient failure must not tear the window down (it retries next tick).
            }
        });
}
