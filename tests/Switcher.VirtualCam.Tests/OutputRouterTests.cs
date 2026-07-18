using Switcher.Contracts;

namespace Switcher.VirtualCam.Tests;

public sealed class OutputRouterTests
{
    private static FrameData MakeFrame(int width = 4, int height = 2, byte fill = 0x80) =>
        new(width, height, new byte[width * height * 4].Select(_ => fill).ToArray());

    [Fact]
    public void CurrentAssignments_DefaultsToPgm1Vcam1AndPgm2Vcam2()
    {
        var router = new OutputRouter(new FakeDualVirtualCameraOutput());

        var assignments = router.CurrentAssignments;

        Assert.Equal(2, assignments.Count);
        Assert.Contains(assignments, a => a.Sink == OutputSink.Vcam1 && a.Source == OutputSource.Pgm1);
        Assert.Contains(assignments, a => a.Sink == OutputSink.Vcam2 && a.Source == OutputSource.Pgm2);
    }

    [Fact]
    public void ApplyOutputs_ChangesAssignment()
    {
        var router = new OutputRouter(new FakeDualVirtualCameraOutput());

        router.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm2, null, null, null),
        ]));

        var assignment = Assert.Single(router.CurrentAssignments, a => a.Sink == OutputSink.Vcam1);
        Assert.Equal(OutputSource.Pgm2, assignment.Source);
        // Vcam2's default assignment is untouched by a request that only mentions Vcam1.
        Assert.Contains(router.CurrentAssignments, a => a.Sink == OutputSink.Vcam2 && a.Source == OutputSource.Pgm2);
    }

    [Fact]
    public void ApplyOutputs_HdmiWithDisplayId_Succeeds()
    {
        var router = new OutputRouter(new FakeDualVirtualCameraOutput());

        router.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Hdmi, OutputSource.Pgm1, DisplayId: 1, HideCursor: true, Fullscreen: true),
        ]));

        var assignment = Assert.Single(router.CurrentAssignments, a => a.Sink == OutputSink.Hdmi);
        Assert.Equal(1, assignment.DisplayId);
        Assert.True(assignment.HideCursor);
        Assert.True(assignment.Fullscreen);
    }

    [Fact]
    public void ApplyOutputs_HdmiWithoutDisplayId_ThrowsAndLeavesAssignmentsUnchanged()
    {
        var router = new OutputRouter(new FakeDualVirtualCameraOutput());

        Assert.Throws<ArgumentException>(() => router.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Hdmi, OutputSource.Pgm1, DisplayId: null, HideCursor: null, Fullscreen: null),
        ])));

        Assert.DoesNotContain(router.CurrentAssignments, a => a.Sink == OutputSink.Hdmi);
    }

    [Fact]
    public void ApplyOutputs_DuplicateSinkInRequest_ThrowsAndLeavesAssignmentsUnchanged()
    {
        var router = new OutputRouter(new FakeDualVirtualCameraOutput());

        Assert.Throws<ArgumentException>(() => router.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm2, null, null, null),
        ])));

        Assert.Contains(router.CurrentAssignments, a => a.Sink == OutputSink.Vcam1 && a.Source == OutputSource.Pgm1);
    }

    [Fact]
    public void ApplyOutputs_UnknownSinkOrSource_Throws()
    {
        var router = new OutputRouter(new FakeDualVirtualCameraOutput());

        Assert.Throws<ArgumentException>(() => router.ApplyOutputs(new OutputsRequest([
            new OutputAssignment((OutputSink)999, OutputSource.Pgm1, null, null, null),
        ])));
        Assert.Throws<ArgumentException>(() => router.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Vcam1, (OutputSource)999, null, null, null),
        ])));
    }

    [Fact]
    public void RouteFrame_DefaultAssignment_SendsPgm1ToVcam1Only()
    {
        var vcam = new FakeDualVirtualCameraOutput();
        var router = new OutputRouter(vcam);
        var frame = MakeFrame();

        router.RouteFrame(OutputSource.Pgm1, frame);

        var call = Assert.Single(vcam.SubmitFrameCalls);
        Assert.Equal(OutputSink.Vcam1, call.Sink);
        Assert.Same(frame, call.Frame);
    }

    [Fact]
    public void RouteFrame_MultipleSinksOnSameSource_SendsToBoth()
    {
        var vcam = new FakeDualVirtualCameraOutput();
        var router = new OutputRouter(vcam);
        router.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm1, null, null, null),
        ]));
        var frame = MakeFrame();

        router.RouteFrame(OutputSource.Pgm1, frame);

        Assert.Equal(2, vcam.SubmitFrameCalls.Count);
        Assert.Contains(vcam.SubmitFrameCalls, c => c.Sink == OutputSink.Vcam1);
        Assert.Contains(vcam.SubmitFrameCalls, c => c.Sink == OutputSink.Vcam2);
    }

    [Fact]
    public void RouteFrame_RoutesToAttachedHdmiSink()
    {
        var vcam = new FakeDualVirtualCameraOutput();
        var hdmi = new FakeHdmiFullscreenOutput();
        var router = new OutputRouter(vcam, hdmi);
        router.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Hdmi, OutputSource.Pgm1, DisplayId: 0, HideCursor: true, Fullscreen: true),
        ]));
        var frame = MakeFrame();

        router.RouteFrame(OutputSource.Pgm1, frame);

        Assert.Same(frame, Assert.Single(hdmi.PresentCalls));
        // Default Vcam1->Pgm1 assignment still fires alongside the HDMI sink.
        Assert.Contains(vcam.SubmitFrameCalls, c => c.Sink == OutputSink.Vcam1);
    }

    [Fact]
    public void RouteFrame_NoSinkForSource_DoesNothing()
    {
        var vcam = new FakeDualVirtualCameraOutput();
        var router = new OutputRouter(vcam);
        router.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm2, null, null, null),
            new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm2, null, null, null),
        ]));

        router.RouteFrame(OutputSource.Pgm1, MakeFrame());

        Assert.Empty(vcam.SubmitFrameCalls);
    }

    [Fact]
    public void RouteFrame_OneSinkFails_OtherSinkStillReceivesFrameAndFailureIsSurfaced()
    {
        var vcam = new FakeDualVirtualCameraOutput { ThrowOnSubmitToSink = OutputSink.Vcam1 };
        var hdmi = new FakeHdmiFullscreenOutput();
        var router = new OutputRouter(vcam, hdmi);
        router.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Hdmi, OutputSource.Pgm1, DisplayId: 0, HideCursor: false, Fullscreen: true),
        ]));
        var frame = MakeFrame();

        var ex = Assert.Throws<AggregateException>(() => router.RouteFrame(OutputSource.Pgm1, frame));

        Assert.Single(ex.InnerExceptions);
        Assert.Same(frame, Assert.Single(hdmi.PresentCalls));
    }
}
