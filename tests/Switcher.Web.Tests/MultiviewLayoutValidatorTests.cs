using Switcher.Contracts;

namespace Switcher.Web.Tests;

public class MultiviewLayoutValidatorTests
{
    private static readonly string[] ValidSixteenCells =
    [
        "PGM1", "PGM2", "PVW1", "PVW2",
        "SRC:src-ndi-cam1", "SRC:src-webcam-1", "EMPTY", "EMPTY",
        "EMPTY", "EMPTY", "EMPTY", "EMPTY",
        "EMPTY", "EMPTY", "EMPTY", "EMPTY",
    ];

    [Fact]
    public void Validate_AcceptsSixteenValidTokens()
    {
        var layout = new MultiviewLayout(ValidSixteenCells);

        var errors = MultiviewLayoutValidator.Validate(layout);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsWrongCellCount()
    {
        var layout = new MultiviewLayout(["PGM1", "EMPTY"]);

        var errors = MultiviewLayoutValidator.Validate(layout);

        Assert.Contains(errors, e => e.Contains("exactly 16"));
    }

    [Fact]
    public void Validate_RejectsUnknownToken()
    {
        var cells = (string[])ValidSixteenCells.Clone();
        cells[0] = "NOT_A_TOKEN";
        var layout = new MultiviewLayout(cells);

        var errors = MultiviewLayoutValidator.Validate(layout);

        Assert.Single(errors);
    }

    [Fact]
    public void Validate_RejectsBareSrcPrefixWithoutId()
    {
        var cells = (string[])ValidSixteenCells.Clone();
        cells[4] = "SRC:";
        var layout = new MultiviewLayout(cells);

        var errors = MultiviewLayoutValidator.Validate(layout);

        Assert.Single(errors);
    }

    [Fact]
    public void Validate_RejectsNullRequest()
    {
        var errors = MultiviewLayoutValidator.Validate(null);

        Assert.NotEmpty(errors);
    }

    private static readonly MultiviewGrid Grid4x4 = new(4, 4);

    /// <summary>4x4 fully covered by a 2x2 merged region at top-left plus 12 remaining 1x1 cells.</summary>
    private static IReadOnlyList<MultiviewRegion> FullCoverageWithMerge()
    {
        var regions = new List<MultiviewRegion> { new(0, 0, 2, 2, "PGM1") };
        for (var r = 0; r < 4; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                if (r < 2 && c < 2)
                {
                    continue; // covered by the merged region
                }

                regions.Add(new MultiviewRegion(r, c, 1, 1, "EMPTY"));
            }
        }

        return regions;
    }

    [Fact]
    public void Validate_AcceptsRegionsThatFullyCoverGrid()
    {
        var layout = new MultiviewLayout([], Grid4x4, FullCoverageWithMerge());

        var errors = MultiviewLayoutValidator.Validate(layout);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsOverlappingRegions()
    {
        var regions = new List<MultiviewRegion>(FullCoverageWithMerge())
        {
            new(0, 0, 1, 1, "PGM2"), // overlaps the 2x2 merged region
        };
        var layout = new MultiviewLayout([], Grid4x4, regions);

        var errors = MultiviewLayoutValidator.Validate(layout);

        Assert.Contains(errors, e => e.Contains("overlaps"));
    }

    [Fact]
    public void Validate_RejectsRegionsThatLeaveGaps()
    {
        // Only the merged 2x2 region; the other 12 cells are uncovered.
        var layout = new MultiviewLayout([], Grid4x4, [new MultiviewRegion(0, 0, 2, 2, "PGM1")]);

        var errors = MultiviewLayoutValidator.Validate(layout);

        Assert.Contains(errors, e => e.Contains("uncovered"));
    }

    [Fact]
    public void Validate_RejectsRegionOutsideGrid()
    {
        var layout = new MultiviewLayout([], Grid4x4, [new MultiviewRegion(0, 0, 5, 5, "PGM1")]);

        var errors = MultiviewLayoutValidator.Validate(layout);

        Assert.Contains(errors, e => e.Contains("rectangle inside"));
    }

    [Theory]
    [InlineData(3, 4)]   // below the 4x4 floor
    [InlineData(4, 7)]   // above the 6x6 ceiling
    [InlineData(0, 0)]
    public void Validate_RejectsGridOutsideSupportedRange(int rows, int cols)
    {
        var layout = new MultiviewLayout([], new MultiviewGrid(rows, cols), [new MultiviewRegion(0, 0, 1, 1, "PGM1")]);

        var errors = MultiviewLayoutValidator.Validate(layout);

        Assert.Contains(errors, e => e.Contains("between 4 and 6"));
    }

    [Fact]
    public void Validate_AcceptsA6x6Grid()
    {
        var regions = new List<MultiviewRegion>();
        for (var row = 0; row < 6; row++)
        {
            for (var col = 0; col < 6; col++)
            {
                regions.Add(new MultiviewRegion(row, col, 1, 1, "EMPTY"));
            }
        }

        var errors = MultiviewLayoutValidator.Validate(new MultiviewLayout([], new MultiviewGrid(6, 6), regions));

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsRegionWithInvalidContent()
    {
        var regions = new List<MultiviewRegion>(FullCoverageWithMerge());
        regions[0] = new MultiviewRegion(0, 0, 2, 2, "NOT_A_TOKEN");
        var layout = new MultiviewLayout([], Grid4x4, regions);

        var errors = MultiviewLayoutValidator.Validate(layout);

        Assert.Contains(errors, e => e.Contains("content"));
    }

    [Fact]
    public void Validate_RejectsBothCellsAndRegions()
    {
        var layout = new MultiviewLayout(ValidSixteenCells, Grid4x4, FullCoverageWithMerge());

        var errors = MultiviewLayoutValidator.Validate(layout);

        Assert.Contains(errors, e => e.Contains("not both"));
    }

    [Fact]
    public void Validate_RejectsNeitherCellsNorRegions()
    {
        var layout = new MultiviewLayout([], null, null);

        var errors = MultiviewLayoutValidator.Validate(layout);

        Assert.Contains(errors, e => e.Contains("required"));
    }
}
