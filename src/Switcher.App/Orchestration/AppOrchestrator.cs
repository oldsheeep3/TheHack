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
    private BacklightCalculator _backlightCalculator = new();
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

    // The definition each id was created from, kept so a source whose device was busy/absent at creation
    // can be re-applied later (see ReconnectSourceAsync) - IVideoEngine only reports SourceInfo, not the
    // device/URL settings a re-open needs.
    private readonly Dictionary<string, SourceDefinition> _sourceDefinitions = new(StringComparer.Ordinal);

    private bool _backlightFailureReported;
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
        var outputs = _runtimeConfig.OutputAssignments;

        // A table saved before every-bus-needs-an-output was enforced can leave PGM2 with nowhere to go.
        // Coming up on the defaults is better than coming up with a dead program bus, and the operator
        // can re-route from the Outputs dock either way.
        if (OutputRules.DescribeMissingBuses(OutputRules.MissingBuses(outputs)) is { } missing)
        {
            _logger.LogWarning("{Problem} Falling back to the default output routing.", missing);
            outputs = OutputDefaults.Default;
            lock (_stateLock)
            {
                SaveRuntimeConfigLocked(_runtimeConfig with { OutputAssignments = outputs });
            }
        }

        try
        {
            _engine.ApplyOutputs(new OutputsRequest(outputs));
            WarnAboutDeadBuses();
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Persisted output assignments were invalid; keeping engine defaults.");
        }
    }

    /// <summary>
    /// Re-creates the persisted input sources on the engine and re-applies the persisted multiview
    /// layout, so a restart comes back with the same inputs and the same cell arrangement. Runs in the
    /// same post-<see cref="IVideoEngine.StartAsync"/> step as <see cref="RestorePersistedOutputs"/>,
    /// and before the operator window is built so its tiles/cells populate from the restored state.
    ///
    /// A source whose device is gone (unplugged webcam, deleted image file) fails to create; it is
    /// logged and skipped rather than dropped from the config, so plugging the device back in and
    /// pressing Reconnect restores it without re-adding it by hand.
    /// </summary>
    public void RestorePersistedSources()
    {
        List<SourceDefinition> sources;
        lock (_stateLock)
        {
            // Mixes reference other sources by id, and the native layer can only wire up members that
            // already exist, so restore every plain source before any mix regardless of the order they
            // happen to sit in the config file.
            sources = [.. _runtimeConfig.SourceList.OrderBy(s => s.Type == SourceType.Mix ? 1 : 0)];
            foreach (var source in sources)
            {
                _sourceDefinitions[source.Id] = source;
            }
        }

        foreach (var source in sources)
        {
            try
            {
                _engine.AddSource(source);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                _logger.LogWarning(
                    ex, "Could not restore persisted source {SourceId} ({Type}); it stays configured but disconnected.",
                    source.Id, source.Type);
            }
        }

        try
        {
            _engine.ApplyMultiview(CurrentMultiviewLayout);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Could not restore the persisted multiview layout onto the engine.");
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
            _sourceDefinitions[source.Id] = source;
            PersistSourcesLocked();

            // An ON source has to be audible the moment it exists, and an AFV one has to start silent.
            // Waiting for the next bus change would leave either in the wrong state.
            RefreshSourceAudioLocked();
        }

        return Task.CompletedTask;
    }

    public Task UpdateSourceAsync(string id, SourceDefinition source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_stateLock)
        {
            var updated = source with { Id = id };
            _engine.AddSource(updated);
            _sourceDefinitions[id] = updated;
            PersistSourcesLocked();
            RefreshSourceAudioLocked();  // the audio mode may have changed
        }

        return Task.CompletedTask;
    }

    /// <summary>Re-applies a source's definition to the engine so it re-opens its device. A capture
    /// device that was busy or unplugged when the source was created (e.g. a second source bound to a
    /// webcam another source already held) never retries on its own, leaving a permanently disconnected
    /// tile with no recovery path short of restarting the app.</summary>
    /// <returns><c>false</c> when the id has no remembered definition (legacy channel-based sources).</returns>
    public Task<bool> ReconnectSourceAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        lock (_stateLock)
        {
            if (!_sourceDefinitions.TryGetValue(id, out var definition))
            {
                return Task.FromResult(false);
            }

            _engine.AddSource(definition);
        }

        return Task.FromResult(true);
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
            _sourceDefinitions.Remove(id);
            PersistSourcesLocked();

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

            if (request.Take)
            {
                // The native engine stages the layers onto the preview scene and then swaps the bus's
                // program/preview scenes, so after a TAKE the *previous* program composition is what sits
                // on PVW (hardware-switcher behaviour). Mirror that swap here or the broadcast tally and
                // the module backlights keep reporting the just-taken sources as still staged.
                _previewSourceIds[request.Bus] = _programSourceIds[request.Bus];
                _programSourceIds[request.Bus] = resolvedIds;
            }
            else
            {
                _previewSourceIds[request.Bus] = resolvedIds;
            }

            RecomputeAndPublishV2Locked();
        }

        return Task.CompletedTask;
    }

    /// <summary>Takes PVW→PGM on a single bus (<paramref name="durationMs"/> 0 = CUT, &gt;0 = AUTO fade),
    /// mirroring the native program/preview scene swap into the shadow state that drives tally and
    /// module backlights. The other bus is untouched.</summary>
    public Task TakeAsync(ProgramBus bus, int durationMs = 0, CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            _engine.Take(bus, durationMs);
            (_programSourceIds[bus], _previewSourceIds[bus]) = (_previewSourceIds[bus], _programSourceIds[bus]);
            RecomputeAndPublishV2Locked();
        }

        return Task.CompletedTask;
    }

    /// <summary>The definition a source was created from, or <c>null</c> for an id that was never added
    /// through this orchestrator (legacy channel sources). Used by the mix editor to reload a mix's
    /// layers, which <see cref="IVideoEngine"/> does not report back.</summary>
    public SourceDefinition? GetSourceDefinition(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_stateLock) { return _sourceDefinitions.GetValueOrDefault(id); }
    }

    /// <summary>Snapshot of the source ids currently live on <paramref name="bus"/>'s program output,
    /// including the members of any mix on that bus (see <see cref="ExpandMixMembersLocked"/>).</summary>
    public IReadOnlySet<string> GetProgramSourceIds(ProgramBus bus)
    {
        lock (_stateLock) { return ExpandMixMembersLocked(_programSourceIds[bus]); }
    }

    /// <summary>Snapshot of the source ids currently staged on <paramref name="bus"/>'s preview,
    /// including the members of any staged mix.</summary>
    public IReadOnlySet<string> GetPreviewSourceIds(ProgramBus bus)
    {
        lock (_stateLock) { return ExpandMixMembersLocked(_previewSourceIds[bus]); }
    }

    /// <summary>
    /// Expands a bus membership set to include everything a mix on that bus is showing, recursively.
    ///
    /// A camera inside a live mix <em>is</em> on air — its pixels are being broadcast — so tally must say
    /// so. Without this the operator sees the mix lit red while the camera feeding it looks idle, and the
    /// talent in front of that camera gets no tally light at all, which is the one thing a tally system
    /// exists to prevent.
    ///
    /// Must be called while holding <see cref="_stateLock"/>.
    /// </summary>
    private HashSet<string> ExpandMixMembersLocked(IEnumerable<string> ids)
    {
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(ids);

        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (!expanded.Add(id))
            {
                continue;  // already visited; also breaks any cycle a config could describe
            }

            if (_sourceDefinitions.TryGetValue(id, out var definition) && definition.Mix is { } mix)
            {
                foreach (var layer in mix.Layers)
                {
                    pending.Push(layer.SourceId);
                }
            }
        }

        return expanded;
    }

    public Task ApplyMultiviewAsync(MultiviewLayout layout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);

        lock (_stateLock)
        {
            // Push the layout to the engine as well as persisting it: the native multiview scene is what
            // the full-screen multiview window and any MULTIVIEW-targeted display render. Without this,
            // the layout only ever reached the engine when MultiviewFullscreenWindow opened, so every
            // merge/split/cell change made afterwards was invisible on the multiview output.
            try
            {
                _engine.ApplyMultiview(layout);
            }
            catch (InvalidOperationException ex)
            {
                // Engine not started yet (layout replayed during startup) - persistence still applies and
                // MultiviewFullscreenWindow re-applies on attach.
                _logger.LogWarning(ex, "Could not push the multiview layout to the engine; persisting only.");
            }

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

        // Guarded here as well as in the Web validator, because the operator window applies outputs
        // directly and must not be able to leave a program bus going nowhere.
        if (OutputRules.DescribeMissingBuses(OutputRules.MissingBuses(request.Outputs)) is { } missing)
        {
            throw new ArgumentException(missing, nameof(request));
        }

        lock (_stateLock)
        {
            _engine.ApplyOutputs(request);
            SaveRuntimeConfigLocked(_runtimeConfig with { OutputAssignments = _engine.CurrentAssignments });
        }

        WarnAboutDeadBuses();

        return Task.CompletedTask;
    }

    /// <summary>Program buses whose sinks are all assigned but none running — an NDI sink with no NDI
    /// runtime installed, a virtual camera another application holds. Empty when everything egresses.
    /// An HDMI-only bus reads as dead until its fullscreen window is opened, which is true.</summary>
    public IReadOnlyList<OutputSource> BusesWithoutRunningOutput()
    {
        try
        {
            return OutputRules.BusesWithoutRunningOutput(_engine.QueryOutputStatus());
        }
        catch (InvalidOperationException)
        {
            return [];  // engine not started yet
        }
    }

    private void WarnAboutDeadBuses()
    {
        foreach (var bus in BusesWithoutRunningOutput())
        {
            _logger.LogWarning(
                "{Bus} has outputs assigned but none of them started. Check the NDI runtime and whether "
                + "another application is holding the virtual camera.", bus);
        }
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

        ModulesChanged?.Invoke(this, CurrentModuleMappings);
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

            // A module switch loads the source straight onto the PROGRAM bus
            // (docs/specs/00-system-overview.md §3: "各ソースを PGM1／PGM2 のどちらのプログラムバスに
            // 載せるかを直接トグルする"), which is exactly what engine_set_source_enabled does natively -
            // it mounts the item on the live program scene. Recording it as preview state (as this did)
            // made the tally broadcast and the module backlight report PVW/green for a source whose
            // pixels were already on air.
            _engine.SetSourceEnabled(bus, sourceId, edge.IsRising);
            if (edge.IsRising)
            {
                _programSourceIds[bus].Add(sourceId);
            }
            else
            {
                _programSourceIds[bus].Remove(sourceId);
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
        // Everything downstream of here - the UDP tally, the module backlights, the UI frames - works
        // from the *expanded* sets, so a source inside a live mix counts as on air everywhere.
        var pgm1 = ExpandMixMembersLocked(_programSourceIds[ProgramBus.Pgm1]);
        var pgm2 = ExpandMixMembersLocked(_programSourceIds[ProgramBus.Pgm2]);
        var pvw1 = ExpandMixMembersLocked(_previewSourceIds[ProgramBus.Pgm1]);
        var pvw2 = ExpandMixMembersLocked(_previewSourceIds[ProgramBus.Pgm2]);

        var tallyV2 = new TallyStateV2(
            ActivePgm1: ResolveChannelsLocked(pgm1),
            ActivePgm2: ResolveChannelsLocked(pgm2),
            ActivePvw1: ResolveChannelsLocked(pvw1),
            ActivePvw2: ResolveChannelsLocked(pvw2));

        // AFV lives or dies on this: the moment what is on air changes is the moment an audio-follows-
        // video source must become audible or fall silent.
        RefreshSourceAudioLocked();

        _tallyBroadcaster.Publish(tallyV2);
        TallyChangedV2?.Invoke(this, tallyV2);

        var programState = new ProgramBusesState(pgm1, pgm2);
        var previewState = new PreviewBusesState(pvw1, pvw2);
        var reports = _backlightCalculator.Compute(programState, previewState, _moduleMappings);

        try
        {
            _hidBacklightService.Send(reports);
            _backlightFailureReported = false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or IOException)
        {
            // Once per outage, not once per bus change: with no controller attached this fires on every
            // TAKE, and a log that repeats the same line hundreds of times is a log nobody reads.
            if (!_backlightFailureReported)
            {
                _backlightFailureReported = true;
                _logger.LogWarning(ex, "Failed to send HID backlight update; further failures are logged at debug level.");
            }
            else
            {
                _logger.LogDebug(ex, "Failed to send HID backlight update.");
            }
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

    /// <summary>Writes the current source registry into the persisted runtime config. Must be called
    /// while holding <see cref="_stateLock"/>.</summary>
    private void PersistSourcesLocked() =>
        SaveRuntimeConfigLocked(_runtimeConfig with { Sources = [.. _sourceDefinitions.Values] });

    // ── audio ───────────────────────────────────────────────────────────────────

    private const int Pgm1Bit = 1 << 0;
    private const int Pgm2Bit = 1 << 1;

    /// <summary>
    /// Re-applies every source's audio routing from its mode and the current bus membership.
    ///
    /// libobs has no notion of audio-follows-video: a source is simply in an audio track or it isn't.
    /// AFV is therefore a policy this class owns — recompute the mask whenever what is on air changes,
    /// which is exactly the moment an AFV source should become audible or fall silent. ON sources sit in
    /// both tracks permanently, OFF in neither.
    ///
    /// Membership is the *program* set, expanded through mixes so a camera inside a live mix is heard as
    /// well as seen. Preview deliberately does not open the audio: cueing a source must never put it on
    /// air.
    ///
    /// Must be called while holding <see cref="_stateLock"/>.
    /// </summary>
    private void RefreshSourceAudioLocked()
    {
        var livePgm1 = ExpandMixMembersLocked(_programSourceIds[ProgramBus.Pgm1]);
        var livePgm2 = ExpandMixMembersLocked(_programSourceIds[ProgramBus.Pgm2]);

        foreach (var (id, definition) in _sourceDefinitions)
        {
            var mask = definition.AudioMode switch
            {
                SourceAudioMode.On => Pgm1Bit | Pgm2Bit,
                SourceAudioMode.Afv =>
                    (livePgm1.Contains(id) ? Pgm1Bit : 0) | (livePgm2.Contains(id) ? Pgm2Bit : 0),
                _ => 0,
            };

            try
            {
                _engine.SetSourceAudioMixers(id, mask);
            }
            catch (InvalidOperationException)
            {
                // Engine not started yet (startup restore); the next recompute applies it.
            }
        }
    }

    /// <summary>The audio device routing currently applied.</summary>
    public IReadOnlyList<AudioOutputAssignment> CurrentAudioOutputs
    {
        get { lock (_stateLock) { return _runtimeConfig.AudioOutputList; } }
    }

    /// <summary>Audio render endpoints this machine can play to.</summary>
    public IReadOnlyList<AudioDeviceInfo> QueryAudioDevices() => _engine.QueryAudioDevices();

    /// <summary>Routes program buses to audio devices and persists the choice. A bus may be routed to
    /// several devices, and each bus is independent of the other.</summary>
    public Task ApplyAudioOutputsAsync(AudioOutputsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_stateLock)
        {
            _engine.ApplyAudioOutputs(request);
            SaveRuntimeConfigLocked(_runtimeConfig with { AudioOutputs = [.. request.Outputs] });
        }

        return Task.CompletedTask;
    }

    /// <summary>Replays the persisted audio routing onto the engine and re-applies every source's audio
    /// mode. Runs in the same post-start step as the video outputs and sources.</summary>
    public void RestorePersistedAudio()
    {
        List<AudioOutputAssignment> outputs;
        lock (_stateLock)
        {
            outputs = [.. _runtimeConfig.AudioOutputList];
            RefreshSourceAudioLocked();
        }

        if (outputs.Count == 0)
        {
            return;
        }

        try
        {
            _engine.ApplyAudioOutputs(new AudioOutputsRequest(outputs));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Could not restore the persisted audio routing.");
        }
    }

    /// <summary>
    /// Subscribed to <see cref="HidInputService.ModulePresenceChanged"/> by the composition root:
    /// reconciles the module mapping list against the Pico's <c>module_present</c> bitmap
    /// (docs/specs/00-system-overview.md §4.1, bit n = module n attached). A module that appears gets a
    /// default unbound mapping so the operator can assign sources to it; a module that disappears loses
    /// its row entirely, so the UI never offers switches for hardware that is not plugged in. The
    /// mapping of a module that is still present is left untouched, so unplugging and re-plugging the
    /// controller does not clear the operator's bindings within a session.
    /// </summary>
    public void HandleModulePresence(byte presentMask)
    {
        bool changed;
        lock (_stateLock)
        {
            var byIndex = _moduleMappings.ToDictionary(m => m.Index);
            var reconciled = new List<ModuleMapping>();
            for (var index = 0; index < ProtocolConstants.MaxModules; index++)
            {
                if ((presentMask & (1 << index)) == 0)
                {
                    continue;
                }

                reconciled.Add(byIndex.TryGetValue(index, out var existing) ? existing : NewModuleMapping(index));
            }

            changed = !reconciled.SequenceEqual(_moduleMappings);
            if (!changed)
            {
                return;
            }

            _logger.LogInformation(
                "Controller reports modules [{Modules}] present; module list reconciled.",
                string.Join(", ", reconciled.Select(m => m.Index)));

            _moduleMappings = reconciled;
            SaveRuntimeConfigLocked(_runtimeConfig with { ModuleMappings = reconciled });
            RecomputeAndPublishV2Locked();
        }

        ModulesChanged?.Invoke(this, CurrentModuleMappings);
    }

    /// <summary>Raised when the module list itself changes (a module was attached to or detached from
    /// the controller), so the operator window can rebuild its module rows.</summary>
    public event EventHandler<IReadOnlyList<ModuleMapping>>? ModulesChanged;

    /// <summary>Raised after the tally colours change, so the UI can restyle and the controller can be
    /// re-lit.</summary>
    public event EventHandler<TallyColors>? TallyColorsChanged;

    /// <summary>The colours currently used for on-air / staged, per bus.</summary>
    public TallyColors CurrentTallyColors
    {
        get { lock (_stateLock) { return _runtimeConfig.Tally; } }
    }

    /// <summary>
    /// Applies a new tally palette. The same values drive the operator window and the module LEDs, so
    /// the controller is re-lit immediately rather than waiting for the next bus change — otherwise the
    /// panel would keep showing the old colours until someone happened to switch something.
    /// </summary>
    public Task ApplyTallyColorsAsync(TallyColors colors, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(colors);

        lock (_stateLock)
        {
            SaveRuntimeConfigLocked(_runtimeConfig with { TallyColors = colors });
            _backlightCalculator = new BacklightCalculator(new ConfiguredBacklightPolicy(colors));
            RecomputeAndPublishV2Locked();
        }

        TallyColorsChanged?.Invoke(this, colors);
        return Task.CompletedTask;
    }

    private static ModuleMapping NewModuleMapping(int index) =>
        new(index, new ModuleSourceBinding(null, nameof(VrTarget.Assignable)), new ModuleSourceBinding(null, nameof(VrTarget.Assignable)));

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
