using Microsoft.Extensions.Logging;
using Switcher.Contracts;
using Switcher.Media;
using Switcher.VirtualCam;
using Switcher.VirtualCam.Ndi;

namespace Switcher.App.Services;

/// <summary>
/// Pulls the composited PGM1/PGM2/PVW1/PVW2 frames off <see cref="CompositorEngine"/> on a fixed
/// interval, routes the two PGM frames to whichever sinks <see cref="OutputRouter"/> currently assigns
/// them to (VCAM1/VCAM2/HDMI), and caches the latest frame for every 4x4-multiview cell token
/// (<c>PGM1</c>/<c>PGM2</c>/<c>PVW1</c>/<c>PVW2</c>/<c>SRC:&lt;id&gt;</c>) so the UI can render whatever
/// layout is currently configured (docs/specs/pc-switcher-app.md §2.2-§2.3). Runs on a background loop,
/// not the WPF dispatcher thread, so a slow GPU readback never blocks the UI; subscribers are
/// responsible for marshalling back to their own thread (e.g. via <c>Dispatcher.BeginInvoke</c>). A
/// failure rendering/routing one bus never stops the other bus's tick (docs/specs/00-system-overview.md
/// §5: 1つの障害が全体を止めない).
/// </summary>
public sealed class FramePumpService : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1000.0 / 30);
    private static readonly ProgramBus[] Buses = [ProgramBus.Pgm1, ProgramBus.Pgm2];

    private readonly CompositorEngine _compositor;
    private readonly InputSourceManager _sourceManager;
    private readonly IDualVirtualCameraOutput _virtualCameraOutput;
    private readonly IDualNdiOutput _ndiOutput;
    private readonly OutputRouter _outputRouter;
    private readonly ILogger<FramePumpService> _logger;

    private readonly object _cellFramesLock = new();
    private readonly Dictionary<string, FrameData> _latestCellFrames = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public FramePumpService(
        CompositorEngine compositor,
        InputSourceManager sourceManager,
        IDualVirtualCameraOutput virtualCameraOutput,
        IDualNdiOutput ndiOutput,
        OutputRouter outputRouter,
        ILogger<FramePumpService> logger)
    {
        _compositor = compositor;
        _sourceManager = sourceManager;
        _virtualCameraOutput = virtualCameraOutput;
        _ndiOutput = ndiOutput;
        _outputRouter = outputRouter;
        _logger = logger;
    }

    public event EventHandler<(ProgramBus Bus, FrameData Frame)>? ProgramFrameReady;

    public event EventHandler<(ProgramBus Bus, FrameData Frame)>? PreviewFrameReady;

    /// <summary>Raised once per pump interval after every bus/source has been refreshed, so the
    /// multiview UI can re-read <see cref="TryGetCellFrame"/> for its currently configured cells.</summary>
    public event EventHandler? Tick;

    public void Start()
    {
        if (_loopTask is not null)
        {
            return;
        }

        _virtualCameraOutput.Start();
        _ndiOutput.Start();
        _cts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();

        try
        {
            if (_loopTask is not null)
            {
                await _loopTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            _virtualCameraOutput.Stop();
            _ndiOutput.Stop();
            _cts.Dispose();
            _cts = null;
            _loopTask = null;
        }
    }

    /// <summary>The most recently rendered frame for one multiview cell token (<c>PGM1</c>/<c>PGM2</c>/
    /// <c>PVW1</c>/<c>PVW2</c>/<c>SRC:&lt;id&gt;</c>), or <c>null</c> if none has been rendered yet.</summary>
    public FrameData? TryGetCellFrame(string token)
    {
        lock (_cellFramesLock)
        {
            return _latestCellFrames.TryGetValue(token, out var frame) ? frame : null;
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var bus in Buses)
            {
                PumpProgram(bus);
                PumpPreview(bus);
            }

            PumpSourceCells();

            Tick?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PumpProgram(ProgramBus bus)
    {
        try
        {
            var frame = _compositor.GetProgramFrame(bus);
            SetCellFrame(BusToken(bus, isProgram: true), frame);
            ProgramFrameReady?.Invoke(this, (bus, frame));
            _outputRouter.RouteFrame(bus == ProgramBus.Pgm1 ? OutputSource.Pgm1 : OutputSource.Pgm2, frame);
        }
        catch (Exception ex)
        {
            // Rendering depends on a Direct3D 11 runtime (see Switcher.Media/README.md); routing
            // depends on the output sinks currently attached. Neither may take the pump loop down.
            _logger.LogWarning(ex, "Program frame pump tick failed for {Bus}; will retry next interval.", bus);
        }
    }

    private void PumpPreview(ProgramBus bus)
    {
        try
        {
            var frame = _compositor.GetPreviewFrame(bus);
            SetCellFrame(BusToken(bus, isProgram: false), frame);
            PreviewFrameReady?.Invoke(this, (bus, frame));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preview frame pump tick failed for {Bus}; will retry next interval.", bus);
        }
    }

    private void PumpSourceCells()
    {
        foreach (var source in _sourceManager.GetSources())
        {
            if (source.Id is not { } id)
            {
                continue;
            }

            try
            {
                if (_sourceManager.TryGetLatestFrame(source.Channel, out var frame) && frame is not null)
                {
                    SetCellFrame($"SRC:{id}", frame);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read latest frame for source {SourceId}; will retry next interval.", id);
            }
        }
    }

    private void SetCellFrame(string token, FrameData frame)
    {
        lock (_cellFramesLock)
        {
            _latestCellFrames[token] = frame;
        }
    }

    private static string BusToken(ProgramBus bus, bool isProgram) => (bus, isProgram) switch
    {
        (ProgramBus.Pgm1, true) => "PGM1",
        (ProgramBus.Pgm2, true) => "PGM2",
        (ProgramBus.Pgm1, false) => "PVW1",
        (ProgramBus.Pgm2, false) => "PVW2",
        _ => throw new ArgumentOutOfRangeException(nameof(bus), bus, "Unsupported bus."),
    };

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
