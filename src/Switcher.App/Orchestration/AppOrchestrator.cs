using System.IO;
using Microsoft.Extensions.Logging;
using Switcher.App.Configuration;
using Switcher.Atem;
using Switcher.Contracts;
using Switcher.Hid;
using Switcher.Hid.Backlight;
using Switcher.Hid.Input;
using Switcher.Web;

namespace Switcher.App.Orchestration;

/// <summary>
/// Core facade/orchestrator (docs/tasks/agent-A2-006-app-integration-v2.md step 2): implements the
/// 2-bus <see cref="ISwitcherConfigService"/> that <see cref="WebHost"/> calls into, the single-consumer
/// <see cref="IControllerInputSink"/> that the deprecated WS fallback feeds, and the HID switch/VR event
/// handlers that <see cref="HidInputService"/> feeds (subscribed by the composition root). Every
/// mutation that can change which sources are on PGM1/PGM2/PVW1/PVW2 is guarded by
/// <see cref="_stateLock"/>, generalized (from docs/tasks/agent-A-004-app-integration.md's single-bus
/// version) across all three input paths - HID, Web, and WS/UI - so they can never interleave into an
/// inconsistent tally or backlight state.
///
/// Drives the single <see cref="IVideoEngine"/> abstraction (libobs migration) for all source/ME/output
/// mutations, plus the concrete <see cref="AtemController"/> for ATEM relay. The v2 dual-ME/OBS-source
/// surface (SetSourceEnabled, AddSource(SourceDefinition), ...) lives on <see cref="IVideoEngine"/>;
/// <see cref="ApplyConfigAsync"/> still uses the legacy single-channel add for back-compat.
/// </summary>
public sealed class AppOrchestrator : ISwitcherConfigService, IControllerInputSink
{
    // Reserved (ControllerId, ButtonId) keys used to route HID/Web events through
    // Switcher.Atem.ButtonCommandMapping's lookup-by-ButtonEvent API, which has no other way to
    // target a specific mapping entry (docs/tasks/agent-A2-006-app-integration-v2.md: "Web DTO ->
    // 既存 AtemCommandMapping/ButtonCommandMapping への変換をApp側で行う").
    private const string HidRelayControllerId = "__hid__";
    private const string DirectCommandControllerId = "__direct__";
    private const int DirectCommandButtonId = 0;

    private readonly IVideoEngine _engine;
    private readonly AtemController _atemController;
    private readonly ITallyBroadcaster _tallyBroadcaster;
    private readonly HidBacklightService _hidBacklightService;
    private readonly BacklightCalculator _backlightCalculator = new();
    private readonly RuntimeConfigStore _runtimeConfigStore;
    private readonly AppConfig _config;
    private readonly ILogger<AppOrchestrator> _logger;

    private readonly IReadOnlyDictionary<(string ControllerId, int ButtonId), CompositeButtonMapping> _compositeMappings;

    private readonly object _stateLock = new();

    // --- v1 (legacy int-channel) shadow state, used only by ApplyConfigAsync/HandleCompositeAction. ---
    private readonly Dictionary<int, PipSettings> _previewSettings = new();
    private IReadOnlyList<int> _programActiveChannels = [];

    // --- v2 (id-based, dual-bus) shadow state. CompositorEngine exposes no query for "what source
    // ids are currently enabled on bus X", so - like the v1 state above - App must track it itself to
    // compute TallyStateV2/BacklightCalculator inputs without re-deriving them from GPU frame state. ---
    private readonly Dictionary<ProgramBus, HashSet<string>> _programSourceIds = new()
    {
        [ProgramBus.Pgm1] = [],
        [ProgramBus.Pgm2] = [],
    };

    private readonly Dictionary<ProgramBus, HashSet<string>> _previewSourceIds = new()
    {
        [ProgramBus.Pgm1] = [],
        [ProgramBus.Pgm2] = [],
    };

    private IReadOnlyList<ModuleMapping> _moduleMappings = [];
    private Dictionary<(string ControllerId, int ButtonId), AtemCommandMapping> _atemMappingTable = new();
    private RuntimeConfig _runtimeConfig;

    public AppOrchestrator(
        IVideoEngine engine,
        AtemController atemController,
        ITallyBroadcaster tallyBroadcaster,
        HidBacklightService hidBacklightService,
        RuntimeConfigStore runtimeConfigStore,
        AppConfig config,
        ILogger<AppOrchestrator> logger)
    {
        _engine = engine;
        _atemController = atemController;
        _tallyBroadcaster = tallyBroadcaster;
        _hidBacklightService = hidBacklightService;
        _runtimeConfigStore = runtimeConfigStore;
        _config = config;
        _logger = logger;
        _compositeMappings = config.CompositeButtonMappings.ToDictionary(m => (m.ControllerId, m.ButtonId));

        _runtimeConfig = runtimeConfigStore.Current;
        _moduleMappings = _runtimeConfig.ModuleMappings;

        lock (_stateLock)
        {
            RebuildAtemMappingLocked();
        }
    }

    /// <summary>
    /// Replays the persisted output routing onto the engine. Must run <em>after</em>
    /// <see cref="IVideoEngine.StartAsync"/> - the libobs engine's output methods reject calls before the
    /// native context exists, so this cannot live in the constructor (the DI graph is built before the
    /// startup sequence boots the engine). The app host invokes this as the first step after the engine
    /// starts.
    /// </summary>
    public void RestorePersistedOutputs()
    {
        try
        {
            _engine.ApplyOutputs(new OutputsRequest(_runtimeConfig.OutputAssignments));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Persisted output assignments were invalid; keeping engine defaults.");
        }
    }

    /// <summary>Raised whenever a legacy v1 (single-bus) PGM/PVW-affecting change is published.</summary>
    public event EventHandler<TallyState>? TallyChanged;

    /// <summary>Raised whenever a 2-bus PGM/PVW-affecting change is published, so the UI can mirror the
    /// broadcast tally without listening to its own UDP packets.</summary>
    public event EventHandler<TallyStateV2>? TallyChangedV2;

    /// <summary>Raised whenever the multiview cell layout changes (Web-driven or loaded at startup), so
    /// the 4x4 multiview UI can mirror it.</summary>
    public event EventHandler<MultiviewLayout>? MultiviewChanged;

    /// <summary>The multiview layout most recently applied (via Web or persisted from a previous run).
    /// Carries the merged-region form when one was applied (requirement 3), falling back to the legacy
    /// 16-cell form for back-compat.</summary>
    public MultiviewLayout CurrentMultiviewLayout =>
        new(_runtimeConfig.MultiviewCells, _runtimeConfig.MultiviewGrid, _runtimeConfig.MultiviewRegions);

    /// <summary>The module mappings most recently applied (via Web or persisted from a previous run).</summary>
    public IReadOnlyList<ModuleMapping> CurrentModuleMappings
    {
        get { lock (_stateLock) { return _moduleMappings; } }
    }

    public Task ApplyConfigAsync(ConfigChangeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_stateLock)
        {
            _engine.AddSource(request.TargetChannel, request.SourceType, request.SourceUrl);

            if (request.PipSettings is { } pip)
            {
                _previewSettings[request.TargetChannel] = pip;
                _engine.ApplyPipSettings(request.TargetChannel, pip);
                PublishTallyV1Locked();
            }
        }

        return Task.CompletedTask;
    }

    public Task AddSourceAsync(SourceDefinition source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_stateLock)
        {
            _engine.AddSource(source);
        }

        return Task.CompletedTask;
    }

    public Task UpdateSourceAsync(string id, SourceDefinition source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_stateLock)
        {
            _engine.AddSource(source with { Id = id });
        }

        return Task.CompletedTask;
    }

    public Task RemoveSourceAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        lock (_stateLock)
        {
            // Unmount from both buses first so no stale PGM/PVW layer survives the source's removal
            // (CompositorEngine has no "source was deleted" notion of its own).
            _engine.SetSourceEnabled(ProgramBus.Pgm1, id, enabled: false);
            _engine.SetSourceEnabled(ProgramBus.Pgm2, id, enabled: false);
            _engine.RemoveSource(id);

            foreach (var bus in _previewSourceIds.Keys)
            {
                _previewSourceIds[bus].Remove(id);
                _programSourceIds[bus].Remove(id);
            }

            RecomputeAndPublishV2Locked();
        }

        return Task.CompletedTask;
    }

    public Task ApplyProgramAsync(ProgramRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_stateLock)
        {
            _engine.ApplyProgram(request);

            var resolvedIds = request.Layers
                .Select(layer => layer.SourceId)
                .Where(id => _engine.TryResolveChannel(id, out _))
                .ToHashSet();
            _previewSourceIds[request.Bus] = resolvedIds;

            if (request.Take)
            {
                _programSourceIds[request.Bus] = [.. resolvedIds];
            }

            RecomputeAndPublishV2Locked();
        }

        return Task.CompletedTask;
    }

    public Task ApplyMultiviewAsync(MultiviewLayout layout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);

        lock (_stateLock)
        {
            SaveRuntimeConfigLocked(_runtimeConfig with
            {
                MultiviewCells = layout.Cells,
                MultiviewGrid = layout.Grid,
                MultiviewRegions = layout.Regions,
            });
        }

        MultiviewChanged?.Invoke(this, layout);
        return Task.CompletedTask;
    }

    public Task ApplyOutputsAsync(OutputsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_stateLock)
        {
            _engine.ApplyOutputs(request);
            SaveRuntimeConfigLocked(_runtimeConfig with { OutputAssignments = _engine.CurrentAssignments });
        }

        return Task.CompletedTask;
    }

    public Task ApplyModulesAsync(ModulesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_stateLock)
        {
            _moduleMappings = request.Modules;
            SaveRuntimeConfigLocked(_runtimeConfig with { ModuleMappings = request.Modules });
            RecomputeAndPublishV2Locked();
        }

        return Task.CompletedTask;
    }

    public Task ApplyAtemConfigAsync(AtemConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_stateLock)
        {
            SaveRuntimeConfigLocked(_runtimeConfig with { AtemConfig = config });
            RebuildAtemMappingLocked();

            if (config.Enabled && !string.IsNullOrWhiteSpace(config.Ip))
            {
                _atemController.Connect(config.Ip);
            }
        }

        return Task.CompletedTask;
    }

    public Task SendAtemCommandAsync(AtemCommandRequest command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_stateLock)
        {
            if (!Enum.TryParse<AtemAction>(command.Action, ignoreCase: true, out var action))
            {
                throw new ArgumentException($"Unknown ATEM action '{command.Action}'.", nameof(command));
            }

            var table = new Dictionary<(string, int), AtemCommandMapping>(_atemMappingTable)
            {
                [(DirectCommandControllerId, DirectCommandButtonId)] =
                    new AtemCommandMapping(action, (byte)command.MixEffect, (ushort)command.Source),
            };

            _atemController.SetMapping(new ButtonCommandMapping(table));
            _atemController.SendCommand(new ButtonEvent(DirectCommandControllerId, DirectCommandButtonId, TimestampMs()));
        }

        return Task.CompletedTask;
    }

    public Task ApplyPicoNetworkConfigAsync(PicoNetworkConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_stateLock)
        {
            // Persistence is the concrete deliverable here; pushing the config over a HID feature
            // report/wireless channel is a future extension (IHidDevice exposes no feature-report
            // primitive today - docs/tasks/agent-A2-006-app-integration-v2.md step 6: "実投入は将来拡張枠でも可").
            SaveRuntimeConfigLocked(_runtimeConfig with { PicoNetwork = config });
        }

        return Task.CompletedTask;
    }

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

    /// <summary>Subscribed to <see cref="HidInputService.SwitchEdge"/> by the composition root
    /// (docs/tasks/agent-A2-006-app-integration-v2.md step 2): translates a physical module switch
    /// edge into either a PGM-bus mount/unmount (<c>IVideoEngine.SetSourceEnabled</c>) or an
    /// ATEM relay, per whichever (module_index, switch) pairs are present in the current
    /// <see cref="AtemConfig"/> mapping loaded via <see cref="ApplyAtemConfigAsync"/>.</summary>
    public void HandleSwitchEdge(SwitchEdgeEvent edge)
    {
        lock (_stateLock)
        {
            var mapping = _moduleMappings.FirstOrDefault(m => m.Index == edge.ModuleIndex);
            if (mapping is null)
            {
                return;
            }

            var (bus, binding) = ResolveBinding(mapping, edge.Switch);
            if (binding.SourceId is not { } sourceId)
            {
                return;
            }

            var relayKey = (HidRelayControllerId, HidButtonKey(edge.ModuleIndex, edge.Switch));
            if (edge.IsRising && _atemMappingTable.ContainsKey(relayKey))
            {
                _atemController.SendCommand(new ButtonEvent(relayKey.Item1, relayKey.Item2, TimestampMs()));
                return;
            }

            _engine.SetSourceEnabled(bus, sourceId, edge.IsRising);
            if (edge.IsRising)
            {
                _previewSourceIds[bus].Add(sourceId);
            }
            else
            {
                _previewSourceIds[bus].Remove(sourceId);
            }

            RecomputeAndPublishV2Locked();
        }
    }

    /// <summary>Subscribed to <see cref="HidInputService.VrChanged"/> by the composition root. Only
    /// <c>"Opacity"</c> is wired today, and only for sources already mounted on PGM1: PGM2 has no
    /// bus-aware "update one layer's settings" primitive in <c>IVideoEngine</c> today (only
    /// the PGM1-only <c>IVideoEngine.ApplyPipSettings</c> compat shim), and adding one is out
    /// of this task's subtree (<c>Switcher.Media</c> is owned by agent-A2-002).</summary>
    public void HandleVrChanged(VrChangedEvent vrEvent)
    {
        lock (_stateLock)
        {
            var mapping = _moduleMappings.FirstOrDefault(m => m.Index == vrEvent.ModuleIndex);
            if (mapping is null)
            {
                return;
            }

            var binding = vrEvent.Channel == VrChannel.Src1 ? mapping.Src1 : mapping.Src2;
            if (binding.SourceId is not { } sourceId ||
                !string.Equals(binding.VrTarget, nameof(VrTarget.Opacity), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!_engine.TryResolveChannel(sourceId, out var channel) ||
                !_previewSettings.TryGetValue(channel, out var current))
            {
                return;
            }

            var updated = current with { Opacity = vrEvent.Value / 255.0 };
            _previewSettings[channel] = updated;
            _engine.ApplyPipSettings(channel, updated);
        }
    }

    private void HandleCompositeAction(CompositeButtonMapping mapping)
    {
        lock (_stateLock)
        {
            switch (mapping.Action)
            {
                case CompositeAction.Take:
                    _engine.Take();
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

            PublishTallyV1Locked();
        }
    }

    /// <summary>Must be called while holding <see cref="_stateLock"/>.</summary>
    private void ToggleChannelLocked(int channel)
    {
        var current = _previewSettings.TryGetValue(channel, out var existing) ? existing : DefaultPipSettings(channel);
        var updated = current with { Enabled = !current.Enabled };
        _previewSettings[channel] = updated;
        _engine.ApplyPipSettings(channel, updated);
    }

    private static PipSettings DefaultPipSettings(int channel) =>
        new(
            Enabled: true,
            X: 0,
            Y: 0,
            Width: EngineDefaults.CanvasWidth,
            Height: EngineDefaults.CanvasHeight,
            Opacity: 1.0,
            ZOrder: channel,
            Crop: null);

    /// <summary>Must be called while holding <see cref="_stateLock"/>.</summary>
    private IReadOnlyList<int> EnabledChannelsLocked() =>
        _previewSettings.Where(kv => kv.Value.Enabled).Select(kv => kv.Key).OrderBy(c => c).ToList();

    /// <summary>Must be called while holding <see cref="_stateLock"/>.</summary>
    private void PublishTallyV1Locked()
    {
        var state = new TallyState(_programActiveChannels, EnabledChannelsLocked());
        _tallyBroadcaster.Publish(state);
        TallyChanged?.Invoke(this, state);
    }

    /// <summary>Recomputes and broadcasts the 2-bus tally, then recomputes and best-effort sends module
    /// backlight state. Must be called while holding <see cref="_stateLock"/>. Backlight send failures
    /// are logged and swallowed - a HID I/O fault must never block PGM/PVW state changes
    /// (docs/specs/00-system-overview.md §5: 1つの障害が全体を止めない).</summary>
    private void RecomputeAndPublishV2Locked()
    {
        var tallyV2 = new TallyStateV2(
            ActivePgm1: ResolveChannelsLocked(_programSourceIds[ProgramBus.Pgm1]),
            ActivePgm2: ResolveChannelsLocked(_programSourceIds[ProgramBus.Pgm2]),
            ActivePvw1: ResolveChannelsLocked(_previewSourceIds[ProgramBus.Pgm1]),
            ActivePvw2: ResolveChannelsLocked(_previewSourceIds[ProgramBus.Pgm2]));

        _tallyBroadcaster.Publish(tallyV2);
        TallyChangedV2?.Invoke(this, tallyV2);

        var programState = new ProgramBusesState(_programSourceIds[ProgramBus.Pgm1], _programSourceIds[ProgramBus.Pgm2]);
        var previewState = new PreviewBusesState(_previewSourceIds[ProgramBus.Pgm1], _previewSourceIds[ProgramBus.Pgm2]);
        var reports = _backlightCalculator.Compute(programState, previewState, _moduleMappings);

        try
        {
            _hidBacklightService.Send(reports);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or IOException)
        {
            _logger.LogWarning(ex, "Failed to send HID backlight update.");
        }
    }

    private List<int> ResolveChannelsLocked(IReadOnlySet<string> sourceIds)
    {
        var channels = new List<int>();
        foreach (var id in sourceIds)
        {
            if (_engine.TryResolveChannel(id, out var channel))
            {
                channels.Add(channel);
            }
        }

        channels.Sort();
        return channels;
    }

    /// <summary>Must be called while holding <see cref="_stateLock"/>.</summary>
    private void SaveRuntimeConfigLocked(RuntimeConfig updated)
    {
        _runtimeConfig = updated;
        _runtimeConfigStore.Save(updated);
    }

    /// <summary>Rebuilds the ATEM button-command table from the persisted <see cref="AtemConfig"/>
    /// (HID module/switch relays, keyed under <see cref="HidRelayControllerId"/>) plus the legacy
    /// static <c>AppConfig.AtemButtonMappings</c> (WS fallback, keyed by their own controller/button
    /// ids) and hot-swaps it into <see cref="AtemController"/>. Must be called while holding
    /// <see cref="_stateLock"/>.</summary>
    private void RebuildAtemMappingLocked()
    {
        var table = new Dictionary<(string, int), AtemCommandMapping>();

        foreach (var mapping in _runtimeConfig.AtemConfig.Mappings)
        {
            if (TryParseSwitch(mapping.Switch, out var switchId) &&
                Enum.TryParse<AtemAction>(mapping.Action, ignoreCase: true, out var action))
            {
                table[(HidRelayControllerId, HidButtonKey(mapping.ModuleIndex, switchId))] =
                    new AtemCommandMapping(action, (byte)mapping.MixEffect, (ushort)mapping.Source);
            }
            else
            {
                _logger.LogWarning(
                    "Skipping unrecognized ATEM mapping for module {ModuleIndex} switch '{Switch}' action '{Action}'.",
                    mapping.ModuleIndex, mapping.Switch, mapping.Action);
            }
        }

        foreach (var mapping in _config.AtemButtonMappings)
        {
            table[(mapping.ControllerId, mapping.ButtonId)] = new AtemCommandMapping(mapping.Action, mapping.MixEffect, mapping.Source);
        }

        _atemMappingTable = table;
        _atemController.SetMapping(new ButtonCommandMapping(table));
    }

    private static (ProgramBus Bus, ModuleSourceBinding Binding) ResolveBinding(ModuleMapping mapping, SwitchId switchId) => switchId switch
    {
        SwitchId.Pgm1Src1 => (ProgramBus.Pgm1, mapping.Src1),
        SwitchId.Pgm1Src2 => (ProgramBus.Pgm1, mapping.Src2),
        SwitchId.Pgm2Src1 => (ProgramBus.Pgm2, mapping.Src1),
        SwitchId.Pgm2Src2 => (ProgramBus.Pgm2, mapping.Src2),
        _ => throw new ArgumentOutOfRangeException(nameof(switchId), switchId, "Unsupported switch id."),
    };

    private static bool TryParseSwitch(string switchToken, out SwitchId switchId) =>
        Enum.TryParse(switchToken.Replace("_", "", StringComparison.Ordinal), ignoreCase: true, out switchId);

    private static int HidButtonKey(int moduleIndex, SwitchId switchId) =>
        (moduleIndex * ProtocolConstants.SwitchesPerModule) + (int)switchId;

    private static long TimestampMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
