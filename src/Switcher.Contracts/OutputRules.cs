namespace Switcher.Contracts;

/// <summary>
/// Rules every output table has to satisfy, shared by the Web API validator and the App so both reject
/// the same tables with the same wording.
/// </summary>
public static class OutputRules
{
    /// <summary>
    /// Program buses with no sink feeding them.
    ///
    /// A dual-M/E switcher exists to run two independent program feeds, so each bus must reach at least
    /// one output: a PGM nobody can see is a mis-patch, not a configuration. <c>PUT /api/v1/outputs</c>
    /// replaces the whole table, so the check is over the complete list.
    /// </summary>
    public static IReadOnlyList<OutputSource> MissingBuses(IReadOnlyList<OutputAssignment>? outputs)
    {
        var missing = new List<OutputSource>();
        foreach (var bus in Enum.GetValues<OutputSource>())
        {
            if (outputs is null || !outputs.Any(o => o.Source == bus))
            {
                missing.Add(bus);
            }
        }

        return missing;
    }

    /// <summary>
    /// Program buses that have sinks assigned but none of them actually egressing.
    ///
    /// This is the failure the routing table cannot show: the assignment was accepted, the sink just
    /// never started. A bus with no assignment at all is <see cref="MissingBuses"/>'s business.
    /// </summary>
    public static IReadOnlyList<OutputSource> BusesWithoutRunningOutput(IReadOnlyList<OutputStatus>? status)
    {
        var dead = new List<OutputSource>();
        if (status is null)
        {
            return dead;
        }

        foreach (var bus in Enum.GetValues<OutputSource>())
        {
            var forBus = status.Where(s => s.Source == bus).ToList();
            if (forBus.Count > 0 && !forBus.Any(s => s.Running))
            {
                dead.Add(bus);
            }
        }

        return dead;
    }

    /// <summary>
    /// Complaints about how many sinks the table holds: at most <see cref="OutputCatalog.MaxOf"/> of any
    /// one kind and at most <see cref="OutputCatalog.MaxTotal"/> in total.
    ///
    /// The App disables its "add output" affordance at the same ceilings, so a table that trips this has
    /// come from the Web API or a hand-edited config rather than from the UI — which is exactly why the
    /// limit is enforced here and not only in the UI. A table written by a build that predates
    /// <see cref="OutputCatalog.MaxWebcamSinks"/> can also trip it, by carrying the old
    /// <c>PGM1→VCAM1 / PGM2→VCAM2</c> default; the App falls back to the current defaults when a restored
    /// table is over limit, so that upgrade path resets the routing rather than failing to start.
    /// </summary>
    public static IReadOnlyList<string> DescribeOverLimit(IReadOnlyList<OutputAssignment>? outputs)
    {
        var errors = new List<string>();
        if (outputs is null)
        {
            return errors;
        }

        if (outputs.Count > OutputCatalog.MaxTotal)
        {
            errors.Add($"outputs holds {outputs.Count} sinks; at most {OutputCatalog.MaxTotal} are allowed.");
        }

        foreach (var kind in Enum.GetValues<OutputKind>())
        {
            var count = outputs.Count(o => OutputCatalog.KindOf(o.Sink) == kind);
            if (count > OutputCatalog.MaxOf(kind))
            {
                errors.Add(
                    $"outputs holds {count} {OutputCatalog.TokenOf(kind)} sinks; at most " +
                    $"{OutputCatalog.MaxOf(kind)} are allowed.");
            }
        }

        return errors;
    }

    /// <summary>Human-readable complaint for <paramref name="missing"/>, or <c>null</c> when the table
    /// is complete.</summary>
    public static string? DescribeMissingBuses(IReadOnlyList<OutputSource> missing)
    {
        ArgumentNullException.ThrowIfNull(missing);

        return missing.Count == 0
            ? null
            : $"Every program bus needs at least one output; {string.Join(" and ", missing)} has none.";
    }
}
