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

internal sealed class FakeInputSourceManager : IInputSourceManager
{
    private readonly List<SourceInfo> _sources;

    public FakeInputSourceManager(IEnumerable<SourceInfo> sources) => _sources = [.. sources];

    public IReadOnlyList<SourceInfo> GetSources() => _sources;

    public void AddSource(int channel, SourceProtocol protocol, string? sourceUrl) =>
        _sources.Add(new SourceInfo(channel, $"ch{channel}", protocol, null, SourceStatus.Connected));

    public void RemoveSource(int channel) => _sources.RemoveAll(s => s.Channel == channel);

    public event EventHandler<SourceInfo>? SourceStatusChanged;

    public void RaiseStatusChanged(SourceInfo info) => SourceStatusChanged?.Invoke(this, info);
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
