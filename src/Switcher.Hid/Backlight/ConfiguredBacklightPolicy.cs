using Switcher.Contracts;

namespace Switcher.Hid.Backlight;

/// <summary>
/// Colours the module LEDs from the operator's own <see cref="TallyColors"/>, per bus.
///
/// The RGB values sent to the controller are exactly the ones the operator picked in the app — the
/// firmware holds no palette of its own. That is what keeps the screen and the physical panel from
/// ever disagreeing about what is on air.
/// </summary>
public sealed class ConfiguredBacklightPolicy : IBacklightPolicy
{
    private static readonly BacklightColor Off = new(0, 0, 0);

    private readonly TallyColors _colors;

    public ConfiguredBacklightPolicy(TallyColors colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        _colors = colors;
    }

    public BacklightColor Compute(BacklightSwitchContext context) => context switch
    {
        // Program wins over preview: a source on both buses is on air, and that is the fact the camera
        // operator must not miss.
        { IsProgram: true } => _colors.ProgramFor(context.Bus),
        { IsPreview: true } => _colors.PreviewFor(context.Bus),
        { IsSelectable: true } => _colors.Idle,
        _ => Off,
    };
}
