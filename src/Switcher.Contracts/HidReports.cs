namespace Switcher.Contracts;

/// <summary>
/// Pure byte-layout types for the USB-HID reports exchanged with the Pico 2W controller
/// (docs/specs/00-system-overview.md §4.1). Encoding/decoding to/from the wire is implemented
/// by Switcher.Hid; this project only defines the shapes.
/// </summary>
public sealed record ModuleSwitchState(bool Pgm1Src1, bool Pgm1Src2, bool Pgm2Src1, bool Pgm2Src2);

public sealed record ModuleVrState(byte VrSrc1, byte VrSrc2);

/// <summary>
/// Input report (Report ID <see cref="ProtocolConstants.HidInputReportId"/>, Pico → PC).
/// Length is 1 + MAX_MODULES + 2*MAX_MODULES + 1.
/// </summary>
public sealed record HidInputReport(
    byte ModulePresent,
    IReadOnlyList<ModuleSwitchState> Switches,
    IReadOnlyList<ModuleVrState> Vrs,
    byte Seq);

public sealed record BacklightColor(byte R, byte G, byte B);

/// <summary>
/// Output report (Report ID <see cref="ProtocolConstants.HidOutputReportId"/>, PC → Pico).
/// Colors holds 4 entries (12 bytes) for one module's backlights.
/// </summary>
public sealed record HidOutputReport(byte ModuleIndex, IReadOnlyList<BacklightColor> Colors);
