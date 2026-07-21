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

    [Fact]
    public void ApplyOutputs_HdmiWithoutDisplayId_Throws()
    {
        var engine = new FakeVideoEngine();
        Assert.Throws<ArgumentException>(() => engine.ApplyOutputs(new OutputsRequest([
            new OutputAssignment(OutputSink.Hdmi, OutputSource.Pgm1, DisplayId: null, HideCursor: true, Fullscreen: true),
        ])));
    }
}
