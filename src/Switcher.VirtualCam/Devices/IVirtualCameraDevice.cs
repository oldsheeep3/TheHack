namespace Switcher.VirtualCam.Devices;

/// <summary>
/// Abstraction over the native virtual camera device (the DirectShow source filter's IPC channel;
/// see README.md, §"Virtual camera device"), so <see cref="Switcher.VirtualCam.VirtualCameraOutput"/>
/// can be unit tested (lifecycle, resolution changes) without the Windows-only native runtime.
/// </summary>
internal interface IVirtualCameraDevice : IDisposable
{
    /// <summary>Opens (or re-opens, if already open) the device at the given resolution. Idempotent
    /// when called again with the same resolution.</summary>
    void Open(int width, int height);

    /// <summary>Publishes one already-converted NV12 frame to the device. Throws if the device is
    /// not open.</summary>
    void PushFrame(ReadOnlySpan<byte> nv12Frame);

    /// <summary>Closes the device, releasing any native resources. Safe to call multiple times, and
    /// safe to call when the device was never opened.</summary>
    void Close();
}
