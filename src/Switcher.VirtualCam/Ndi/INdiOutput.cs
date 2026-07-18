using Switcher.Contracts;

namespace Switcher.VirtualCam.Ndi;

/// <summary>
/// A single NDI network output sink (<see cref="OutputSink.Ndi1"/>/<see cref="OutputSink.Ndi2"/>,
/// docs/specs/multiview-output-revision.md §2.5): publishes composited PGM frames on the network under
/// a configurable sender name. When the NDI SDK is not installed, send-out is disabled gracefully (a
/// no-op) so it never disrupts the other sinks. See <see cref="NdiOutput"/>.
/// </summary>
public interface INdiOutput
{
    /// <summary>The NDI sender name this output currently publishes under (e.g. <c>SWITCHER PGM1</c>).</summary>
    string SenderName { get; }

    /// <summary>Marks the output as active. Safe to call again while already started (no-op).</summary>
    void Start();

    /// <summary>Changes the NDI sender name. If the sender is already open, it is re-opened under the
    /// new name on the next frame. <paramref name="senderName"/> must be non-empty.</summary>
    void SetSenderName(string senderName);

    /// <summary>Submits one composited frame for network send-out. No-op when the NDI SDK is absent.</summary>
    void SubmitFrame(FrameData frame);

    /// <summary>Stops the output and releases the NDI sender. Safe to call again, or when never started
    /// (no-op).</summary>
    void Stop();
}
