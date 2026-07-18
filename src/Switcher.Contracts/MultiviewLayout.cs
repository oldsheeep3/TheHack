namespace Switcher.Contracts;

/// <summary>
/// Multiview grid dimensions (docs/specs/multiview-output-revision.md §4.1).
/// </summary>
public sealed record MultiviewGrid(int Rows, int Cols);

/// <summary>
/// A single rectangular region inside a multiview layout
/// (docs/specs/multiview-output-revision.md §4.1).
/// <paramref name="Content"/> is one of
/// "PGM1"|"PGM2"|"PVW1"|"PVW2"|"SRC:&lt;id&gt;"|"EMPTY".
/// </summary>
public sealed record MultiviewRegion(int Row, int Col, int RowSpan, int ColSpan, string Content);

/// <summary>
/// Request body for PUT /api/v1/multiview (docs/specs/00-system-overview.md §4.2,
/// docs/specs/multiview-output-revision.md §4.1).
/// <para>
/// Backward compatible: legacy payloads carry <see cref="Cells"/> (16 entries, each one of
/// "PGM1"|"PGM2"|"PVW1"|"PVW2"|"SRC:&lt;id&gt;"|"EMPTY"). New payloads instead carry a
/// <see cref="Grid"/> plus a list of <see cref="Regions"/>. Exactly one representation is populated;
/// use <see cref="MultiviewLayoutNormalizer.ToRegions"/> to obtain the canonical region list.
/// Validation is left to the Web layer.
/// </para>
/// </summary>
public sealed record MultiviewLayout(
    IReadOnlyList<string> Cells,
    MultiviewGrid? Grid = null,
    IReadOnlyList<MultiviewRegion>? Regions = null);

/// <summary>
/// Normalizes a <see cref="MultiviewLayout"/> (either <c>cells</c> or <c>regions</c> form) into a
/// canonical list of <see cref="MultiviewRegion"/>. Centralized here so App/Web/Media share a single
/// implementation (docs/specs/multiview-output-revision.md §4.1).
/// </summary>
public static class MultiviewLayoutNormalizer
{
    /// <summary>Number of columns implied by the legacy <c>cells</c> representation.</summary>
    public const int LegacyColumns = 4;

    /// <summary>
    /// Returns the region list for <paramref name="layout"/>. When <c>regions</c> is present it is
    /// returned as-is; otherwise the <c>cells</c> array is expanded into 1x1 regions laid out across
    /// <see cref="LegacyColumns"/> columns (a full 16-cell layout becomes 16 1x1 regions).
    /// </summary>
    public static IReadOnlyList<MultiviewRegion> ToRegions(MultiviewLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (layout.Regions is not null)
        {
            return layout.Regions;
        }

        var cells = layout.Cells;
        if (cells is null || cells.Count == 0)
        {
            return Array.Empty<MultiviewRegion>();
        }

        var regions = new MultiviewRegion[cells.Count];
        for (var i = 0; i < cells.Count; i++)
        {
            regions[i] = new MultiviewRegion(
                Row: i / LegacyColumns,
                Col: i % LegacyColumns,
                RowSpan: 1,
                ColSpan: 1,
                Content: cells[i]);
        }

        return regions;
    }
}
