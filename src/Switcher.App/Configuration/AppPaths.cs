using System.IO;

namespace Switcher.App.Configuration;

/// <summary>
/// Where the app keeps the things it writes.
///
/// Everything writable lives under <c>%LOCALAPPDATA%\Switcher</c>, never next to the executable: a
/// distributed build is installed somewhere like <c>C:\Program Files\Switcher</c>, which a standard user
/// cannot write to. Saving the operator's configuration there fails for every non-elevated install, and
/// the failure surfaces on whichever thread happened to trigger the save.
/// </summary>
public static class AppPaths
{
    private const string FolderName = "Switcher";

    /// <summary>Root for configuration and logs. Created on first use.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    /// <summary>Log files, one per day.</summary>
    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");

    /// <summary>
    /// Creates the data and log directories, returning the data directory. Falls back to the executable's
    /// own directory if the profile is unavailable (a service account with no local profile, a redirected
    /// folder that is offline) — a degraded location still beats refusing to start.
    /// </summary>
    public static string EnsureDataDirectory()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogDirectory);
            return DataDirectory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return AppContext.BaseDirectory;
        }
    }
}
