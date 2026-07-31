namespace Switcher.Contracts;

/// <summary>
/// Default output sink assignments (docs/specs/00-system-overview.md §4.2, multiview-output-revision §2.5).
/// Moved out of the deleted GStreamer-era OutputRouter during the libobs migration so the runtime config
/// and the engine share one source of truth.
/// <para>
/// A fresh install starts with one webcam and one display — PGM1→VCAM1, PGM2→HDMI1 — which is the
/// smallest table that still gives every program bus somewhere to go (<see cref="OutputRules"/>). The
/// operator adds the rest, up to <see cref="OutputCatalog.MaxOf"/> per kind and
/// <see cref="OutputCatalog.MaxTotal"/> overall. <see cref="DefaultHdmiDisplayId"/> keeps the default
/// table valid without the operator having to pick a display first; nothing is actually presented until
/// they open a projector window.
/// </para>
/// </summary>
public static class OutputDefaults
{
    public const string Ndi1SenderName = "SWITCHER PGM1";

    public const string Ndi2SenderName = "SWITCHER PGM2";

    public const string Ndi3SenderName = "SWITCHER PGM3";

    /// <summary>Display an HDMI sink targets until the operator picks another: the primary monitor.</summary>
    public const int DefaultHdmiDisplayId = 0;

    public static IReadOnlyList<OutputAssignment> Default { get; } =
    [
        new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, DisplayId: null, HideCursor: null, Fullscreen: null),
        new OutputAssignment(OutputSink.Hdmi1, OutputSource.Pgm2, DisplayId: DefaultHdmiDisplayId, HideCursor: true, Fullscreen: true),
    ];

    public static IReadOnlyList<OutputAssignment> DefaultWithNdi { get; } =
    [
        new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, DisplayId: null, HideCursor: null, Fullscreen: null),
        new OutputAssignment(OutputSink.Hdmi1, OutputSource.Pgm2, DisplayId: DefaultHdmiDisplayId, HideCursor: true, Fullscreen: true),
        new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, DisplayId: null, HideCursor: null, Fullscreen: null, NdiName: Ndi1SenderName),
        new OutputAssignment(OutputSink.Ndi2, OutputSource.Pgm2, DisplayId: null, HideCursor: null, Fullscreen: null, NdiName: Ndi2SenderName),
    ];

    /// <summary>Sender name a new NDI sink gets when the operator does not type one.</summary>
    public static string NdiSenderName(OutputSink sink) => sink switch
    {
        OutputSink.Ndi1 => Ndi1SenderName,
        OutputSink.Ndi2 => Ndi2SenderName,
        OutputSink.Ndi3 => Ndi3SenderName,
        _ => Ndi1SenderName,
    };

    /// <summary>The assignment a freshly added <paramref name="sink"/> starts from.</summary>
    public static OutputAssignment NewAssignment(OutputSink sink, OutputSource source) =>
        OutputCatalog.KindOf(sink) switch
        {
            OutputKind.Hdmi => new OutputAssignment(
                sink, source, DisplayId: DefaultHdmiDisplayId, HideCursor: true, Fullscreen: true),
            OutputKind.Ndi => new OutputAssignment(
                sink, source, DisplayId: null, HideCursor: null, Fullscreen: null, NdiName: NdiSenderName(sink)),
            _ => new OutputAssignment(sink, source, DisplayId: null, HideCursor: null, Fullscreen: null),
        };
}
