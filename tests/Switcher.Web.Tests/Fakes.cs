using Switcher.Contracts;

namespace Switcher.Web.Tests;

internal sealed class FakeSwitcherConfigService : ISwitcherConfigService
{
    public List<ConfigChangeRequest> Received { get; } = [];
    public List<SourceDefinition> AddedSources { get; } = [];
    public List<(string Id, SourceDefinition Source)> UpdatedSources { get; } = [];
    public List<string> RemovedSourceIds { get; } = [];
    public List<ProgramRequest> AppliedPrograms { get; } = [];
    public List<MultiviewLayout> AppliedMultiviews { get; } = [];
    public List<OutputsRequest> AppliedOutputs { get; } = [];
    public List<ModulesRequest> AppliedModules { get; } = [];
    public List<AudioOutputsRequest> AppliedAudioOutputs { get; } = [];
    public List<AtemConfig> AppliedAtemConfigs { get; } = [];
    public List<AtemCommandRequest> SentAtemCommands { get; } = [];
    public List<PicoNetworkConfig> AppliedPicoNetworkConfigs { get; } = [];

    public Task ApplyConfigAsync(ConfigChangeRequest request, CancellationToken cancellationToken = default)
    {
        Received.Add(request);
        return Task.CompletedTask;
    }

    public Task AddSourceAsync(SourceDefinition source, CancellationToken cancellationToken = default)
    {
        AddedSources.Add(source);
        return Task.CompletedTask;
    }

    public Task UpdateSourceAsync(string id, SourceDefinition source, CancellationToken cancellationToken = default)
    {
        UpdatedSources.Add((id, source));
        return Task.CompletedTask;
    }

    public Task RemoveSourceAsync(string id, CancellationToken cancellationToken = default)
    {
        RemovedSourceIds.Add(id);
        return Task.CompletedTask;
    }

    public Task ApplyProgramAsync(ProgramRequest request, CancellationToken cancellationToken = default)
    {
        AppliedPrograms.Add(request);
        return Task.CompletedTask;
    }

    public Task ApplyMultiviewAsync(MultiviewLayout layout, CancellationToken cancellationToken = default)
    {
        AppliedMultiviews.Add(layout);
        return Task.CompletedTask;
    }

    public Task ApplyOutputsAsync(OutputsRequest request, CancellationToken cancellationToken = default)
    {
        AppliedOutputs.Add(request);
        return Task.CompletedTask;
    }

    public Task ApplyAudioOutputsAsync(AudioOutputsRequest request, CancellationToken cancellationToken = default)
    {
        AppliedAudioOutputs.Add(request);
        return Task.CompletedTask;
    }

    public Task ApplyModulesAsync(ModulesRequest request, CancellationToken cancellationToken = default)
    {
        AppliedModules.Add(request);
        return Task.CompletedTask;
    }

    public Task ApplyAtemConfigAsync(AtemConfig config, CancellationToken cancellationToken = default)
    {
        AppliedAtemConfigs.Add(config);
        return Task.CompletedTask;
    }

    public Task SendAtemCommandAsync(AtemCommandRequest command, CancellationToken cancellationToken = default)
    {
        SentAtemCommands.Add(command);
        return Task.CompletedTask;
    }

    public Task ApplyPicoNetworkConfigAsync(PicoNetworkConfig config, CancellationToken cancellationToken = default)
    {
        AppliedPicoNetworkConfigs.Add(config);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Minimal <see cref="IVideoEngine"/> test double for the Web layer: only <see cref="GetSources"/> is
/// exercised by the endpoints (GET /api/v1/sources), so the compositing/output surface is a no-op. Named
/// for its historical role; the Web layer depends on the engine abstraction since the libobs migration.
/// </summary>
internal sealed class FakeInputSourceManager : IVideoEngine
{
    private readonly List<SourceInfo> _sources;

    public FakeInputSourceManager(IEnumerable<SourceInfo> sources) => _sources = [.. sources];

    public Task StartAsync(EngineOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public IReadOnlyList<SourceInfo> GetSources() => _sources;

    public IReadOnlyList<DeviceInfo> QueryDevices(DeviceQueryType type) => [];

    public void AddSource(SourceDefinition source) =>
        _sources.Add(new SourceInfo(_sources.Count, source.Name, SourceProtocol.Uvc, null, SourceStatus.Connected, source.Id, _sources.Count));

    public void AddSource(int channel, SourceProtocol protocol, string? sourceUrl) =>
        _sources.Add(new SourceInfo(channel, $"ch{channel}", protocol, null, SourceStatus.Connected));

    public void RemoveSource(string id)
    {
        _sources.RemoveAll(s => s.Id == id);
        SourceRemoved?.Invoke(this, id);
    }

    public event EventHandler<string>? SourceRemoved;

    public bool TryResolveChannel(string id, out int channel)
    {
        var match = _sources.FirstOrDefault(s => s.Id == id);
        channel = match?.Channel ?? -1;
        return match is not null;
    }

    public event EventHandler<SourceInfo>? SourceStatusChanged;

    public void RaiseStatusChanged(SourceInfo info) => SourceStatusChanged?.Invoke(this, info);

    public void SetSourceEnabled(ProgramBus bus, string sourceId, bool enabled) { }

    public void ApplyProgram(ProgramRequest request) { }

    public void ApplyPipSettings(int channel, PipSettings settings) { }

    public void Take() { }

    public void Take(ProgramBus bus, int durationMs) { }

    public FrameData GetFrame(string target) => new(0, 0, ReadOnlyMemory<byte>.Empty);

    public void ApplyMultiview(MultiviewLayout layout) { }

    public void SetSourceAudioMixers(string id, int mixerMask) { }

    public IReadOnlyList<AudioDeviceInfo> QueryAudioDevices() => [];

    public void ApplyAudioOutputs(AudioOutputsRequest request) { }

    public void ApplyOutputs(OutputsRequest request) { }

    public IReadOnlyList<OutputAssignment> CurrentAssignments => [];

    public IReadOnlyList<OutputStatus> QueryOutputStatus() => [];

    public void StartDisplayOutput(string target, IntPtr windowHandle, int displayId) { }

    public void StopDisplayOutput(string target) { }
}

/// <summary>
/// In-memory <see cref="IDeviceQueryService"/> returning canned enumeration/SRT results, so the
/// devices/SRT endpoints can be exercised without touching the OS or the Media implementation.
/// </summary>
internal sealed class FakeDeviceQueryService : IDeviceQueryService
{
    private readonly IReadOnlyDictionary<DeviceQueryType, IReadOnlyList<DeviceInfo>> _devices;
    private readonly SrtSetupInfo _srtSetup;

    public FakeDeviceQueryService(
        IReadOnlyDictionary<DeviceQueryType, IReadOnlyList<DeviceInfo>>? devices = null,
        SrtSetupInfo? srtSetup = null)
    {
        _devices = devices ?? new Dictionary<DeviceQueryType, IReadOnlyList<DeviceInfo>>();
        _srtSetup = srtSetup ?? new SrtSetupInfo(9000, ["192.168.1.50"], "srt://192.168.1.50:9000", 120, "Point your encoder here.");
    }

    public List<DeviceQueryType> EnumerateCalls { get; } = [];

    public Task<IReadOnlyList<DeviceInfo>> EnumerateAsync(DeviceQueryType type, CancellationToken ct = default)
    {
        EnumerateCalls.Add(type);
        var result = _devices.TryGetValue(type, out var devices) ? devices : Array.Empty<DeviceInfo>();
        return Task.FromResult(result);
    }

    public Task<SrtSetupInfo> GetSrtSetupAsync(CancellationToken ct = default) => Task.FromResult(_srtSetup);
}

/// <summary>
/// Records every <see cref="Enqueue"/> call and detects re-entrancy, so tests can assert that
/// <see cref="ControllerInputQueue"/> delivers events one at a time even under concurrent producers.
/// </summary>
internal sealed class RecordingControllerInputSink : IControllerInputSink
{
    private readonly object _lock = new();
    private int _concurrentCalls;

    public List<ButtonEvent> Received { get; } = [];

    public bool ObservedOverlap { get; private set; }

    public void Enqueue(ButtonEvent buttonEvent)
    {
        lock (_lock)
        {
            _concurrentCalls++;
            if (_concurrentCalls > 1)
            {
                ObservedOverlap = true;
            }
        }

        // Simulate non-trivial work so overlapping calls would be likely to be observed if the
        // caller failed to serialize them.
        Thread.Sleep(1);

        lock (_lock)
        {
            Received.Add(buttonEvent);
            _concurrentCalls--;
        }
    }
}
