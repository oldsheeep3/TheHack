using Switcher.App.Display;
using Switcher.App.Multiview;
using Switcher.App.ViewModels;
using Switcher.Contracts;

namespace Switcher.App.Tests;

/// <summary>
/// End-to-end integration smoke test for the UI revision flow (docs/tasks/agent-A3-005-app-ui-integration.md
/// step 9): startup -> fake device enumeration -> 3-stage source add -> multiview rectangular merge ->
/// persist multiview(regions) -> output assignment incl NDI (via the engine) -> same-screen warning
/// judgement, all without real HW/NDI/D3D, asserting the flow never throws and the core state converges.
/// UI-independent logic (region merge / display-conflict) runs against the same classes the WPF windows
/// drive. Since the libobs migration, output routing and full-screen presentation are owned by the
/// engine, so those steps assert against <see cref="IVideoEngine"/> state rather than the former
/// OutputRouter / presenter seams.
/// </summary>
public sealed class AppUiIntegrationSmokeTests
{
    [Fact]
    public async Task FullUiFlow_DevicesThroughOutputs_NeverThrows()
    {
        using var harness = new OrchestratorTestHarness();

        // (1) Device enumeration (fake): the 3-stage add-source UI's device dropdown is populated from
        // IDeviceQueryService.EnumerateAsync.
        var deviceQuery = new FakeDeviceQueryService();
        var webcams = await deviceQuery.EnumerateAsync(DeviceQueryType.Webcam);
        Assert.NotEmpty(webcams);
        var srtSetup = await deviceQuery.GetSrtSetupAsync();
        Assert.Equal(ProtocolConstants.SrtListenPort, srtSetup.ListenerPort);

        // (2) 3-stage source add: name + WEBCAM type + selected device -> SourceDefinition.
        var device = webcams[0];
        var source = new SourceDefinition("cam-1", "Camera 1", SourceType.Webcam, null, new WebcamConfig(device.Id, device.Formats?.FirstOrDefault()), null);
        await harness.Orchestrator.AddSourceAsync(source);
        Assert.True(harness.Engine.TryResolveChannel("cam-1", out _));

        // (3) Multiview rectangular merge -> (4) persist the regions form.
        var model = new MultiviewRegionModel();
        model.SetContent(0, 0, "PGM1");
        Assert.True(model.TryMerge([(0, 0), (0, 1), (1, 0), (1, 1)], out _));
        await harness.Orchestrator.ApplyMultiviewAsync(model.ToLayout());

        var persisted = harness.Orchestrator.CurrentMultiviewLayout;
        var regions = MultiviewLayoutNormalizer.ToRegions(persisted);
        Assert.Contains(regions, r => r.RowSpan == 2 && r.ColSpan == 2 && r.Content == "PGM1");

        // (5) Output assignment including the NDI sinks (requirement 5): the engine records the
        // assignments (it owns the actual VCAM/NDI fan-out internally since the libobs migration).
        await harness.Orchestrator.ApplyOutputsAsync(new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, null, null, null, NdiName: "STUDIO PGM1"),
            new OutputAssignment(OutputSink.Ndi2, OutputSource.Pgm2, null, null, null, NdiName: "STUDIO PGM2"),
        ]));
        var ndi1 = Assert.Single(harness.Engine.CurrentAssignments, a => a.Sink == OutputSink.Ndi1);
        Assert.Equal("STUDIO PGM1", ndi1.NdiName);

        // (5b) The Outputs dock's own table: seeded from the routing that is now live, an HDMI sink added
        // through the same affordance the operator's + button drives, and applied back.
        var outputs = new OutputTableViewModel();
        outputs.Load(harness.Engine.CurrentAssignments);
        Assert.Equal(3, outputs.Rows.Count);

        var added = outputs.Add(OutputKind.Hdmi);
        Assert.Equal(OutputSink.Hdmi1, added!.Sink);
        Assert.Empty(outputs.Validate());

        await harness.Orchestrator.ApplyOutputsAsync(outputs.ToRequest());
        Assert.Contains(harness.Engine.CurrentAssignments, a => a.Sink == OutputSink.Hdmi1);

        // (6) Same-screen warning judgement (requirement 1/4).
        Assert.True(DisplayConflictEvaluator.ConflictsWithOperator(operatorDisplayIndex: 1, targetDisplayId: 1));
        Assert.False(DisplayConflictEvaluator.ConflictsWithOperator(operatorDisplayIndex: 1, targetDisplayId: 0));
    }
}

internal sealed class FakeDeviceQueryService : IDeviceQueryService
{
    public Task<IReadOnlyList<DeviceInfo>> EnumerateAsync(DeviceQueryType type, CancellationToken ct = default)
    {
        IReadOnlyList<DeviceInfo> devices = type == DeviceQueryType.Webcam
            ? [new DeviceInfo("/dev/video0", "USB Camera", ["1920x1080@30"])]
            : [new DeviceInfo("STUDIO (Cam 1)", "STUDIO (Cam 1)", null)];
        return Task.FromResult(devices);
    }

    public Task<SrtSetupInfo> GetSrtSetupAsync(CancellationToken ct = default) =>
        Task.FromResult(new SrtSetupInfo(
            ProtocolConstants.SrtListenPort,
            ["192.168.1.50"],
            $"srt://192.168.1.50:{ProtocolConstants.SrtListenPort}",
            40,
            "Set the ATEM streaming output to Caller with the URL above."));
}
