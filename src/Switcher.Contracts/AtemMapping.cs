namespace Switcher.Contracts;

/// <summary>
/// Web-boundary JSON DTOs for ATEM remote control config (docs/specs/00-system-overview.md §4.2).
/// These are distinct from Switcher.Atem's AtemAction/ButtonCommandMapping, which are unchanged;
/// the App layer converts between the two.
/// </summary>
public sealed record AtemButtonMapping(
    string ControllerId,
    int ModuleIndex,
    string Switch,
    string Action,
    int MixEffect,
    int Source);

/// <summary>
/// Request/response body for PUT /api/v1/atem.
/// </summary>
public sealed record AtemConfig(bool Enabled, string Ip, IReadOnlyList<AtemButtonMapping> Mappings);

/// <summary>
/// Request body for POST /api/v1/atem/command (one-off Program/Preview switch or Cut/Auto).
/// </summary>
public sealed record AtemCommandRequest(string Action, int MixEffect, int Source);
