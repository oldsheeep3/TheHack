namespace Switcher.Hid.Devices;

/// <summary>
/// Abstraction over one USB-HID device connection to the Pico 2W controller
/// (docs/specs/00-system-overview.md §4.1), so <see cref="Switcher.Hid.HidInputService"/> and
/// <see cref="Switcher.Hid.HidBacklightService"/> can be unit tested without real HID hardware.
/// Report bodies only — the Report ID byte (<see cref="Switcher.Contracts.ProtocolConstants.HidInputReportId"/>
/// / <see cref="Switcher.Contracts.ProtocolConstants.HidOutputReportId"/>) is a wire-transport detail
/// that implementations strip/prepend internally, keeping <see cref="Switcher.Hid.Reports.HidReportParser"/>
/// and its callers ID-agnostic. Also the design's insertion point for a future non-HID (Wi-Fi/BT)
/// input source (docs/specs/pc-switcher-app.md §2.6: "任意/無線運用"): an alternate implementation
/// could supply equivalent input/output report bytes over a different transport.
/// </summary>
internal interface IHidDevice : IDisposable
{
    /// <summary>Opens (or re-opens, if already open) the device connection. Idempotent when already
    /// open.</summary>
    void Open();

    /// <summary>Blocking read of one input report body (Report ID already stripped). Returns null if
    /// the device was closed while the read was pending. Throws if called before <see cref="Open"/>.</summary>
    byte[]? ReadInputReport();

    /// <summary>Writes one output report body (Report ID prepended internally). Throws if called
    /// before <see cref="Open"/>.</summary>
    void WriteOutputReport(ReadOnlySpan<byte> reportBody);

    /// <summary>Closes the device, releasing any native resources and unblocking any pending
    /// <see cref="ReadInputReport"/>. Safe to call multiple times, and safe to call when the device
    /// was never opened.</summary>
    void Close();
}
