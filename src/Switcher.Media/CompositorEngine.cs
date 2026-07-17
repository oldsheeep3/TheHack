using System.Collections.Concurrent;
using Switcher.Contracts;
using Switcher.Media.Compositing;

namespace Switcher.Media;

/// <summary>
/// Composites PGM/PVW outputs from input sources, applies PiP layout, and handles TAKE
/// (docs/specs/pc-switcher-app.md §2.2). <see cref="ApplyPipSettings"/> edits the Preview scene;
/// <see cref="Take"/> promotes the current Preview scene to Program, matching the spec's "Take() で
/// PVW→PGM 切替". Layout math (<see cref="PipLayoutCalculator"/>) and GPU rendering
/// (<see cref="IGpuCompositor"/>) are both pure/abstracted so this class is unit testable without a
/// GStreamer/DirectX runtime.
/// </summary>
public sealed class CompositorEngine : ICompositorEngine, IDisposable
{
    public const int DefaultCanvasWidth = 1920;
    public const int DefaultCanvasHeight = 1080;

    private readonly IFrameSource _frameSource;
    private readonly IGpuCompositor _gpuCompositor;
    private readonly int _canvasWidth;
    private readonly int _canvasHeight;

    private readonly object _sceneLock = new();
    private readonly ConcurrentDictionary<int, PipSettings> _previewSettings = new();
    private IReadOnlyList<CompositedLayer> _previewScene = [];
    private IReadOnlyList<CompositedLayer> _programScene = [];

    public CompositorEngine(InputSourceManager sourceManager, int canvasWidth = DefaultCanvasWidth, int canvasHeight = DefaultCanvasHeight)
        : this((IFrameSource)sourceManager, new DirectX11Compositor(), canvasWidth, canvasHeight)
    {
    }

    internal CompositorEngine(IFrameSource frameSource, IGpuCompositor gpuCompositor, int canvasWidth = DefaultCanvasWidth, int canvasHeight = DefaultCanvasHeight)
    {
        _frameSource = frameSource;
        _gpuCompositor = gpuCompositor;
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
    }

    public void ApplyPipSettings(int channel, PipSettings settings)
    {
        lock (_sceneLock)
        {
            _previewSettings[channel] = settings;
            _previewScene = PipLayoutCalculator.BuildScene(_previewSettings);
        }
    }

    /// <summary>Promotes the current Preview scene to Program. A reference swap, so it applies in
    /// well under a millisecond (docs/specs/pc-switcher-app.md §2.2: "ミリ秒単位でリアルタイム変更").</summary>
    public void Take()
    {
        lock (_sceneLock)
        {
            _programScene = _previewScene;
        }
    }

    public FrameData GetProgramFrame() => Render(_programScene);

    public FrameData GetPreviewFrame() => Render(_previewScene);

    private FrameData Render(IReadOnlyList<CompositedLayer> scene) =>
        _gpuCompositor.Compose(scene, TryGetFrame, _canvasWidth, _canvasHeight);

    private FrameData? TryGetFrame(int channel) =>
        _frameSource.TryGetLatestFrame(channel, out var frame) ? frame : null;

    public void Dispose() => _gpuCompositor.Dispose();
}
