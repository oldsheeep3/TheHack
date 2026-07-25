namespace Switcher.App.ViewModels;

/// <summary>
/// What a multiview region is currently carrying, which decides its frame colour: red for on air, green
/// for staged on preview.
///
/// Deliberately in its own WPF-free file (rather than beside <c>MultiviewCellViewModel</c>) so the tally
/// rules in <see cref="Switcher.App.Multiview.MultiviewTally"/> can be compiled and unit-tested without
/// dragging the WindowsDesktop framework into the headless test assembly.
/// </summary>
public enum CellTally
{
    None,
    Program,
    Preview,
}
