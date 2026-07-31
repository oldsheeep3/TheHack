using Switcher.Contracts;
using Switcher.Engine;

namespace Switcher.Engine.Tests;

public sealed class FakeVideoEngineTests
{
    private static SourceDefinition Webcam(string id, string name = "cam") =>
        new(id, name, SourceType.Webcam, Ndi: null, Webcam: new WebcamConfig("dev0", null), Srt: null);

    [Fact]
    public async Task StartStop_TogglesStartedState()
    {
        var engine = new FakeVideoEngine();
        Assert.False(engine.IsStarted);

        await engine.StartAsync(new EngineOptions());
        Assert.True(engine.IsStarted);

        await engine.StopAsync();
        Assert.False(engine.IsStarted);
    }

    [Fact]
    public void AddSource_AssignsStableChannel_AndResolves()
    {
        var engine = new FakeVideoEngine();
        engine.AddSource(Webcam("cam-a"));
        engine.AddSource(Webcam("cam-b"));

        Assert.True(engine.TryResolveChannel("cam-a", out var chA));
        Assert.True(engine.TryResolveChannel("cam-b", out var chB));
        Assert.NotEqual(chA, chB);

        // Re-adding the same id keeps its channel (update, not duplicate).
        engine.AddSource(Webcam("cam-a", "renamed"));
        Assert.True(engine.TryResolveChannel("cam-a", out var chA2));
        Assert.Equal(chA, chA2);
        Assert.Equal(2, engine.GetSources().Count);
    }

    [Fact]
    public void RemoveSource_DropsChannelResolution()
    {
        var engine = new FakeVideoEngine();
        engine.AddSource(Webcam("cam-a"));
        engine.RemoveSource("cam-a");

        Assert.False(engine.TryResolveChannel("cam-a", out _));
        Assert.Empty(engine.GetSources());
    }

    [Fact]
    public void SetSourceEnabled_And_ApplyProgram_TrackBusMounts()
    {
        var engine = new FakeVideoEngine();
        engine.AddSource(Webcam("cam-a"));

        engine.SetSourceEnabled(ProgramBus.Pgm1, "cam-a", enabled: true);
        Assert.True(engine.IsSourceEnabled(ProgramBus.Pgm1, "cam-a"));
        Assert.False(engine.IsSourceEnabled(ProgramBus.Pgm2, "cam-a"));

        engine.ApplyProgram(new ProgramRequest(
            ProgramBus.Pgm2,
            [new ProgramLayer("cam-a", new PipSettings(true, 0, 0, 1920, 1080, 1.0, 0, null))],
            Take: true));
        Assert.True(engine.IsSourceEnabled(ProgramBus.Pgm2, "cam-a"));
    }

    [Fact]
    public void ApplyOutputs_StoresAssignments()
    {
        var engine = new FakeVideoEngine();
        engine.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Ndi2, OutputSource.Pgm2, null, null, null, NdiName: "SWITCHER PGM2"),
        ]));

        Assert.Equal(2, engine.CurrentAssignments.Count);
        Assert.Contains(engine.CurrentAssignments, a => a.Sink == OutputSink.Vcam1);
    }

    [Fact]
    public void ApplyOutputs_DuplicateSink_ThrowsAndLeavesPriorAssignmentsIntact()
    {
        var engine = new FakeVideoEngine();
        engine.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
        ]));

        Assert.Throws<ArgumentException>(() => engine.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm2, null, null, null),
        ])));

        // Atomic: the failed apply must not have mutated the stored assignments.
        Assert.Single(engine.CurrentAssignments);
        Assert.Equal(OutputSink.Vcam1, engine.CurrentAssignments[0].Sink);
    }

    [Theory]
    [InlineData(OutputSink.Hdmi1)]
    [InlineData(OutputSink.Hdmi3)]
    public void ApplyOutputs_HdmiWithoutDisplayId_Throws(OutputSink sink)
    {
        var engine = new FakeVideoEngine();
        Assert.Throws<ArgumentException>(() => engine.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(sink, OutputSource.Pgm1, DisplayId: null, HideCursor: true, Fullscreen: true),
        ])));
    }

    [Fact]
    public void ApplyOutputs_AcceptsThreeOfAKind()
    {
        var engine = new FakeVideoEngine();
        engine.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm2, null, null, null),
            new OutputAssignment(OutputSink.Vcam3, OutputSource.Pgm2, null, null, null),
            new OutputAssignment(OutputSink.Ndi3, OutputSource.Pgm2, null, null, null, NdiName: "SWITCHER PGM3"),
        ]));

        Assert.Equal(4, engine.CurrentAssignments.Count);
        Assert.Contains(engine.CurrentAssignments, a => a.Sink == OutputSink.Vcam3);
    }

    [Fact]
    public void StartDisplayOutput_TracksEachTargetSeparately()
    {
        var engine = new FakeVideoEngine();

        // Two HDMI projectors are open at once - they are addressed by sink token, not by the bus they
        // carry, so both stay attached even though both show PGM1.
        engine.StartDisplayOutput("HDMI1", new IntPtr(1), displayId: 0);
        engine.StartDisplayOutput("HDMI2", new IntPtr(2), displayId: 1);

        Assert.Equal(new[] { "HDMI1", "HDMI2" }, engine.DisplayTargets.Order());
        var hdmi2 = engine.DisplayFor("HDMI2");
        Assert.NotNull(hdmi2);
        Assert.Equal(new IntPtr(2), hdmi2.Value.WindowHandle);
        Assert.Equal(1, hdmi2.Value.DisplayId);

        engine.StopDisplayOutput("HDMI1");
        Assert.Equal(new[] { "HDMI2" }, engine.DisplayTargets);
        Assert.Null(engine.DisplayFor("HDMI1"));
    }

    [Fact]
    public void QueryOutputStatus_HdmiRunsOnlyWhileItsProjectorIsAttached()
    {
        var engine = new FakeVideoEngine();
        engine.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Hdmi1, OutputSource.Pgm2, DisplayId: 0, HideCursor: true, Fullscreen: true),
            new OutputAssignment(OutputSink.Hdmi2, OutputSource.Pgm2, DisplayId: 1, HideCursor: true, Fullscreen: true),
        ]));

        // Assigned but not presented: an HDMI sink with no window is not egressing, which is the whole
        // point of asking the engine rather than reading the routing table.
        Assert.All(engine.QueryOutputStatus().Where(s => s.Sink != OutputSink.Vcam1),
            s => Assert.False(s.Running));

        engine.StartDisplayOutput("HDMI2", new IntPtr(2), displayId: 1);
        var status = engine.QueryOutputStatus();
        Assert.False(status.Single(s => s.Sink == OutputSink.Hdmi1).Running);
        Assert.True(status.Single(s => s.Sink == OutputSink.Hdmi2).Running);
        Assert.True(status.Single(s => s.Sink == OutputSink.Vcam1).Running);
    }

    [Fact]
    public void QueryOutputStatus_FailingSinkReportsStopped()
    {
        var engine = new FakeVideoEngine();
        engine.FailingSinks.Add(OutputSink.Ndi2);
        engine.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Ndi2, OutputSource.Pgm2, null, null, null, NdiName: "SWITCHER PGM2"),
        ]));

        var status = engine.QueryOutputStatus();
        Assert.True(status.Single(s => s.Sink == OutputSink.Vcam1).Running);
        Assert.False(status.Single(s => s.Sink == OutputSink.Ndi2).Running);
    }
}
