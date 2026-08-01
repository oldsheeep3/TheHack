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
            new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm2, null, null, null),
            new OutputAssignment(OutputSink.Hdmi1, OutputSource.Pgm1, DisplayId: 1, HideCursor: true, Fullscreen: true),
        ]);

        var errors = OutputsRequestValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(OutputSink.Hdmi1)]
    [InlineData(OutputSink.Hdmi2)]
    [InlineData(OutputSink.Hdmi3)]
    public void Validate_RejectsHdmiOutputWithoutDisplayId(OutputSink sink)
    {
        // Every HDMI ordinal opens its own projector window, so every one of them needs a display.
        var request = new OutputsRequest(
        [
            new OutputAssignment(sink, OutputSource.Pgm1, null, null, null),
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

    [Theory]
    [InlineData(OutputSink.Ndi1)]
    [InlineData(OutputSink.Ndi2)]
    [InlineData(OutputSink.Ndi3)]
    public void Validate_RejectsNdiOutputWithEmptyName(OutputSink sink)
    {
        // A sender with no name is invisible to receivers, whichever ordinal the operator added.
        var request = new OutputsRequest(
        [
            new OutputAssignment(sink, OutputSource.Pgm1, null, null, null, string.Empty),
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

    [Fact]
    public void Validate_RejectsMoreThanThreeSinksOfOneKind()
    {
        // Only three NDI ordinals exist, so a fourth NDI row has to repeat one; the point of the test is
        // the per-kind ceiling, which counts rows rather than distinct sinks.
        var request = new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Ndi2, OutputSource.Pgm2, null, null, null),
            new OutputAssignment(OutputSink.Ndi3, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm2, null, null, null),
        ]);

        var errors = OutputsRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("4 NDI sinks") && e.Contains("at most 3"));
    }

    [Fact]
    public void Validate_RejectsMoreThanSixSinksInTotal()
    {
        // Three HDMI and three NDI: neither kind is over its own ceiling, only the table as a whole is.
        var request = new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Hdmi1, OutputSource.Pgm2, DisplayId: 0, HideCursor: true, Fullscreen: true),
            new OutputAssignment(OutputSink.Hdmi2, OutputSource.Pgm1, DisplayId: 1, HideCursor: true, Fullscreen: true),
            new OutputAssignment(OutputSink.Hdmi3, OutputSource.Pgm2, DisplayId: 2, HideCursor: true, Fullscreen: true),
            new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Ndi2, OutputSource.Pgm2, null, null, null),
            new OutputAssignment(OutputSink.Ndi3, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
        ]);

        var errors = OutputsRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("7 sinks") && e.Contains("at most 6"));
    }

    [Fact]
    public void Validate_RejectsASecondWebcamSink()
    {
        // OBS exposes one virtual camera, so a second webcam sink is refused even in a table with room
        // to spare — the ceiling is the kind's, not the table's.
        var request = new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm2, null, null, null),
        ]);

        var errors = OutputsRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("2 WEBCAM sinks") && e.Contains("at most 1"));
    }

    [Fact]
    public void Validate_AcceptsATableAtBothCeilings()
    {
        // One webcam, three of the kinds that allow three, six in total: the largest table an operator
        // can build, so it must pass.
        var request = new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Hdmi1, OutputSource.Pgm2, DisplayId: 0, HideCursor: true, Fullscreen: true),
            new OutputAssignment(OutputSink.Hdmi2, OutputSource.Pgm1, DisplayId: 1, HideCursor: true, Fullscreen: true),
            new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, null, null, null, "Feed 1"),
            new OutputAssignment(OutputSink.Ndi2, OutputSource.Pgm2, null, null, null, "Feed 2"),
            new OutputAssignment(OutputSink.Ndi3, OutputSource.Pgm2, null, null, null, "Feed 3"),
        ]);

        var errors = OutputsRequestValidator.Validate(request);

        Assert.Empty(errors);
    }
}
