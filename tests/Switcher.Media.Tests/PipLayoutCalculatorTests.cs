using Switcher.Contracts;
using Switcher.Media.Compositing;

namespace Switcher.Media.Tests;

public class PipLayoutCalculatorTests
{
    [Fact]
    public void BuildScene_OrdersByZOrderAscending()
    {
        var settings = new Dictionary<int, PipSettings>
        {
            [1] = new PipSettings(true, 0, 0, 100, 100, 1.0, 5, null),
            [2] = new PipSettings(true, 0, 0, 100, 100, 1.0, 1, null),
            [3] = new PipSettings(true, 0, 0, 100, 100, 1.0, 3, null),
        };

        var scene = PipLayoutCalculator.BuildScene(settings);

        Assert.Equal([2, 3, 1], scene.Select(l => l.Channel));
    }

    [Fact]
    public void BuildScene_FiltersDisabledChannels()
    {
        var settings = new Dictionary<int, PipSettings>
        {
            [1] = new PipSettings(true, 0, 0, 100, 100, 1.0, 0, null),
            [2] = new PipSettings(false, 0, 0, 100, 100, 1.0, 1, null),
        };

        var scene = PipLayoutCalculator.BuildScene(settings);

        Assert.Equal([1], scene.Select(l => l.Channel));
    }

    [Fact]
    public void BuildScene_PreservesCoordinatesAndCrop()
    {
        var crop = new CropRect(10, 20, 300, 200);
        var settings = new Dictionary<int, PipSettings>
        {
            [1] = new PipSettings(true, 50, 60, 320, 240, 0.8, 0, crop),
        };

        var scene = PipLayoutCalculator.BuildScene(settings);

        var layer = Assert.Single(scene);
        Assert.Equal(50, layer.Settings.X);
        Assert.Equal(60, layer.Settings.Y);
        Assert.Equal(320, layer.Settings.Width);
        Assert.Equal(240, layer.Settings.Height);
        Assert.Equal(crop, layer.Settings.Crop);
    }

    [Theory]
    [InlineData(1.5, 1.0)]
    [InlineData(-0.5, 0.0)]
    [InlineData(0.4, 0.4)]
    public void BuildScene_ClampsOpacityToUnitRange(double input, double expected)
    {
        var settings = new Dictionary<int, PipSettings>
        {
            [1] = new PipSettings(true, 0, 0, 100, 100, input, 0, null),
        };

        var scene = PipLayoutCalculator.BuildScene(settings);

        Assert.Equal(expected, Assert.Single(scene).Settings.Opacity);
    }

    [Fact]
    public void BuildScene_TieBreaksEqualZOrderByChannel()
    {
        var settings = new Dictionary<int, PipSettings>
        {
            [5] = new PipSettings(true, 0, 0, 100, 100, 1.0, 0, null),
            [2] = new PipSettings(true, 0, 0, 100, 100, 1.0, 0, null),
        };

        var scene = PipLayoutCalculator.BuildScene(settings);

        Assert.Equal([2, 5], scene.Select(l => l.Channel));
    }
}
