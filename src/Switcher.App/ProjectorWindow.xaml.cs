using System.Windows;
using System.Windows.Interop;
using Switcher.App.Configuration;
using Switcher.Contracts;

namespace Switcher.App;

/// <summary>
/// Sub-operator source projector: a borderless, topmost, full-screen window mirroring whichever PGM
/// bus the operator assigned to one HDMI output sink (docs/specs/pc-switcher-app.md §2.3/§5). Since the
/// libobs migration the native engine renders directly into this window's HWND via
/// <see cref="IVideoEngine.StartDisplayOutput"/>, so this window only owns the window/monitor placement
/// and mouse-cursor policy - it does no frame handling of its own.
/// <para>
/// The window is per <see cref="OutputSink"/> rather than per app: an operator may configure up to
/// <see cref="OutputCatalog.MaxSinksPerKind"/> HDMI sinks, each on its own display, and each needs its own
/// window. That also decides the native target string — <c>HDMI1</c>/<c>HDMI2</c>/<c>HDMI3</c> from
/// <see cref="OutputCatalog.TokenOf(OutputSink)"/>, not <c>PGM1</c>/<c>PGM2</c>. Two HDMI sinks may carry
/// the same program bus, and a bus-named target would make both windows the same display inside the
/// engine, with the second attach replacing the first; the engine resolves the sink token to whichever
/// bus that sink currently carries.
/// </para>
/// </summary>
public partial class ProjectorWindow : Window
{
    private readonly IVideoEngine _engine;
    private readonly AppConfig _config;
    private string? _startedTarget;

    public ProjectorWindow(OutputSink sink, IVideoEngine engine, AppConfig config)
    {
        InitializeComponent();
        Sink = sink;
        _engine = engine;
        _config = config;

        // Several projectors can be open at once, so each says which sink it presents - in the taskbar
        // preview and in any window-picker the operator is screen-sharing through.
        Title = $"Source Projector - {OutputCatalog.LabelOf(OutputCatalog.KindOf(sink))} {OutputCatalog.OrdinalOf(sink)}";

        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) =>
        {
            if (_startedTarget is { } target)
            {
                _engine.StopDisplayOutput(target);
            }
        };
    }

    /// <summary>Which output sink this window presents; the owning window keys its open projectors on
    /// it so each HDMI sink can be opened and closed independently.</summary>
    public OutputSink Sink { get; }

    /// <summary>Positions this window full-screen on the display currently assigned to its sink (falling
    /// back to <see cref="AppConfig.ProjectorDisplayIndex"/> if none has been configured yet) and shows
    /// it. The native display attach happens once the HWND exists, in
    /// <see cref="OnSourceInitialized"/>.</summary>
    public void ShowOnConfiguredDisplay()
    {
        var displayIndex = ConfiguredDisplayIndex();

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

    private int ConfiguredDisplayIndex() =>
        _engine.CurrentAssignments.FirstOrDefault(a => a.Sink == Sink)?.DisplayId ?? _config.ProjectorDisplayIndex;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var target = OutputCatalog.TokenOf(Sink);
        var hwnd = new WindowInteropHelper(this).Handle;
        _engine.StartDisplayOutput(target, hwnd, ConfiguredDisplayIndex());
        _startedTarget = target;
    }
}
