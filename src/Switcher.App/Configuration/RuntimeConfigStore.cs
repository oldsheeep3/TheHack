using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Switcher.Contracts;

namespace Switcher.App.Configuration;

/// <summary>
/// Loads/persists <see cref="RuntimeConfig"/> to <c>runtime-config.json</c> next to the executable,
/// mirroring <see cref="AppConfigLoader"/>'s "fall back to defaults, never block startup" behavior.
/// Registered as a DI singleton so <see cref="Orchestration.AppOrchestrator"/> can seed its in-memory
/// state on construction and re-save on every operator change.
/// </summary>
public sealed class RuntimeConfigStore
{
    private const string FileName = "runtime-config.json";

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly object _lock = new();

    private RuntimeConfig _current;

    public RuntimeConfigStore(string basePath, ILogger logger)
    {
        _path = Path.Combine(basePath, FileName);
        _logger = logger;
        _current = Load();
    }

    /// <summary>The most recently loaded/saved configuration.</summary>
    public RuntimeConfig Current
    {
        get { lock (_lock) { return _current; } }
    }

    /// <summary>Persists <paramref name="config"/> and makes it the new <see cref="Current"/>. Never
    /// throws: a failed write is logged and otherwise ignored, since losing the persisted copy of an
    /// already-applied in-memory change must not take the app down (docs/specs/00-system-overview.md
    /// §5: 1つの障害が全体を止めない).</summary>
    public void Save(RuntimeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_lock)
        {
            _current = config;

            try
            {
                // PicoNetworkConfig carries Wi-Fi credentials - never log `config` itself.
                var json = JsonSerializer.Serialize(config, ProtocolJsonOptions.Default);
                File.WriteAllText(_path, json);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to persist runtime config to {Path}.", _path);
            }
        }
    }

    private RuntimeConfig Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                _logger.LogInformation("No runtime-config.json found at {Path}; using default runtime configuration.", _path);
                return RuntimeConfig.CreateDefault();
            }

            var json = File.ReadAllText(_path);
            var config = JsonSerializer.Deserialize<RuntimeConfig>(json, ProtocolJsonOptions.Default);
            if (config is null)
            {
                _logger.LogWarning("runtime-config.json at {Path} deserialized to null; using default runtime configuration.", _path);
                return RuntimeConfig.CreateDefault();
            }

            return config;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to load runtime-config.json at {Path}; using default runtime configuration.", _path);
            return RuntimeConfig.CreateDefault();
        }
    }
}
