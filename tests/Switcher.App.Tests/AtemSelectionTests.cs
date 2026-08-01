using Switcher.App.Orchestration;
using Switcher.Contracts;
using Switcher.Hid.Input;

namespace Switcher.App.Tests;

/// <summary>
/// Selecting which ATEM to control, keeping that selection across a restart, and what a module switch
/// does once it carries an ATEM action (docs/specs/pc-switcher-app.md §2.8).
/// </summary>
public class AtemSelectionTests
{
    [Fact]
    public async Task ApplyAtemConfigAsync_WhenEnabled_DialsTheSelectedSwitcher()
    {
        using var harness = new OrchestratorTestHarness();

        await harness.Orchestrator.ApplyAtemConfigAsync(Selection("127.0.0.1", enabled: true));

        Assert.Equal("127.0.0.1", harness.AtemController.TargetIp);
    }

    [Fact]
    public async Task ApplyAtemConfigAsync_WhenNotEnabled_LeavesTheClientIdle()
    {
        using var harness = new OrchestratorTestHarness();

        await harness.Orchestrator.ApplyAtemConfigAsync(Selection("127.0.0.1", enabled: false));

        // Saving a switcher's address is not the same as asking to control it; an unticked box must not
        // start a session (or a reconnect loop) against it.
        Assert.Null(harness.AtemController.TargetIp);
    }

    [Fact]
    public async Task ApplyAtemConfigAsync_RemembersTheSelectionAcrossARestart()
    {
        using var first = new OrchestratorTestHarness();
        await first.Orchestrator.ApplyAtemConfigAsync(
            new AtemConfig(Enabled: true, Ip: "127.0.0.1", Mappings: [], Name: "ATEM Mini Pro"));

        using var restarted = new OrchestratorTestHarness(runtimeConfigDir: first.RuntimeConfigDir);

        var restored = restarted.Orchestrator.CurrentAtemConfig;
        Assert.Equal("127.0.0.1", restored.Ip);
        Assert.Equal("ATEM Mini Pro", restored.Name);
        Assert.True(restored.Enabled);
    }

    [Fact]
    public async Task ApplyAtemConfigAsync_AnnouncesTheChangeSoOpenUiFollowsIt()
    {
        using var harness = new OrchestratorTestHarness();
        var announced = new List<AtemConfig>();
        harness.Orchestrator.AtemConfigChanged += (_, config) => announced.Add(config);

        await harness.Orchestrator.ApplyAtemConfigAsync(Selection("127.0.0.1", enabled: true));

        Assert.Equal("127.0.0.1", Assert.Single(announced).Ip);
    }

    [Fact]
    public void ConnectConfiguredAtem_WithNothingSelected_StaysIdle()
    {
        using var harness = new OrchestratorTestHarness();

        harness.Orchestrator.ConnectConfiguredAtem();

        // A fresh install has no ATEM chosen, and the app must not spend the session retrying a hello
        // against an address nobody picked.
        Assert.Null(harness.AtemController.TargetIp);
    }

    [Fact]
    public async Task ConnectConfiguredAtem_AfterARestart_DialsTheSavedSwitcher()
    {
        using var first = new OrchestratorTestHarness();
        await first.Orchestrator.ApplyAtemConfigAsync(Selection("127.0.0.1", enabled: true));

        using var restarted = new OrchestratorTestHarness(runtimeConfigDir: first.RuntimeConfigDir);
        restarted.Orchestrator.ConnectConfiguredAtem();

        Assert.Equal("127.0.0.1", restarted.AtemController.TargetIp);
    }

    [Fact]
    public async Task ConfigureAtemStreamingAsync_WithNothingConnected_ReportsFailureInsteadOfThrowing()
    {
        using var harness = new OrchestratorTestHarness();

        var applied = await harness.Orchestrator.ConfigureAtemStreamingAsync(
            new AtemStreamingRequest("srt://192.168.1.50:9000"));

        Assert.False(applied);
    }

    [Fact]
    public async Task HandleSwitchEdge_WithAnUnassignedSwitch_StillSwitchesTheLocalSource()
    {
        using var harness = new OrchestratorTestHarness();
        harness.AddTestSource("cam-a");

        await harness.Orchestrator.ApplyModulesAsync(new ModulesRequest(
        [
            new ModuleMapping(0, new ModuleSourceBinding("cam-a", "Assignable"), new ModuleSourceBinding(null, "Assignable")),
        ]));

        // The settings window writes a row for every switch, including the ones left on "None"; those
        // must stay ordinary source toggles rather than being swallowed by the ATEM relay.
        await harness.Orchestrator.ApplyAtemConfigAsync(new AtemConfig(
            Enabled: false,
            Ip: "127.0.0.1",
            Mappings:
            [
                new AtemButtonMapping("hid", ModuleIndex: 0, Switch: "Pgm1Src1", Action: AppOrchestrator.NoAtemAction, MixEffect: 0, Source: 0),
            ]));

        harness.TallyBroadcaster.PublishedV2.Clear();
        harness.Orchestrator.HandleSwitchEdge(new SwitchEdgeEvent(0, SwitchId.Pgm1Src1, IsRising: true));

        Assert.NotEmpty(harness.TallyBroadcaster.PublishedV2);
    }

    [Fact]
    public async Task HandleSwitchEdge_WithAProgramInputAssignment_RelaysToTheAtemInstead()
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
            Mappings:
            [
                new AtemButtonMapping("hid", ModuleIndex: 0, Switch: "Pgm1Src1", Action: "ProgramInput", MixEffect: 0, Source: 3),
            ]));

        harness.TallyBroadcaster.PublishedV2.Clear();
        harness.Orchestrator.HandleSwitchEdge(new SwitchEdgeEvent(0, SwitchId.Pgm1Src1, IsRising: true));

        Assert.Empty(harness.TallyBroadcaster.PublishedV2);
    }

    private static AtemConfig Selection(string ip, bool enabled) => new(enabled, ip, Mappings: []);
}
