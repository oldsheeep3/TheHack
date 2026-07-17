namespace Switcher.Contracts;

/// <summary>
/// UDP broadcast tally payload (docs/specs/00-system-overview.md §4.3).
/// </summary>
public sealed record TallyState(IReadOnlyList<int> ActivePgm, IReadOnlyList<int> ActivePvw);
