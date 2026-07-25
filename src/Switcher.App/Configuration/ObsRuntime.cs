using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Switcher.Contracts;

namespace Switcher.App.Configuration;

/// <summary>
/// Locates an installed OBS Studio runtime and turns it into the path knobs libobs needs at startup.
///
/// The native <c>switcher-engine</c> links against <c>obs.dll</c> and needs libobs' <b>core data path</b>
/// (<c>&lt;obs&gt;/data/libobs/</c>) registered before <c>obs_reset_video</c>, or graphics init fails with
/// "Native switcher-engine failed to start (libobs unavailable?)". Rather than rely on the developer-only
/// <c>launchSettings.json</c> <c>PATH</c>/env plumbing (which is absent when the built <c>.exe</c> is run
/// directly), the App discovers the OBS install itself and feeds the paths through <see cref="EngineOptions"/>.
///
/// It also prepends the OBS <c>bin\64bit</c> directory to the process DLL search path so <c>obs.dll</c> and
/// the <c>libobs-d3d11</c> graphics module resolve regardless of how the app was launched.
/// </summary>
public static class ObsRuntime
{
    /// <summary>
    /// Builds the OBS-derived <see cref="EngineOptions"/> for engine startup. Detection order:
    /// <list type="number">
    /// <item>an explicit <paramref name="configuredInstallPath"/> (from <c>appsettings.json</c>),</item>
    /// <item>the <c>SWITCHER_OBS_DATA_PATH</c> environment variable's parent OBS root (dev/CI override),</item>
    /// <item>well-known install locations and the OBS Studio uninstall registry key.</item>
    /// </list>
    /// When an install is found, its <c>bin\64bit</c> is added to the DLL search path and the returned
    /// options carry the data / plugin paths. When nothing is found the base <paramref name="baseline"/>
    /// options are returned unchanged (with a warning) — the native layer then falls back to any
    /// <c>SWITCHER_OBS_*</c> environment configuration, matching the previous behaviour.
    /// </summary>
    public static EngineOptions Configure(EngineOptions baseline, string? configuredInstallPath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(logger);

        var root = Locate(configuredInstallPath, logger);
        if (root is null)
        {
            logger.LogWarning(
                "Could not locate an OBS Studio install. Falling back to SWITCHER_OBS_* environment " +
                "configuration for the native engine. Set \"ObsInstallPath\" in appsettings.json (pointing at " +
                "the OBS Studio folder) if the engine fails to start.");
            return baseline;
        }

        var binDir = Path.Combine(root, "bin", "64bit");
        var dataPath = Path.Combine(root, "data", "libobs");
        var moduleBin = Path.Combine(root, "obs-plugins", "64bit");
        var moduleData = Path.Combine(root, "data", "obs-plugins");

        // Make obs.dll + the graphics module resolvable even for a direct .exe launch (no launchSettings PATH).
        AddDllSearchDirectory(binDir, logger);

        LogRuntimeVersion(binDir, logger);
        logger.LogInformation("Using OBS runtime at {Root} for the native switcher-engine.", root);
        return baseline with
        {
            DataPath = dataPath,
            ModuleBinPath = Directory.Exists(moduleBin) ? moduleBin : baseline.ModuleBinPath,
            ModuleDataPath = Directory.Exists(moduleData) ? moduleData : baseline.ModuleDataPath,
        };
    }

    /// <summary>Directory name of a runtime shipped alongside the executable.</summary>
    private const string BundledRuntimeDirectory = "obs-runtime";

    /// <summary>
    /// libobs versions this build is known to work against. The engine only uses long-stable libobs
    /// APIs, so a newer patch or minor release is accepted with a note rather than refused — but the
    /// version is always logged, because "it started but nothing renders" is otherwise indistinguishable
    /// from a genuine ABI mismatch.
    /// </summary>
    private static readonly Version MinimumTested = new(30, 0);

    private static readonly Version MaximumTested = new(32, 99);

    /// <summary>Resolves the OBS runtime root, or null when none can be validated.</summary>
    private static string? Locate(string? configuredInstallPath, ILogger logger)
    {
        // A runtime shipped with the app wins over anything installed on the machine: it is the version
        // this build was tested against, and it makes the app self-contained (no OBS install required).
        var bundled = Path.Combine(AppContext.BaseDirectory, BundledRuntimeDirectory);
        if (IsObsRoot(bundled))
        {
            logger.LogInformation("Using the bundled OBS runtime at {Root}.", bundled);
            return Path.GetFullPath(bundled);
        }

        if (!string.IsNullOrWhiteSpace(configuredInstallPath))
        {
            if (IsObsRoot(configuredInstallPath))
            {
                return Path.GetFullPath(configuredInstallPath);
            }

            logger.LogWarning(
                "Configured ObsInstallPath \"{Path}\" does not look like an OBS install (missing " +
                "bin\\64bit\\obs.dll or data\\libobs). Ignoring it and probing default locations.",
                configuredInstallPath);
        }

        // Respect an explicit data-path env override by walking up to its OBS root (<obs>\data\libobs).
        var envData = Environment.GetEnvironmentVariable("SWITCHER_OBS_DATA_PATH");
        if (!string.IsNullOrWhiteSpace(envData))
        {
            var candidate = Directory.GetParent(envData)?.Parent?.FullName;
            if (candidate is not null && IsObsRoot(candidate))
            {
                return candidate;
            }
        }

        foreach (var candidate in CandidateRoots())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && IsObsRoot(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateRoots()
    {
        // Uninstall registry key written by the OBS Studio installer (per-machine, both bitness views).
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            var fromRegistry = ReadRegistryInstallLocation(view);
            if (fromRegistry is not null)
            {
                yield return fromRegistry;
            }
        }

        foreach (var env in new[] { "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432" })
        {
            var programFiles = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "obs-studio");
            }
        }
    }

    private static string? ReadRegistryInstallLocation(RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = baseKey.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OBS Studio");
            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsObsRoot(string root)
    {
        try
        {
            return File.Exists(Path.Combine(root, "bin", "64bit", "obs.dll"))
                && Directory.Exists(Path.Combine(root, "data", "libobs"));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reports the libobs version that will actually be loaded, and warns when it falls outside the
    /// tested range. Read from <c>obs.dll</c>'s file version rather than from libobs itself so the
    /// warning appears <em>before</em> the engine tries to start — a mismatch otherwise surfaces as a
    /// bare "failed to start" with nothing pointing at the cause.
    /// </summary>
    private static void LogRuntimeVersion(string binDir, ILogger logger)
    {
        try
        {
            var obsDll = Path.Combine(binDir, "obs.dll");
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(obsDll);
            var version = new Version(info.FileMajorPart, info.FileMinorPart);

            if (version < MinimumTested || version > MaximumTested)
            {
                logger.LogWarning(
                    "libobs {Version} is outside the tested range ({Min}–{Max}). The engine only uses "
                    + "long-stable libobs APIs so it will probably work, but if sources or outputs "
                    + "misbehave, this is the first thing to check.",
                    info.FileVersion, MinimumTested, MaximumTested);
            }
            else
            {
                logger.LogInformation("libobs {Version} detected.", info.FileVersion);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning(ex, "Could not read the libobs version from {BinDir}.", binDir);
        }
    }

    private static void AddDllSearchDirectory(string binDir, ILogger logger)
    {
        if (!Directory.Exists(binDir))
        {
            return;
        }

        try
        {
            // Both are needed: AddDllDirectory covers the OS loader when the switcher-engine.dll import of
            // obs.dll is resolved; prepending PATH covers any legacy LoadLibrary the native layer may do.
            _ = AddDllDirectory(binDir);
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            if (!path.Split(Path.PathSeparator).Contains(binDir, StringComparer.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable("PATH", binDir + Path.PathSeparator + path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to add OBS bin directory {BinDir} to the DLL search path.", binDir);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr AddDllDirectory(string newDirectory);
}
