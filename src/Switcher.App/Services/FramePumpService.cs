using Microsoft.Extensions.Logging;
using Switcher.Contracts;

namespace Switcher.App.Services;

/// <summary>
/// Pulls the composited PGM/PVW frames off <see cref="ICompositorEngine"/> on a fixed interval and
/// fans them out to the virtual camera output and to any UI subscriber (multiview PGM/PVW panels,
/// the full-screen source projector). Runs on a background loop, not the WPF dispatcher thread, so a
/// slow GPU readback never blocks the UI; subscribers are responsible for marshalling back to their
/// own thread (e.g. via <c>Dispatcher.BeginInvoke</c>).
/// </summary>
public sealed class FramePumpService : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1000.0 / 30);

    private readonly ICompositorEngine _compositor;
    private readonly IVirtualCameraOutput _virtualCameraOutput;
    private readonly ILogger<FramePumpService> _logger;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public FramePumpService(ICompositorEngine compositor, IVirtualCameraOutput virtualCameraOutput, ILogger<FramePumpService> logger)
    {
        _compositor = compositor;
        _virtualCameraOutput = virtualCameraOutput;
        _logger = logger;
    }

    public event EventHandler<FrameData>? ProgramFrameReady;

    public event EventHandler<FrameData>? PreviewFrameReady;

    public void Start()
    {
        if (_loopTask is not null)
        {
            return;
        }

        _virtualCameraOutput.Start();
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
            _cts.Dispose();
            _cts = null;
            _loopTask = null;
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var program = _compositor.GetProgramFrame();
                _virtualCameraOutput.SubmitFrame(program);
                ProgramFrameReady?.Invoke(this, program);

                var preview = _compositor.GetPreviewFrame();
                PreviewFrameReady?.Invoke(this, preview);
            }
            catch (Exception ex)
            {
                // Frame rendering depends on a Direct3D 11 runtime (see Switcher.Media/README.md); on
                // a host without one this loop must keep retrying rather than take the app down.
                _logger.LogWarning(ex, "Frame pump tick failed; will retry next interval.");
            }
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
