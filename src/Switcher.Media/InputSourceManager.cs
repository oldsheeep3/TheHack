using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Switcher.Contracts;
using Switcher.Media.GStreamer;

namespace Switcher.Media;

/// <summary>
/// Manages the lifecycle and status of all input sources (UVC / NDI / SRT), up to 10+ concurrent
/// channels (docs/specs/pc-switcher-app.md §2.1). Each channel is an independent
/// <see cref="InputSource"/> with its own thread and pipeline; add/remove/status are safe to call
/// concurrently across channels and a failure on one channel never affects the others.
/// </summary>
public sealed class InputSourceManager : IInputSourceManager, IFrameSource, IDisposable
{
    private readonly ConcurrentDictionary<int, InputSource> _sources = new();
    private readonly Func<SourceProtocol, string?, IGstPipeline> _pipelineFactory;
    private readonly ILoggerFactory _loggerFactory;

    public InputSourceManager(ILoggerFactory loggerFactory)
        : this(loggerFactory, new GstPipelineFactory().Create)
    {
    }

    internal InputSourceManager(ILoggerFactory loggerFactory, Func<SourceProtocol, string?, IGstPipeline> pipelineFactory)
    {
        _loggerFactory = loggerFactory;
        _pipelineFactory = pipelineFactory;
    }

    public event EventHandler<SourceInfo>? SourceStatusChanged;

    public IReadOnlyList<SourceInfo> GetSources() =>
        _sources.Values.Select(source => source.Info).OrderBy(info => info.Channel).ToList();

    public void AddSource(int channel, SourceProtocol protocol, string? sourceUrl)
    {
        RemoveSourceInternal(channel);

        var source = new InputSource(channel, protocol, sourceUrl, _pipelineFactory, _loggerFactory.CreateLogger<InputSource>());
        source.StatusChanged += OnSourceStatusChanged;
        _sources[channel] = source;
        source.Start();
    }

    public void RemoveSource(int channel) => RemoveSourceInternal(channel);

    public bool TryGetLatestFrame(int channel, out FrameData? frame)
    {
        if (_sources.TryGetValue(channel, out var source))
        {
            frame = source.LatestFrame;
            return frame is not null;
        }

        frame = null;
        return false;
    }

    private void RemoveSourceInternal(int channel)
    {
        if (_sources.TryRemove(channel, out var existing))
        {
            existing.StatusChanged -= OnSourceStatusChanged;
            existing.Dispose();
        }
    }

    private void OnSourceStatusChanged(object? sender, SourceInfo info) => SourceStatusChanged?.Invoke(this, info);

    public void Dispose()
    {
        foreach (var channel in _sources.Keys.ToList())
        {
            RemoveSourceInternal(channel);
        }
    }
}
