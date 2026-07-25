using Switcher.App.Multiview;
using Switcher.Contracts;

namespace Switcher.App.Tests;

public sealed class MultiviewRegionModelTests
{
    [Fact]
    public void Reset_ProducesSixteenEmptyCells()
    {
        var model = new MultiviewRegionModel();

        Assert.Equal(16, model.Regions.Count);
        Assert.All(model.Regions, r =>
        {
            Assert.Equal(1, r.RowSpan);
            Assert.Equal(1, r.ColSpan);
            Assert.Equal("EMPTY", r.Content);
        });
    }

    [Fact]
    public void TryMerge_FilledRectangle_CreatesSpanningRegion()
    {
        var model = new MultiviewRegionModel();
        model.SetContent(0, 0, "PGM1");

        var merged = model.TryMerge([(0, 0), (0, 1), (1, 0), (1, 1)], out var error);

        Assert.True(merged);
        Assert.Null(error);
        var region = model.RegionAt(0, 0);
        Assert.NotNull(region);
        Assert.Equal(2, region!.RowSpan);
        Assert.Equal(2, region.ColSpan);
        Assert.Equal("PGM1", region.Content);
        // 2x2 merged + 12 remaining 1x1 cells.
        Assert.Equal(13, model.Regions.Count);
    }

    [Fact]
    public void TryMerge_NonRectangularSelection_IsRejected()
    {
        var model = new MultiviewRegionModel();

        // L-shape: (0,0),(1,0),(1,1) - bounding box (0,0)-(1,1) is not fully selected.
        var merged = model.TryMerge([(0, 0), (1, 0), (1, 1)], out var error);

        Assert.False(merged);
        Assert.NotNull(error);
        Assert.Equal(16, model.Regions.Count);
    }

    [Fact]
    public void TryMerge_SingleCell_IsRejected()
    {
        var model = new MultiviewRegionModel();

        Assert.False(model.TryMerge([(0, 0)], out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Split_RevertsMergedRegionToUnitCells()
    {
        var model = new MultiviewRegionModel();
        model.SetContent(0, 0, "PGM2");
        Assert.True(model.TryMerge([(0, 0), (0, 1), (1, 0), (1, 1)], out _));

        model.Split(0, 0);

        Assert.Equal(16, model.Regions.Count);
        Assert.All(model.Regions, r => Assert.Equal(1, r.RowSpan * r.ColSpan));
        Assert.Equal("PGM2", model.RegionAt(0, 0)!.Content);
        Assert.Equal("EMPTY", model.RegionAt(0, 1)!.Content);
    }

    [Fact]
    public void ToLayout_CarriesGridAndRegions()
    {
        var model = new MultiviewRegionModel();
        Assert.True(model.TryMerge([(2, 2), (2, 3), (3, 2), (3, 3)], out _));

        var layout = model.ToLayout();

        Assert.NotNull(layout.Grid);
        Assert.Equal(4, layout.Grid!.Rows);
        Assert.Equal(4, layout.Grid.Cols);
        Assert.NotNull(layout.Regions);
        Assert.Contains(layout.Regions!, r => r.RowSpan == 2 && r.ColSpan == 2 && r.Row == 2 && r.Col == 2);
        Assert.Equal(16, layout.Cells.Count);
    }

    [Fact]
    public void Load_RoundTripsRegionsForm()
    {
        var model = new MultiviewRegionModel();
        model.SetContent(0, 0, "PGM1");
        Assert.True(model.TryMerge([(0, 0), (0, 1), (1, 0), (1, 1)], out _));
        var layout = model.ToLayout();

        var reloaded = new MultiviewRegionModel();
        reloaded.Load(layout);

        var region = reloaded.RegionAt(0, 0);
        Assert.Equal(2, region!.RowSpan);
        Assert.Equal(2, region.ColSpan);
        Assert.Equal("PGM1", region.Content);
    }

    [Fact]
    public void Load_LegacyCellsForm_IsBackwardCompatible()
    {
        var cells = Enumerable.Repeat("EMPTY", 16).ToList();
        cells[5] = "PVW1";
        var model = new MultiviewRegionModel();

        model.Load(new MultiviewLayout(cells));

        Assert.Equal(16, model.Regions.Count);
        Assert.Equal("PVW1", model.RegionAt(1, 1)!.Content);
        Assert.Equal(4, model.Rows);
        Assert.Equal(4, model.Cols);
    }

    // --- resizable grid (4x4 … 6x6) ---------------------------------------------

    [Theory]
    [InlineData(5, 5, 25)]
    [InlineData(6, 6, 36)]
    [InlineData(4, 6, 24)]
    public void Resize_GrowsTheGridAndFillsTheNewCells(int rows, int cols, int expectedCells)
    {
        var model = new MultiviewRegionModel();

        model.Resize(rows, cols);

        Assert.Equal(rows, model.Rows);
        Assert.Equal(cols, model.Cols);
        Assert.Equal(expectedCells, model.Regions.Count);
        Assert.All(model.Regions, r => Assert.Equal("EMPTY", r.Content));
    }

    [Fact]
    public void Resize_ClampsToTheSupportedRange()
    {
        var model = new MultiviewRegionModel();

        model.Resize(1, 99);

        Assert.Equal(MultiviewRegionModel.MinSize, model.Rows);
        Assert.Equal(MultiviewRegionModel.MaxSize, model.Cols);
    }

    [Fact]
    public void Resize_KeepsMergedRegionsThatStillFitAndDropsThoseThatDont()
    {
        var model = new MultiviewRegionModel();
        model.Resize(6, 6);
        model.SetContent(0, 0, "PGM1");
        Assert.True(model.TryMerge([(0, 0), (0, 1), (1, 0), (1, 1)], out _));           // fits in 4x4
        Assert.True(model.TryMerge([(4, 4), (4, 5), (5, 4), (5, 5)], out _));           // only fits in 6x6

        model.Resize(4, 4);

        var kept = model.RegionAt(0, 0)!;
        Assert.Equal(2, kept.RowSpan);
        Assert.Equal("PGM1", kept.Content);

        // The out-of-range merge is gone and the grid is still fully tiled.
        Assert.Equal(4, model.Rows);
        Assert.Equal(13, model.Regions.Count);  // 16 cells - 4 merged into 1
        Assert.All(model.Regions, r =>
        {
            Assert.InRange(r.Row + r.RowSpan, 1, 4);
            Assert.InRange(r.Col + r.ColSpan, 1, 4);
        });
    }

    [Fact]
    public void ToLayout_ThenLoad_RoundTripsANonSquareGrid()
    {
        var model = new MultiviewRegionModel();
        model.Resize(5, 6);
        model.SetContent(4, 5, "PGM2");

        var reloaded = new MultiviewRegionModel();
        reloaded.Load(model.ToLayout());

        Assert.Equal(5, reloaded.Rows);
        Assert.Equal(6, reloaded.Cols);
        Assert.Equal("PGM2", reloaded.RegionAt(4, 5)!.Content);
    }
}
