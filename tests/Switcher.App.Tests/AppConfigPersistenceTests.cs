using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Switcher.App.Configuration;

namespace Switcher.App.Tests;

public sealed class AppConfigPersistenceTests
{
    [Fact]
    public void OperatorDisplayIndex_DefaultsToZero() =>
        Assert.Equal(0, AppConfig.CreateDefault().OperatorDisplayIndex);

    [Fact]
    public void Save_ThenLoad_RoundTripsOperatorDisplayIndex()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"switcher-appconfig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var config = AppConfig.CreateDefault() with { OperatorDisplayIndex = 2 };
            AppConfigLoader.Save(dir, config, NullLogger.Instance);

            var loaded = AppConfigLoader.Load(dir, NullLogger.Instance);

            Assert.Equal(2, loaded.OperatorDisplayIndex);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
