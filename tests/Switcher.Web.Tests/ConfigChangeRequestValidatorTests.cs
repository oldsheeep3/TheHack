using Switcher.Contracts;

namespace Switcher.Web.Tests;

public class ConfigChangeRequestValidatorTests
{
    [Fact]
    public void Validate_AcceptsMinimalValidUvcRequest()
    {
        var request = new ConfigChangeRequest(1, SourceProtocol.Uvc, SourceUrl: null, PipSettings: null);

        var errors = ConfigChangeRequestValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_AcceptsFullSrtRequestWithPip()
    {
        var request = new ConfigChangeRequest(
            2,
            SourceProtocol.Srt,
            "srt://192.168.1.100:9000?mode=caller",
            new PipSettings(true, 1420, 80, 480, 270, 1.0, 0, new CropRect(0, 0, 1920, 1080)));

        var errors = ConfigChangeRequestValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsNullRequest()
    {
        var errors = ConfigChangeRequestValidator.Validate(null);

        Assert.Single(errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsNonPositiveTargetChannel(int channel)
    {
        var request = new ConfigChangeRequest(channel, SourceProtocol.Uvc, null, null);

        var errors = ConfigChangeRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("target_channel"));
    }

    [Theory]
    [InlineData(SourceProtocol.Ndi)]
    [InlineData(SourceProtocol.Srt)]
    public void Validate_RequiresSourceUrlForNetworkProtocols(SourceProtocol protocol)
    {
        var request = new ConfigChangeRequest(1, protocol, SourceUrl: null, PipSettings: null);

        var errors = ConfigChangeRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("source_url"));
    }

    [Fact]
    public void Validate_RejectsOpacityOutOfRange()
    {
        var request = new ConfigChangeRequest(
            1, SourceProtocol.Uvc, null,
            new PipSettings(true, 0, 0, 100, 100, 1.5, 0, null));

        var errors = ConfigChangeRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("opacity"));
    }

    [Fact]
    public void Validate_RejectsInvertedCropRectangle()
    {
        var request = new ConfigChangeRequest(
            1, SourceProtocol.Uvc, null,
            new PipSettings(true, 0, 0, 100, 100, 1.0, 0, new CropRect(100, 100, 10, 10)));

        var errors = ConfigChangeRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("crop"));
    }

    [Fact]
    public void Validate_RejectsNegativePipDimensions()
    {
        var request = new ConfigChangeRequest(
            1, SourceProtocol.Uvc, null,
            new PipSettings(true, -1, -1, -10, -10, 1.0, 0, null));

        var errors = ConfigChangeRequestValidator.Validate(request);

        Assert.True(errors.Count >= 4);
    }
}
