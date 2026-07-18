namespace Switcher.Hid.Input;

/// <summary>A rising (pressed) or falling (released) edge on one module's switch, derived from a
/// state diff between two consecutive input reports (docs/specs/pc-switcher-app.md §2.6).</summary>
public sealed record SwitchEdgeEvent(int ModuleIndex, SwitchId Switch, bool IsRising);

/// <summary>A VR value that changed by at least the configured deadband since the last reported
/// value for that channel (docs/specs/pc-switcher-app.md §2.6).</summary>
public sealed record VrChangedEvent(int ModuleIndex, VrChannel Channel, byte Value);
