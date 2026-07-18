using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Switcher.App.Configuration;
using Switcher.Contracts;
using Switcher.Media;
using Switcher.Web;

namespace Switcher.App.Orchestration;

/// <summary>
/// Core facade/orchestrator (docs/tasks/agent-A-004-app-integration.md step 2): implements the
/// service interface that <see cref="WebHost"/> calls into for config changes
/// (<see cref="ISwitcherConfigService"/>, delegated to <see cref="IInputSourceManager"/> /
/// <see cref="ICompositorEngine"/>) and the single-consumer sink that
/// <see cref="Switcher.Web.ControllerInputQueue"/> feeds serialized button events into
/// (<see cref="IControllerInputSink"/>). Every mutation that can change which channels are on
/// PGM/PVW is guarded by <see cref="_stateLock"/> so a main+sub operator pressing buttons at the
/// same time as a WebUI config change can never interleave into an inconsistent tally.
/// </summary>
public sealed class AppOrchestrator : ISwitcherConfigService, IControllerInputSink
{
    private readonly IInputSourceManager _sourceManager;
    private readonly ICompositorEngine _compositor;
    private readonly IAtemController _atemController;
    private readonly ITallyBroadcaster _tallyBroadcaster;
    private readonly ILogger<AppOrchestrator> _logger;

    private readonly IReadOnlyDictionary<(string ControllerId, int ButtonId), CompositeButtonMapping> _compositeMappings;

    private readonly object _stateLock = new();
    private readonly ConcurrentDictionary<int, PipSettings> _previewSettings = new();
    private IReadOnlyList<int> _programActiveChannels = [];

    public AppOrchestrator(
        IInputSourceManager sourceManager,
        ICompositorEngine compositor,
        IAtemController atemController,
        ITallyBroadcaster tallyBroadcaster,
        AppConfig config,
        ILogger<AppOrchestrator> logger)
    {
        _sourceManager = sourceManager;
        _compositor = compositor;
        _atemController = atemController;
        _tallyBroadcaster = tallyBroadcaster;
        _logger = logger;
        _compositeMappings = config.CompositeButtonMappings.ToDictionary(m => (m.ControllerId, m.ButtonId));
    }

    /// <summary>Raised whenever a PGM/PVW-affecting change is published, so the UI can mirror the
    /// broadcast tally without listening to its own UDP packets.</summary>
    public event EventHandler<TallyState>? TallyChanged;

    public Task ApplyConfigAsync(ConfigChangeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_stateLock)
        {
            _sourceManager.AddSource(request.TargetChannel, request.SourceType, request.SourceUrl);

            if (request.PipSettings is { } pip)
            {
                _previewSettings[request.TargetChannel] = pip;
                _compositor.ApplyPipSettings(request.TargetChannel, pip);
                PublishTallyLocked();
            }
        }

        return Task.CompletedTask;
    }

    // The v2 API surface below (agent-A2-003-web-api-v2) is wired into WebHostEndpoints now, but
    // routing it into real core state (2-bus compositor, OBS-style sources, multiview, outputs,
    // modules, ATEM, Pico network) is the direct integration work of agent-A2-006-app-integration-v2.
    public Task AddSourceAsync(SourceDefinition source, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Wired by agent-A2-006-app-integration-v2.");

    public Task UpdateSourceAsync(string id, SourceDefinition source, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Wired by agent-A2-006-app-integration-v2.");

    public Task RemoveSourceAsync(string id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Wired by agent-A2-006-app-integration-v2.");

    public Task ApplyProgramAsync(ProgramRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Wired by agent-A2-006-app-integration-v2.");

    public Task ApplyMultiviewAsync(MultiviewLayout layout, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Wired by agent-A2-006-app-integration-v2.");

    public Task ApplyOutputsAsync(OutputsRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Wired by agent-A2-006-app-integration-v2.");

    public Task ApplyModulesAsync(ModulesRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Wired by agent-A2-006-app-integration-v2.");

    public Task ApplyAtemConfigAsync(AtemConfig config, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Wired by agent-A2-006-app-integration-v2.");

    public Task SendAtemCommandAsync(AtemCommandRequest command, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Wired by agent-A2-006-app-integration-v2.");

    public Task ApplyPicoNetworkConfigAsync(PicoNetworkConfig config, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Wired by agent-A2-006-app-integration-v2.");

    public void Enqueue(ButtonEvent buttonEvent)
    {
        ArgumentNullException.ThrowIfNull(buttonEvent);

        if (_compositeMappings.TryGetValue((buttonEvent.ControllerId, buttonEvent.ButtonId), out var mapping))
        {
            HandleCompositeAction(mapping);
            return;
        }

        // Not a locally-handled button: relay to the ATEM Mini. Safe to call unconditionally -
        // AtemController drops/no-ops when disconnected or when it has no mapping either.
        _atemController.SendCommand(buttonEvent);
    }

    private void HandleCompositeAction(CompositeButtonMapping mapping)
    {
        lock (_stateLock)
        {
            switch (mapping.Action)
            {
                case CompositeAction.Take:
                    _compositor.Take();
                    _programActiveChannels = EnabledChannelsLocked();
                    break;

                case CompositeAction.TogglePip:
                    if (mapping.Channel is { } channel)
                    {
                        ToggleChannelLocked(channel);
                    }
                    else
                    {
                        _logger.LogWarning("TogglePip mapping for button {ButtonId} has no channel configured.", mapping.ButtonId);
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mapping), mapping.Action, "Unsupported composite action.");
            }

            PublishTallyLocked();
        }
    }

    /// <summary>Must be called while holding <see cref="_stateLock"/>.</summary>
    private void ToggleChannelLocked(int channel)
    {
        var current = _previewSettings.TryGetValue(channel, out var existing) ? existing : DefaultPipSettings(channel);
        var updated = current with { Enabled = !current.Enabled };
        _previewSettings[channel] = updated;
        _compositor.ApplyPipSettings(channel, updated);
    }

    private static PipSettings DefaultPipSettings(int channel) =>
        new(
            Enabled: true,
            X: 0,
            Y: 0,
            Width: CompositorEngine.DefaultCanvasWidth,
            Height: CompositorEngine.DefaultCanvasHeight,
            Opacity: 1.0,
            ZOrder: channel,
            Crop: null);

    /// <summary>Must be called while holding <see cref="_stateLock"/>.</summary>
    private IReadOnlyList<int> EnabledChannelsLocked() =>
        _previewSettings.Where(kv => kv.Value.Enabled).Select(kv => kv.Key).OrderBy(c => c).ToList();

    /// <summary>Must be called while holding <see cref="_stateLock"/>.</summary>
    private void PublishTallyLocked()
    {
        var state = new TallyState(_programActiveChannels, EnabledChannelsLocked());
        _tallyBroadcaster.Publish(state);
        TallyChanged?.Invoke(this, state);
    }
}
