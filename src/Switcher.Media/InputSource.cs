using Microsoft.Extensions.Logging;
using Switcher.Contracts;
using Switcher.Media.GStreamer;

namespace Switcher.Media;

/// <summary>
/// Owns the full lifecycle of one input channel on its own dedicated thread: builds the protocol's
/// GStreamer pipeline, holds the latest decoded frame for GPU consumption, and reconnects with
/// exponential backoff on error/EOS. An exception anywhere in this loop is caught and turned into
/// <see cref="SourceStatus.Error"/> rather than propagating, so one channel's failure never affects
/// the others (docs/specs/pc-switcher-app.md §2.1, §4).
/// </summary>
internal sealed class InputSource : IDisposable
{
    private static readonly TimeSpan DefaultInitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultMaxBackoff = TimeSpan.FromSeconds(30);

    private readonly int _channel;
    private readonly SourceProtocol _protocol;
    private readonly string? _sourceUrl;
    private readonly Func<SourceProtocol, string?, IGstPipeline> _pipelineFactory;
    private readonly ILogger _logger;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maxBackoff;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private readonly object _infoLock = new();

    private SourceInfo _info;
    private FrameData? _latestFrame;

    public event EventHandler<SourceInfo>? StatusChanged;

    public InputSource(
        int channel,
        SourceProtocol protocol,
        string? sourceUrl,
        Func<SourceProtocol, string?, IGstPipeline> pipelineFactory,
        ILogger logger,
        TimeSpan? initialBackoff = null,
        TimeSpan? maxBackoff = null,
        string? id = null,
        string? name = null,
        int? order = null)
    {
        _channel = channel;
        _protocol = protocol;
        _sourceUrl = sourceUrl;
        _pipelineFactory = pipelineFactory;
        _logger = logger;
        _initialBackoff = initialBackoff ?? DefaultInitialBackoff;
        _maxBackoff = maxBackoff ?? DefaultMaxBackoff;

        _info = new SourceInfo(channel, name ?? $"ch{channel}", protocol, Resolution: null, SourceStatus.Disconnected, id, order);
        _thread = new Thread(RunLoop) { IsBackground = true, Name = $"input-source-ch{channel}" };
    }

    public SourceInfo Info => Volatile.Read(ref _info);

    /// <summary>Needed by <see cref="InputSourceManager.Duplicate"/> to spin up a new
    /// <see cref="InputSource"/> with the same pipeline configuration under a different id/channel.</summary>
    public SourceProtocol Protocol => _protocol;

    public string? SourceUrl => _sourceUrl;

    /// <summary>Updates display order only (§2.1 OBS-like reorder); channel/id stay stable so §4.3
    /// tally ordinals never change as a result of reordering.</summary>
    public void UpdateOrder(int order)
    {
        SourceInfo updated;
        lock (_infoLock)
        {
            var current = Info;
            if (current.Order == order)
            {
                return;
            }

            updated = current with { Order = order };
            Volatile.Write(ref _info, updated);
        }

        StatusChanged?.Invoke(this, updated);
    }

    /// <summary>The most recently decoded frame, if any. Reference assignment is atomic, so readers
    /// never observe a torn frame without needing a lock (double-buffering via reference swap).</summary>
    public FrameData? LatestFrame => Volatile.Read(ref _latestFrame);

    public void Start() => _thread.Start();

    private void RunLoop()
    {
        var token = _cts.Token;
        var backoff = _initialBackoff;

        while (!token.IsCancellationRequested)
        {
            try
            {
                RunOnce(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Input source ch{Channel} failed unexpectedly.", _channel);
            }

            if (token.IsCancellationRequested)
            {
                break;
            }

            SetStatus(SourceStatus.Error);

            if (token.WaitHandle.WaitOne(backoff))
            {
                break; // Cancelled while backing off.
            }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, _maxBackoff.TotalSeconds));
        }
    }

    private void RunOnce(CancellationToken token)
    {
        using var pipeline = _pipelineFactory(_protocol, _sourceUrl);
        var faulted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFrame(object? sender, FrameData frame)
        {
            Volatile.Write(ref _latestFrame, frame);
            // Every frame reasserts Connected (with its resolution), including recovery from a
            // transient Error should the pipeline keep delivering frames after a reported fault.
            SetStatus(SourceStatus.Connected, frame);
        }

        void OnFaulted(object? sender, string reason)
        {
            _logger.LogWarning("Input source ch{Channel} faulted: {Reason}", _channel, reason);
            faulted.TrySetResult(reason);
        }

        pipeline.FrameReady += OnFrame;
        pipeline.Faulted += OnFaulted;

        try
        {
            pipeline.Start();
            SetStatus(SourceStatus.Connected);

            using var registration = token.Register(() => faulted.TrySetResult("Cancelled."));
            faulted.Task.GetAwaiter().GetResult();
        }
        finally
        {
            pipeline.FrameReady -= OnFrame;
            pipeline.Faulted -= OnFaulted;
            pipeline.Stop();
        }
    }

    private void SetStatus(SourceStatus status, FrameData? frame = null)
    {
        SourceInfo updated;
        lock (_infoLock)
        {
            var current = Info;
            var resolution = frame is { } f ? $"{f.Width}x{f.Height}" : current.Resolution;
            if (current.Status == status && current.Resolution == resolution)
            {
                return;
            }

            updated = current with { Status = status, Resolution = resolution };
            Volatile.Write(ref _info, updated);
        }

        StatusChanged?.Invoke(this, updated);
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(5));
        }

        _cts.Dispose();
    }
}
