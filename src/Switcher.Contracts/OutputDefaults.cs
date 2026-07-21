namespace Switcher.Contracts;

/// <summary>
/// Default output sink assignments (docs/specs/00-system-overview.md §4.2, multiview-output-revision §2.5).
/// Moved out of the deleted GStreamer-era OutputRouter during the libobs migration so the runtime config
/// and the engine share one source of truth. PGM1→VCAM1, PGM2→VCAM2; HDMI stays unassigned until a
/// display id is chosen. NDI sinks default to PGM1→NDI1 / PGM2→NDI2 with the standard sender names.
/// </summary>
public static class OutputDefaults
{
    public const string Ndi1SenderName = "SWITCHER PGM1";

    public const string Ndi2SenderName = "SWITCHER PGM2";

    public static IReadOnlyList<OutputAssignment> Default { get; } =
    [
        new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, DisplayId: null, HideCursor: null, Fullscreen: null),
        new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm2, DisplayId: null, HideCursor: null, Fullscreen: null),
    ];

    public static IReadOnlyList<OutputAssignment> DefaultWithNdi { get; } =
    [
        new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, DisplayId: null, HideCursor: null, Fullscreen: null),
        new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm2, DisplayId: null, HideCursor: null, Fullscreen: null),
        new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, DisplayId: null, HideCursor: null, Fullscreen: null, NdiName: Ndi1SenderName),
        new OutputAssignment(OutputSink.Ndi2, OutputSource.Pgm2, DisplayId: null, HideCursor: null, Fullscreen: null, NdiName: Ndi2SenderName),
    ];
}
