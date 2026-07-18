using Switcher.Contracts;

namespace Switcher.App.Multiview;

/// <summary>
/// UI-independent model of the 4x4 multiview grid's rectangular-merge state (requirement 3,
/// docs/specs/multiview-output-revision.md §2.3/§4.1). Holds the current set of non-overlapping
/// <see cref="MultiviewRegion"/>s that tile the whole grid, and enforces the "only a filled rectangle
/// can be merged" guard. Deliberately free of any WPF dependency so the merge/split/normalize rules are
/// unit-testable headlessly; the <c>MultiviewControl</c> drag UI drives this model and renders its
/// <see cref="Regions"/>.
/// </summary>
public sealed class MultiviewRegionModel
{
    public const int Rows = 4;
    public const int Cols = 4;

    public const string Empty = "EMPTY";

    private readonly List<MultiviewRegion> _regions = [];

    public MultiviewRegionModel() => Reset();

    /// <summary>The current tiling: one region per merged rectangle plus one 1x1 region per unmerged
    /// cell. Ordered top-left to bottom-right by the region's origin.</summary>
    public IReadOnlyList<MultiviewRegion> Regions => _regions;

    /// <summary>Resets to 16 empty 1x1 cells.</summary>
    public void Reset()
    {
        _regions.Clear();
        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Cols; col++)
            {
                _regions.Add(new MultiviewRegion(row, col, 1, 1, Empty));
            }
        }
    }

    /// <summary>The region that owns the base cell at (<paramref name="row"/>, <paramref name="col"/>),
    /// or <c>null</c> if the coordinate is off-grid.</summary>
    public MultiviewRegion? RegionAt(int row, int col) =>
        _regions.FirstOrDefault(r =>
            row >= r.Row && row < r.Row + r.RowSpan &&
            col >= r.Col && col < r.Col + r.ColSpan);

    /// <summary>Attempts to merge the given base cells into a single region. Succeeds only when the
    /// selection is a filled rectangle (requirement 3's rectangular guard) that does not partially cut
    /// through an existing merged region. The merged region takes its content from the selection's
    /// top-left cell. Returns <c>false</c> with <paramref name="error"/> set when the selection is not
    /// mergeable, leaving the model unchanged.</summary>
    public bool TryMerge(IReadOnlyCollection<(int Row, int Col)> cells, out string? error)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var selected = cells.ToHashSet();
        if (selected.Count < 2)
        {
            error = "Select at least two cells to merge.";
            return false;
        }

        if (selected.Any(c => c.Row < 0 || c.Row >= Rows || c.Col < 0 || c.Col >= Cols))
        {
            error = "Selection is out of bounds.";
            return false;
        }

        var minRow = selected.Min(c => c.Row);
        var maxRow = selected.Max(c => c.Row);
        var minCol = selected.Min(c => c.Col);
        var maxCol = selected.Max(c => c.Col);

        // Rectangular guard: every cell inside the bounding box must be selected.
        for (var row = minRow; row <= maxRow; row++)
        {
            for (var col = minCol; col <= maxCol; col++)
            {
                if (!selected.Contains((row, col)))
                {
                    error = "Only a filled rectangle can be merged.";
                    return false;
                }
            }
        }

        // Every region overlapping the bounding box must be fully contained in it, otherwise the merge
        // would split an existing merged region.
        foreach (var region in _regions)
        {
            var overlaps = region.Row <= maxRow && region.Row + region.RowSpan - 1 >= minRow &&
                           region.Col <= maxCol && region.Col + region.ColSpan - 1 >= minCol;
            if (!overlaps)
            {
                continue;
            }

            var contained = region.Row >= minRow && region.Row + region.RowSpan - 1 <= maxRow &&
                            region.Col >= minCol && region.Col + region.ColSpan - 1 <= maxCol;
            if (!contained)
            {
                error = "Selection overlaps an existing merged region only partially.";
                return false;
            }
        }

        var content = RegionAt(minRow, minCol)?.Content ?? Empty;

        _regions.RemoveAll(r =>
            r.Row >= minRow && r.Row + r.RowSpan - 1 <= maxRow &&
            r.Col >= minCol && r.Col + r.ColSpan - 1 <= maxCol);
        _regions.Add(new MultiviewRegion(minRow, minCol, maxRow - minRow + 1, maxCol - minCol + 1, content));
        Normalize();

        error = null;
        return true;
    }

    /// <summary>Splits the merged region owning (<paramref name="row"/>, <paramref name="col"/>) back
    /// into 1x1 cells (the top-left keeps the content, the rest become empty). No-op for a 1x1 region or
    /// an off-grid coordinate.</summary>
    public void Split(int row, int col)
    {
        var region = RegionAt(row, col);
        if (region is null || (region.RowSpan == 1 && region.ColSpan == 1))
        {
            return;
        }

        _regions.Remove(region);
        for (var r = region.Row; r < region.Row + region.RowSpan; r++)
        {
            for (var c = region.Col; c < region.Col + region.ColSpan; c++)
            {
                var content = r == region.Row && c == region.Col ? region.Content : Empty;
                _regions.Add(new MultiviewRegion(r, c, 1, 1, content));
            }
        }

        Normalize();
    }

    /// <summary>Sets the content token of the region owning (<paramref name="row"/>,
    /// <paramref name="col"/>). No-op for an off-grid coordinate.</summary>
    public void SetContent(int row, int col, string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var region = RegionAt(row, col);
        if (region is null)
        {
            return;
        }

        var index = _regions.IndexOf(region);
        _regions[index] = region with { Content = content };
    }

    /// <summary>Serializes the current tiling to a <see cref="MultiviewLayout"/> carrying both the legacy
    /// 16-cell array (for back-compat consumers) and the new grid/regions form (the canonical one).</summary>
    public MultiviewLayout ToLayout()
    {
        var cells = new string[Rows * Cols];
        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Cols; col++)
            {
                cells[(row * Cols) + col] = RegionAt(row, col)?.Content ?? Empty;
            }
        }

        return new MultiviewLayout(cells, new MultiviewGrid(Rows, Cols), _regions.ToList());
    }

    /// <summary>Loads a layout (legacy <c>cells</c> or new <c>regions</c> form, normalized via
    /// <see cref="MultiviewLayoutNormalizer"/>) into the model, clamping to the 4x4 grid and filling any
    /// uncovered cells with empty 1x1 regions.</summary>
    public void Load(MultiviewLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var incoming = MultiviewLayoutNormalizer.ToRegions(layout);
        var occupied = new bool[Rows, Cols];
        _regions.Clear();

        foreach (var region in incoming)
        {
            if (region.Row < 0 || region.Col < 0 || region.Row >= Rows || region.Col >= Cols)
            {
                continue;
            }

            var rowSpan = Math.Min(region.RowSpan, Rows - region.Row);
            var colSpan = Math.Min(region.ColSpan, Cols - region.Col);
            if (rowSpan < 1 || colSpan < 1)
            {
                continue;
            }

            var free = true;
            for (var r = region.Row; r < region.Row + rowSpan && free; r++)
            {
                for (var c = region.Col; c < region.Col + colSpan; c++)
                {
                    if (occupied[r, c])
                    {
                        free = false;
                        break;
                    }
                }
            }

            if (!free)
            {
                continue;
            }

            for (var r = region.Row; r < region.Row + rowSpan; r++)
            {
                for (var c = region.Col; c < region.Col + colSpan; c++)
                {
                    occupied[r, c] = true;
                }
            }

            _regions.Add(new MultiviewRegion(region.Row, region.Col, rowSpan, colSpan, region.Content ?? Empty));
        }

        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Cols; col++)
            {
                if (!occupied[row, col])
                {
                    _regions.Add(new MultiviewRegion(row, col, 1, 1, Empty));
                }
            }
        }

        Normalize();
    }

    private void Normalize() =>
        _regions.Sort((a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Col.CompareTo(b.Col));
}
