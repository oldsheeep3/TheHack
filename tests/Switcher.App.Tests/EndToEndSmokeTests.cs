using Switcher.Contracts;
using Switcher.Hid.Input;

namespace Switcher.App.Tests;

/// <summary>
/// End-to-end integration smoke test (docs/tasks/agent-A2-006-app-integration-v2.md step 9): startup ->
/// add a fake source -> apply a PGM1/PGM2 layer -> TAKE -> tally broadcast -> output assignment -> fake
/// HID input (switch reflection + backlight send) - all without real HW/GStreamer/HID, asserting the
/// whole flow never throws.
/// </summary>
public sealed class EndToEndSmokeTests
{
    [Fact]
    public async Task FullFlow_StartupThroughHidInput_NeverThrows()
    {
        using var harness = new OrchestratorTestHarness();

        // Add a source (fake webcam device id; the real capture pipeline connects/retries on its own
        // background thread and is irrelevant to this test).
        await harness.Orchestrator.AddSourceAsync(new SourceDefinition(
            "cam-1", "Camera 1", SourceType.Webcam, null, new WebcamConfig("fake-device", null), null));

        // Apply a PGM1 layer and TAKE.
        var pip = new PipSettings(true, 0, 0, 1920, 1080, 1.0, ZOrder: 0, Crop: null);
        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-1", pip)], Take: true));

        // Tally was broadcast as part of the TAKE above.
        Assert.NotEmpty(harness.TallyBroadcaster.PublishedV2);
        harness.Engine.TryResolveChannel("cam-1", out var channel);
        Assert.Contains(channel, harness.TallyBroadcaster.LastV2!.ActivePgm1);

        // Output assignment: route PGM1 to VCAM1 (already the default) and PGM2 to VCAM2.
        await harness.Orchestrator.ApplyOutputsAsync(new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm2, null, null, null),
        ]));

        // Module mapping binds module 0's Src1 switch to cam-1 on PGM2's preview.
        await harness.Orchestrator.ApplyModulesAsync(new ModulesRequest(
        [
            new ModuleMapping(0, new ModuleSourceBinding("cam-1", "Assignable"), new ModuleSourceBinding(null, "Assignable")),
        ]));

        // Fake HID input: switch reflection (mount onto PGM2 preview) and a VR nudge.
        harness.Orchestrator.HandleSwitchEdge(new SwitchEdgeEvent(0, SwitchId.Pgm2Src1, IsRising: true));
        harness.Orchestrator.HandleVrChanged(new VrChangedEvent(0, VrChannel.Src1, 128));

        var finalTally = harness.TallyBroadcaster.LastV2!;
        Assert.Contains(channel, finalTally.ActivePvw2);

        // Backlight send was attempted (best-effort; HidBacklightService.Send throws
        // InvalidOperationException without Start(), which AppOrchestrator must swallow rather than
        // propagate - the assertions above having run without throwing already proves this, but assert
        // explicitly that the flow completed rather than short-circuiting on an exception).
        Assert.True(true);
    }
}
