namespace Switcher.Hid.Backlight;

/// <summary>Which source IDs (<c>ModuleSourceBinding.SourceId</c>, docs/specs/00-system-overview.md
/// §4.2) are currently active on each program bus, for backlight computation.</summary>
public sealed record ProgramBusesState(IReadOnlySet<string> Pgm1SourceIds, IReadOnlySet<string> Pgm2SourceIds);

/// <summary>Which source IDs are currently active on each preview bus, for backlight computation.</summary>
public sealed record PreviewBusesState(IReadOnlySet<string> Pvw1SourceIds, IReadOnlySet<string> Pvw2SourceIds);
