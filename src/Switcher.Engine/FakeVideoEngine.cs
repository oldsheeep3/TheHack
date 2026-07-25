using System.Collections.Concurrent;
using Switcher.Contracts;

namespace Switcher.Engine;

/// <summary>
/// In-memory, native-free <see cref="IVideoEngine"/> used by headless tests (Web/App/Engine) and any
/// non-Windows environment. It faithfully models the *observable* state the orchestrator and endpoints
/// depend on - the source registry (with stable channel resolution) and the current output assignments -
/// while treating the actual video compositing/output as no-ops. libobs is never touched.
/// </summary>
public sealed class FakeVideoEngine : IVideoEngine
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SourceInfo> _sourcesById = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _idByChannel = new();
    private readonly Dictionary<string, int> _channelById = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(ProgramBus, string), bool> _enabled = new();
    private int _nextChannel;
    private IReadOnlyList<OutputAssignment> _assignments = [];
    private bool _started;

    public event EventHandler<SourceInfo>? SourceStatusChanged;

    public event EventHandler<string>? SourceRemoved;

    public Task StartAsync(EngineOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (_gate)
        {
            _started = true;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _started = false;
        }

        return Task.CompletedTask;
    }

    /// <summary>Whether <see cref="StartAsync"/> has run and <see cref="StopAsync"/> has not - exposed for tests.</summary>
    public bool IsStarted
    {
        get { lock (_gate) { return _started; } }
    }

    public IReadOnlyList<SourceInfo> GetSources()
    {
        lock (_gate)
        {
            return _sourcesById.Values.OrderBy(s => s.Order ?? s.Channel).ToList();
        }
    }

    /// <summary>No real device backend in the fake engine; enumeration is always empty.</summary>
    public IReadOnlyList<DeviceInfo> QueryDevices(DeviceQueryType type) => [];

    /// <summary>Test helper: the ids handed to <see cref="AddSource(SourceDefinition)"/>, in order — a
    /// mix has to reach the engine after the sources it references.</summary>
    public IReadOnlyList<string> AddedSourceIds
    {
        get { lock (_gate) { return _addedSourceIds.ToList(); } }
    }

    private readonly List<string> _addedSourceIds = [];

    public void AddSource(SourceDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            _addedSourceIds.Add(source.Id);
            var channel = _channelById.TryGetValue(source.Id, out var existing) ? existing : AllocateChannelLocked(source.Id);
            var info = new SourceInfo(
                Channel: channel,
                Name: source.Name,
                Protocol: ToProtocol(source.Type),
                Resolution: null,
                Status: SourceStatus.Disconnected,
                Id: source.Id,
                Order: channel);
            _sourcesById[source.Id] = info;
            SourceStatusChanged?.Invoke(this, info);
        }
    }

    public void AddSource(int channel, SourceProtocol protocol, string? sourceUrl)
    {
        lock (_gate)
        {
            var id = $"ch{channel}";
            _idByChannel[channel] = id;
            _channelById[id] = channel;
            if (channel >= _nextChannel)
            {
                _nextChannel = channel + 1;
            }

            var info = new SourceInfo(channel, id, protocol, null, SourceStatus.Disconnected, id, channel);
            _sourcesById[id] = info;
            SourceStatusChanged?.Invoke(this, info);
        }
    }

    public void RemoveSource(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate)
        {
            if (_channelById.TryGetValue(id, out var channel))
            {
                _idByChannel.Remove(channel);
                _channelById.Remove(id);
            }

            _sourcesById.Remove(id);
            _enabled.TryRemove((ProgramBus.Pgm1, id), out _);
            _enabled.TryRemove((ProgramBus.Pgm2, id), out _);
        }

        SourceRemoved?.Invoke(this, id);
    }

    public bool TryResolveChannel(string id, out int channel)
    {
        lock (_gate)
        {
            return _channelById.TryGetValue(id, out channel);
        }
    }

    public void SetSourceEnabled(ProgramBus bus, string sourceId, bool enabled) =>
        _enabled[(bus, sourceId)] = enabled;

    /// <summary>Test helper: whether a source is currently mounted on a bus.</summary>
    public bool IsSourceEnabled(ProgramBus bus, string sourceId) =>
        _enabled.TryGetValue((bus, sourceId), out var on) && on;

    public void ApplyProgram(ProgramRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var layer in request.Layers)
        {
            _enabled[(request.Bus, layer.SourceId)] = true;
        }
    }

    public void ApplyPipSettings(int channel, PipSettings settings)
    {
        // No compositing in the fake; nothing to record.
    }

    public void Take()
    {
        // Global TAKE no-op in the fake.
    }

    public void Take(ProgramBus bus, int durationMs) => _lastTake = (bus, durationMs);

    private (ProgramBus Bus, int DurationMs)? _lastTake;

    /// <summary>Test helper: the most recent per-bus TAKE, or <c>null</c> if none has run.</summary>
    public (ProgramBus Bus, int DurationMs)? LastTake => _lastTake;

    public FrameData GetFrame(string target) => EmptyFrame();

    public void ApplyMultiview(MultiviewLayout layout) => ArgumentNullException.ThrowIfNull(layout);

    private readonly Dictionary<string, int> _audioMixers = new(StringComparer.Ordinal);

    public void SetSourceAudioMixers(string id, int mixerMask)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate) { _audioMixers[id] = mixerMask; }
    }

    /// <summary>Test helper: the mask last applied to a source (0 when never set).</summary>
    public int AudioMixersFor(string id)
    {
        lock (_gate) { return _audioMixers.GetValueOrDefault(id); }
    }

    /// <summary>No real endpoints in the fake.</summary>
    public IReadOnlyList<AudioDeviceInfo> QueryAudioDevices() => [];

    public void ApplyAudioOutputs(AudioOutputsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate) { CurrentAudioOutputs = request.Outputs.ToList(); }
    }

    /// <summary>Test helper: the routing last applied.</summary>
    public IReadOnlyList<AudioOutputAssignment> CurrentAudioOutputs { get; private set; } = [];

    public void ApplyOutputs(OutputsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Validate before mutating so an invalid request never corrupts the current assignments
        // (mirrors the atomic behavior AppOrchestratorTests asserts against OutputRouter).
        var seen = new HashSet<OutputSink>();
        foreach (var assignment in request.Outputs)
        {
            if (!seen.Add(assignment.Sink))
            {
                throw new ArgumentException($"Duplicate output sink '{assignment.Sink}'.", nameof(request));
            }

            if (assignment.Sink == OutputSink.Hdmi && assignment.DisplayId is null)
            {
                throw new ArgumentException("HDMI sink requires a display id.", nameof(request));
            }
        }

        lock (_gate)
        {
            _assignments = request.Outputs.ToList();
        }
    }

    public IReadOnlyList<OutputAssignment> CurrentAssignments
    {
        get { lock (_gate) { return _assignments; } }
    }

    /// <summary>Sinks a test declares as failing to start, keyed by sink. Everything else reports
    /// running, which is what a machine with the devices present would do.</summary>
    public HashSet<OutputSink> FailingSinks { get; } = [];

    public IReadOnlyList<OutputStatus> QueryOutputStatus()
    {
        lock (_gate)
        {
            return [.. _assignments.Select(a => new OutputStatus(a.Sink, a.Source, !FailingSinks.Contains(a.Sink)))];
        }
    }

    public void StartDisplayOutput(string target, IntPtr windowHandle, int displayId)
    {
        // No native display in the fake.
    }

    public void StopDisplayOutput(string target)
    {
        // No native display in the fake.
    }

    private int AllocateChannelLocked(string id)
    {
        var channel = _nextChannel++;
        _idByChannel[channel] = id;
        _channelById[id] = channel;
        return channel;
    }

    private static FrameData EmptyFrame() => new(0, 0, ReadOnlyMemory<byte>.Empty);

    private static SourceProtocol ToProtocol(SourceType type) => type switch
    {
        SourceType.Ndi => SourceProtocol.Ndi,
        SourceType.Webcam => SourceProtocol.Uvc,
        SourceType.Srt => SourceProtocol.Srt,
        SourceType.Image => SourceProtocol.Image,
        SourceType.Html => SourceProtocol.Html,
        SourceType.Mix => SourceProtocol.Mix,
        _ => SourceProtocol.Uvc,
    };
}
