using Switcher.App.Multiview;
using Switcher.App.ViewModels;
using Switcher.Contracts;
using Switcher.Hid.Input;

namespace Switcher.App.Tests;

/// <summary>
/// Edge cases around the orchestrator's bus bookkeeping and the pure helpers the operator window leans
/// on. These are the paths that decide what the tally broadcast, the module backlights and the multiview
/// frames say, so "what happens when the input is odd" matters as much as the happy path.
/// </summary>
public sealed class OrchestratorEdgeCaseTests
{
    private static PipSettings FullFrame => new(true, 0, 0, 1920, 1080, 1.0, 0, null);

    // --- program / take ---------------------------------------------------------

    [Fact]
    public async Task ApplyProgram_WithUnknownSourceIds_IgnoresThem()
    {
        using var harness = new OrchestratorTestHarness();
        var channel = harness.AddTestSource("cam-a");

        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(
            ProgramBus.Pgm1,
            [new ProgramLayer("cam-a", FullFrame), new ProgramLayer("ghost", FullFrame)],
            Take: true));

        Assert.Equal([channel], harness.TallyBroadcaster.LastV2!.ActivePgm1);
    }

    [Fact]
    public async Task ApplyProgram_WithNoLayersAndTake_ClearsTheBus()
    {
        using var harness = new OrchestratorTestHarness();
        harness.AddTestSource("cam-a");
        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-a", FullFrame)], Take: true));

        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm1, [], Take: true));

        Assert.Empty(harness.TallyBroadcaster.LastV2!.ActivePgm1);
    }

    [Fact]
    public async Task Take_OnAnEmptyBus_IsHarmless()
    {
        using var harness = new OrchestratorTestHarness();

        await harness.Orchestrator.TakeAsync(ProgramBus.Pgm1);

        var tally = harness.TallyBroadcaster.LastV2!;
        Assert.Empty(tally.ActivePgm1);
        Assert.Empty(tally.ActivePvw1);
    }

    [Fact]
    public async Task Take_TwiceSwapsBackToTheOriginalComposition()
    {
        using var harness = new OrchestratorTestHarness();
        var a = harness.AddTestSource("cam-a");
        var b = harness.AddTestSource("cam-b");

        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-a", FullFrame)], Take: true));
        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-b", FullFrame)], Take: false));

        await harness.Orchestrator.TakeAsync(ProgramBus.Pgm1);   // b on air, a staged
        Assert.Equal([b], harness.TallyBroadcaster.LastV2!.ActivePgm1);

        await harness.Orchestrator.TakeAsync(ProgramBus.Pgm1);   // back to a
        Assert.Equal([a], harness.TallyBroadcaster.LastV2!.ActivePgm1);
        Assert.Equal([b], harness.TallyBroadcaster.LastV2!.ActivePvw1);
    }

    [Fact]
    public async Task Take_OnOneBusNeverTouchesTheOther()
    {
        using var harness = new OrchestratorTestHarness();
        var a = harness.AddTestSource("cam-a");
        var b = harness.AddTestSource("cam-b");

        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm2, [new ProgramLayer("cam-b", FullFrame)], Take: true));
        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-a", FullFrame)], Take: false));

        await harness.Orchestrator.TakeAsync(ProgramBus.Pgm1, durationMs: 500);

        var tally = harness.TallyBroadcaster.LastV2!;
        Assert.Equal([a], tally.ActivePgm1);
        Assert.Equal([b], tally.ActivePgm2);
        Assert.Equal(500, harness.Engine.LastTake!.Value.DurationMs);
    }

    [Fact]
    public async Task SameSourceOnBothBuses_IsReportedOnBoth()
    {
        using var harness = new OrchestratorTestHarness();
        var channel = harness.AddTestSource("cam-a");

        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-a", FullFrame)], Take: true));
        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm2, [new ProgramLayer("cam-a", FullFrame)], Take: true));

        var tally = harness.TallyBroadcaster.LastV2!;
        Assert.Contains(channel, tally.ActivePgm1);
        Assert.Contains(channel, tally.ActivePgm2);
    }

    // --- source removal ---------------------------------------------------------

    [Fact]
    public async Task RemoveSource_WhileLiveOnBothBuses_ClearsBothTallies()
    {
        using var harness = new OrchestratorTestHarness();
        harness.AddTestSource("cam-a");

        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-a", FullFrame)], Take: true));
        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm2, [new ProgramLayer("cam-a", FullFrame)], Take: true));

        await harness.Orchestrator.RemoveSourceAsync("cam-a");

        var tally = harness.TallyBroadcaster.LastV2!;
        Assert.Empty(tally.ActivePgm1);
        Assert.Empty(tally.ActivePgm2);
        Assert.Empty(tally.ActivePvw1);
        Assert.Empty(tally.ActivePvw2);
    }

    [Fact]
    public async Task RemoveSource_ThatWasNeverAdded_IsHarmless()
    {
        using var harness = new OrchestratorTestHarness();
        await harness.Orchestrator.RemoveSourceAsync("never-existed");
    }

    [Fact]
    public async Task ReconnectSource_WithUnknownId_ReportsFalse()
    {
        using var harness = new OrchestratorTestHarness();
        Assert.False(await harness.Orchestrator.ReconnectSourceAsync("nope"));
    }

    [Fact]
    public async Task ReconnectSource_ReAppliesTheOriginalDefinition()
    {
        using var harness = new OrchestratorTestHarness();
        var definition = new SourceDefinition("cam-a", "Cam A", SourceType.Webcam, null, new WebcamConfig("dev-1", "1280x720@30"), null);
        await harness.Orchestrator.AddSourceAsync(definition);

        Assert.True(await harness.Orchestrator.ReconnectSourceAsync("cam-a"));
        Assert.Equal(["cam-a", "cam-a"], harness.Engine.AddedSourceIds);
    }

    [Fact]
    public async Task UpdateSource_ReplacesTheRememberedDefinition()
    {
        using var harness = new OrchestratorTestHarness();
        await harness.Orchestrator.AddSourceAsync(
            new SourceDefinition("cam-a", "Old", SourceType.Webcam, null, new WebcamConfig("dev-1", null), null));

        await harness.Orchestrator.UpdateSourceAsync("cam-a",
            new SourceDefinition("ignored", "New", SourceType.Webcam, null, new WebcamConfig("dev-2", null), null));

        var stored = harness.Orchestrator.GetSourceDefinition("cam-a")!;
        Assert.Equal("cam-a", stored.Id);          // the route id wins over the body id
        Assert.Equal("New", stored.Name);
        Assert.Equal("dev-2", stored.Webcam!.DeviceId);
    }

    // --- module presence --------------------------------------------------------

    [Fact]
    public void ModulePresence_AllBitsSet_CreatesEveryModule()
    {
        using var harness = new OrchestratorTestHarness();
        harness.Orchestrator.HandleModulePresence(0xFF);

        Assert.Equal(ProtocolConstants.MaxModules, harness.Orchestrator.CurrentModuleMappings.Count);
    }

    [Fact]
    public void ModulePresence_ZeroBits_RemovesEveryModule()
    {
        using var harness = new OrchestratorTestHarness();
        harness.Orchestrator.HandleModulePresence(0xFF);

        harness.Orchestrator.HandleModulePresence(0x00);

        Assert.Empty(harness.Orchestrator.CurrentModuleMappings);
    }

    [Fact]
    public void SwitchEdge_ForAModuleThatWasNeverReported_IsIgnored()
    {
        using var harness = new OrchestratorTestHarness();
        harness.AddTestSource("cam-a");

        harness.Orchestrator.HandleSwitchEdge(new SwitchEdgeEvent(5, SwitchId.Pgm1Src1, IsRising: true));

        Assert.Empty(harness.TallyBroadcaster.PublishedV2);
    }

    [Fact]
    public async Task SwitchEdge_ForAnUnboundSwitch_IsIgnored()
    {
        using var harness = new OrchestratorTestHarness();
        harness.Orchestrator.HandleModulePresence(0b1);
        await harness.Orchestrator.ApplyModulesAsync(new ModulesRequest(
            [new ModuleMapping(0, new ModuleSourceBinding(null, "Assignable"), new ModuleSourceBinding(null, "Assignable"))]));
        harness.TallyBroadcaster.PublishedV2.Clear();

        harness.Orchestrator.HandleSwitchEdge(new SwitchEdgeEvent(0, SwitchId.Pgm1Src1, IsRising: true));

        Assert.Empty(harness.TallyBroadcaster.PublishedV2);
    }

    [Fact]
    public async Task SwitchEdge_RepeatedRisingEdges_DoNotDoubleCount()
    {
        using var harness = new OrchestratorTestHarness();
        var channel = harness.AddTestSource("cam-a");
        harness.Orchestrator.HandleModulePresence(0b1);
        await harness.Orchestrator.ApplyModulesAsync(new ModulesRequest(
            [new ModuleMapping(0, new ModuleSourceBinding("cam-a", "Assignable"), new ModuleSourceBinding(null, "Assignable"))]));

        harness.Orchestrator.HandleSwitchEdge(new SwitchEdgeEvent(0, SwitchId.Pgm1Src1, IsRising: true));
        harness.Orchestrator.HandleSwitchEdge(new SwitchEdgeEvent(0, SwitchId.Pgm1Src1, IsRising: true));

        Assert.Equal([channel], harness.TallyBroadcaster.LastV2!.ActivePgm1);
    }

    // --- multiview persistence --------------------------------------------------

    [Fact]
    public async Task ApplyMultiview_PersistsTheGridAndRegions()
    {
        using var harness = new OrchestratorTestHarness();
        var layout = new MultiviewLayout(
            [],
            new MultiviewGrid(6, 5),
            [new MultiviewRegion(0, 0, 1, 1, "PGM1")]);

        await harness.Orchestrator.ApplyMultiviewAsync(layout);

        var stored = harness.RuntimeConfigStore.Current;
        Assert.Equal(6, stored.MultiviewGrid!.Rows);
        Assert.Equal(5, stored.MultiviewGrid.Cols);
        Assert.Equal(layout.Regions, stored.MultiviewRegions);
    }
}

/// <summary>
/// <see cref="MultiviewTally"/> decides the red/green frames on every multiview cell, in both windows and
/// (conceptually) on the composited output, so its precedence rules get their own coverage.
/// </summary>
public sealed class MultiviewTallyTests
{
    private static IReadOnlySet<string> Set(params string[] ids) => new HashSet<string>(ids, StringComparer.Ordinal);

    [Theory]
    [InlineData("PGM1")]
    [InlineData("PGM2")]
    public void BusProgramTokens_AreAlwaysProgram(string token) =>
        Assert.Equal(CellTally.Program, MultiviewTally.Resolve(token, Set(), Set()));

    [Theory]
    [InlineData("PVW1")]
    [InlineData("PVW2")]
    public void BusPreviewTokens_AreAlwaysPreview(string token) =>
        Assert.Equal(CellTally.Preview, MultiviewTally.Resolve(token, Set(), Set()));

    [Fact]
    public void EmptyToken_IsNotTallied() =>
        Assert.Equal(CellTally.None, MultiviewTally.Resolve("EMPTY", Set("a"), Set("a")));

    [Fact]
    public void SourceOnProgram_IsProgram() =>
        Assert.Equal(CellTally.Program, MultiviewTally.Resolve("SRC:cam", Set("cam"), Set()));

    [Fact]
    public void SourceOnPreview_IsPreview() =>
        Assert.Equal(CellTally.Preview, MultiviewTally.Resolve("SRC:cam", Set(), Set("cam")));

    [Fact]
    public void SourceOnBoth_PrefersProgram() =>
        Assert.Equal(CellTally.Program, MultiviewTally.Resolve("SRC:cam", Set("cam"), Set("cam")));

    [Fact]
    public void SourceOnNeither_IsNotTallied() =>
        Assert.Equal(CellTally.None, MultiviewTally.Resolve("SRC:cam", Set("other"), Set("other")));

    [Fact]
    public void IdsAreMatchedCaseSensitively() =>
        Assert.Equal(CellTally.None, MultiviewTally.Resolve("SRC:Cam", Set("cam"), Set()));

    [Fact]
    public void EmptySourceId_IsNotTallied() =>
        Assert.Equal(CellTally.None, MultiviewTally.Resolve("SRC:", Set(""), Set()));

    [Fact]
    public void UnknownToken_IsNotTallied() =>
        Assert.Equal(CellTally.None, MultiviewTally.Resolve("SOMETHING", Set("a"), Set("a")));
}
