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
/// <param name="Name">Model name of the selected switcher, as reported by its <c>_pin</c> block during
/// discovery. Display only — <paramref name="Ip"/> is what the client connects to. Null for a config
/// written before the ATEM picker existed, or for an address typed in by hand that never answered.</param>
public sealed record AtemConfig(
    bool Enabled,
    string Ip,
    IReadOnlyList<AtemButtonMapping> Mappings,
    string? Name = null);

/// <summary>
/// Request body for POST /api/v1/atem/command (one-off Program/Preview switch or Cut/Auto).
/// </summary>
public sealed record AtemCommandRequest(string Action, int MixEffect, int Source);

/// <summary>
/// One switcher found by a network sweep (GET /api/v1/atem/discover).
/// </summary>
public sealed record AtemDeviceInfo(string Ip, string Name);

/// <summary>
/// Request body for POST /api/v1/atem/streaming: point the connected ATEM's streaming output at a URL
/// (an <c>srt://</c> one, to feed its ON AIR program into this PC as a source) and optionally start it.
/// </summary>
/// <param name="Url">Destination the ATEM should stream to, e.g. <c>srt://192.168.1.50:9000</c>.</param>
/// <param name="ServiceName">Label the ATEM shows for the streaming destination.</param>
/// <param name="StreamKey">Stream key; empty for SRT, which does not use one.</param>
/// <param name="Start">Whether to also put the ATEM on air after applying the settings.</param>
public sealed record AtemStreamingRequest(
    string Url,
    string ServiceName = "Switcher SRT",
    string StreamKey = "",
    bool Start = true);
