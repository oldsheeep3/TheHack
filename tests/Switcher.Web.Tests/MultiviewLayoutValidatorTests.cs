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
}
