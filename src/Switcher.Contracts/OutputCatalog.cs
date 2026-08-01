namespace Switcher.Contracts;

/// <summary>
/// Which output sinks exist, how they group into <see cref="OutputKind"/>s, and how many of each an
/// operator may add (docs/specs/00-system-overview.md §4.2).
/// <para>
/// A fresh configuration starts at <see cref="OutputDefaults.Default"/> — one webcam and one display —
/// and the operator adds sinks from there, at most <see cref="MaxOf"/> of any one kind and at most
/// <see cref="MaxTotal"/> altogether. The ceilings live here rather than in the UI so the Web API
/// validator and the App reject the same tables with the same wording.
/// </para>
/// </summary>
public static class OutputCatalog
{
    /// <summary>
    /// Most webcam sinks an output table may hold — one, because OBS exposes a single virtual-camera
    /// output. A second webcam sink could only be honoured by quietly sending it somewhere that is not a
    /// camera at all (the engine's historical <c>VCAM2</c>→NDI fallback), so the operator is not offered
    /// one rather than being handed an output whose kind lies about where the video goes.
    /// </summary>
    public const int MaxWebcamSinks = 1;

    /// <summary>Most sinks of any other kind an output table may hold.</summary>
    public const int MaxSinksPerKind = 3;

    /// <summary>Most sinks an output table may hold in total, across all kinds.</summary>
    public const int MaxTotal = 6;

    /// <summary>Most sinks of <paramref name="kind"/> an output table may hold.</summary>
    public static int MaxOf(OutputKind kind) =>
        kind == OutputKind.Webcam ? MaxWebcamSinks : MaxSinksPerKind;

    /// <summary>
    /// Every sink of <paramref name="kind"/> that has a wire token, in ordinal order — which is not the
    /// same as every sink an operator may add. <c>VCAM2</c>/<c>VCAM3</c> stay listed because builds that
    /// predate <see cref="MaxWebcamSinks"/> defaulted to <c>PGM2→VCAM2</c> and their configs must still
    /// deserialize; <see cref="MaxOf"/> is what decides how many may be in a table.
    /// </summary>
    public static IReadOnlyList<OutputSink> Sinks(OutputKind kind) => kind switch
    {
        OutputKind.Webcam => [OutputSink.Vcam1, OutputSink.Vcam2, OutputSink.Vcam3],
        OutputKind.Hdmi => [OutputSink.Hdmi1, OutputSink.Hdmi2, OutputSink.Hdmi3],
        OutputKind.Ndi => [OutputSink.Ndi1, OutputSink.Ndi2, OutputSink.Ndi3],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The kind <paramref name="sink"/> belongs to.</summary>
    public static OutputKind KindOf(OutputSink sink) => sink switch
    {
        OutputSink.Vcam1 or OutputSink.Vcam2 or OutputSink.Vcam3 => OutputKind.Webcam,
        OutputSink.Hdmi1 or OutputSink.Hdmi2 or OutputSink.Hdmi3 => OutputKind.Hdmi,
        OutputSink.Ndi1 or OutputSink.Ndi2 or OutputSink.Ndi3 => OutputKind.Ndi,
        _ => throw new ArgumentOutOfRangeException(nameof(sink)),
    };

    /// <summary>1-based position of <paramref name="sink"/> within its kind.</summary>
    public static int OrdinalOf(OutputSink sink) => Sinks(KindOf(sink)).ToList().IndexOf(sink) + 1;

    /// <summary>Wire token for <paramref name="sink"/> (<c>VCAM1</c>, <c>HDMI2</c>, …), the same string
    /// the native engine switches on.</summary>
    public static string TokenOf(OutputSink sink) => KindOf(sink) switch
    {
        OutputKind.Webcam => $"VCAM{OrdinalOf(sink)}",
        OutputKind.Hdmi => $"HDMI{OrdinalOf(sink)}",
        _ => $"NDI{OrdinalOf(sink)}",
    };

    /// <summary>Wire token for <paramref name="kind"/> (<c>WEBCAM</c>/<c>HDMI</c>/<c>NDI</c>).</summary>
    public static string TokenOf(OutputKind kind) => kind switch
    {
        OutputKind.Webcam => "WEBCAM",
        OutputKind.Hdmi => "HDMI",
        OutputKind.Ndi => "NDI",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Operator-facing label for <paramref name="kind"/>.</summary>
    public static string LabelOf(OutputKind kind) => kind switch
    {
        OutputKind.Webcam => "Webcam",
        OutputKind.Hdmi => "HDMI",
        OutputKind.Ndi => "NDI",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Whether a sink of <paramref name="kind"/> can still be added to <paramref name="existing"/>
    /// without breaking <see cref="MaxOf"/> or <see cref="MaxTotal"/>.</summary>
    public static bool CanAdd(IReadOnlyList<OutputSink> existing, OutputKind kind) =>
        NextAvailable(existing, kind) is not null;

    /// <summary>
    /// The lowest-ordinal sink of <paramref name="kind"/> not already in <paramref name="existing"/>, or
    /// <c>null</c> when the kind is full, the table is full, or every ordinal is taken. Adding always
    /// fills the first free slot, so removing <c>NDI1</c> and adding an NDI back reuses <c>NDI1</c>
    /// rather than climbing to an ordinal the operator never asked for.
    /// </summary>
    public static OutputSink? NextAvailable(IReadOnlyList<OutputSink> existing, OutputKind kind)
    {
        ArgumentNullException.ThrowIfNull(existing);

        if (existing.Count >= MaxTotal)
        {
            return null;
        }

        var used = existing.ToHashSet();
        if (Sinks(kind).Count(used.Contains) >= MaxOf(kind))
        {
            return null;
        }

        var free = Sinks(kind).Where(s => !used.Contains(s)).ToList();
        return free.Count == 0 ? null : free[0];
    }
}
