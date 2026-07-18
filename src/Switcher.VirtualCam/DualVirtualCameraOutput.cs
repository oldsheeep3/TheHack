using Switcher.Contracts;
using Switcher.VirtualCam.Devices;

namespace Switcher.VirtualCam;

/// <summary>
/// Default <see cref="IDualVirtualCameraOutput"/>: manages two independent <see cref="VirtualCameraOutput"/>
/// instances (VCAM1/VCAM2), each backed by its own <see cref="SharedMemoryVirtualCameraDevice"/> opened
/// under a distinct shared-memory/event name (see <see cref="Devices.SharedMemoryVirtualCameraDevice"/>)
/// so the two devices coexist without colliding. A failure on one sink (e.g. its native device is not
/// installed) does not stop or otherwise affect the other.
/// </summary>
public sealed class DualVirtualCameraOutput : IDualVirtualCameraOutput, IDisposable
{
    // Distinct from SharedMemoryVirtualCameraDevice.DefaultMemoryMappedFileName/DefaultFrameReadyEventName
    // (the single-camera default) so the VCAM1 sink here never collides with a lone VirtualCameraOutput.
    internal const string Vcam1MemoryMappedFileName = "Local\\HybridSwitcherVCamFrame_VCAM1";
    internal const string Vcam1FrameReadyEventName = "Local\\HybridSwitcherVCamFrameReady_VCAM1";
    internal const string Vcam2MemoryMappedFileName = "Local\\HybridSwitcherVCamFrame_VCAM2";
    internal const string Vcam2FrameReadyEventName = "Local\\HybridSwitcherVCamFrameReady_VCAM2";

    private readonly VirtualCameraOutput _vcam1;
    private readonly VirtualCameraOutput _vcam2;

    public DualVirtualCameraOutput()
        : this(
            new VirtualCameraOutput(new SharedMemoryVirtualCameraDevice(Vcam1MemoryMappedFileName, Vcam1FrameReadyEventName)),
            new VirtualCameraOutput(new SharedMemoryVirtualCameraDevice(Vcam2MemoryMappedFileName, Vcam2FrameReadyEventName)))
    {
    }

    internal DualVirtualCameraOutput(VirtualCameraOutput vcam1, VirtualCameraOutput vcam2)
    {
        _vcam1 = vcam1;
        _vcam2 = vcam2;
    }

    public void Start()
    {
        _vcam1.Start();
        _vcam2.Start();
    }

    public void SubmitFrame(OutputSink sink, FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Resolve(sink).SubmitFrame(frame);
    }

    public void Stop()
    {
        _vcam1.Stop();
        _vcam2.Stop();
    }

    private VirtualCameraOutput Resolve(OutputSink sink) => sink switch
    {
        OutputSink.Vcam1 => _vcam1,
        OutputSink.Vcam2 => _vcam2,
        _ => throw new ArgumentOutOfRangeException(nameof(sink), sink, $"{nameof(DualVirtualCameraOutput)} only handles {OutputSink.Vcam1}/{OutputSink.Vcam2}."),
    };

    public void Dispose()
    {
        _vcam1.Dispose();
        _vcam2.Dispose();
    }
}
