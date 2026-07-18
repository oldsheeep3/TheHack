using Switcher.App.Display;
using Switcher.App.Multiview;
using Switcher.Contracts;
using Switcher.VirtualCam;
using Switcher.VirtualCam.Display;

namespace Switcher.App.Tests;

/// <summary>
/// End-to-end integration smoke test for the A3-005 UI revision (docs/tasks/agent-A3-005-app-ui-integration.md
/// step 9): startup -> fake device enumeration -> 3-stage source add -> multiview rectangular merge ->
/// persist multiview(regions) -> output assignment incl NDI -> fake multiview full-screen presentation ->
/// same-screen warning judgement, all without real HW/NDI/D3D, asserting the flow never throws and the
/// core state converges. UI-independent logic (region merge / display-conflict) runs against the same
/// classes the WPF windows drive.
/// </summary>
public sealed class AppUiIntegrationSmokeTests
{
    [Fact]
    public async Task FullUiFlow_DevicesThroughFullscreen_NeverThrows()
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
        Assert.True(harness.SourceManager.TryResolveChannel("cam-1", out _));

        // (3) Multiview rectangular merge -> (4) persist the regions form.
        var model = new MultiviewRegionModel();
        model.SetContent(0, 0, "PGM1");
        Assert.True(model.TryMerge([(0, 0), (0, 1), (1, 0), (1, 1)], out _));
        await harness.Orchestrator.ApplyMultiviewAsync(model.ToLayout());

        var persisted = harness.Orchestrator.CurrentMultiviewLayout;
        var regions = MultiviewLayoutNormalizer.ToRegions(persisted);
        Assert.Contains(regions, r => r.RowSpan == 2 && r.ColSpan == 2 && r.Content == "PGM1");

        // (5) Output assignment including the NDI sinks (requirement 5).
        await harness.Orchestrator.ApplyOutputsAsync(new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, null, null, null, NdiName: "STUDIO PGM1"),
            new OutputAssignment(OutputSink.Ndi2, OutputSource.Pgm2, null, null, null, NdiName: "STUDIO PGM2"),
        ]));
        var ndi1 = Assert.Single(harness.OutputRouter.CurrentAssignments, a => a.Sink == OutputSink.Ndi1);
        Assert.Equal("STUDIO PGM1", ndi1.NdiName);

        // The NDI fan-out routes frames to the wired dual-NDI output without throwing.
        var ndiOutput = new FakeDualNdiOutput();
        var router = new OutputRouter(new FakeDualVirtualCameraOutput(), ndiOutput: ndiOutput);
        router.ApplyOutputs(new OutputsRequest([new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, null, null, null, NdiName: "STUDIO PGM1")]));
        router.RouteFrame(OutputSource.Pgm1, MakeFrame());
        Assert.True(ndiOutput.ReceivedFrame);
        Assert.Equal("STUDIO PGM1", ndiOutput.LastSenderName);

        // (6) Multiview full-screen presentation (fake presenter from the shared factory seam) - the same
        // presenter contract MultiviewFullscreenWindow drives, exercised without WPF/D3D.
        var factory = new FakeFullscreenPresenterFactory();
        var presenter = factory.Create();
        presenter.Attach(1, windowHandle: 0x1234, hideCursor: true, fullscreen: true);
        presenter.Present(MakeFrame());
        Assert.True(presenter.IsAttached);
        presenter.Detach();
        presenter.Dispose();

        // (7) Same-screen warning judgement (requirement 1/4).
        Assert.True(DisplayConflictEvaluator.ConflictsWithOperator(operatorDisplayIndex: 1, targetDisplayId: 1));
        Assert.False(DisplayConflictEvaluator.ConflictsWithOperator(operatorDisplayIndex: 1, targetDisplayId: 0));
    }

    private static FrameData MakeFrame()
    {
        const int width = 4;
        const int height = 4;
        return new FrameData(width, height, new byte[width * height * 4]);
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

internal sealed class FakeDualVirtualCameraOutput : IDualVirtualCameraOutput
{
    public void Start()
    {
    }

    public void SubmitFrame(OutputSink sink, FrameData frame)
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}

internal sealed class FakeDualNdiOutput : Switcher.VirtualCam.Ndi.IDualNdiOutput
{
    public bool ReceivedFrame { get; private set; }

    public string? LastSenderName { get; private set; }

    public void Start()
    {
    }

    public void SubmitFrame(OutputSink sink, FrameData frame) => ReceivedFrame = true;

    public void SetSenderName(OutputSink sink, string senderName) => LastSenderName = senderName;

    public void Stop()
    {
    }
}

internal sealed class FakeFullscreenPresenterFactory : IFullscreenPresenterFactory
{
    public IHdmiFullscreenOutput Create() => new FakeFullscreenPresenter();
}

internal sealed class FakeFullscreenPresenter : IHdmiFullscreenOutput
{
    public int? DisplayId { get; private set; }

    public bool HideCursor { get; private set; }

    public bool Fullscreen { get; private set; }

    public bool IsAttached { get; private set; }

    public void Attach(int displayId, nint windowHandle, bool hideCursor, bool fullscreen)
    {
        DisplayId = displayId;
        HideCursor = hideCursor;
        Fullscreen = fullscreen;
        IsAttached = true;
    }

    public void Present(FrameData frame)
    {
        if (!IsAttached)
        {
            throw new InvalidOperationException("Not attached.");
        }
    }

    public void Detach()
    {
        IsAttached = false;
        DisplayId = null;
    }

    public void Dispose() => Detach();
}
