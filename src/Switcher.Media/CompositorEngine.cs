using System.Collections.Concurrent;
using Switcher.Contracts;
using Switcher.Media.Compositing;
using Switcher.Media.Multiview;

namespace Switcher.Media;

/// <summary>
/// Composites PGM/PVW outputs from input sources, applies PiP layout, and handles TAKE
/// (docs/specs/pc-switcher-app.md §2.2), across **two independent program buses** (PGM1/PGM2, each
/// with its own PVW). <see cref="ApplyProgram"/>/<see cref="Take(ProgramBus)"/> edit/promote a given
/// bus's scene; <see cref="ApplyPipSettings"/>/<see cref="Take()"/>/<see cref="GetProgramFrame()"/>/
/// <see cref="GetPreviewFrame()"/> are a PGM1-only compat shim over the same per-bus state, for
/// callers not yet updated to the two-bus API. Layout math (<see cref="PipLayoutCalculator"/>) and GPU
/// rendering (<see cref="IGpuCompositor"/>) are both pure/abstracted so this class is unit testable
/// without a GStreamer/DirectX runtime, and are reused as-is across all four PGM/PVW render targets.
/// </summary>
public sealed class CompositorEngine : ICompositorEngine, IDisposable
{
    public const int DefaultCanvasWidth = 1920;
    public const int DefaultCanvasHeight = 1080;

    private readonly IFrameSource _frameSource;
    private readonly IGpuCompositor _gpuCompositor;
    private readonly int _canvasWidth;
    private readonly int _canvasHeight;

    private readonly Dictionary<ProgramBus, BusState> _buses = new()
    {
        [ProgramBus.Pgm1] = new BusState(),
        [ProgramBus.Pgm2] = new BusState(),
    };

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
        var state = _buses[ProgramBus.Pgm1];
        lock (state.Lock)
        {
            state.PreviewSettings[channel] = settings;
            state.PreviewScene = PipLayoutCalculator.BuildScene(state.PreviewSettings);
        }
    }

    /// <summary>Promotes PGM1's current Preview scene to Program. Compat shim for <see cref="Take(ProgramBus)"/>.</summary>
    public void Take() => Take(ProgramBus.Pgm1);

    public FrameData GetProgramFrame() => GetProgramFrame(ProgramBus.Pgm1);

    public FrameData GetPreviewFrame() => GetPreviewFrame(ProgramBus.Pgm1);

    /// <summary>Applies a full layer set to one bus's Preview scene (docs/specs/00-system-overview.md
    /// §4.2 `POST /api/v1/program`), replacing whatever was previously there, then optionally TAKEs.
    /// Layers referencing an unresolvable <see cref="ProgramLayer.SourceId"/> are dropped rather than
    /// failing the whole request, matching the engine's existing per-source failure isolation.</summary>
    public void ApplyProgram(ProgramRequest request)
    {
        var settingsByChannel = new Dictionary<int, PipSettings>();
        foreach (var layer in request.Layers)
        {
            if (_frameSource.TryResolveChannel(layer.SourceId, out var channel))
            {
                settingsByChannel[channel] = layer.Pip;
            }
        }

        var state = _buses[request.Bus];
        lock (state.Lock)
        {
            state.PreviewSettings = new ConcurrentDictionary<int, PipSettings>(settingsByChannel);
            state.PreviewScene = PipLayoutCalculator.BuildScene(state.PreviewSettings);
        }

        if (request.Take)
        {
            Take(request.Bus);
        }
    }

    /// <summary>Promotes <paramref name="bus"/>'s current Preview scene to Program. A reference swap
    /// scoped to that bus's own lock, so it applies in well under a millisecond
    /// (docs/specs/pc-switcher-app.md §2.2: "ミリ秒単位でリアルタイム変更") and never affects the
    /// other bus.</summary>
    public void Take(ProgramBus bus)
    {
        var state = _buses[bus];
        lock (state.Lock)
        {
            state.ProgramScene = state.PreviewScene;
        }
    }

    public FrameData GetProgramFrame(ProgramBus bus) => Render(_buses[bus].ProgramScene);

    public FrameData GetPreviewFrame(ProgramBus bus) => Render(_buses[bus].PreviewScene);

    /// <summary>Hot mount/unmount primitive for the physical module SW matrix (`pgm{1,2}×src{1,2}`,
    /// docs/specs/pc-switcher-app.md §2.2); the HID→operation translation is A2-006's responsibility,
    /// this is only the operation primitive. Toggles <paramref name="sourceId"/> on <paramref name="bus"/>
    /// without disturbing any other layer already on that bus.</summary>
    public void SetSourceEnabled(ProgramBus bus, string sourceId, bool enabled)
    {
        if (!_frameSource.TryResolveChannel(sourceId, out var channel))
        {
            return;
        }

        var state = _buses[bus];
        lock (state.Lock)
        {
            var current = state.PreviewSettings.TryGetValue(channel, out var existing)
                ? existing
                : new PipSettings(false, 0, 0, _canvasWidth, _canvasHeight, 1.0, 0, null);

            state.PreviewSettings[channel] = current with { Enabled = enabled };
            state.PreviewScene = PipLayoutCalculator.BuildScene(state.PreviewSettings);
        }
    }

    /// <summary>Captures both buses' current Preview layer settings for a scene preset
    /// (docs/specs/pc-switcher-app.md §2.2). See <see cref="SceneSnapshot"/> for persistence notes.</summary>
    public SceneSnapshot GetSceneSnapshot()
    {
        var buses = new List<BusSceneState>();
        foreach (var (bus, state) in _buses)
        {
            lock (state.Lock)
            {
                buses.Add(new BusSceneState(bus, new Dictionary<int, PipSettings>(state.PreviewSettings)));
            }
        }

        return new SceneSnapshot(buses);
    }

    /// <summary>Restores both buses' Preview layer settings from a previously captured
    /// <see cref="SceneSnapshot"/>. Does not TAKE: callers decide whether/when to promote to Program.</summary>
    public void ApplySceneSnapshot(SceneSnapshot snapshot)
    {
        foreach (var busState in snapshot.Buses)
        {
            var state = _buses[busState.Bus];
            lock (state.Lock)
            {
                state.PreviewSettings = new ConcurrentDictionary<int, PipSettings>(busState.PreviewLayers);
                state.PreviewScene = PipLayoutCalculator.BuildScene(state.PreviewSettings);
            }
        }
    }

    /// <summary>Synthetic channel numbers for the composed PGM/PVW feeds referenced by multiview
    /// content selectors. Negative so they never collide with real input channels (allocated from 0).</summary>
    private const int MultiviewChannelPgm1 = -1;
    private const int MultiviewChannelPgm2 = -2;
    private const int MultiviewChannelPvw1 = -3;
    private const int MultiviewChannelPvw2 = -4;

    /// <summary>Composites a multiview preview frame from <paramref name="layout"/>
    /// (docs/specs/multiview-output-revision.md §2.3). Each region resolves its <c>content</c>
    /// ("PGM1"|"PGM2"|"PVW1"|"PVW2"|"SRC:&lt;id&gt;"|"EMPTY") to a source frame and is drawn, enlarged,
    /// into its rectangle; combined cells therefore show one feed blown up across the merged area.
    /// <c>EMPTY</c> and unresolvable selectors draw nothing, leaving the black canvas showing through.
    /// The legacy 4x4 <c>cells</c> form is normalized to 16 1x1 regions and behaves as before.</summary>
    public FrameData GetMultiviewFrame(MultiviewLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var tiles = MultiviewLayoutCalculator.Build(layout, _canvasWidth, _canvasHeight);
        var layers = new List<CompositedLayer>(tiles.Count);
        var composedFeeds = new Dictionary<int, FrameData?>();

        var zOrder = 0;
        foreach (var tile in tiles)
        {
            if (!TryResolveMultiviewChannel(tile.Content, composedFeeds, out var channel))
            {
                continue;
            }

            var placement = new PipSettings(
                Enabled: true,
                X: tile.X,
                Y: tile.Y,
                Width: tile.Width,
                Height: tile.Height,
                Opacity: 1.0,
                ZOrder: zOrder++,
                Crop: null);
            layers.Add(new CompositedLayer(channel, placement));
        }

        FrameData? Lookup(int channel) => channel < 0
            ? (composedFeeds.TryGetValue(channel, out var feed) ? feed : null)
            : TryGetFrame(channel);

        return _gpuCompositor.Compose(layers, Lookup, _canvasWidth, _canvasHeight);
    }

    // Maps a multiview content selector to a lookup channel, rendering (and caching) the composed
    // PGM/PVW feed a selector needs on first use. EMPTY, malformed, and unresolvable SRC:<id> selectors
    // return false so their tile stays black rather than failing the whole multiview, matching the
    // engine's per-source failure isolation.
    private bool TryResolveMultiviewChannel(string content, Dictionary<int, FrameData?> composedFeeds, out int channel)
    {
        switch (content)
        {
            case "PGM1":
                channel = MultiviewChannelPgm1;
                CacheComposedFeed(channel, ProgramBus.Pgm1, program: true, composedFeeds);
                return true;
            case "PGM2":
                channel = MultiviewChannelPgm2;
                CacheComposedFeed(channel, ProgramBus.Pgm2, program: true, composedFeeds);
                return true;
            case "PVW1":
                channel = MultiviewChannelPvw1;
                CacheComposedFeed(channel, ProgramBus.Pgm1, program: false, composedFeeds);
                return true;
            case "PVW2":
                channel = MultiviewChannelPvw2;
                CacheComposedFeed(channel, ProgramBus.Pgm2, program: false, composedFeeds);
                return true;
            default:
                if (content is not null && content.StartsWith("SRC:", StringComparison.Ordinal))
                {
                    return _frameSource.TryResolveChannel(content["SRC:".Length..], out channel);
                }

                channel = 0;
                return false;
        }
    }

    private void CacheComposedFeed(int channel, ProgramBus bus, bool program, Dictionary<int, FrameData?> composedFeeds)
    {
        if (composedFeeds.ContainsKey(channel))
        {
            return;
        }

        var state = _buses[bus];
        IReadOnlyList<CompositedLayer> scene;
        lock (state.Lock)
        {
            scene = program ? state.ProgramScene : state.PreviewScene;
        }

        composedFeeds[channel] = Render(scene);
    }

    private FrameData Render(IReadOnlyList<CompositedLayer> scene) =>
        _gpuCompositor.Compose(scene, TryGetFrame, _canvasWidth, _canvasHeight);

    private FrameData? TryGetFrame(int channel) =>
        _frameSource.TryGetLatestFrame(channel, out var frame) ? frame : null;

    public void Dispose() => _gpuCompositor.Dispose();

    /// <summary>Per-bus Preview/Program state. Its own lock keeps PGM1 and PGM2 operations from
    /// contending with each other (docs/tasks/agent-A2-002-media-dualme.md: "ロック粒度に注意（シーン
    /// 更新の _sceneLock をバス単位で保持）").</summary>
    private sealed class BusState
    {
        public readonly object Lock = new();
        public ConcurrentDictionary<int, PipSettings> PreviewSettings = new();
        public IReadOnlyList<CompositedLayer> PreviewScene = [];
        public IReadOnlyList<CompositedLayer> ProgramScene = [];
    }
}
