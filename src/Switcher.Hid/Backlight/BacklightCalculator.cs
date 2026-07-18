using Switcher.Contracts;

namespace Switcher.Hid.Backlight;

/// <summary>
/// Pure transform from the current PGM/PVW bus state + module→source mapping to the backlight
/// output reports for every mapped module (docs/specs/pc-switcher-app.md §2.6). Holds no state of
/// its own; the caller (<see cref="Switcher.Hid.HidBacklightService"/>) owns tracking bus state and
/// module mapping and re-invokes <see cref="Compute"/> whenever either changes.
/// </summary>
public sealed class BacklightCalculator
{
    private readonly IBacklightPolicy _policy;

    public BacklightCalculator() : this(new DefaultBacklightPolicy())
    {
    }

    public BacklightCalculator(IBacklightPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
    }

    /// <summary>Computes one <see cref="HidOutputReport"/> per module mapping, in the same
    /// PGM1×SRC1/PGM1×SRC2/PGM2×SRC1/PGM2×SRC2 order as the input report's SW bits
    /// (docs/specs/00-system-overview.md §4.1) so the 4 colors line up with a module's 4 physical
    /// switches.</summary>
    public IReadOnlyList<HidOutputReport> Compute(
        ProgramBusesState programState,
        PreviewBusesState previewState,
        IReadOnlyList<ModuleMapping> moduleMappings)
    {
        ArgumentNullException.ThrowIfNull(programState);
        ArgumentNullException.ThrowIfNull(previewState);
        ArgumentNullException.ThrowIfNull(moduleMappings);

        var reports = new List<HidOutputReport>(moduleMappings.Count);
        foreach (var mapping in moduleMappings)
        {
            var pgm1Src1 = ResolveContext(mapping.Src1.SourceId, programState.Pgm1SourceIds, previewState.Pvw1SourceIds);
            var pgm1Src2 = ResolveContext(mapping.Src2.SourceId, programState.Pgm1SourceIds, previewState.Pvw1SourceIds);
            var pgm2Src1 = ResolveContext(mapping.Src1.SourceId, programState.Pgm2SourceIds, previewState.Pvw2SourceIds);
            var pgm2Src2 = ResolveContext(mapping.Src2.SourceId, programState.Pgm2SourceIds, previewState.Pvw2SourceIds);

            IReadOnlyList<BacklightColor> colors =
            [
                _policy.Compute(pgm1Src1),
                _policy.Compute(pgm1Src2),
                _policy.Compute(pgm2Src1),
                _policy.Compute(pgm2Src2),
            ];

            reports.Add(new HidOutputReport((byte)mapping.Index, colors));
        }

        return reports;
    }

    private static BacklightSwitchContext ResolveContext(
        string? sourceId, IReadOnlySet<string> programSourceIds, IReadOnlySet<string> previewSourceIds)
    {
        if (sourceId is null)
        {
            return new BacklightSwitchContext(IsProgram: false, IsPreview: false, IsSelectable: false);
        }

        return new BacklightSwitchContext(
            IsProgram: programSourceIds.Contains(sourceId),
            IsPreview: previewSourceIds.Contains(sourceId),
            IsSelectable: true);
    }
}
