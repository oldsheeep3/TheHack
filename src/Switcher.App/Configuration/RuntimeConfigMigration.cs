using System.IO;
using Microsoft.Extensions.Logging;

namespace Switcher.App.Configuration;

/// <summary>
/// Moves a <c>runtime-config.json</c> written by an older build — which kept it next to the executable —
/// into <see cref="AppPaths.DataDirectory"/>, so upgrading does not silently reset an operator's sources,
/// multiview layout, output routing and audio routes.
///
/// Kept out of <see cref="RuntimeConfigStore"/> on purpose: the store is constructed with an explicit
/// directory in tests, and migration must only ever run for the real application paths.
/// </summary>
public static class RuntimeConfigMigration
{
    /// <summary>Copies the legacy file into <paramref name="dataDirectory"/> when there is nothing there
    /// yet. The original is left in place: an install directory may be read-only, and a stale copy next
    /// to the executable is harmless once the new location wins.</summary>
    public static void MigrateLegacyFile(string legacyDirectory, string dataDirectory, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            if (string.Equals(
                    Path.GetFullPath(legacyDirectory), Path.GetFullPath(dataDirectory), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var destination = Path.Combine(dataDirectory, RuntimeConfigStore.FileName);
            var legacy = Path.Combine(legacyDirectory, RuntimeConfigStore.FileName);
            if (File.Exists(destination) || !File.Exists(legacy))
            {
                return;
            }

            Directory.CreateDirectory(dataDirectory);
            File.Copy(legacy, destination);
            logger.LogInformation(
                "Migrated the runtime configuration from {Legacy} to {Destination}.", legacy, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            logger.LogWarning(ex, "Could not migrate the previous runtime configuration; starting from defaults.");
        }
    }
}
