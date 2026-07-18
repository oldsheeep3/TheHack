using System.Text.Json.Serialization;

namespace Switcher.Contracts;

/// <summary>
/// OBS的に自由追加できるソースの種別（docs/specs/00-system-overview.md §4.2）。
/// </summary>
public enum SourceType
{
    [JsonStringEnumMemberName("NDI")]
    Ndi,

    [JsonStringEnumMemberName("WEBCAM")]
    Webcam,

    [JsonStringEnumMemberName("SRT")]
    Srt,
}

public sealed record NdiConfig(string SourceName);

public sealed record WebcamConfig(string DeviceId, string? Format);

public sealed record SrtConfig(string Url, int LatencyMs);

/// <summary>
/// Request body for POST /api/v1/sources (docs/specs/00-system-overview.md §4.2).
/// </summary>
public sealed record SourceDefinition(
    string Id,
    string Name,
    SourceType Type,
    NdiConfig? Ndi,
    WebcamConfig? Webcam,
    SrtConfig? Srt);
