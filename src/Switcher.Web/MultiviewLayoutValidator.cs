using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Validates <see cref="MultiviewLayout"/> payloads received on <c>PUT /api/v1/multiview</c>
/// (docs/specs/00-system-overview.md §4.2): exactly 16 cells, each one of
/// <c>PGM1|PGM2|PVW1|PVW2|SRC:&lt;id&gt;|EMPTY</c>.
/// </summary>
public static class MultiviewLayoutValidator
{
    private const int CellCount = 16;
    private static readonly string[] FixedTokens = ["PGM1", "PGM2", "PVW1", "PVW2", "EMPTY"];

    public static IReadOnlyList<string> Validate(MultiviewLayout? layout)
    {
        if (layout is null)
        {
            return ["Request body is required."];
        }

        var errors = new List<string>();

        if (layout.Cells is null || layout.Cells.Count != CellCount)
        {
            errors.Add($"cells must contain exactly {CellCount} entries.");
            return errors;
        }

        for (var i = 0; i < layout.Cells.Count; i++)
        {
            if (!IsValidToken(layout.Cells[i]))
            {
                errors.Add($"cells[{i}] must be one of PGM1|PGM2|PVW1|PVW2|SRC:<id>|EMPTY (got '{layout.Cells[i]}').");
            }
        }

        return errors;
    }

    private static bool IsValidToken(string? cell) =>
        cell is not null
        && (Array.IndexOf(FixedTokens, cell) >= 0
            || (cell.StartsWith("SRC:", StringComparison.Ordinal) && cell.Length > "SRC:".Length));
}
