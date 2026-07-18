using Switcher.Contracts;

namespace Switcher.App.Tests;

/// <summary>In-memory <see cref="ITallyBroadcaster"/>: records every published payload so tests can
/// assert on 2-bus tally state without a real UDP broadcast socket.</summary>
internal sealed class FakeTallyBroadcaster : ITallyBroadcaster
{
    public List<TallyState> Published { get; } = [];

    public List<TallyStateV2> PublishedV2 { get; } = [];

    public TallyStateV2? LastV2 => PublishedV2.Count == 0 ? null : PublishedV2[^1];

    public void Publish(TallyState state) => Published.Add(state);

    public void Publish(TallyStateV2 state) => PublishedV2.Add(state);
}
