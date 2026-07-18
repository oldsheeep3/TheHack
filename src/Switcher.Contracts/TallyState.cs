namespace Switcher.Contracts;

/// <summary>
/// UDP broadcast tally payload (docs/specs/00-system-overview.md §4.3).
/// Retained for backward compatibility; new code should prefer <see cref="TallyStateV2"/>.
/// </summary>
public sealed record TallyState(IReadOnlyList<int> ActivePgm, IReadOnlyList<int> ActivePvw);

/// <summary>
/// UDP broadcast tally payload for the 2-bus system (docs/specs/00-system-overview.md §4.3).
/// </summary>
public sealed record TallyStateV2(
    IReadOnlyList<int> ActivePgm1,
    IReadOnlyList<int> ActivePgm2,
    IReadOnlyList<int> ActivePvw1,
    IReadOnlyList<int> ActivePvw2);
