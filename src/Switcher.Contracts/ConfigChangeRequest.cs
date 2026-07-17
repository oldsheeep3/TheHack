namespace Switcher.Contracts;

/// <summary>
/// Request body for POST /api/v1/config (docs/specs/00-system-overview.md §4.2).
/// </summary>
public sealed record ConfigChangeRequest(
    int TargetChannel,
    SourceProtocol SourceType,
    string? SourceUrl,
    PipSettings? PipSettings);
