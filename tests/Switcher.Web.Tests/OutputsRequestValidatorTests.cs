using Switcher.Contracts;

namespace Switcher.Web.Tests;

public class OutputsRequestValidatorTests
{
    [Fact]
    public void Validate_AcceptsOneOutputPerSinkWithHdmiDisplayId()
    {
        var request = new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm2, null, null, null),
            new OutputAssignment(OutputSink.Hdmi, OutputSource.Pgm1, DisplayId: 1, HideCursor: true, Fullscreen: true),
        ]);

        var errors = OutputsRequestValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsHdmiOutputWithoutDisplayId()
    {
        var request = new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Hdmi, OutputSource.Pgm1, null, null, null),
        ]);

        var errors = OutputsRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("display_id"));
    }

    [Fact]
    public void Validate_RejectsDuplicateSinkAssignment()
    {
        var request = new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm2, null, null, null),
        ]);

        var errors = OutputsRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("more than once"));
    }

    [Fact]
    public void Validate_RejectsNullRequest()
    {
        var errors = OutputsRequestValidator.Validate(null);

        Assert.NotEmpty(errors);
    }
}
