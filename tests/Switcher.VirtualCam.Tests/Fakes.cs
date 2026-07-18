using Switcher.Contracts;
using Switcher.VirtualCam.Devices;
using Switcher.VirtualCam.Display;
using Switcher.VirtualCam.Ndi;

namespace Switcher.VirtualCam.Tests;

/// <summary>Controllable stand-in for the native virtual camera device: records every call so tests
/// can assert on lifecycle/resolution-change behavior without the Windows-only shared-memory IPC.</summary>
internal sealed class FakeVirtualCameraDevice : IVirtualCameraDevice
{
    public List<(int Width, int Height)> OpenCalls { get; } = [];

    public List<byte[]> PushFrameCalls { get; } = [];

    public int CloseCount { get; private set; }

    public int DisposeCount { get; private set; }

    public bool ThrowOnOpen { get; set; }

    public void Open(int width, int height)
    {
        if (ThrowOnOpen)
        {
            throw new InvalidOperationException("Simulated device failure.");
        }

        OpenCalls.Add((width, height));
    }

    public void PushFrame(ReadOnlySpan<byte> nv12Frame) => PushFrameCalls.Add(nv12Frame.ToArray());

    public void Close() => CloseCount++;

    public void Dispose() => DisposeCount++;
}

/// <summary>Controllable stand-in for <see cref="IDualVirtualCameraOutput"/>: records every call so
/// <see cref="OutputRouterTests"/> can assert on frame fan-out without a real device.</summary>
internal sealed class FakeDualVirtualCameraOutput : IDualVirtualCameraOutput
{
    public List<(OutputSink Sink, FrameData Frame)> SubmitFrameCalls { get; } = [];

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public OutputSink? ThrowOnSubmitToSink { get; set; }

    public void Start() => StartCount++;

    public void SubmitFrame(OutputSink sink, FrameData frame)
    {
        if (sink == ThrowOnSubmitToSink)
        {
            throw new InvalidOperationException($"Simulated failure submitting to {sink}.");
        }

        SubmitFrameCalls.Add((sink, frame));
    }

    public void Stop() => StopCount++;
}

/// <summary>Controllable stand-in for the native NDI Sender: records every call so tests can assert on
/// lifecycle/sender-name/resolution behavior, and can simulate the NDI SDK being absent
/// (<see cref="IsAvailable"/> = <c>false</c>), without the platform NDI runtime.</summary>
internal sealed class FakeNdiSenderDevice : INdiSenderDevice
{
    public bool IsAvailable { get; set; } = true;

    public List<(string Name, int Width, int Height)> OpenCalls { get; } = [];

    public List<FrameData> SendCalls { get; } = [];

    public int CloseCount { get; private set; }

    public int DisposeCount { get; private set; }

    public bool ThrowOnOpen { get; set; }

    public void Open(string senderName, int width, int height)
    {
        if (ThrowOnOpen)
        {
            throw new InvalidOperationException("Simulated NDI sender failure.");
        }

        OpenCalls.Add((senderName, width, height));
    }

    public void Send(FrameData frame) => SendCalls.Add(frame);

    public void Close() => CloseCount++;

    public void Dispose() => DisposeCount++;
}

/// <summary>Controllable stand-in for <see cref="IDualNdiOutput"/>: records every call so
/// <see cref="OutputRouterTests"/> can assert on NDI frame fan-out and sender-name propagation without a
/// real NDI runtime.</summary>
internal sealed class FakeDualNdiOutput : IDualNdiOutput
{
    public List<(OutputSink Sink, FrameData Frame)> SubmitFrameCalls { get; } = [];

    public List<(OutputSink Sink, string SenderName)> SetSenderNameCalls { get; } = [];

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public OutputSink? ThrowOnSubmitToSink { get; set; }

    public void Start() => StartCount++;

    public void SubmitFrame(OutputSink sink, FrameData frame)
    {
        if (sink == ThrowOnSubmitToSink)
        {
            throw new InvalidOperationException($"Simulated failure submitting to {sink}.");
        }

        SubmitFrameCalls.Add((sink, frame));
    }

    public void SetSenderName(OutputSink sink, string senderName) => SetSenderNameCalls.Add((sink, senderName));

    public void Stop() => StopCount++;
}

/// <summary>Controllable stand-in for <see cref="ISwapChainOutput"/>: records every call so
/// <see cref="HdmiFullscreenOutputTests"/> can assert on attach/present/detach without a real DXGI swap
/// chain.</summary>
internal sealed class FakeSwapChainOutput : ISwapChainOutput
{
    public List<(int DisplayIndex, nint WindowHandle)> AttachCalls { get; } = [];

    public List<FrameData> PresentCalls { get; } = [];

    public int DetachCount { get; private set; }

    public int DisposeCount { get; private set; }

    public void AttachToDisplay(int displayIndex, nint windowHandle) => AttachCalls.Add((displayIndex, windowHandle));

    public void Present(FrameData frame) => PresentCalls.Add(frame);

    public void Detach() => DetachCount++;

    public void Dispose() => DisposeCount++;
}

/// <summary>Controllable stand-in for <see cref="ICursorVisibility"/>: records every call so
/// <see cref="HdmiFullscreenOutputTests"/> can assert on the <c>hide_cursor</c> policy without the
/// Windows-only <c>user32.dll</c> call.</summary>
internal sealed class FakeCursorVisibility : ICursorVisibility
{
    public int HideCount { get; private set; }

    public int ShowCount { get; private set; }

    public void Hide() => HideCount++;

    public void Show() => ShowCount++;
}

/// <summary>Controllable stand-in for <see cref="IHdmiFullscreenOutput"/>: records every call so
/// <see cref="OutputRouterTests"/> can assert on frame fan-out without a real swap chain.</summary>
internal sealed class FakeHdmiFullscreenOutput : IHdmiFullscreenOutput
{
    public List<FrameData> PresentCalls { get; } = [];

    public bool ThrowOnPresent { get; set; }

    public int? DisplayId { get; private set; }

    public bool HideCursor { get; private set; }

    public bool Fullscreen { get; private set; }

    public bool IsAttached { get; private set; }

    public void Attach(int displayId, nint windowHandle, bool hideCursor, bool fullscreen)
    {
        DisplayId = displayId;
        HideCursor = hideCursor;
        Fullscreen = fullscreen;
        IsAttached = true;
    }

    public void Present(FrameData frame)
    {
        if (ThrowOnPresent)
        {
            throw new InvalidOperationException("Simulated HDMI present failure.");
        }

        PresentCalls.Add(frame);
    }

    public void Detach()
    {
        IsAttached = false;
        DisplayId = null;
    }

    public void Dispose()
    {
    }
}
