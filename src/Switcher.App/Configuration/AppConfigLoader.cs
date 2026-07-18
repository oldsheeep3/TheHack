using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Switcher.App.Configuration;

/// <summary>
/// Loads <see cref="AppConfig"/> from <c>appsettings.json</c> next to the executable. Falls back to
/// <see cref="AppConfig.CreateDefault"/> (with a warning) when the file is missing or invalid, so a
/// bad/absent config never prevents the app from starting.
/// </summary>
public static class AppConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private const string FileName = "appsettings.json";

    /// <summary>Persists <paramref name="config"/> to <c>appsettings.json</c> next to the executable so
    /// operator-facing bootstrap settings that can change at runtime (e.g.
    /// <see cref="AppConfig.OperatorDisplayIndex"/>, requirement 2) survive a restart. Never throws: a
    /// failed write is logged and ignored, mirroring <see cref="RuntimeConfigStore.Save"/>'s
    /// "an I/O fault must not take the app down" policy.</summary>
    public static void Save(string basePath, AppConfig config, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(config);

        var path = Path.Combine(basePath, FileName);
        try
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            logger.LogWarning(ex, "Failed to persist appsettings.json at {Path}.", path);
        }
    }

    public static AppConfig Load(string basePath, ILogger logger)
    {
        var path = Path.Combine(basePath, FileName);

        try
        {
            if (!File.Exists(path))
            {
                logger.LogInformation("No appsettings.json found at {Path}; using default configuration.", path);
                return AppConfig.CreateDefault();
            }

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (config is null)
            {
                logger.LogWarning("appsettings.json at {Path} deserialized to null; using default configuration.", path);
                return AppConfig.CreateDefault();
            }

            return config;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            logger.LogWarning(ex, "Failed to load appsettings.json at {Path}; using default configuration.", path);
            return AppConfig.CreateDefault();
        }
    }
}
