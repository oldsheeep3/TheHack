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
}
