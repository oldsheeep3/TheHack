using Switcher.Contracts;

namespace Switcher.Media.Devices;

/// <summary>
/// OS-specific enumeration of local video capture (webcam/UVC) devices, split out from
/// <see cref="DeviceQueryService"/> so the Windows/GStreamer runtime dependency stays behind a seam and
/// unit tests can inject a fake (docs/specs/multiview-output-revision.md §2.6, §3).
/// </summary>
internal interface IWebcamDeviceProvider
{
    /// <summary>Returns the capture devices the OS currently exposes, or an empty list if none/unavailable.</summary>
    IReadOnlyList<DeviceInfo> Enumerate();
}

/// <summary>
/// Discovery of NDI sources on the local network, behind a seam for the same reasons as
/// <see cref="IWebcamDeviceProvider"/>. When the NDI SDK/plugin is not installed this returns an empty
/// list rather than throwing (docs/specs/multiview-output-revision.md §2.6).
/// </summary>
internal interface INdiSourceProvider
{
    /// <summary>Returns the NDI sources currently visible on the network, or an empty list when the NDI
    /// SDK is not detected.</summary>
    IReadOnlyList<DeviceInfo> Enumerate();
}

/// <summary>
/// Enumeration of this PC's LAN IPv4 addresses (one per active NIC, loopback/link-local excluded),
/// used to build SRT listener guidance. Behind a seam so <see cref="DeviceQueryService.GetSrtSetupAsync"/>
/// can be tested deterministically (docs/specs/multiview-output-revision.md §2.7).
/// </summary>
internal interface ILocalAddressProvider
{
    IReadOnlyList<string> GetLanIPv4Addresses();
}
