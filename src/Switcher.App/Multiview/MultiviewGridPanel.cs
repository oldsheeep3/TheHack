using System.Windows;
using Switcher.App.ViewModels;

// WindowsForms is enabled in this project, so Panel/Size are ambiguous without these.
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace Switcher.App.Multiview;

/// <summary>
/// Lays multiview cells out on an R×C grid of equal cells, honouring each cell's
/// <see cref="MultiviewCellViewModel.Row"/>/<see cref="MultiviewCellViewModel.Col"/> and its merge
/// spans.
///
/// A WPF <c>Grid</c> in an <c>ItemsPanelTemplate</c> can't do this: its row and column definitions are
/// fixed in XAML, so the grid could only ever be 4×4. This panel takes the dimensions as properties
/// instead, which is what lets the operator choose anything from 4×4 up to 6×6.
/// </summary>
public sealed class MultiviewGridPanel : Panel
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(int), typeof(MultiviewGridPanel),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColsProperty = DependencyProperty.Register(
        nameof(Cols), typeof(int), typeof(MultiviewGridPanel),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public int Cols
    {
        get => (int)GetValue(ColsProperty);
        set => SetValue(ColsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (rows, cols) = SafeDimensions();

        // An infinite constraint (inside a ScrollViewer, say) has no cell size to divide up; fall back
        // to measuring children unconstrained so the panel still reports a sane desired size.
        var cellWidth = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width / cols;
        var cellHeight = double.IsInfinity(availableSize.Height) ? double.PositiveInfinity : availableSize.Height / rows;

        foreach (UIElement child in InternalChildren)
        {
            var (_, _, rowSpan, colSpan) = SpanOf(child);
            child.Measure(new Size(cellWidth * colSpan, cellHeight * rowSpan));
        }

        return double.IsInfinity(availableSize.Width) || double.IsInfinity(availableSize.Height)
            ? new Size(0, 0)
            : availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (rows, cols) = SafeDimensions();
        var cellWidth = finalSize.Width / cols;
        var cellHeight = finalSize.Height / rows;

        foreach (UIElement child in InternalChildren)
        {
            var (row, col, rowSpan, colSpan) = SpanOf(child);

            // Snap to the *next* boundary rather than accumulating a fractional width, so cells stay
            // flush with each other instead of leaving hairline gaps at fractional sizes.
            var left = Math.Round(col * cellWidth);
            var top = Math.Round(row * cellHeight);
            var right = Math.Round((col + colSpan) * cellWidth);
            var bottom = Math.Round((row + rowSpan) * cellHeight);

            child.Arrange(new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top)));
        }

        return finalSize;
    }

    private (int Rows, int Cols) SafeDimensions() => (Math.Max(1, Rows), Math.Max(1, Cols));

    /// <summary>Reads a child's grid placement from the cell view model it presents.</summary>
    private static (int Row, int Col, int RowSpan, int ColSpan) SpanOf(UIElement child) =>
        child is FrameworkElement { DataContext: MultiviewCellViewModel vm }
            ? (vm.Row, vm.Col, Math.Max(1, vm.RowSpan), Math.Max(1, vm.ColSpan))
            : (0, 0, 1, 1);
}
