using Microsoft.Extensions.Logging;
using Switcher.Contracts;

namespace Switcher.App.Services;

/// <summary>
/// Reads the composited PGM1/PGM2/PVW1/PVW2 frames and each source's frame off the <see cref="IVideoEngine"/>
/// on a fixed interval and caches the latest frame per 4x4-multiview cell token (<c>PGM1</c>/<c>PGM2</c>/
/// <c>PVW1</c>/<c>PVW2</c>/<c>SRC:&lt;id&gt;</c>) so the UI can render whatever layout is configured
/// (docs/specs/pc-switcher-app.md §2.2-§2.3). Since the libobs migration, output routing (VCAM/NDI/HDMI)
/// is owned by the engine itself - this pump is UI preview only. Runs on a background loop, not the WPF
/// dispatcher thread, so a slow readback never blocks the UI; subscribers marshal back to their own
/// thread. A failure reading one target never stops the others (docs/specs/00-system-overview.md §5).
/// </summary>
public sealed class FramePumpService : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1000.0 / 30);
    private static readonly ProgramBus[] Buses = [ProgramBus.Pgm1, ProgramBus.Pgm2];

    private readonly IVideoEngine _engine;
    private readonly ILogger<FramePumpService> _logger;

    private readonly object _cellFramesLock = new();
    private readonly Dictionary<string, FrameData> _latestCellFrames = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public FramePumpService(IVideoEngine engine, ILogger<FramePumpService> logger)
    {
        _engine = engine;
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
            _cts.Dispose();
            _cts = null;
            _loopTask = null;
        }
    }

    /// <summary>The most recently read frame for one multiview cell token (<c>PGM1</c>/<c>PGM2</c>/
    /// <c>PVW1</c>/<c>PVW2</c>/<c>SRC:&lt;id&gt;</c>), or <c>null</c> if none has been read yet.</summary>
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
                PumpBus(bus);
            }

            PumpSourceCells();

            Tick?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PumpBus(ProgramBus bus)
    {
        try
        {
            var program = _engine.GetFrame(BusToken(bus, isProgram: true));
            SetCellFrame(BusToken(bus, isProgram: true), program);
            ProgramFrameReady?.Invoke(this, (bus, program));

            var preview = _engine.GetFrame(BusToken(bus, isProgram: false));
            SetCellFrame(BusToken(bus, isProgram: false), preview);
            PreviewFrameReady?.Invoke(this, (bus, preview));
        }
        catch (Exception ex)
        {
            // Readback depends on the engine's render pipeline; a transient failure must not take the
            // pump loop down (it retries next interval).
            _logger.LogWarning(ex, "Frame pump tick failed for {Bus}; will retry next interval.", bus);
        }
    }

    private void PumpSourceCells()
    {
        foreach (var source in _engine.GetSources())
        {
            if (source.Id is not { } id)
            {
                continue;
            }

            try
            {
                SetCellFrame($"SRC:{id}", _engine.GetFrame($"SRC:{id}"));
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
