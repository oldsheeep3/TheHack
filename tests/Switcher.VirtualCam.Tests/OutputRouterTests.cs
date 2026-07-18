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

    [Fact]
    public void CurrentAssignments_WithNdiOutput_SeedsDefaultPgm1Ndi1AndPgm2Ndi2()
    {
        var router = new OutputRouter(new FakeDualVirtualCameraOutput(), ndiOutput: new FakeDualNdiOutput());

        var assignments = router.CurrentAssignments;

        Assert.Equal(4, assignments.Count);
        var ndi1 = Assert.Single(assignments, a => a.Sink == OutputSink.Ndi1);
        Assert.Equal(OutputSource.Pgm1, ndi1.Source);
        Assert.Equal("SWITCHER PGM1", ndi1.NdiName);
        var ndi2 = Assert.Single(assignments, a => a.Sink == OutputSink.Ndi2);
        Assert.Equal(OutputSource.Pgm2, ndi2.Source);
        Assert.Equal("SWITCHER PGM2", ndi2.NdiName);
    }

    [Fact]
    public void CurrentAssignments_WithoutNdiOutput_HasNoNdiSinks()
    {
        var router = new OutputRouter(new FakeDualVirtualCameraOutput());

        Assert.DoesNotContain(router.CurrentAssignments, a => a.Sink is OutputSink.Ndi1 or OutputSink.Ndi2);
    }

    [Fact]
    public void RouteFrame_DefaultNdiAssignment_SendsPgm1ToNdi1AndPgm2ToNdi2()
    {
        var ndi = new FakeDualNdiOutput();
        var router = new OutputRouter(new FakeDualVirtualCameraOutput(), ndiOutput: ndi);
        var pgm1 = MakeFrame();
        var pgm2 = MakeFrame();

        router.RouteFrame(OutputSource.Pgm1, pgm1);
        router.RouteFrame(OutputSource.Pgm2, pgm2);

        Assert.Contains(ndi.SubmitFrameCalls, c => c.Sink == OutputSink.Ndi1 && ReferenceEquals(c.Frame, pgm1));
        Assert.Contains(ndi.SubmitFrameCalls, c => c.Sink == OutputSink.Ndi2 && ReferenceEquals(c.Frame, pgm2));
    }

    [Fact]
    public void ApplyOutputs_ChangesNdiSourceAndPropagatesSenderName()
    {
        var ndi = new FakeDualNdiOutput();
        var router = new OutputRouter(new FakeDualVirtualCameraOutput(), ndiOutput: ndi);

        router.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm2, DisplayId: null, HideCursor: null, Fullscreen: null, NdiName: "CUSTOM PGM"),
        ]));

        var assignment = Assert.Single(router.CurrentAssignments, a => a.Sink == OutputSink.Ndi1);
        Assert.Equal(OutputSource.Pgm2, assignment.Source);
        Assert.Equal("CUSTOM PGM", assignment.NdiName);
        Assert.Contains((OutputSink.Ndi1, "CUSTOM PGM"), ndi.SetSenderNameCalls);

        var frame = MakeFrame();
        router.RouteFrame(OutputSource.Pgm2, frame);
        Assert.Contains(ndi.SubmitFrameCalls, c => c.Sink == OutputSink.Ndi1);
    }

    [Fact]
    public void ApplyOutputs_NdiWithoutNdiName_DoesNotPropagateSenderName()
    {
        var ndi = new FakeDualNdiOutput();
        var router = new OutputRouter(new FakeDualVirtualCameraOutput(), ndiOutput: ndi);

        router.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, DisplayId: null, HideCursor: null, Fullscreen: null, NdiName: null),
        ]));

        Assert.Empty(ndi.SetSenderNameCalls);
    }

    [Fact]
    public void ApplyOutputs_NdiWithInvalidSource_ThrowsAndLeavesAssignmentsUnchanged()
    {
        var ndi = new FakeDualNdiOutput();
        var router = new OutputRouter(new FakeDualVirtualCameraOutput(), ndiOutput: ndi);

        Assert.Throws<ArgumentException>(() => router.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Ndi1, (OutputSource)999, DisplayId: null, HideCursor: null, Fullscreen: null, NdiName: "X"),
        ])));

        Assert.Empty(ndi.SetSenderNameCalls);
        var ndi1 = Assert.Single(router.CurrentAssignments, a => a.Sink == OutputSink.Ndi1);
        Assert.Equal(OutputSource.Pgm1, ndi1.Source);
    }

    [Fact]
    public void RouteFrame_NdiSinkFails_OtherSinksStillReceiveFrame()
    {
        var vcam = new FakeDualVirtualCameraOutput();
        var ndi = new FakeDualNdiOutput { ThrowOnSubmitToSink = OutputSink.Ndi1 };
        var router = new OutputRouter(vcam, ndiOutput: ndi);
        var frame = MakeFrame();

        var ex = Assert.Throws<AggregateException>(() => router.RouteFrame(OutputSource.Pgm1, frame));

        Assert.Single(ex.InnerExceptions);
        // VCAM1 (default PGM1 sink) still received the frame despite NDI1 failing.
        Assert.Contains(vcam.SubmitFrameCalls, c => c.Sink == OutputSink.Vcam1);
    }
}
