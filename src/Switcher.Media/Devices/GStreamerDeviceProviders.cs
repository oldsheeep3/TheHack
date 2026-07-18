using Microsoft.Extensions.Logging;
using Switcher.Contracts;

namespace Switcher.Media.Devices;

/// <summary>
/// Shared GStreamer <c>DeviceMonitor</c> plumbing for the OS-specific device providers. Initialization
/// and enumeration are fully guarded: a missing native GStreamer runtime, an absent NDI plugin, or a
/// probe failure yields an empty list instead of throwing, so device discovery can never take down the
/// caller (docs/specs/multiview-output-revision.md §2.6). The real Windows dependency (Media
/// Foundation / DirectShow via GStreamer) stays inside this file.
/// </summary>
internal static class GstDeviceMonitor
{
    private static readonly object InitLock = new();
    private static bool s_initialized;

    /// <summary>Enumerates devices matching <paramref name="classFilter"/> (e.g. "Video/Source",
    /// "Source/Network"), projecting each to a <see cref="DeviceInfo"/>. Returns empty on any failure.</summary>
    public static IReadOnlyList<DeviceInfo> Enumerate(string classFilter, Func<Gst.Device, DeviceInfo?> project, ILogger logger)
    {
        if (!TryInitialize(logger))
        {
            return Array.Empty<DeviceInfo>();
        }

        Gst.DeviceMonitor? monitor = null;
        try
        {
            monitor = new Gst.DeviceMonitor();
            monitor.AddFilter(classFilter, null);

            // Start() returns false when no provider is available (e.g. NDI plugin/SDK missing); treat as
            // "no devices" rather than an error.
            if (!monitor.Start())
            {
                return Array.Empty<DeviceInfo>();
            }

            var results = new List<DeviceInfo>();
            foreach (var device in monitor.Devices)
            {
                try
                {
                    if (project(device) is { } info)
                    {
                        results.Add(info);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to project a GStreamer device for filter {Filter}.", classFilter);
                }
                finally
                {
                    device.Dispose();
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GStreamer device enumeration failed for filter {Filter}.", classFilter);
            return Array.Empty<DeviceInfo>();
        }
        finally
        {
            try
            {
                monitor?.Stop();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Ignoring GStreamer device monitor stop failure.");
            }

            monitor?.Dispose();
        }
    }

    /// <summary>Reads the first present field from <paramref name="candidateFields"/> of a device's
    /// properties, used to derive a stable device id across platforms/capture backends.</summary>
    public static string? ReadProperty(Gst.Device device, params string[] candidateFields)
    {
        var properties = device.Properties;
        if (properties is null)
        {
            return null;
        }

        foreach (var field in candidateFields)
        {
            if (properties.HasField(field))
            {
                var value = properties.GetString(field);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    /// <summary>Extracts up to <paramref name="max"/> distinct "WxH@FPS" candidates from a device's
    /// caps. Returns null when no resolution is advertised so callers can omit the field.</summary>
    public static IReadOnlyList<string>? ReadFormats(Gst.Device device, int max = 16)
    {
        var caps = device.Caps;
        if (caps is null || caps.Size == 0)
        {
            return null;
        }

        var formats = new List<string>();
        for (uint i = 0; i < caps.Size && formats.Count < max; i++)
        {
            var structure = caps.GetStructure(i);
            if (structure is null || !structure.GetInt("width", out var width) || !structure.GetInt("height", out var height) ||
                width <= 0 || height <= 0)
            {
                continue;
            }

            var label = structure.GetFraction("framerate", out var numerator, out var denominator) && denominator > 0
                ? $"{width}x{height}@{numerator / denominator}"
                : $"{width}x{height}";

            if (!formats.Contains(label))
            {
                formats.Add(label);
            }
        }

        return formats.Count > 0 ? formats : null;
    }

    private static bool TryInitialize(ILogger logger)
    {
        if (s_initialized)
        {
            return true;
        }

        lock (InitLock)
        {
            if (s_initialized)
            {
                return true;
            }

            try
            {
                Gst.Application.Init();
                s_initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "GStreamer runtime is unavailable; device enumeration will return no devices.");
                return false;
            }
        }
    }
}

/// <summary>
/// Default <see cref="IWebcamDeviceProvider"/> backed by a GStreamer "Video/Source" device monitor
/// (Media Foundation / DirectShow on Windows, V4L2 on Linux). Ids are taken from the device path when
/// available, falling back to the display name (docs/specs/multiview-output-revision.md §2.6).
/// </summary>
internal sealed class GStreamerWebcamDeviceProvider : IWebcamDeviceProvider
{
    private readonly ILogger _logger;

    public GStreamerWebcamDeviceProvider(ILogger logger) => _logger = logger;

    public IReadOnlyList<DeviceInfo> Enumerate() =>
        GstDeviceMonitor.Enumerate("Video/Source", device =>
        {
            var name = device.DisplayName ?? "Camera";
            var id = GstDeviceMonitor.ReadProperty(device, "device.path", "api.v4l2.path", "object.path", "device.api") ?? name;
            return new DeviceInfo(id, name, GstDeviceMonitor.ReadFormats(device));
        }, _logger);
}

/// <summary>
/// Default <see cref="INdiSourceProvider"/> backed by a GStreamer "Source/Network" device monitor,
/// which surfaces NDI senders through the NDI GStreamer plugin. When the plugin/SDK is not installed
/// the monitor yields no devices and an empty list is returned (the download prompt is shown by the
/// Web/App layer, docs/specs/multiview-output-revision.md §2.6).
/// </summary>
internal sealed class GStreamerNdiSourceProvider : INdiSourceProvider
{
    private readonly ILogger _logger;

    public GStreamerNdiSourceProvider(ILogger logger) => _logger = logger;

    public IReadOnlyList<DeviceInfo> Enumerate() =>
        GstDeviceMonitor.Enumerate("Source/Network", device =>
        {
            var name = device.DisplayName;
            return string.IsNullOrWhiteSpace(name) ? null : new DeviceInfo(name, name, Formats: null);
        }, _logger);
}
