using Switcher.Contracts;

namespace Switcher.VirtualCam.Ndi;

/// <summary>
/// Manages the two NDI network sinks (<see cref="OutputSink.Ndi1"/>/<see cref="OutputSink.Ndi2"/>,
/// docs/specs/multiview-output-revision.md §2.5) as independent senders, so a frame can be routed to
/// either without one sender's failure (or a missing SDK) affecting the other. See
/// <see cref="DualNdiOutput"/>.
/// </summary>
public interface IDualNdiOutput
{
    /// <summary>Starts both NDI senders. Safe to call again while already started (no-op per sink).</summary>
    void Start();

    /// <summary>Submits a frame to the given NDI sink. <paramref name="sink"/> must be
    /// <see cref="OutputSink.Ndi1"/> or <see cref="OutputSink.Ndi2"/>.</summary>
    void SubmitFrame(OutputSink sink, FrameData frame);

    /// <summary>Sets the NDI sender name for the given NDI sink (from the assignment's <c>ndi_name</c>).
    /// <paramref name="sink"/> must be <see cref="OutputSink.Ndi1"/> or <see cref="OutputSink.Ndi2"/>.</summary>
    void SetSenderName(OutputSink sink, string senderName);

    /// <summary>Stops both NDI senders. Safe to call again, or when never started (no-op).</summary>
    void Stop();
}
