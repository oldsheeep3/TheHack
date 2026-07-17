using Switcher.Contracts;

namespace Switcher.Media.GStreamer;

/// <summary>
/// A single GStreamer pipeline (see <see cref="PipelineDescriptorFactory"/>) whose output caps are
/// negotiated to raw BGRA and pulled from a named <c>appsink</c>. Frames arrive on the internal
/// GStreamer streaming thread (no GLib main loop is required for <c>appsink</c> "new-sample"
/// delivery); pipeline errors/EOS are detected by polling the bus from a dedicated thread instead of
/// requiring a running main loop.
/// </summary>
internal sealed class GstAppSinkPipeline : IGstPipeline
{
    private const string AppSinkName = "sink";
    private const ulong BusPollTimeoutNs = 200 * (ulong)Gst.Constants.MSECOND;

    private static readonly object InitLock = new();
    private static bool s_gstInitialized;

    private readonly string _launchDescription;
    private readonly object _lifecycleLock = new();

    private Gst.Pipeline? _pipeline;
    private Gst.App.AppSink? _appSink;
    private Thread? _busThread;
    private CancellationTokenSource? _busCts;
    private bool _disposed;

    public event EventHandler<FrameData>? FrameReady;
    public event EventHandler<string>? Faulted;

    public GstAppSinkPipeline(string launchDescription)
    {
        _launchDescription = launchDescription;
    }

    public void Start()
    {
        EnsureGstInitialized();

        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pipeline is not null)
            {
                return;
            }

            var element = Gst.Parse.Launch(_launchDescription);
            var pipeline = (Gst.Pipeline)element;
            var sinkElement = ((Gst.Bin)pipeline).GetByName(AppSinkName)
                ?? throw new InvalidOperationException($"Pipeline is missing an appsink named '{AppSinkName}': {_launchDescription}");

            var appSink = new Gst.App.AppSink(sinkElement.Handle);
            appSink.NewSample += OnNewSample;

            var busCts = new CancellationTokenSource();
            var busThread = new Thread(() => PollBus(pipeline.Bus, busCts.Token))
            {
                IsBackground = true,
                Name = "gst-bus-poll",
            };

            _pipeline = pipeline;
            _appSink = appSink;
            _busCts = busCts;
            _busThread = busThread;

            pipeline.SetState(Gst.State.Playing);
            busThread.Start();
        }
    }

    public void Stop()
    {
        lock (_lifecycleLock)
        {
            TeardownLocked();
        }
    }

    private void TeardownLocked()
    {
        _busCts?.Cancel();
        try
        {
            if (_busThread is { IsAlive: true } && _busThread != Thread.CurrentThread)
            {
                _busThread.Join(TimeSpan.FromSeconds(2));
            }
        }
        catch (ThreadStateException)
        {
            // Already stopped.
        }

        if (_appSink is not null)
        {
            _appSink.NewSample -= OnNewSample;
            _appSink.Dispose();
            _appSink = null;
        }

        if (_pipeline is not null)
        {
            _pipeline.SetState(Gst.State.Null);
            _pipeline.Dispose();
            _pipeline = null;
        }

        _busCts?.Dispose();
        _busCts = null;
        _busThread = null;
    }

    private void OnNewSample(object o, Gst.App.NewSampleArgs args)
    {
        Gst.Sample? sample = null;
        try
        {
            sample = _appSink?.PullSample();
            if (sample?.Buffer is not { } buffer)
            {
                return;
            }

            if (!buffer.Map(out var mapInfo, Gst.MapFlags.Read))
            {
                return;
            }

            try
            {
                var (width, height) = ReadResolution(sample.Caps);
                FrameReady?.Invoke(this, new FrameData(width, height, mapInfo.Data));
            }
            finally
            {
                buffer.Unmap(mapInfo);
            }
        }
        catch (Exception ex)
        {
            // A malformed sample must not take down the streaming thread (and with it, this source).
            Faulted?.Invoke(this, $"Failed to process a decoded sample: {ex.Message}");
        }
        finally
        {
            sample?.Dispose();
        }
    }

    private static (int Width, int Height) ReadResolution(Gst.Caps? caps)
    {
        if (caps is null || caps.Size == 0)
        {
            return (0, 0);
        }

        // GetStructure returns a view owned by caps; it must not be disposed here.
        var structure = caps.GetStructure(0);
        structure.GetInt("width", out var width);
        structure.GetInt("height", out var height);
        return (width, height);
    }

    private void PollBus(Gst.Bus bus, CancellationToken cancellationToken)
    {
        const Gst.MessageType watched = Gst.MessageType.Error | Gst.MessageType.Eos;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = bus.TimedPopFiltered(BusPollTimeoutNs, watched);
                if (message is null)
                {
                    continue;
                }

                try
                {
                    if (message.Type == Gst.MessageType.Error)
                    {
                        message.ParseError(out var error, out var debug);
                        Faulted?.Invoke(this, $"{error.Message} ({debug})");
                    }
                    else if (message.Type == Gst.MessageType.Eos)
                    {
                        Faulted?.Invoke(this, "End of stream.");
                    }
                }
                finally
                {
                    message.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or NullReferenceException)
        {
            // Expected if the pipeline was disposed concurrently with a poll iteration.
        }
    }

    private static void EnsureGstInitialized()
    {
        if (s_gstInitialized)
        {
            return;
        }

        lock (InitLock)
        {
            if (s_gstInitialized)
            {
                return;
            }

            Gst.Application.Init();
            s_gstInitialized = true;
        }
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            TeardownLocked();
            _disposed = true;
        }
    }
}
