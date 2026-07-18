using Switcher.Contracts;

namespace Switcher.Media.Multiview;

/// <summary>One resolved multiview tile: the pixel rectangle a region occupies on the multiview canvas
/// plus its <c>content</c> selector ("PGM1"|"PGM2"|"PVW1"|"PVW2"|"SRC:&lt;id&gt;"|"EMPTY").</summary>
internal sealed record MultiviewTile(string Content, int X, int Y, int Width, int Height);

/// <summary>
/// Turns a <see cref="MultiviewLayout"/> (either the legacy 16-entry <c>cells</c> form or the newer
/// <c>grid</c>+<c>regions</c> form) into pixel-space tiles for a given canvas
/// (docs/specs/multiview-output-revision.md §2.3). Combined cells (row/col span &gt; 1) expand into a
/// single enlarged rectangle covering their grid area, generalizing the previous 16 equal-cell layout
/// to region-driven placement. Pure/deterministic so it can be unit tested without a GPU, and reuses
/// <see cref="MultiviewLayoutNormalizer.ToRegions"/> instead of re-implementing normalization.
/// </summary>
internal static class MultiviewLayoutCalculator
{
    /// <summary>
    /// Computes the pixel rectangle of every region in <paramref name="layout"/> across a
    /// <paramref name="canvasWidth"/> x <paramref name="canvasHeight"/> canvas. Cell edges are derived
    /// from the region's grid coordinates (right/bottom minus left/top) so adjacent tiles share exact
    /// boundaries with no rounding gaps, and the union of all tiles covers the whole canvas.
    /// </summary>
    public static IReadOnlyList<MultiviewTile> Build(MultiviewLayout layout, int canvasWidth, int canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var regions = MultiviewLayoutNormalizer.ToRegions(layout);
        if (regions.Count == 0)
        {
            return Array.Empty<MultiviewTile>();
        }

        var (rows, cols) = ResolveGrid(layout, regions);
        if (rows <= 0 || cols <= 0)
        {
            return Array.Empty<MultiviewTile>();
        }

        var tiles = new MultiviewTile[regions.Count];
        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            var left = region.Col * canvasWidth / cols;
            var top = region.Row * canvasHeight / rows;
            var right = (region.Col + region.ColSpan) * canvasWidth / cols;
            var bottom = (region.Row + region.RowSpan) * canvasHeight / rows;

            tiles[i] = new MultiviewTile(region.Content, left, top, right - left, bottom - top);
        }

        return tiles;
    }

    // Grid dimensions come from the explicit grid when present (region form); otherwise they are the
    // tightest grid that bounds every region, which for the legacy cells form (1x1 regions laid across
    // MultiviewLayoutNormalizer.LegacyColumns columns) reproduces the original 4-column layout.
    private static (int Rows, int Cols) ResolveGrid(MultiviewLayout layout, IReadOnlyList<MultiviewRegion> regions)
    {
        if (layout.Grid is { } grid)
        {
            return (grid.Rows, grid.Cols);
        }

        var rows = 0;
        var cols = 0;
        foreach (var region in regions)
        {
            rows = Math.Max(rows, region.Row + region.RowSpan);
            cols = Math.Max(cols, region.Col + region.ColSpan);
        }

        return (rows, cols);
    }
}
