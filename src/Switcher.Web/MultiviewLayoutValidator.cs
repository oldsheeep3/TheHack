using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Validates <see cref="MultiviewLayout"/> payloads received on <c>PUT /api/v1/multiview</c>
/// (docs/specs/00-system-overview.md §4.2, docs/specs/multiview-output-revision.md §4.2).
/// <para>
/// Backward compatible. A payload carries exactly one representation:
/// <list type="bullet">
/// <item><c>cells</c> (legacy): exactly 16 entries, each one of
/// <c>PGM1|PGM2|PVW1|PVW2|SRC:&lt;id&gt;|EMPTY</c>.</item>
/// <item><c>regions</c> (new): a rectangular-merge layout whose regions must all be rectangles inside
/// the grid, cover it with no overlap and no gap, and use only the tokens above.</item>
/// </list>
/// Supplying both representations, or neither, is rejected. Normalization is delegated to
/// <see cref="MultiviewLayoutNormalizer.ToRegions"/> so App/Web/Media share one implementation.
/// </para>
/// </summary>
public static class MultiviewLayoutValidator
{
    private const int CellCount = 16;
    private const int DefaultGridSize = 4;
    private static readonly string[] FixedTokens = ["PGM1", "PGM2", "PVW1", "PVW2", "EMPTY"];

    public static IReadOnlyList<string> Validate(MultiviewLayout? layout)
    {
        if (layout is null)
        {
            return ["Request body is required."];
        }

        var hasCells = layout.Cells is { Count: > 0 };
        var hasRegions = layout.Regions is not null;

        if (hasCells && hasRegions)
        {
            return ["Specify either cells or regions, not both."];
        }

        if (!hasCells && !hasRegions)
        {
            return ["Either cells or regions is required."];
        }

        return hasRegions ? ValidateRegions(layout) : ValidateCells(layout.Cells!);
    }

    private static IReadOnlyList<string> ValidateCells(IReadOnlyList<string> cells)
    {
        var errors = new List<string>();

        if (cells.Count != CellCount)
        {
            errors.Add($"cells must contain exactly {CellCount} entries.");
            return errors;
        }

        for (var i = 0; i < cells.Count; i++)
        {
            if (!IsValidToken(cells[i]))
            {
                errors.Add($"cells[{i}] must be one of PGM1|PGM2|PVW1|PVW2|SRC:<id>|EMPTY (got '{cells[i]}').");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateRegions(MultiviewLayout layout)
    {
        var errors = new List<string>();

        var grid = layout.Grid ?? new MultiviewGrid(DefaultGridSize, DefaultGridSize);
        if (grid.Rows <= 0 || grid.Cols <= 0)
        {
            errors.Add($"grid must have positive rows and cols (got {grid.Rows}x{grid.Cols}).");
            return errors;
        }

        var regions = MultiviewLayoutNormalizer.ToRegions(layout);
        if (regions.Count == 0)
        {
            errors.Add("regions must contain at least one region.");
            return errors;
        }

        // Occupancy grid: each cell must be covered by exactly one region (no overlap, no gap).
        var occupied = new bool[grid.Rows, grid.Cols];

        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];

            if (!IsValidToken(region.Content))
            {
                errors.Add($"regions[{i}].content must be one of PGM1|PGM2|PVW1|PVW2|SRC:<id>|EMPTY (got '{region.Content}').");
            }

            if (region.RowSpan <= 0 || region.ColSpan <= 0
                || region.Row < 0 || region.Col < 0
                || region.Row + region.RowSpan > grid.Rows
                || region.Col + region.ColSpan > grid.Cols)
            {
                errors.Add(
                    $"regions[{i}] must be a rectangle inside the {grid.Rows}x{grid.Cols} grid "
                    + $"(got row={region.Row}, col={region.Col}, row_span={region.RowSpan}, col_span={region.ColSpan}).");
                continue; // Out-of-bounds regions cannot be marked without indexing errors.
            }

            for (var r = region.Row; r < region.Row + region.RowSpan; r++)
            {
                for (var c = region.Col; c < region.Col + region.ColSpan; c++)
                {
                    if (occupied[r, c])
                    {
                        errors.Add($"regions[{i}] overlaps another region at cell (row={r}, col={c}).");
                    }

                    occupied[r, c] = true;
                }
            }
        }

        for (var r = 0; r < grid.Rows; r++)
        {
            for (var c = 0; c < grid.Cols; c++)
            {
                if (!occupied[r, c])
                {
                    errors.Add($"regions leave cell (row={r}, col={c}) uncovered; the grid must be fully covered.");
                }
            }
        }

        return errors;
    }

    private static bool IsValidToken(string? cell) =>
        cell is not null
        && (Array.IndexOf(FixedTokens, cell) >= 0
            || (cell.StartsWith("SRC:", StringComparison.Ordinal) && cell.Length > "SRC:".Length));
}
