namespace Switcher.Hid.Backlight;

/// <summary>The bus state one physical switch's backlight should reflect, resolved from the current
/// PGM/PVW state and module→source mapping (docs/specs/pc-switcher-app.md §2.6).</summary>
/// <param name="IsProgram">The source bound to this switch is the active source on this switch's bus (PGM1 or PGM2).</param>
/// <param name="IsPreview">The source bound to this switch is on the corresponding preview bus (PVW1 or PVW2).</param>
/// <param name="IsSelectable">This switch is bound to a source at all (vs. an empty/unbound slot).</param>
public readonly record struct BacklightSwitchContext(bool IsProgram, bool IsPreview, bool IsSelectable);
