using Switcher.Contracts;
using Switcher.Hid.Backlight;

namespace Switcher.App.Tests;

/// <summary>
/// Two rules that decide what the camera operator sees on the physical panel: a source inside a live mix
/// counts as on air, and the LED colours are the operator's own per-bus palette rather than a fixed one.
/// </summary>
public sealed class TallyColorsAndMixTallyTests
{
    private static PipSettings FullFrame => new(true, 0, 0, 1920, 1080, 1.0, 0, null);

    private static SourceDefinition Mix(string id, params string[] members) =>
        new(id, id, SourceType.Mix, null, null, null, null, null,
            new MixConfig([.. members.Select((m, i) => new MixLayer(m, 0, 0, 960, 540, i))]));

    // --- mix members inherit the mix's tally ------------------------------------

    [Fact]
    public async Task SourceInsideALiveMix_IsReportedOnAir()
    {
        using var harness = new OrchestratorTestHarness();
        var cam = harness.AddTestSource("cam-a");
        harness.AddTestSource("logo");
        await harness.Orchestrator.AddSourceAsync(Mix("mix", "cam-a", "logo"));
        harness.AddTestSource("mix");

        await harness.Orchestrator.ApplyProgramAsync(
            new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("mix", FullFrame)], Take: true));

        // The camera's pixels are being broadcast, so its tally must say so even though only the mix
        // was put on the bus.
        Assert.Contains(cam, harness.TallyBroadcaster.LastV2!.ActivePgm1);
        Assert.Contains("cam-a", harness.Orchestrator.GetProgramSourceIds(ProgramBus.Pgm1));
    }

    [Fact]
    public async Task SourceInsideAStagedMix_IsReportedOnPreview()
    {
        using var harness = new OrchestratorTestHarness();
        harness.AddTestSource("cam-a");
        await harness.Orchestrator.AddSourceAsync(Mix("mix", "cam-a"));
        harness.AddTestSource("mix");

        await harness.Orchestrator.ApplyProgramAsync(
            new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("mix", FullFrame)], Take: false));

        Assert.Contains("cam-a", harness.Orchestrator.GetPreviewSourceIds(ProgramBus.Pgm1));
        Assert.DoesNotContain("cam-a", harness.Orchestrator.GetProgramSourceIds(ProgramBus.Pgm1));
    }

    [Fact]
    public async Task NestedMixes_PropagateAllTheWayDown()
    {
        using var harness = new OrchestratorTestHarness();
        harness.AddTestSource("cam-a");
        await harness.Orchestrator.AddSourceAsync(Mix("inner", "cam-a"));
        harness.AddTestSource("inner");
        await harness.Orchestrator.AddSourceAsync(Mix("outer", "inner"));
        harness.AddTestSource("outer");

        await harness.Orchestrator.ApplyProgramAsync(
            new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("outer", FullFrame)], Take: true));

        var live = harness.Orchestrator.GetProgramSourceIds(ProgramBus.Pgm1);
        Assert.Contains("outer", live);
        Assert.Contains("inner", live);
        Assert.Contains("cam-a", live);
    }

    [Fact]
    public async Task AMixThatSomehowContainsItself_DoesNotHang()
    {
        using var harness = new OrchestratorTestHarness();
        harness.AddTestSource("mix");

        // The validator rejects this at the API edge, but the expansion must still terminate if a
        // hand-edited config describes a cycle.
        await harness.Orchestrator.AddSourceAsync(Mix("mix", "mix"));

        await harness.Orchestrator.ApplyProgramAsync(
            new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("mix", FullFrame)], Take: true));

        Assert.Equal(["mix"], harness.Orchestrator.GetProgramSourceIds(ProgramBus.Pgm1));
    }

    [Fact]
    public async Task RemovingAMix_ClearsItsMembersTally()
    {
        using var harness = new OrchestratorTestHarness();
        harness.AddTestSource("cam-a");
        await harness.Orchestrator.AddSourceAsync(Mix("mix", "cam-a"));
        harness.AddTestSource("mix");
        await harness.Orchestrator.ApplyProgramAsync(
            new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("mix", FullFrame)], Take: true));

        await harness.Orchestrator.RemoveSourceAsync("mix");

        Assert.Empty(harness.Orchestrator.GetProgramSourceIds(ProgramBus.Pgm1));
    }

    // --- configurable LED colours ----------------------------------------------

    [Fact]
    public async Task TallyColours_ArePersistedAndReadBack()
    {
        using var harness = new OrchestratorTestHarness();
        var colors = new TallyColors(
            new BacklightColor(10, 20, 30), new BacklightColor(40, 50, 60),
            new BacklightColor(70, 80, 90), new BacklightColor(100, 110, 120),
            new BacklightColor(1, 2, 3));

        await harness.Orchestrator.ApplyTallyColorsAsync(colors);

        Assert.Equal(colors, harness.Orchestrator.CurrentTallyColors);
        Assert.Equal(colors, harness.RuntimeConfigStore.Current.Tally);
    }

    [Fact]
    public void DefaultColours_DistinguishTheTwoBuses()
    {
        var defaults = TallyColors.CreateDefault();

        Assert.NotEqual(defaults.Pgm1, defaults.Pgm2);
        Assert.NotEqual(defaults.Pvw1, defaults.Pvw2);
        Assert.Equal(defaults.Pgm1, defaults.ProgramFor(ProgramBus.Pgm1));
        Assert.Equal(defaults.Pgm2, defaults.ProgramFor(ProgramBus.Pgm2));
        Assert.Equal(defaults.Pvw2, defaults.PreviewFor(ProgramBus.Pgm2));
    }

    [Fact]
    public void BacklightPolicy_UsesThePerBusColours()
    {
        var colors = new TallyColors(
            new BacklightColor(1, 0, 0), new BacklightColor(2, 0, 0),
            new BacklightColor(3, 0, 0), new BacklightColor(4, 0, 0),
            new BacklightColor(5, 0, 0));
        var policy = new ConfiguredBacklightPolicy(colors);

        Assert.Equal(colors.Pgm1, policy.Compute(new BacklightSwitchContext(ProgramBus.Pgm1, true, false, true)));
        Assert.Equal(colors.Pgm2, policy.Compute(new BacklightSwitchContext(ProgramBus.Pgm2, true, false, true)));
        Assert.Equal(colors.Pvw1, policy.Compute(new BacklightSwitchContext(ProgramBus.Pgm1, false, true, true)));
        Assert.Equal(colors.Pvw2, policy.Compute(new BacklightSwitchContext(ProgramBus.Pgm2, false, true, true)));
        Assert.Equal(colors.Idle, policy.Compute(new BacklightSwitchContext(ProgramBus.Pgm1, false, false, true)));

        // Unbound switches stay dark whatever the palette says.
        Assert.Equal(new BacklightColor(0, 0, 0),
            policy.Compute(new BacklightSwitchContext(ProgramBus.Pgm1, false, false, false)));
    }

    [Fact]
    public void BacklightPolicy_ProgramWinsOverPreview()
    {
        var colors = TallyColors.CreateDefault();
        var policy = new ConfiguredBacklightPolicy(colors);

        Assert.Equal(colors.Pgm2,
            policy.Compute(new BacklightSwitchContext(ProgramBus.Pgm2, IsProgram: true, IsPreview: true, IsSelectable: true)));
    }

    [Fact]
    public async Task ChangingColours_RelightsTheModulesImmediately()
    {
        using var harness = new OrchestratorTestHarness();
        harness.AddTestSource("cam-a");
        harness.Orchestrator.HandleModulePresence(0b1);
        await harness.Orchestrator.ApplyModulesAsync(new ModulesRequest(
            [new ModuleMapping(0, new ModuleSourceBinding("cam-a", "Assignable"), new ModuleSourceBinding(null, "Assignable"))]));
        await harness.Orchestrator.ApplyProgramAsync(
            new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-a", FullFrame)], Take: true));

        harness.TallyBroadcaster.PublishedV2.Clear();
        await harness.Orchestrator.ApplyTallyColorsAsync(TallyColors.CreateDefault() with
        {
            Pgm1 = new BacklightColor(7, 7, 7),
        });

        // A palette change republishes state so the panel doesn't keep the old colour until the next
        // time somebody happens to switch a source.
        Assert.NotEmpty(harness.TallyBroadcaster.PublishedV2);
    }
}
