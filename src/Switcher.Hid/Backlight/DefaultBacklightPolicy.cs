using Switcher.Contracts;

namespace Switcher.Hid.Backlight;

/// <summary>Default color scheme from docs/specs/pc-switcher-app.md §2.6: program = red, preview =
/// green, selectable-but-inactive = dim white, unbound = off.</summary>
public sealed class DefaultBacklightPolicy : IBacklightPolicy
{
    private static readonly BacklightColor Program = new(255, 0, 0);
    private static readonly BacklightColor Preview = new(0, 255, 0);
    private static readonly BacklightColor Selectable = new(24, 24, 24);
    private static readonly BacklightColor Off = new(0, 0, 0);

    public BacklightColor Compute(BacklightSwitchContext context) => context switch
    {
        { IsProgram: true } => Program,
        { IsPreview: true } => Preview,
        { IsSelectable: true } => Selectable,
        _ => Off,
    };
}
