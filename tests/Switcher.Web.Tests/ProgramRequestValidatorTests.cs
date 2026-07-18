using Switcher.Contracts;

namespace Switcher.Web.Tests;

public class ProgramRequestValidatorTests
{
    [Fact]
    public void Validate_AcceptsValidLayers()
    {
        var request = new ProgramRequest(
            ProgramBus.Pgm1,
            [new ProgramLayer("src-ndi-cam1", new PipSettings(true, 0, 0, 1920, 1080, 1.0, 0, null))],
            Take: false);

        var errors = ProgramRequestValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsLayerWithMissingSourceId()
    {
        var request = new ProgramRequest(
            ProgramBus.Pgm1,
            [new ProgramLayer("", new PipSettings(true, 0, 0, 1920, 1080, 1.0, 0, null))],
            Take: false);

        var errors = ProgramRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("source_id"));
    }

    [Fact]
    public void Validate_RejectsNullRequest()
    {
        var errors = ProgramRequestValidator.Validate(null);

        Assert.NotEmpty(errors);
    }
}
