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

    [Fact]
    public void Validate_AcceptsNdiOutputWithPgmSourceAndName()
    {
        var request = new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, null, null, null, "Switcher PGM1"),
            new OutputAssignment(OutputSink.Ndi2, OutputSource.Pgm2, null, null, null, null),
        ]);

        var errors = OutputsRequestValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsNdiOutputWithEmptyName()
    {
        var request = new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, null, null, null, string.Empty),
        ]);

        var errors = OutputsRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("ndi_name"));
    }

    [Fact]
    public void Validate_AllowsNdiOutputWithNullNameForDefault()
    {
        var request = new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, null, null, null, null),
            new OutputAssignment(OutputSink.Ndi2, OutputSource.Pgm2, null, null, null, null),
        ]);

        var errors = OutputsRequestValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsATableThatLeavesAProgramBusWithNoOutput()
    {
        // Both sinks point at PGM1, so PGM2 is being switched but never reaches a device.
        var request = new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, null, null, null),
        ]);

        var errors = OutputsRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("Pgm2"));
    }

    [Fact]
    public void Validate_RejectsAnEmptyOutputTable()
    {
        var errors = OutputsRequestValidator.Validate(new OutputsRequest([]));

        Assert.Contains(errors, e => e.Contains("at least one output"));
    }

    [Fact]
    public void Validate_RejectsNdiOutputWithInvalidSource()
    {
        // A source value outside PGM1/PGM2 (only reachable if a caller crafts an out-of-range enum).
        var request = new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Ndi1, (OutputSource)99, null, null, null, "Feed"),
        ]);

        var errors = OutputsRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("source") && e.Contains("NDI"));
    }
}
