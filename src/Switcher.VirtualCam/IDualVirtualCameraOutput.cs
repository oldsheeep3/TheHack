using Switcher.Contracts;

namespace Switcher.VirtualCam;

/// <summary>
/// Manages the two virtual-camera sinks (<see cref="OutputSink.Vcam1"/>/<see cref="OutputSink.Vcam2"/>,
/// docs/specs/00-system-overview.md §4.2) as independent devices, so a frame can be routed to either
/// (or both) without one sink's failure affecting the other. See <see cref="DualVirtualCameraOutput"/>.
/// </summary>
public interface IDualVirtualCameraOutput
{
    /// <summary>Starts both sinks. Safe to call again while already started (no-op per sink).</summary>
    void Start();

    /// <summary>Submits a frame to the given sink. <paramref name="sink"/> must be
    /// <see cref="OutputSink.Vcam1"/> or <see cref="OutputSink.Vcam2"/>.</summary>
    void SubmitFrame(OutputSink sink, FrameData frame);

    /// <summary>Stops both sinks. Safe to call again, or when never started (no-op).</summary>
    void Stop();
}
