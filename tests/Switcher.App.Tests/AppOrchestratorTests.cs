using System.IO;
using Switcher.Contracts;
using Switcher.Hid.Input;

namespace Switcher.App.Tests;

public sealed class AppOrchestratorTests
{
    [Fact]
    public async Task HandleSwitchEdge_RisingEdge_MountsSourceOnProgramBus_AndPublishesTallyV2()
    {
        using var harness = new OrchestratorTestHarness();
        var channel = harness.AddTestSource("cam-a");

        await harness.Orchestrator.ApplyModulesAsync(new ModulesRequest(
        [
            new ModuleMapping(0, new ModuleSourceBinding("cam-a", "Assignable"), new ModuleSourceBinding(null, "Assignable")),
        ]));
        harness.TallyBroadcaster.PublishedV2.Clear();

        harness.Orchestrator.HandleSwitchEdge(new SwitchEdgeEvent(0, SwitchId.Pgm1Src1, IsRising: true));

        // A module switch toggles the source directly onto the program bus - engine_set_source_enabled
        // mounts it on the live program scene natively, so the tally must report PGM, not PVW.
        var tally = Assert.Single(harness.TallyBroadcaster.PublishedV2);
        Assert.Contains(channel, tally.ActivePgm1);
        Assert.DoesNotContain(channel, tally.ActivePvw1);
    }

    [Fact]
    public async Task HandleSwitchEdge_FallingEdge_UnmountsSource()
    {
        using var harness = new OrchestratorTestHarness();
        var channel = harness.AddTestSource("cam-a");

        await harness.Orchestrator.ApplyModulesAsync(new ModulesRequest(
        [
            new ModuleMapping(0, new ModuleSourceBinding("cam-a", "Assignable"), new ModuleSourceBinding(null, "Assignable")),
        ]));

        harness.Orchestrator.HandleSwitchEdge(new SwitchEdgeEvent(0, SwitchId.Pgm2Src1, IsRising: true));
        Assert.Contains(channel, harness.TallyBroadcaster.LastV2!.ActivePgm2);

        harness.Orchestrator.HandleSwitchEdge(new SwitchEdgeEvent(0, SwitchId.Pgm2Src1, IsRising: false));
        Assert.DoesNotContain(channel, harness.TallyBroadcaster.LastV2!.ActivePgm2);
    }

    [Fact]
    public void HandleSwitchEdge_UnknownModule_IsNoOp()
    {
        using var harness = new OrchestratorTestHarness();

        harness.Orchestrator.HandleSwitchEdge(new SwitchEdgeEvent(3, SwitchId.Pgm1Src1, IsRising: true));

        Assert.Empty(harness.TallyBroadcaster.PublishedV2);
    }

    [Fact]
    public async Task HandleSwitchEdge_WithAtemRelayMapping_DoesNotMountLocally()
    {
        using var harness = new OrchestratorTestHarness();
        harness.AddTestSource("cam-a");

        await harness.Orchestrator.ApplyModulesAsync(new ModulesRequest(
        [
            new ModuleMapping(0, new ModuleSourceBinding("cam-a", "Assignable"), new ModuleSourceBinding(null, "Assignable")),
        ]));

        await harness.Orchestrator.ApplyAtemConfigAsync(new AtemConfig(
            Enabled: true,
            Ip: "127.0.0.1",
            Mappings: [new Switcher.Contracts.AtemButtonMapping("ignored", ModuleIndex: 0, Switch: "Pgm1Src1", Action: "Cut", MixEffect: 0, Source: 1)]));

        harness.TallyBroadcaster.PublishedV2.Clear();
        harness.Orchestrator.HandleSwitchEdge(new SwitchEdgeEvent(0, SwitchId.Pgm1Src1, IsRising: true));

        // Relayed to ATEM instead of mounted locally: no PGM/PVW tally change is published.
        Assert.Empty(harness.TallyBroadcaster.PublishedV2);
    }

    [Fact]
    public async Task ApplyProgramAsync_WithTake_PromotesPreviewToProgram()
    {
        using var harness = new OrchestratorTestHarness();
        var channel = harness.AddTestSource("cam-a");

        var pip = new PipSettings(true, 0, 0, 1920, 1080, 1.0, 0, null);
        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(
            ProgramBus.Pgm1,
            [new ProgramLayer("cam-a", pip)],
            Take: true));

        var tally = harness.TallyBroadcaster.LastV2!;
        Assert.Contains(channel, tally.ActivePgm1);

        // The native TAKE swaps the bus's program and preview scenes, so PVW1 holds the *previous*
        // program composition (empty here) rather than a copy of what was just taken.
        Assert.DoesNotContain(channel, tally.ActivePvw1);
    }

    [Fact]
    public async Task TakeAsync_SwapsProgramAndPreviewOnOneBusOnly()
    {
        using var harness = new OrchestratorTestHarness();
        var channelA = harness.AddTestSource("cam-a");
        var channelB = harness.AddTestSource("cam-b");

        var pip = new PipSettings(true, 0, 0, 1920, 1080, 1.0, 0, null);

        // cam-a live on PGM1, cam-b live on PGM2.
        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-a", pip)], Take: true));
        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm2, [new ProgramLayer("cam-b", pip)], Take: true));

        // Stage cam-b on PVW1 and take it: PGM1 becomes cam-b, PVW1 inherits the outgoing cam-a.
        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-b", pip)], Take: false));
        await harness.Orchestrator.TakeAsync(ProgramBus.Pgm1);

        var tally = harness.TallyBroadcaster.LastV2!;
        Assert.Equal([channelB], tally.ActivePgm1);
        Assert.Equal([channelA], tally.ActivePvw1);

        // ME2 was not touched by ME1's take.
        Assert.Equal([channelB], tally.ActivePgm2);
        Assert.Equal(ProgramBus.Pgm1, harness.Engine.LastTake!.Value.Bus);
    }

    [Fact]
    public async Task RemoveSourceAsync_UnmountsFromBothBuses()
    {
        using var harness = new OrchestratorTestHarness();
        var channel = harness.AddTestSource("cam-a");

        var pip = new PipSettings(true, 0, 0, 1920, 1080, 1.0, 0, null);
        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-a", pip)], Take: true));
        Assert.Contains(channel, harness.TallyBroadcaster.LastV2!.ActivePgm1);

        await harness.Orchestrator.RemoveSourceAsync("cam-a");

        var tally = harness.TallyBroadcaster.LastV2!;
        Assert.DoesNotContain(channel, tally.ActivePgm1);
        Assert.DoesNotContain(channel, tally.ActivePvw1);
    }

    [Fact]
    public async Task ApplyOutputsAsync_UpdatesEngineAssignments()
    {
        using var harness = new OrchestratorTestHarness();

        await harness.Orchestrator.ApplyOutputsAsync(new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm2, null, null, null),
            new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm1, null, null, null),
        ]));

        var assignment = Assert.Single(harness.Engine.CurrentAssignments, a => a.Sink == OutputSink.Vcam1);
        Assert.Equal(OutputSource.Pgm2, assignment.Source);
    }

    [Fact]
    public async Task ApplyOutputsAsync_RejectsATableThatLeavesAProgramBusWithNoOutput()
    {
        using var harness = new OrchestratorTestHarness();
        var before = harness.Engine.CurrentAssignments;

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Orchestrator.ApplyOutputsAsync(
            new OutputsRequest([new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null)])));

        // Rejected before the engine is touched, so the outputs that were live stay live.
        Assert.Equal(before, harness.Engine.CurrentAssignments);
    }

    [Fact]
    public async Task BusesWithoutRunningOutput_ReportsABusWhoseOnlySinkNeverStarted()
    {
        using var harness = new OrchestratorTestHarness();

        // The default routing sends PGM2 to VCAM2, which is an NDI sender — on a machine with no NDI
        // runtime it is accepted and then never starts, leaving PGM2 with nowhere to go.
        harness.Engine.FailingSinks.Add(OutputSink.Vcam2);
        await harness.Orchestrator.ApplyOutputsAsync(new OutputsRequest(OutputDefaults.Default));

        Assert.Equal([OutputSource.Pgm2], harness.Orchestrator.BusesWithoutRunningOutput());
    }

    [Fact]
    public async Task BusesWithoutRunningOutput_IsQuietWhenAnotherSinkOnTheBusIsRunning()
    {
        using var harness = new OrchestratorTestHarness();
        harness.Engine.FailingSinks.Add(OutputSink.Ndi2);

        await harness.Orchestrator.ApplyOutputsAsync(new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm2, null, null, null),
            new OutputAssignment(OutputSink.Ndi2, OutputSource.Pgm2, null, null, null),
        ]));

        Assert.Empty(harness.Orchestrator.BusesWithoutRunningOutput());
    }

    [Fact]
    public void RestorePersistedOutputs_FallsBackToDefaultsWhenAProgramBusHasNoOutput()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"switcher-outputs-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            // A config from before the rule existed: everything on PGM1, so PGM2 reaches nothing.
            var store = new Switcher.App.Configuration.RuntimeConfigStore(
                dir, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            store.Save(store.Current with
            {
                OutputAssignments = [new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null)],
            });

            using var harness = new OrchestratorTestHarnessAtPath(dir);
            harness.Orchestrator.RestorePersistedOutputs();

            Assert.Equal(OutputDefaults.Default, harness.Engine.CurrentAssignments);
            Assert.Equal(OutputDefaults.Default, harness.RuntimeConfigStore.Current.OutputAssignments);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyModulesAsync_PersistsToRuntimeConfigStore()
    {
        using var harness = new OrchestratorTestHarness();

        var mapping = new ModuleMapping(2, new ModuleSourceBinding("cam-a", "Opacity"), new ModuleSourceBinding(null, "Assignable"));
        await harness.Orchestrator.ApplyModulesAsync(new ModulesRequest([mapping]));

        Assert.Equal([mapping], harness.Orchestrator.CurrentModuleMappings);
        Assert.Equal([mapping], harness.RuntimeConfigStore.Current.ModuleMappings);
    }

    [Fact]
    public async Task ApplyMultiviewAsync_PersistsAndRaisesMultiviewChanged()
    {
        using var harness = new OrchestratorTestHarness();

        MultiviewLayout? raised = null;
        harness.Orchestrator.MultiviewChanged += (_, layout) => raised = layout;

        var cells = Enumerable.Repeat("EMPTY", 16).ToList();
        cells[0] = "PGM1";
        await harness.Orchestrator.ApplyMultiviewAsync(new MultiviewLayout(cells));

        Assert.Equal("PGM1", raised?.Cells[0]);
        Assert.Equal("PGM1", harness.Orchestrator.CurrentMultiviewLayout.Cells[0]);
    }

    [Fact]
    public async Task RuntimeConfig_SurvivesRestart()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"switcher-app-restart-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var mapping = new ModuleMapping(1, new ModuleSourceBinding("src-1", "Assignable"), new ModuleSourceBinding(null, "Assignable"));

            using (var first = new OrchestratorTestHarnessAtPath(dir))
            {
                await first.Orchestrator.ApplyModulesAsync(new ModulesRequest([mapping]));
            }

            using var second = new OrchestratorTestHarnessAtPath(dir);
            Assert.Equal([mapping], second.Orchestrator.CurrentModuleMappings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentHidWebAndLegacyInput_NeverThrowsAndConvergesToConsistentState()
    {
        using var harness = new OrchestratorTestHarness();
        harness.AddTestSource("cam-a");
        await harness.Orchestrator.ApplyModulesAsync(new ModulesRequest(
        [
            new ModuleMapping(0, new ModuleSourceBinding("cam-a", "Assignable"), new ModuleSourceBinding(null, "Assignable")),
        ]));

        var tasks = new List<Task>();
        for (var i = 0; i < 50; i++)
        {
            var rising = i % 2 == 0;
            tasks.Add(Task.Run(() => harness.Orchestrator.HandleSwitchEdge(new SwitchEdgeEvent(0, SwitchId.Pgm1Src1, rising))));
            tasks.Add(Task.Run(() => harness.Orchestrator.Enqueue(new ButtonEvent("main", 999, i))));
            tasks.Add(Task.Run(() => harness.Orchestrator.ApplyOutputsAsync(new OutputsRequest(
            [
                new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm1, null, null, null),
                new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm2, null, null, null),
            ]))));
        }

        await Task.WhenAll(tasks);

        // The final HandleSwitchEdge call above uses i=49 (odd -> falling), so the source ends
        // unmounted; the key assertion is that concurrent HID/Web/legacy input never throws or
        // corrupts OutputRouter's assignment table.
        var assignment = Assert.Single(harness.Engine.CurrentAssignments, a => a.Sink == OutputSink.Vcam2);
        Assert.Equal(OutputSource.Pgm1, assignment.Source);
    }
}

/// <summary>Thin variant of <see cref="OrchestratorTestHarness"/> that persists to a caller-supplied
/// directory (rather than a fresh one per instance) so <see cref="AppOrchestratorTests.RuntimeConfig_SurvivesRestart"/>
/// can simulate a restart against the same <c>runtime-config.json</c>.</summary>
internal sealed class OrchestratorTestHarnessAtPath : IDisposable
{
    private readonly Switcher.Atem.AtemController _atemController;
    private readonly Switcher.Hid.HidBacklightService _hidBacklightService;

    public OrchestratorTestHarnessAtPath(string path)
    {
        var engine = new Switcher.Engine.FakeVideoEngine();
        _atemController = new Switcher.Atem.AtemController(
            Switcher.Atem.ButtonCommandMapping.Empty,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Switcher.Atem.AtemController>.Instance);
        _hidBacklightService = new Switcher.Hid.HidBacklightService();
        var runtimeConfigStore = new Switcher.App.Configuration.RuntimeConfigStore(path, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        Engine = engine;
        RuntimeConfigStore = runtimeConfigStore;

        Orchestrator = new Switcher.App.Orchestration.AppOrchestrator(
            engine,
            _atemController,
            new FakeTallyBroadcaster(),
            _hidBacklightService,
            runtimeConfigStore,
            Switcher.App.Configuration.AppConfig.CreateDefault(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Switcher.App.Orchestration.AppOrchestrator>.Instance);
    }

    public Switcher.App.Orchestration.AppOrchestrator Orchestrator { get; }

    public Switcher.Engine.FakeVideoEngine Engine { get; }

    public Switcher.App.Configuration.RuntimeConfigStore RuntimeConfigStore { get; }

    public void Dispose()
    {
        _atemController.Dispose();
        _hidBacklightService.Dispose();
    }
}
