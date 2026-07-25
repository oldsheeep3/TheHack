namespace Switcher.App.Multiview;

/// <summary>
/// The multiview grid's dimensions, published on the cell <c>ItemsControl</c>'s <c>Tag</c> so the
/// <see cref="MultiviewGridPanel"/> inside its <c>ItemsPanelTemplate</c> can bind to them. A panel
/// declared in an items-panel template has no direct route to the window's own state, and reassigning
/// <c>Tag</c> is what makes the bindings re-evaluate when the operator picks a different grid.
/// </summary>
public sealed record MultiviewGridSize(int Rows, int Cols);
