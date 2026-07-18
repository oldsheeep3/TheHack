namespace Switcher.Contracts;

/// <summary>
/// Broadcasts the current tally state over UDP (docs/specs/00-system-overview.md §4.3).
/// </summary>
public interface ITallyBroadcaster
{
    void Publish(TallyState state);

    /// <summary>
    /// Broadcasts the 2-bus tally payload (<c>active_pgm1/2</c>, <c>active_pvw1/2</c>).
    /// Design adopted per agent-A2-001-contracts-v2's deferred decision: an additive overload
    /// rather than replacing <see cref="Publish(TallyState)"/>, so single-bus callers are unaffected.
    /// </summary>
    void Publish(TallyStateV2 state);
}
