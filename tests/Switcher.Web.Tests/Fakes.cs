using Switcher.Contracts;

namespace Switcher.Web.Tests;

internal sealed class FakeSwitcherConfigService : ISwitcherConfigService
{
    public List<ConfigChangeRequest> Received { get; } = [];

    public Task ApplyConfigAsync(ConfigChangeRequest request, CancellationToken cancellationToken = default)
    {
        Received.Add(request);
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
