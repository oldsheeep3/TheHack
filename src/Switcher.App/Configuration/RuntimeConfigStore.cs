using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Switcher.Contracts;

namespace Switcher.App.Configuration;

/// <summary>
/// Loads/persists <see cref="RuntimeConfig"/> to <c>runtime-config.json</c> under
/// <see cref="AppPaths.DataDirectory"/>, mirroring <see cref="AppConfigLoader"/>'s "fall back to
/// defaults, never block startup" behavior. Registered as a DI singleton so
/// <see cref="Orchestration.AppOrchestrator"/> can seed its in-memory state on construction and re-save
/// on every operator change.
/// </summary>
public sealed class RuntimeConfigStore
{
    public const string FileName = "runtime-config.json";

    private readonly string _path;
    private readonly string _backupPath;
    private readonly string _tempPath;
    private readonly ILogger _logger;
    private readonly object _lock = new();

    private RuntimeConfig _current;
    private bool _saveFailureReported;

    public RuntimeConfigStore(string basePath, ILogger logger)
    {
        _path = Path.Combine(basePath, FileName);
        _backupPath = _path + ".bak";
        _tempPath = _path + ".tmp";
        _logger = logger;
        _current = Load();
    }

    /// <summary>The most recently loaded/saved configuration.</summary>
    public RuntimeConfig Current
    {
        get { lock (_lock) { return _current; } }
    }

    /// <summary>
    /// Persists <paramref name="config"/> and makes it the new <see cref="Current"/>.
    ///
    /// <para><b>Never throws.</b> Losing the persisted copy of an already-applied in-memory change must
    /// not take the app down (docs/specs/00-system-overview.md §5: 1つの障害が全体を止めない) — and this
    /// runs on whichever thread caused the change, including the HID read loop, where an escaping
    /// exception would terminate the process.</para>
    ///
    /// <para>The write goes to a temporary file and is then moved over the real one, keeping the previous
    /// version as <c>.bak</c>. Writing in place would mean a crash or power loss mid-write leaves a
    /// truncated file, and the next start would silently come up on defaults — losing every source,
    /// layout, output and audio route the operator had configured.</para>
    /// </summary>
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
                File.WriteAllText(_tempPath, json);

                if (File.Exists(_path))
                {
                    File.Move(_path, _backupPath, overwrite: true);
                }

                File.Move(_tempPath, _path, overwrite: true);
                _saveFailureReported = false;
            }
            catch (Exception ex)
            {
                // Deliberately broad: UnauthorizedAccessException (an install directory or a locked-down
                // profile), SecurityException, JsonException and IOException all reach here, and none of
                // them is worth ending a live show over.
                if (!_saveFailureReported)
                {
                    _saveFailureReported = true;  // once per outage, not once per change
                    _logger.LogError(
                        ex,
                        "Failed to persist runtime config to {Path}. The current settings are still applied, "
                        + "but they will not survive a restart until this is resolved.",
                        _path);
                }
            }
        }
    }

    private RuntimeConfig Load()
    {
        if (TryLoad(_path, out var config))
        {
            return config;
        }

        // The main file was missing or unreadable. A backup exists whenever a save has completed since
        // the file was first written, so preferring it to the defaults keeps the operator's setup.
        if (TryLoad(_backupPath, out var backup))
        {
            _logger.LogWarning(
                "Recovered the runtime configuration from {Path} after the main file could not be read.",
                _backupPath);
            return backup;
        }

        return RuntimeConfig.CreateDefault();
    }

    private bool TryLoad(string path, out RuntimeConfig config)
    {
        config = RuntimeConfig.CreateDefault();

        try
        {
            if (!File.Exists(path))
            {
                if (path == _path)
                {
                    _logger.LogInformation("No {FileName} found at {Path}; using default runtime configuration.", FileName, path);
                }

                return false;
            }

            var loaded = JsonSerializer.Deserialize<RuntimeConfig>(File.ReadAllText(path), ProtocolJsonOptions.Default);
            if (loaded is null)
            {
                _logger.LogWarning("{Path} deserialized to null.", path);
                return false;
            }

            config = loaded;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to load the runtime configuration from {Path}.", path);
            return false;
        }
    }
}
