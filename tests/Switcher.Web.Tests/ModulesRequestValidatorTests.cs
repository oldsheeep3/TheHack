using Switcher.Contracts;

namespace Switcher.Web.Tests;

public class ModulesRequestValidatorTests
{
    [Fact]
    public void Validate_AcceptsInRangeUniqueIndices()
    {
        var request = new ModulesRequest(
        [
            new ModuleMapping(0, new ModuleSourceBinding("src-ndi-cam1", "transition"), new ModuleSourceBinding("src-webcam-1", "opacity")),
        ]);

        var errors = ModulesRequestValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsIndexOutOfRange()
    {
        var request = new ModulesRequest(
        [
            new ModuleMapping(ProtocolConstants.MaxModules, new ModuleSourceBinding(null, "transition"), new ModuleSourceBinding(null, "opacity")),
        ]);

        var errors = ModulesRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("must be between"));
    }

    [Fact]
    public void Validate_RejectsDuplicateIndex()
    {
        var request = new ModulesRequest(
        [
            new ModuleMapping(0, new ModuleSourceBinding(null, "transition"), new ModuleSourceBinding(null, "opacity")),
            new ModuleMapping(0, new ModuleSourceBinding(null, "transition"), new ModuleSourceBinding(null, "opacity")),
        ]);

        var errors = ModulesRequestValidator.Validate(request);

        Assert.Contains(errors, e => e.Contains("more than once"));
    }

    [Fact]
    public void Validate_RejectsNullRequest()
    {
        var errors = ModulesRequestValidator.Validate(null);

        Assert.NotEmpty(errors);
    }
}
