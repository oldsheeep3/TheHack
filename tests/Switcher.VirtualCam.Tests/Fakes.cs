using Switcher.VirtualCam.Devices;

namespace Switcher.VirtualCam.Tests;

/// <summary>Controllable stand-in for the native virtual camera device: records every call so tests
/// can assert on lifecycle/resolution-change behavior without the Windows-only shared-memory IPC.</summary>
internal sealed class FakeVirtualCameraDevice : IVirtualCameraDevice
{
    public List<(int Width, int Height)> OpenCalls { get; } = [];

    public List<byte[]> PushFrameCalls { get; } = [];

    public int CloseCount { get; private set; }

    public int DisposeCount { get; private set; }

    public void Open(int width, int height) => OpenCalls.Add((width, height));

    public void PushFrame(ReadOnlySpan<byte> nv12Frame) => PushFrameCalls.Add(nv12Frame.ToArray());

    public void Close() => CloseCount++;

    public void Dispose() => DisposeCount++;
}
