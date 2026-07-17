using Switcher.Contracts;

namespace Switcher.Atem.Tests;

public class ButtonCommandMappingTests
{
    [Fact]
    public void TryGetMapping_WhenControllerAndButtonMatch_ReturnsTheConfiguredCommand()
    {
        var expected = new AtemCommandMapping(AtemAction.Cut, MixEffect: 0);
        var mapping = new ButtonCommandMapping(new Dictionary<(string, int), AtemCommandMapping>
        {
            [("main", 1)] = expected,
        });

        var found = mapping.TryGetMapping(new ButtonEvent("main", 1, 100), out var result);

        Assert.True(found);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryGetMapping_WhenButtonIdDiffers_ReturnsFalse()
    {
        var mapping = new ButtonCommandMapping(new Dictionary<(string, int), AtemCommandMapping>
        {
            [("main", 1)] = new AtemCommandMapping(AtemAction.Cut),
        });

        var found = mapping.TryGetMapping(new ButtonEvent("main", 2, 100), out _);

        Assert.False(found);
    }

    [Fact]
    public void TryGetMapping_WhenControllerIdDiffers_ReturnsFalse()
    {
        var mapping = new ButtonCommandMapping(new Dictionary<(string, int), AtemCommandMapping>
        {
            [("main", 1)] = new AtemCommandMapping(AtemAction.Cut),
        });

        var found = mapping.TryGetMapping(new ButtonEvent("sub", 1, 100), out _);

        Assert.False(found);
    }

    [Fact]
    public void Empty_HasNoMappings()
    {
        var found = ButtonCommandMapping.Empty.TryGetMapping(new ButtonEvent("main", 1, 100), out _);

        Assert.False(found);
    }
}
