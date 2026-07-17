namespace Switcher.Contracts;

/// <summary>
/// Controller input event (docs/specs/00-system-overview.md §4.1).
/// </summary>
public sealed record ButtonEvent(string ControllerId, int ButtonId, long Timestamp);

/// <summary>
/// WebSocket envelope wrapping a <see cref="ButtonEvent"/>, e.g. {"event":"button_press","data":{...}}.
/// </summary>
public sealed record WsEnvelope(string Event, ButtonEvent Data);
