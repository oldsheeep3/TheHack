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
    private readonly ConcurrentDictionary<string, int> _idToChannel = new();

    // Guards channel allocation + the (_sources, _idToChannel) pair of inserts for id-based
    // add/duplicate, so two concurrent calls can never be handed the same freshly-allocated channel.
    // The legacy int-channel API doesn't need this: its channel is caller-supplied, not allocated.
    private readonly object _addLock = new();

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

    /// <summary>Ordered by display <see cref="SourceInfo.Order"/> when set (id-based sources, §2.1
    /// reorder), falling back to the stable channel ordinal for legacy int-channel sources.</summary>
    public IReadOnlyList<SourceInfo> GetSources() =>
        _sources.Values.Select(source => source.Info)
            .OrderBy(info => info.Order ?? info.Channel)
            .ThenBy(info => info.Channel)
            .ToList();

    public void AddSource(int channel, SourceProtocol protocol, string? sourceUrl)
    {
        RemoveSourceInternal(channel);

        var source = new InputSource(channel, protocol, sourceUrl, _pipelineFactory, _loggerFactory.CreateLogger<InputSource>());
        source.StatusChanged += OnSourceStatusChanged;
        _sources[channel] = source;
        source.Start();
    }

    public void RemoveSource(int channel) => RemoveSourceInternal(channel);

    /// <summary>OBS-like add by <see cref="SourceDefinition.Id"/> (§2.1): NDI/WebCam/SRT config maps
    /// onto the existing protocol/URL pipeline (<see cref="PipelineDescriptorFactory"/>). Re-adding an
    /// already-known id replaces it in place, reusing its previously assigned channel/ordinal so §4.3
    /// tally references stay stable.</summary>
    public SourceInfo AddSource(SourceDefinition definition)
    {
        var (protocol, sourceUrl) = PipelineDescriptorFactory.MapToProtocolAndUrl(definition);

        InputSource source;
        lock (_addLock)
        {
            var channel = _idToChannel.TryGetValue(definition.Id, out var existingChannel)
                ? existingChannel
                : AllocateChannel();

            RemoveSourceInternal(channel);

            source = new InputSource(
                channel, protocol, sourceUrl, _pipelineFactory, _loggerFactory.CreateLogger<InputSource>(),
                id: definition.Id, name: definition.Name, order: channel);
            source.StatusChanged += OnSourceStatusChanged;
            _sources[channel] = source;
            _idToChannel[definition.Id] = channel;
        }

        source.Start();
        return source.Info;
    }

    public void RemoveSource(string id)
    {
        if (_idToChannel.TryRemove(id, out var channel))
        {
            RemoveSourceInternal(channel);
        }
    }

    /// <summary>Sets display order for each listed id to its index in <paramref name="orderedIds"/>
    /// (§2.1 free reorder). Unknown ids are ignored; ids not listed keep their current order.</summary>
    public void Reorder(IReadOnlyList<string> orderedIds)
    {
        for (var order = 0; order < orderedIds.Count; order++)
        {
            if (_idToChannel.TryGetValue(orderedIds[order], out var channel) && _sources.TryGetValue(channel, out var source))
            {
                source.UpdateOrder(order);
            }
        }
    }

    /// <summary>Clones the source at <paramref name="id"/> under a newly generated id, with its own
    /// channel/thread/pipeline (§2.1 duplicate). Returns the new source's info so the caller learns
    /// the generated id.</summary>
    public SourceInfo Duplicate(string id)
    {
        if (!_idToChannel.TryGetValue(id, out var existingChannel) || !_sources.TryGetValue(existingChannel, out var existing))
        {
            throw new ArgumentException($"Unknown source id '{id}'.", nameof(id));
        }

        InputSource duplicate;
        lock (_addLock)
        {
            var newId = GenerateDuplicateId(id);
            var channel = AllocateChannel();

            duplicate = new InputSource(
                channel, existing.Protocol, existing.SourceUrl, _pipelineFactory, _loggerFactory.CreateLogger<InputSource>(),
                id: newId, name: $"{existing.Info.Name} copy", order: channel);
            duplicate.StatusChanged += OnSourceStatusChanged;
            _sources[channel] = duplicate;
            _idToChannel[newId] = channel;
        }

        duplicate.Start();
        return duplicate.Info;
    }

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

    public bool TryResolveChannel(string sourceId, out int channel) => _idToChannel.TryGetValue(sourceId, out channel);

    // Must be called with _addLock held when racing with an id-based allocation (AddSource/Duplicate);
    // _sources is the source of truth for occupied channels, covering both legacy int-channel and
    // id-based entries (every id-based entry is also inserted into _sources under the same channel).
    private int AllocateChannel()
    {
        var channel = 0;
        while (_sources.ContainsKey(channel))
        {
            channel++;
        }

        return channel;
    }

    private string GenerateDuplicateId(string id)
    {
        var candidate = $"{id}-copy";
        var suffix = 2;
        while (_idToChannel.ContainsKey(candidate))
        {
            candidate = $"{id}-copy-{suffix++}";
        }

        return candidate;
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
