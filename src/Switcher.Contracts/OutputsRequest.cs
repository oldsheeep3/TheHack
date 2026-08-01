using System.Text.Json.Serialization;

namespace Switcher.Contracts;

/// <summary>
/// Kind of output sink an operator can add (docs/specs/00-system-overview.md §4.2).
/// <para>
/// The output table is no longer a fixed set of five sinks: the operator adds sinks of these kinds up
/// to <see cref="OutputCatalog.MaxOf"/> each and <see cref="OutputCatalog.MaxTotal"/> overall.
/// <c>WEBCAM</c> keeps the <c>VCAM<i>n</i></c> wire tokens it has always had, so an existing
/// <c>runtime-config.json</c> and the ATEM/phone bridges keep parsing.
/// </para>
/// </summary>
public enum OutputKind
{
    [JsonStringEnumMemberName("WEBCAM")]
    Webcam,

    [JsonStringEnumMemberName("HDMI")]
    Hdmi,

    [JsonStringEnumMemberName("NDI")]
    Ndi,
}

/// <summary>
/// Physical output sink (docs/specs/00-system-overview.md §4.2): an <see cref="OutputKind"/> plus a
/// 1-based ordinal within that kind. Which of these exist in a running configuration is up to the
/// operator — see <see cref="OutputCatalog"/> for the add/remove rules.
/// </summary>
[JsonConverter(typeof(OutputSinkJsonConverter))]
public enum OutputSink
{
    [JsonStringEnumMemberName("VCAM1")]
    Vcam1,

    [JsonStringEnumMemberName("VCAM2")]
    Vcam2,

    [JsonStringEnumMemberName("VCAM3")]
    Vcam3,

    [JsonStringEnumMemberName("HDMI1")]
    Hdmi1,

    [JsonStringEnumMemberName("HDMI2")]
    Hdmi2,

    [JsonStringEnumMemberName("HDMI3")]
    Hdmi3,

    [JsonStringEnumMemberName("NDI1")]
    Ndi1,

    [JsonStringEnumMemberName("NDI2")]
    Ndi2,

    [JsonStringEnumMemberName("NDI3")]
    Ndi3,
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
    bool? Fullscreen,
    string? NdiName = null);

/// <summary>
/// Request body for PUT /api/v1/outputs (docs/specs/00-system-overview.md §4.2).
/// </summary>
public sealed record OutputsRequest(IReadOnlyList<OutputAssignment> Outputs);

/// <summary>
/// Whether an assigned sink is actually egressing.
/// <para>
/// An assignment can be accepted and still never start — an NDI sink on a machine with no NDI runtime,
/// a virtual camera another application already holds. The routing table alone cannot tell an operator
/// that a program bus is going nowhere, so the engine reports this separately.
/// </para>
/// </summary>
public sealed record OutputStatus(OutputSink Sink, OutputSource Source, bool Running);

/// <summary>Engine reply for the output-status query.</summary>
public sealed record OutputStatusReport(IReadOnlyList<OutputStatus> Outputs);
