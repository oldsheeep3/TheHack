using Switcher.App.Multiview;
using Switcher.Contracts;

namespace Switcher.App.Tests;

/// <summary>
/// Edge cases for <see cref="MultiviewRegionModel"/>: the merge guard, grid resizing and the tolerance
/// required of <see cref="MultiviewRegionModel.Load"/>, which has to accept whatever a persisted config
/// or the Web API hands it and still end up with a fully tiled grid.
/// </summary>
public sealed class MultiviewRegionModelEdgeCaseTests
{
    private static MultiviewRegionModel Model(int rows = 4, int cols = 4)
    {
        var model = new MultiviewRegionModel();
        model.Resize(rows, cols);
        return model;
    }

    /// <summary>Every cell covered exactly once — the invariant every operation must preserve.</summary>
    private static void AssertFullyTiled(MultiviewRegionModel model)
    {
        var covered = new int[model.Rows, model.Cols];
        foreach (var region in model.Regions)
        {
            for (var r = region.Row; r < region.Row + region.RowSpan; r++)
            {
                for (var c = region.Col; c < region.Col + region.ColSpan; c++)
                {
                    Assert.InRange(r, 0, model.Rows - 1);
                    Assert.InRange(c, 0, model.Cols - 1);
                    covered[r, c]++;
                }
            }
        }

        for (var r = 0; r < model.Rows; r++)
        {
            for (var c = 0; c < model.Cols; c++)
            {
                Assert.Equal(1, covered[r, c]);
            }
        }
    }

    // --- merge guard ------------------------------------------------------------

    [Fact]
    public void Merge_SingleCell_IsRejected()
    {
        var model = Model();
        Assert.False(model.TryMerge([(0, 0)], out var error));
        Assert.Contains("at least two", error);
    }

    [Fact]
    public void Merge_EmptySelection_IsRejected() =>
        Assert.False(Model().TryMerge([], out _));

    [Fact]
    public void Merge_LShape_IsRejected()
    {
        var model = Model();
        Assert.False(model.TryMerge([(0, 0), (0, 1), (1, 0)], out var error));
        Assert.Contains("rectangle", error);
        AssertFullyTiled(model);
    }

    [Fact]
    public void Merge_DiagonalCells_IsRejected() =>
        Assert.False(Model().TryMerge([(0, 0), (1, 1)], out _));

    [Fact]
    public void Merge_OutOfBounds_IsRejected()
    {
        var model = Model();
        Assert.False(model.TryMerge([(0, 0), (0, 99)], out var error));
        Assert.Contains("out of bounds", error);
    }

    [Fact]
    public void Merge_PartiallyOverlappingAnExistingMerge_IsRejected()
    {
        var model = Model();
        Assert.True(model.TryMerge([(0, 0), (0, 1), (1, 0), (1, 1)], out _));

        // (1,1) is inside the 2x2 merge but (2,1)/(2,2) are not, so this would cut the merge in half.
        Assert.False(model.TryMerge([(1, 1), (1, 2), (2, 1), (2, 2)], out var error));
        Assert.Contains("partially", error);
        AssertFullyTiled(model);
    }

    [Fact]
    public void Merge_WhollyContainingAnExistingMerge_IsAllowed()
    {
        var model = Model();
        Assert.True(model.TryMerge([(0, 0), (0, 1), (1, 0), (1, 1)], out _));

        var whole = new List<(int, int)>();
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                whole.Add((r, c));
            }
        }

        Assert.True(model.TryMerge(whole, out _));
        Assert.Equal(3, model.RegionAt(2, 2)!.RowSpan);
        AssertFullyTiled(model);
    }

    [Fact]
    public void Merge_TakesContentFromTheTopLeftCell()
    {
        var model = Model();
        model.SetContent(0, 0, "PGM1");
        model.SetContent(1, 1, "PVW2");

        Assert.True(model.TryMerge([(0, 0), (0, 1), (1, 0), (1, 1)], out _));

        Assert.Equal("PGM1", model.RegionAt(1, 1)!.Content);
    }

    [Fact]
    public void Merge_EntireGrid_LeavesOneRegion()
    {
        var model = Model(6, 6);
        var all = new List<(int, int)>();
        for (var r = 0; r < 6; r++)
        {
            for (var c = 0; c < 6; c++)
            {
                all.Add((r, c));
            }
        }

        Assert.True(model.TryMerge(all, out _));
        Assert.Single(model.Regions);
        AssertFullyTiled(model);
    }

    // --- split ------------------------------------------------------------------

    [Fact]
    public void Split_A1x1Cell_IsANoOp()
    {
        var model = Model();
        var before = model.Regions.Count;
        model.Split(2, 2);
        Assert.Equal(before, model.Regions.Count);
    }

    [Fact]
    public void Split_OffGrid_IsANoOp()
    {
        var model = Model();
        model.Split(99, 99);
        AssertFullyTiled(model);
    }

    [Fact]
    public void Split_KeepsContentOnTheTopLeftAndEmptiesTheRest()
    {
        var model = Model();
        model.SetContent(0, 0, "PGM1");
        Assert.True(model.TryMerge([(0, 0), (0, 1), (1, 0), (1, 1)], out _));

        model.Split(1, 1);

        Assert.Equal("PGM1", model.RegionAt(0, 0)!.Content);
        Assert.Equal("EMPTY", model.RegionAt(0, 1)!.Content);
        Assert.Equal("EMPTY", model.RegionAt(1, 1)!.Content);
        AssertFullyTiled(model);
    }

    // --- resize -----------------------------------------------------------------

    [Theory]
    [InlineData(4, 4)]
    [InlineData(4, 6)]
    [InlineData(6, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    public void Resize_LeavesTheGridFullyTiled(int rows, int cols)
    {
        var model = Model(rows, cols);
        AssertFullyTiled(model);
        Assert.Equal(rows * cols, model.Regions.Count);
    }

    [Fact]
    public void Resize_ToTheSameSize_KeepsMergesIntact()
    {
        var model = Model();
        Assert.True(model.TryMerge([(0, 0), (0, 1), (1, 0), (1, 1)], out _));
        var before = model.Regions.Count;

        model.Resize(4, 4);

        Assert.Equal(before, model.Regions.Count);
    }

    [Fact]
    public void Resize_ShrinkThenGrow_LeavesNoOverlapOrGap()
    {
        var model = Model(6, 6);
        Assert.True(model.TryMerge([(4, 4), (4, 5), (5, 4), (5, 5)], out _));

        model.Resize(4, 4);
        AssertFullyTiled(model);

        model.Resize(6, 6);
        AssertFullyTiled(model);
    }

    [Fact]
    public void SetContent_OffGrid_IsANoOp()
    {
        var model = Model();
        model.SetContent(99, 99, "PGM1");
        AssertFullyTiled(model);
    }

    [Fact]
    public void SetContent_OnAnyCellOfAMergedRegion_SetsTheWholeRegion()
    {
        var model = Model();
        Assert.True(model.TryMerge([(0, 0), (0, 1), (1, 0), (1, 1)], out _));

        model.SetContent(1, 1, "PVW1");

        Assert.Equal("PVW1", model.RegionAt(0, 0)!.Content);
    }

    // --- load tolerance ---------------------------------------------------------

    [Fact]
    public void Load_OverlappingRegions_KeepsTheFirstAndFillsTheRest()
    {
        var model = Model();
        model.Load(new MultiviewLayout([], new MultiviewGrid(4, 4),
        [
            new MultiviewRegion(0, 0, 2, 2, "PGM1"),
            new MultiviewRegion(1, 1, 2, 2, "PGM2"),  // overlaps the first
        ]));

        Assert.Equal("PGM1", model.RegionAt(0, 0)!.Content);
        AssertFullyTiled(model);
    }

    [Fact]
    public void Load_RegionsOutsideTheGrid_AreDropped()
    {
        var model = Model();
        model.Load(new MultiviewLayout([], new MultiviewGrid(4, 4),
        [
            new MultiviewRegion(9, 9, 1, 1, "PGM1"),
            new MultiviewRegion(-1, 0, 1, 1, "PGM2"),
        ]));

        AssertFullyTiled(model);
        Assert.All(model.Regions, r => Assert.Equal("EMPTY", r.Content));
    }

    [Fact]
    public void Load_RegionOverhangingTheEdge_IsClampedNotDropped()
    {
        var model = Model();
        model.Load(new MultiviewLayout([], new MultiviewGrid(4, 4),
            [new MultiviewRegion(3, 3, 4, 4, "PGM1")]));

        var region = model.RegionAt(3, 3)!;
        Assert.Equal("PGM1", region.Content);
        Assert.Equal(1, region.RowSpan);
        AssertFullyTiled(model);
    }

    [Fact]
    public void Load_EmptyLayout_StillTilesTheGrid()
    {
        var model = Model(5, 5);
        model.Load(new MultiviewLayout([], new MultiviewGrid(5, 5), []));

        Assert.Equal(25, model.Regions.Count);
        AssertFullyTiled(model);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(99, 99)]
    [InlineData(0, 0)]
    public void Load_GridOutsideTheSupportedRange_IsClamped(int rows, int cols)
    {
        var model = Model();
        model.Load(new MultiviewLayout([], new MultiviewGrid(rows, cols), []));

        Assert.InRange(model.Rows, MultiviewRegionModel.MinSize, MultiviewRegionModel.MaxSize);
        Assert.InRange(model.Cols, MultiviewRegionModel.MinSize, MultiviewRegionModel.MaxSize);
        AssertFullyTiled(model);
    }

    [Fact]
    public void Load_LegacyCells_ResetsToTheFourByFourGrid()
    {
        var model = Model(6, 6);
        model.Load(new MultiviewLayout([.. Enumerable.Repeat("EMPTY", 16)]));

        Assert.Equal(4, model.Rows);
        Assert.Equal(4, model.Cols);
        AssertFullyTiled(model);
    }

    [Fact]
    public void ToLayout_RoundTripsThroughEveryGridSize()
    {
        for (var rows = MultiviewRegionModel.MinSize; rows <= MultiviewRegionModel.MaxSize; rows++)
        {
            for (var cols = MultiviewRegionModel.MinSize; cols <= MultiviewRegionModel.MaxSize; cols++)
            {
                var model = Model(rows, cols);
                model.SetContent(rows - 1, cols - 1, "PVW2");

                var reloaded = new MultiviewRegionModel();
                reloaded.Load(model.ToLayout());

                Assert.Equal(rows, reloaded.Rows);
                Assert.Equal(cols, reloaded.Cols);
                Assert.Equal("PVW2", reloaded.RegionAt(rows - 1, cols - 1)!.Content);
                AssertFullyTiled(reloaded);
            }
        }
    }
}
