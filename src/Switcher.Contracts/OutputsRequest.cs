using System.Text.Json.Serialization;

namespace Switcher.Contracts;

/// <summary>
/// Physical output sink (docs/specs/00-system-overview.md §4.2).
/// </summary>
public enum OutputSink
{
    [JsonStringEnumMemberName("VCAM1")]
    Vcam1,

    [JsonStringEnumMemberName("VCAM2")]
    Vcam2,

    [JsonStringEnumMemberName("HDMI")]
    Hdmi,
}

/// <summary>
/// Program bus feeding an output sink (docs/specs/00-system-overview.md §4.2).
/// </summary>
public enum OutputSource
{
    [JsonStringEnumMemberName("PGM1")]
    Pgm1,

    [JsonStringEnumMemberName("PGM2")]
    Pgm2,
}

public sealed record OutputAssignment(
    OutputSink Sink,
    OutputSource Source,
    int? DisplayId,
    bool? HideCursor,
    bool? Fullscreen);

/// <summary>
/// Request body for PUT /api/v1/outputs (docs/specs/00-system-overview.md §4.2).
/// </summary>
public sealed record OutputsRequest(IReadOnlyList<OutputAssignment> Outputs);
