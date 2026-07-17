namespace Switcher.Contracts;

/// <summary>
/// Broadcasts the current tally state over UDP (docs/specs/00-system-overview.md §4.3).
/// </summary>
public interface ITallyBroadcaster
{
    void Publish(TallyState state);
}
