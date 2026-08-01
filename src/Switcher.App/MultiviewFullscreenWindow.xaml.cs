using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Switcher.Contracts;
using Forms = System.Windows.Forms;

namespace Switcher.App;

/// <summary>
/// Independent multiview full-screen window (requirement 4, docs/specs/multiview-output-revision.md §2.4):
/// presents the composited multiview onto a chosen display, borderless/top-most with the cursor hidden,
/// dismissed with <c>Esc</c>. Since the libobs migration the native engine renders the live multiview
/// directly into this window's HWND via <see cref="IVideoEngine.StartDisplayOutput"/> (target
/// <c>MULTIVIEW</c>), so this window only owns the window/monitor placement and applies the current
/// layout to the engine on attach. This is a separate system from the routed output sinks.
/// </summary>
public partial class MultiviewFullscreenWindow : Window
{
    private const string Target = "MULTIVIEW";

    private readonly IVideoEngine _engine;
    private readonly Func<MultiviewLayout> _layoutProvider;
    private readonly int _displayIndex;

    public MultiviewFullscreenWindow(
        IVideoEngine engine,
        Func<MultiviewLayout> layoutProvider,
        int displayIndex)
    {
        InitializeComponent();

        _engine = engine;
        _layoutProvider = layoutProvider;
        _displayIndex = displayIndex;

        SourceInitialized += OnSourceInitialized;
        KeyDown += OnKeyDown;
        Closed += (_, _) => _engine.StopDisplayOutput(Target);
    }

    /// <summary>Positions this window full-screen on the configured display and shows it. The native
    /// display attach happens once the HWND exists, in <see cref="OnSourceInitialized"/>.</summary>
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
        _engine.ApplyMultiview(_layoutProvider());
        var hwnd = new WindowInteropHelper(this).Handle;
        _engine.StartDisplayOutput(Target, hwnd, _displayIndex);
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
