using Switcher.Contracts;

namespace Switcher.Web.Tests;

/// <summary>
/// Edge cases for <see cref="SourceDefinitionValidator"/>, with a case per source type. The API is the
/// only place a malformed source can enter from outside the app, so every type-specific config that the
/// engine dereferences has to be required here rather than blowing up natively later.
/// </summary>
public sealed class SourceDefinitionValidatorEdgeCaseTests
{
    private static SourceDefinition Bare(SourceType type, string id = "s1") =>
        new(id, "Source", type, null, null, null);

    [Fact]
    public void NullBody_IsRejected() =>
        Assert.Contains(SourceDefinitionValidator.Validate(null), e => e.Contains("Request body"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankIdAndName_AreRejected(string blank)
    {
        var errors = SourceDefinitionValidator.Validate(
            new SourceDefinition(blank, blank, SourceType.Webcam, null, new WebcamConfig("dev", null), null));

        Assert.Contains(errors, e => e.Contains("id is required"));
        Assert.Contains(errors, e => e.Contains("name is required"));
    }

    [Theory]
    [InlineData(SourceType.Ndi, "ndi.source_name")]
    [InlineData(SourceType.Webcam, "webcam.device_id")]
    [InlineData(SourceType.Srt, "srt.url")]
    [InlineData(SourceType.Image, "image.file_path")]
    [InlineData(SourceType.Html, "html.url")]
    [InlineData(SourceType.Mix, "mix is required")]
    public void MissingTypeConfig_IsRejected(SourceType type, string expected) =>
        Assert.Contains(SourceDefinitionValidator.Validate(Bare(type)), e => e.Contains(expected));

    [Theory]
    [InlineData(0, 1080, 30)]
    [InlineData(1920, 0, 30)]
    [InlineData(1920, 1080, 0)]
    [InlineData(-1, -1, -1)]
    public void Html_NonPositiveGeometry_IsRejected(int width, int height, int fps)
    {
        var source = new SourceDefinition(
            "s1", "Page", SourceType.Html, null, null, null, null,
            new HtmlConfig("https://example.test", width, height, false, fps));

        Assert.NotEmpty(SourceDefinitionValidator.Validate(source));
    }

    [Fact]
    public void Html_ValidPage_IsAccepted()
    {
        var source = new SourceDefinition(
            "s1", "Page", SourceType.Html, null, null, null, null,
            new HtmlConfig("https://example.test", 1280, 720, false, 30));

        Assert.Empty(SourceDefinitionValidator.Validate(source));
    }

    [Fact]
    public void Mix_WithNoLayers_IsRejected()
    {
        var source = new SourceDefinition(
            "m", "Mix", SourceType.Mix, null, null, null, null, null, new MixConfig([]));

        Assert.Contains(SourceDefinitionValidator.Validate(source), e => e.Contains("at least one layer"));
    }

    [Fact]
    public void Mix_ContainingItself_IsRejected()
    {
        var source = new SourceDefinition(
            "m", "Mix", SourceType.Mix, null, null, null, null, null,
            new MixConfig([new MixLayer("m", 0, 0, 100, 100)]));

        Assert.Contains(SourceDefinitionValidator.Validate(source), e => e.Contains("the mix itself"));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    public void Mix_LayerWithNonPositiveSize_IsRejected(int width, int height)
    {
        var source = new SourceDefinition(
            "m", "Mix", SourceType.Mix, null, null, null, null, null,
            new MixConfig([new MixLayer("cam", 0, 0, width, height)]));

        Assert.Contains(SourceDefinitionValidator.Validate(source), e => e.Contains("positive width and height"));
    }

    [Fact]
    public void Mix_LayerWithBlankSourceId_IsRejected()
    {
        var source = new SourceDefinition(
            "m", "Mix", SourceType.Mix, null, null, null, null, null,
            new MixConfig([new MixLayer("  ", 0, 0, 100, 100)]));

        Assert.Contains(SourceDefinitionValidator.Validate(source), e => e.Contains("source_id is required"));
    }

    [Fact]
    public void Mix_NonPositiveCanvas_IsRejected()
    {
        var source = new SourceDefinition(
            "m", "Mix", SourceType.Mix, null, null, null, null, null,
            new MixConfig([new MixLayer("cam", 0, 0, 100, 100)], 0, -1));

        Assert.Contains(SourceDefinitionValidator.Validate(source), e => e.Contains("canvas_width"));
    }

    [Fact]
    public void Mix_LayersMayOverlapAndSitOffCanvas()
    {
        // Overlap is the whole point of a mix, and a partly off-canvas layer is a legitimate way to bleed
        // a graphic off the edge - neither is an error.
        var source = new SourceDefinition(
            "m", "Mix", SourceType.Mix, null, null, null, null, null,
            new MixConfig(
            [
                new MixLayer("cam", 0, 0, 1920, 1080),
                new MixLayer("logo", -100, 900, 640, 360, ZOrder: 1),
            ]));

        Assert.Empty(SourceDefinitionValidator.Validate(source));
    }
}
