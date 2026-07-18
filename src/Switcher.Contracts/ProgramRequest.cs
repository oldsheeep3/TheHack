using System.Text.Json.Serialization;

namespace Switcher.Contracts;

/// <summary>
/// Which of the two program buses a request targets (docs/specs/00-system-overview.md §4.2).
/// </summary>
public enum ProgramBus
{
    [JsonStringEnumMemberName("PGM1")]
    Pgm1,

    [JsonStringEnumMemberName("PGM2")]
    Pgm2,
}

public sealed record ProgramLayer(string SourceId, PipSettings Pip);

/// <summary>
/// Request body for POST /api/v1/program (docs/specs/00-system-overview.md §4.2).
/// </summary>
public sealed record ProgramRequest(ProgramBus Bus, IReadOnlyList<ProgramLayer> Layers, bool Take);
