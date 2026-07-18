using Switcher.Contracts;

namespace Switcher.Web.Tests;

public class SourceDefinitionValidatorTests
{
    [Fact]
    public void Validate_AcceptsNdiSourceWithSourceName()
    {
        var source = new SourceDefinition(
            "src-ndi-cam1", "Cam 1 (NDI)", SourceType.Ndi, new NdiConfig("STUDIO (Cam1)"), null, null);

        var errors = SourceDefinitionValidator.Validate(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsNdiSourceWithoutNdiConfig()
    {
        var source = new SourceDefinition("src-ndi-cam1", "Cam 1 (NDI)", SourceType.Ndi, null, null, null);

        var errors = SourceDefinitionValidator.Validate(source);

        Assert.Contains(errors, e => e.Contains("ndi.source_name"));
    }

    [Fact]
    public void Validate_RejectsMissingIdAndName()
    {
        var source = new SourceDefinition("", "", SourceType.Webcam, null, new WebcamConfig("cam0", null), null);

        var errors = SourceDefinitionValidator.Validate(source);

        Assert.Contains(errors, e => e.Contains("id is required"));
        Assert.Contains(errors, e => e.Contains("name is required"));
    }

    [Fact]
    public void Validate_RejectsNullRequest()
    {
        var errors = SourceDefinitionValidator.Validate(null);

        Assert.NotEmpty(errors);
    }
}
