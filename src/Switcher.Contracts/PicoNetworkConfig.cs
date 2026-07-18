namespace Switcher.Contracts;

/// <summary>
/// Request body for PUT /api/v1/pico/network (docs/specs/00-system-overview.md §4.6).
/// Carries Wi-Fi credentials — callers must not log this record's contents.
/// </summary>
public sealed record PicoNetworkConfig(
    string? WifiSsid,
    string? WifiPassword,
    string? ControllerId,
    bool BluetoothEnabled);
