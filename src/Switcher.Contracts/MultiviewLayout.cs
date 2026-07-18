namespace Switcher.Contracts;

/// <summary>
/// Request body for PUT /api/v1/multiview (docs/specs/00-system-overview.md §4.2).
/// Cells holds 16 entries, each one of "PGM1"|"PGM2"|"PVW1"|"PVW2"|"SRC:&lt;id&gt;"|"EMPTY";
/// validation is left to the Web layer.
/// </summary>
public sealed record MultiviewLayout(IReadOnlyList<string> Cells);
