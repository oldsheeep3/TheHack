using Switcher.Contracts;

namespace Switcher.Media.Tests;

public class MultiviewCompositionTests
{
    private const int CanvasWidth = 1920;
    private const int CanvasHeight = 1080;

    [Fact]
    public void GetMultiviewFrame_CombinedRegion_PlacesTheSourceEnlargedAcrossTheMergedRectangle()
    {
        var gpu = new FakeGpuCompositor();
        var frameSource = new FakeFrameSource { ChannelsById = { ["cam"] = 5 } };
        using var engine = new CompositorEngine(frameSource, gpu, CanvasWidth, CanvasHeight);

        var layout = new MultiviewLayout(
            Cells: [],
            Grid: new MultiviewGrid(4, 4),
            Regions:
            [
                new MultiviewRegion(0, 0, 2, 2, "SRC:cam"),
                new MultiviewRegion(0, 2, 1, 1, "EMPTY"),
            ]);

        engine.GetMultiviewFrame(layout);

        var layer = Assert.Single(gpu.ComposeCalls[^1]);
        Assert.Equal(5, layer.Channel);
        Assert.Equal((0, 0, CanvasWidth / 2, CanvasHeight / 2),
            (layer.Settings.X, layer.Settings.Y, layer.Settings.Width, layer.Settings.Height));
    }

    [Fact]
    public void GetMultiviewFrame_EmptyContent_LeavesTheTileBlankWithNoLayer()
    {
        var gpu = new FakeGpuCompositor();
        using var engine = new CompositorEngine(new FakeFrameSource(), gpu, CanvasWidth, CanvasHeight);

        var layout = new MultiviewLayout(
            Cells: [],
            Grid: new MultiviewGrid(1, 1),
            Regions: [new MultiviewRegion(0, 0, 1, 1, "EMPTY")]);

        engine.GetMultiviewFrame(layout);

        Assert.Empty(gpu.ComposeCalls[^1]);
    }

    [Fact]
    public void GetMultiviewFrame_UnknownSource_IsDroppedRatherThanThrowing()
    {
        var gpu = new FakeGpuCompositor();
        using var engine = new CompositorEngine(new FakeFrameSource(), gpu, CanvasWidth, CanvasHeight);

        var layout = new MultiviewLayout(
            Cells: [],
            Grid: new MultiviewGrid(1, 1),
            Regions: [new MultiviewRegion(0, 0, 1, 1, "SRC:missing")]);

        engine.GetMultiviewFrame(layout);

        Assert.Empty(gpu.ComposeCalls[^1]);
    }

    [Fact]
    public void GetMultiviewFrame_LegacyCellsAndEquivalentRegionForm_ComposeTheSameLayers()
    {
        var gpu = new FakeGpuCompositor();
        var frameSource = new FakeFrameSource();
        for (var i = 0; i < 16; i++)
        {
            frameSource.ChannelsById[$"src-{i}"] = i;
        }

        using var engine = new CompositorEngine(frameSource, gpu, CanvasWidth, CanvasHeight);

        var cells = Enumerable.Range(0, 16).Select(i => $"SRC:src-{i}").ToArray();
        engine.GetMultiviewFrame(new MultiviewLayout(cells));
        var fromCells = Snapshot(gpu.ComposeCalls[^1]);

        var regions = Enumerable.Range(0, 16)
            .Select(i => new MultiviewRegion(i / 4, i % 4, 1, 1, $"SRC:src-{i}"))
            .ToArray();
        engine.GetMultiviewFrame(new MultiviewLayout([], new MultiviewGrid(4, 4), regions));
        var fromRegions = Snapshot(gpu.ComposeCalls[^1]);

        Assert.Equal(fromCells, fromRegions);
    }

    private static IReadOnlyList<(int Channel, int X, int Y, int W, int H, int Z)> Snapshot(
        IEnumerable<Switcher.Media.Compositing.CompositedLayer> layers) =>
        layers.Select(l => (l.Channel, l.Settings.X, l.Settings.Y, l.Settings.Width, l.Settings.Height, l.Settings.ZOrder)).ToList();
}
