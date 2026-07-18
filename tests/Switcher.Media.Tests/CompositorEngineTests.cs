using Switcher.Contracts;

namespace Switcher.Media.Tests;

public class CompositorEngineTests
{
    [Fact]
    public void ApplyPipSettings_UpdatesThePreviewSceneOrderedByZOrder()
    {
        var gpu = new FakeGpuCompositor();
        using var engine = new CompositorEngine(new FakeFrameSource(), gpu, canvasWidth: 100, canvasHeight: 100);

        engine.ApplyPipSettings(1, new PipSettings(true, 0, 0, 50, 50, 1.0, 2, null));
        engine.ApplyPipSettings(2, new PipSettings(true, 0, 0, 50, 50, 1.0, 1, null));

        engine.GetPreviewFrame();

        var layers = gpu.ComposeCalls[^1];
        Assert.Equal([2, 1], layers.Select(l => l.Channel));
    }

    [Fact]
    public void ApplyPipSettings_DisabledChannel_IsExcludedFromTheScene()
    {
        var gpu = new FakeGpuCompositor();
        using var engine = new CompositorEngine(new FakeFrameSource(), gpu, canvasWidth: 100, canvasHeight: 100);

        engine.ApplyPipSettings(1, new PipSettings(false, 0, 0, 50, 50, 1.0, 0, null));

        engine.GetPreviewFrame();

        Assert.Empty(gpu.ComposeCalls[^1]);
    }

    [Fact]
    public void GetProgramFrame_BeforeAnyTake_IsEmpty()
    {
        var gpu = new FakeGpuCompositor();
        using var engine = new CompositorEngine(new FakeFrameSource(), gpu, canvasWidth: 100, canvasHeight: 100);

        engine.ApplyPipSettings(1, new PipSettings(true, 0, 0, 50, 50, 1.0, 0, null));
        engine.GetProgramFrame();

        Assert.Empty(gpu.ComposeCalls[^1]);
    }

    [Fact]
    public void Take_PromotesTheCurrentPreviewSceneToProgram()
    {
        var gpu = new FakeGpuCompositor();
        using var engine = new CompositorEngine(new FakeFrameSource(), gpu, canvasWidth: 100, canvasHeight: 100);

        engine.ApplyPipSettings(1, new PipSettings(true, 0, 0, 50, 50, 1.0, 0, null));
        engine.Take();
        engine.GetProgramFrame();

        var layers = gpu.ComposeCalls[^1];
        Assert.Equal([1], layers.Select(l => l.Channel));
    }

    [Fact]
    public void Take_ThenFurtherPreviewEdits_DoNotRetroactivelyAffectProgram()
    {
        var gpu = new FakeGpuCompositor();
        using var engine = new CompositorEngine(new FakeFrameSource(), gpu, canvasWidth: 100, canvasHeight: 100);

        engine.ApplyPipSettings(1, new PipSettings(true, 0, 0, 50, 50, 1.0, 0, null));
        engine.Take();

        engine.ApplyPipSettings(2, new PipSettings(true, 0, 0, 50, 50, 1.0, 0, null));

        engine.GetProgramFrame();
        Assert.Equal([1], gpu.ComposeCalls[^1].Select(l => l.Channel));

        engine.GetPreviewFrame();
        Assert.Equal([1, 2], gpu.ComposeCalls[^1].Select(l => l.Channel).OrderBy(c => c));
    }

    [Fact]
    public void Dispose_DisposesTheUnderlyingGpuCompositor()
    {
        var gpu = new FakeGpuCompositor();
        var engine = new CompositorEngine(new FakeFrameSource(), gpu, canvasWidth: 100, canvasHeight: 100);

        engine.Dispose();

        Assert.Equal(1, gpu.DisposeCount);
    }

    [Fact]
    public void Take_OnOneBus_DoesNotAffectTheOtherBus()
    {
        var gpu = new FakeGpuCompositor();
        var frameSource = new FakeFrameSource { ChannelsById = { ["src-1"] = 1, ["src-2"] = 2 } };
        using var engine = new CompositorEngine(frameSource, gpu, canvasWidth: 100, canvasHeight: 100);

        engine.ApplyProgram(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("src-1", Pip())], Take: true));
        engine.ApplyProgram(new ProgramRequest(ProgramBus.Pgm2, [new ProgramLayer("src-2", Pip())], Take: false));

        engine.GetProgramFrame(ProgramBus.Pgm1);
        Assert.Equal([1], gpu.ComposeCalls[^1].Select(l => l.Channel));

        engine.GetProgramFrame(ProgramBus.Pgm2);
        Assert.Empty(gpu.ComposeCalls[^1]);

        engine.Take(ProgramBus.Pgm2);
        engine.GetProgramFrame(ProgramBus.Pgm2);
        Assert.Equal([2], gpu.ComposeCalls[^1].Select(l => l.Channel));

        engine.GetProgramFrame(ProgramBus.Pgm1);
        Assert.Equal([1], gpu.ComposeCalls[^1].Select(l => l.Channel));
    }

    [Fact]
    public void ApplyProgram_TheSameSourceCanBeLoadedOnBothBusesIndependently()
    {
        var gpu = new FakeGpuCompositor();
        var frameSource = new FakeFrameSource { ChannelsById = { ["src-1"] = 1 } };
        using var engine = new CompositorEngine(frameSource, gpu, canvasWidth: 100, canvasHeight: 100);

        engine.ApplyProgram(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("src-1", Pip())], Take: false));
        engine.ApplyProgram(new ProgramRequest(ProgramBus.Pgm2, [new ProgramLayer("src-1", Pip())], Take: false));

        engine.GetPreviewFrame(ProgramBus.Pgm1);
        Assert.Equal([1], gpu.ComposeCalls[^1].Select(l => l.Channel));

        engine.GetPreviewFrame(ProgramBus.Pgm2);
        Assert.Equal([1], gpu.ComposeCalls[^1].Select(l => l.Channel));
    }

    [Fact]
    public void ApplyProgram_UnresolvableSourceId_IsDroppedRatherThanThrowing()
    {
        var gpu = new FakeGpuCompositor();
        using var engine = new CompositorEngine(new FakeFrameSource(), gpu, canvasWidth: 100, canvasHeight: 100);

        engine.ApplyProgram(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("does-not-exist", Pip())], Take: false));
        engine.GetPreviewFrame(ProgramBus.Pgm1);

        Assert.Empty(gpu.ComposeCalls[^1]);
    }

    [Fact]
    public void ApplyProgram_ReplacesThePreviousLayerSetForThatBus()
    {
        var gpu = new FakeGpuCompositor();
        var frameSource = new FakeFrameSource { ChannelsById = { ["src-1"] = 1, ["src-2"] = 2 } };
        using var engine = new CompositorEngine(frameSource, gpu, canvasWidth: 100, canvasHeight: 100);

        engine.ApplyProgram(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("src-1", Pip())], Take: false));
        engine.ApplyProgram(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("src-2", Pip())], Take: false));

        engine.GetPreviewFrame(ProgramBus.Pgm1);
        Assert.Equal([2], gpu.ComposeCalls[^1].Select(l => l.Channel));
    }

    [Fact]
    public void SetSourceEnabled_TogglesASourceOnABusWithoutDisturbingOtherLayers()
    {
        var gpu = new FakeGpuCompositor();
        var frameSource = new FakeFrameSource { ChannelsById = { ["src-1"] = 1, ["src-2"] = 2 } };
        using var engine = new CompositorEngine(frameSource, gpu, canvasWidth: 100, canvasHeight: 100);
        engine.ApplyProgram(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("src-1", Pip())], Take: false));

        engine.SetSourceEnabled(ProgramBus.Pgm1, "src-2", enabled: true);
        engine.GetPreviewFrame(ProgramBus.Pgm1);
        Assert.Equal([1, 2], gpu.ComposeCalls[^1].Select(l => l.Channel).OrderBy(c => c));

        engine.SetSourceEnabled(ProgramBus.Pgm1, "src-1", enabled: false);
        engine.GetPreviewFrame(ProgramBus.Pgm1);
        Assert.Equal([2], gpu.ComposeCalls[^1].Select(l => l.Channel));
    }

    [Fact]
    public void SceneSnapshot_RoundTripsBothBusesPreviewState()
    {
        var gpu = new FakeGpuCompositor();
        var frameSource = new FakeFrameSource { ChannelsById = { ["src-1"] = 1, ["src-2"] = 2 } };
        using var source = new CompositorEngine(frameSource, gpu, canvasWidth: 100, canvasHeight: 100);
        source.ApplyProgram(new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("src-1", Pip())], Take: false));
        source.ApplyProgram(new ProgramRequest(ProgramBus.Pgm2, [new ProgramLayer("src-2", Pip())], Take: false));

        var snapshot = source.GetSceneSnapshot();

        using var target = new CompositorEngine(frameSource, gpu, canvasWidth: 100, canvasHeight: 100);
        target.ApplySceneSnapshot(snapshot);

        target.GetPreviewFrame(ProgramBus.Pgm1);
        Assert.Equal([1], gpu.ComposeCalls[^1].Select(l => l.Channel));

        target.GetPreviewFrame(ProgramBus.Pgm2);
        Assert.Equal([2], gpu.ComposeCalls[^1].Select(l => l.Channel));
    }

    private static PipSettings Pip() => new(true, 0, 0, 50, 50, 1.0, 0, null);
}
