using System.Text.Json.Serialization;

namespace Switcher.Contracts;

/// <summary>
/// Kind of device to enumerate via <see cref="IDeviceQueryService"/>
/// (docs/specs/multiview-output-revision.md §4.1). SRT is not enumerable.
/// </summary>
public enum DeviceQueryType
{
    [JsonStringEnumMemberName("WEBCAM")]
    Webcam,

    [JsonStringEnumMemberName("NDI")]
    Ndi,
}

/// <summary>
/// A discovered input device (docs/specs/multiview-output-revision.md §4.1).
/// For webcams, <see cref="Formats"/> lists selectable "WxH@FPS" candidates; for NDI it may be null.
/// </summary>
public sealed record DeviceInfo(string Id, string Name, IReadOnlyList<string>? Formats);
