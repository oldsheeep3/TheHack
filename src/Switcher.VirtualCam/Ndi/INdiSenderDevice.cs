using Switcher.Contracts;

namespace Switcher.VirtualCam.Ndi;

/// <summary>
/// Abstraction over the native NDI Sender (the NDI SDK's <c>NDIlib_send_*</c> API; see README.md,
/// §"NDI output"), so <see cref="NdiOutput"/> can be unit tested (lifecycle, sender-name/resolution
/// changes, SDK-absent no-op) without the platform-specific NDI runtime. The real implementation is
/// <see cref="NdiSdkSenderDevice"/>.
/// </summary>
internal interface INdiSenderDevice : IDisposable
{
    /// <summary>Whether the NDI SDK/runtime was detected on this machine. When <c>false</c>,
    /// <see cref="NdiOutput"/> disables send-out entirely (no-op) rather than throwing, so a missing
    /// SDK never disrupts the other output sinks.</summary>
    bool IsAvailable { get; }

    /// <summary>Opens (or re-opens, if already open) an NDI Sender publishing under
    /// <paramref name="senderName"/> at the given frame resolution. Idempotent when called again with
    /// the same name/resolution is the caller's responsibility (<see cref="NdiOutput"/> tracks that).</summary>
    void Open(string senderName, int width, int height);

    /// <summary>Publishes one composited <see cref="FrameData"/> (BGRA32) to the network as an NDI
    /// video frame. Throws/ignores if not open (see the implementation); <see cref="NdiOutput"/> only
    /// calls this after a successful <see cref="Open"/>.</summary>
    void Send(FrameData frame);

    /// <summary>Closes the NDI Sender, releasing native resources. Safe to call multiple times, and
    /// safe to call when the sender was never opened.</summary>
    void Close();
}
