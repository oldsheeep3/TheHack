using Switcher.App.Orchestration;
using Switcher.App.ViewModels;
using Switcher.Contracts;

namespace Switcher.App.Multiview;

/// <summary>
/// Resolves the red/green tally frame for a multiview cell token. Shared by the operator window's
/// multiview dock and the multiview settings editor so both always agree, and mirrored natively by
/// <c>region_tally</c> in switcher-engine so the composited multiview output carries the same frames.
/// </summary>
public static class MultiviewTally
{
    /// <summary>
    /// <c>PGM1</c>/<c>PGM2</c> are program by definition and <c>PVW1</c>/<c>PVW2</c> preview; a
    /// <c>SRC:&lt;id&gt;</c> cell is tallied when that source is mounted on a bus. Program wins over
    /// preview when a source is on both, because "this is on air" is the fact the operator must not
    /// miss.
    /// </summary>
    public static CellTally Resolve(
        string token,
        IReadOnlySet<string> programSourceIds,
        IReadOnlySet<string> previewSourceIds)
    {
        ArgumentNullException.ThrowIfNull(programSourceIds);
        ArgumentNullException.ThrowIfNull(previewSourceIds);

        switch (token)
        {
            case "PGM1" or "PGM2":
                return CellTally.Program;
            case "PVW1" or "PVW2":
                return CellTally.Preview;
        }

        if (token is null || !token.StartsWith("SRC:", StringComparison.Ordinal))
        {
            return CellTally.None;
        }

        var id = token[4..];
        if (id.Length == 0)
        {
            // A bare "SRC:" names no source, so it can't be on air whatever the bus sets contain.
            return CellTally.None;
        }

        if (programSourceIds.Contains(id))
        {
            return CellTally.Program;
        }

        return previewSourceIds.Contains(id) ? CellTally.Preview : CellTally.None;
    }

    /// <summary>Convenience overload that reads the current membership straight off the orchestrator.</summary>
    public static CellTally Resolve(string token, AppOrchestrator orchestrator) =>
        ResolveDetailed(token, orchestrator).State;

    /// <summary>
    /// Resolves both the tally state and <em>which bus</em> it comes from, so the cell can be drawn in
    /// that bus's colour. Bus 1 is checked first, so a source live on both buses is shown as PGM1 —
    /// consistent with program winning over preview: the more urgent fact is reported.
    /// </summary>
    public static (CellTally State, ProgramBus Bus) ResolveDetailed(string token, AppOrchestrator orchestrator)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);

        switch (token)
        {
            case "PGM1": return (CellTally.Program, ProgramBus.Pgm1);
            case "PGM2": return (CellTally.Program, ProgramBus.Pgm2);
            case "PVW1": return (CellTally.Preview, ProgramBus.Pgm1);
            case "PVW2": return (CellTally.Preview, ProgramBus.Pgm2);
        }

        foreach (var bus in new[] { ProgramBus.Pgm1, ProgramBus.Pgm2 })
        {
            if (Resolve(token, orchestrator.GetProgramSourceIds(bus), Empty) == CellTally.Program)
            {
                return (CellTally.Program, bus);
            }
        }

        foreach (var bus in new[] { ProgramBus.Pgm1, ProgramBus.Pgm2 })
        {
            if (Resolve(token, Empty, orchestrator.GetPreviewSourceIds(bus)) == CellTally.Preview)
            {
                return (CellTally.Preview, bus);
            }
        }

        return (CellTally.None, ProgramBus.Pgm1);
    }

    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.Ordinal);
}
