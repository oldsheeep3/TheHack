using Switcher.Contracts;
using Switcher.Media.Multiview;

namespace Switcher.Media.Tests;

public class MultiviewLayoutCalculatorTests
{
    private const int CanvasWidth = 1920;
    private const int CanvasHeight = 1080;

    [Fact]
    public void Build_CombinedTopLeft2x2_ExpandsToTheMergedRectangle()
    {
        var layout = new MultiviewLayout(
            Cells: [],
            Grid: new MultiviewGrid(4, 4),
            Regions:
            [
                new MultiviewRegion(0, 0, 2, 2, "PGM1"),
                new MultiviewRegion(0, 2, 1, 1, "PVW1"),
            ]);

        var tiles = MultiviewLayoutCalculator.Build(layout, CanvasWidth, CanvasHeight);

        var pgm = tiles.Single(t => t.Content == "PGM1");
        Assert.Equal((0, 0, CanvasWidth / 2, CanvasHeight / 2), (pgm.X, pgm.Y, pgm.Width, pgm.Height));

        var pvw = tiles.Single(t => t.Content == "PVW1");
        Assert.Equal((CanvasWidth / 2, 0, CanvasWidth / 4, CanvasHeight / 4), (pvw.X, pvw.Y, pvw.Width, pvw.Height));
    }

    [Fact]
    public void Build_LegacyCells_ProduceSixteenEqualOneByOneTiles()
    {
        var cells = Enumerable.Range(0, 16).Select(i => $"SRC:src-{i}").ToArray();
        var layout = new MultiviewLayout(cells);

        var tiles = MultiviewLayoutCalculator.Build(layout, CanvasWidth, CanvasHeight);

        Assert.Equal(16, tiles.Count);
        // Cell (row=1, col=2) is index 6 in the 4-column legacy layout.
        var tile = tiles[6];
        Assert.Equal("SRC:src-6", tile.Content);
        Assert.Equal((2 * CanvasWidth / 4, 1 * CanvasHeight / 4, CanvasWidth / 4, CanvasHeight / 4),
            (tile.X, tile.Y, tile.Width, tile.Height));
    }

    [Fact]
    public void Build_LegacyCellsAndEquivalentRegionForm_ProduceIdenticalTiles()
    {
        var cells = Enumerable.Range(0, 16).Select(i => $"SRC:src-{i}").ToArray();
        var legacy = new MultiviewLayout(cells);

        var regions = Enumerable.Range(0, 16)
            .Select(i => new MultiviewRegion(i / 4, i % 4, 1, 1, $"SRC:src-{i}"))
            .ToArray();
        var regionForm = new MultiviewLayout([], new MultiviewGrid(4, 4), regions);

        var fromCells = MultiviewLayoutCalculator.Build(legacy, CanvasWidth, CanvasHeight);
        var fromRegions = MultiviewLayoutCalculator.Build(regionForm, CanvasWidth, CanvasHeight);

        Assert.Equal(fromCells, fromRegions);
    }

    [Fact]
    public void Build_TilesCoverTheWholeCanvasWithoutGapsAtTheRightAndBottomEdges()
    {
        // 3 columns over 1920 doesn't divide evenly; edge-derived widths must still reach the border.
        var layout = new MultiviewLayout(
            Cells: [],
            Grid: new MultiviewGrid(1, 3),
            Regions:
            [
                new MultiviewRegion(0, 0, 1, 1, "A"),
                new MultiviewRegion(0, 1, 1, 1, "B"),
                new MultiviewRegion(0, 2, 1, 1, "C"),
            ]);

        var tiles = MultiviewLayoutCalculator.Build(layout, 1920, 1080);

        var last = tiles.Single(t => t.Content == "C");
        Assert.Equal(1920, last.X + last.Width);
        Assert.All(tiles, t => Assert.Equal(1080, t.Y + t.Height));
    }

    [Fact]
    public void Build_EmptyLayout_ReturnsNoTiles()
    {
        Assert.Empty(MultiviewLayoutCalculator.Build(new MultiviewLayout([]), CanvasWidth, CanvasHeight));
    }
}
