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
