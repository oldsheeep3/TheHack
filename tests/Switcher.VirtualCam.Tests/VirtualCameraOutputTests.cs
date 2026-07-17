using Switcher.Contracts;

namespace Switcher.VirtualCam.Tests;

public sealed class VirtualCameraOutputTests
{
    private static FrameData MakeFrame(int width, int height, byte fill = 0x80) =>
        new(width, height, new byte[width * height * 4].Select(_ => fill).ToArray());

    [Fact]
    public void SubmitFrame_BeforeStart_Throws()
    {
        var device = new FakeVirtualCameraDevice();
        var output = new VirtualCameraOutput(device);

        Assert.Throws<InvalidOperationException>(() => output.SubmitFrame(MakeFrame(4, 2)));
        Assert.Empty(device.OpenCalls);
    }

    [Fact]
    public void SubmitFrame_AfterStart_OpensDeviceOnceAndPushesConvertedFrame()
    {
        var device = new FakeVirtualCameraDevice();
        var output = new VirtualCameraOutput(device);

        output.Start();
        output.SubmitFrame(MakeFrame(4, 2));

        var openCall = Assert.Single(device.OpenCalls);
        Assert.Equal((4, 2), openCall);

        var pushed = Assert.Single(device.PushFrameCalls);
        Assert.Equal(4 * 2 + (4 / 2) * (2 / 2) * 2, pushed.Length);
    }

    [Fact]
    public void Start_CalledTwice_IsIdempotentAndDoesNotTouchDevice()
    {
        var device = new FakeVirtualCameraDevice();
        var output = new VirtualCameraOutput(device);

        output.Start();
        output.Start();

        Assert.Empty(device.OpenCalls);
    }

    [Fact]
    public void SubmitFrame_SameResolutionTwice_OpensDeviceOnlyOnce()
    {
        var device = new FakeVirtualCameraDevice();
        var output = new VirtualCameraOutput(device);

        output.Start();
        output.SubmitFrame(MakeFrame(4, 2));
        output.SubmitFrame(MakeFrame(4, 2));

        Assert.Single(device.OpenCalls);
        Assert.Equal(2, device.PushFrameCalls.Count);
    }

    [Fact]
    public void SubmitFrame_ResolutionChanges_ReopensDevice()
    {
        var device = new FakeVirtualCameraDevice();
        var output = new VirtualCameraOutput(device);

        output.Start();
        output.SubmitFrame(MakeFrame(4, 2));
        output.SubmitFrame(MakeFrame(8, 4));

        Assert.Equal([(4, 2), (8, 4)], device.OpenCalls);
    }

    [Fact]
    public void Stop_ClosesDeviceAndIsIdempotent()
    {
        var device = new FakeVirtualCameraDevice();
        var output = new VirtualCameraOutput(device);

        output.Start();
        output.SubmitFrame(MakeFrame(4, 2));
        output.Stop();
        output.Stop();

        Assert.Equal(1, device.CloseCount);
    }

    [Fact]
    public void Stop_WithoutStart_DoesNotThrowOrTouchDevice()
    {
        var device = new FakeVirtualCameraDevice();
        var output = new VirtualCameraOutput(device);

        output.Stop();

        Assert.Equal(0, device.CloseCount);
    }

    [Fact]
    public void SubmitFrame_AfterStop_ThrowsUntilStartedAgain()
    {
        var device = new FakeVirtualCameraDevice();
        var output = new VirtualCameraOutput(device);

        output.Start();
        output.SubmitFrame(MakeFrame(4, 2));
        output.Stop();

        Assert.Throws<InvalidOperationException>(() => output.SubmitFrame(MakeFrame(4, 2)));

        output.Start();
        output.SubmitFrame(MakeFrame(4, 2));

        Assert.Equal(2, device.OpenCalls.Count);
    }

    [Fact]
    public void Dispose_ClosesAndDisposesDeviceAndIsIdempotent()
    {
        var device = new FakeVirtualCameraDevice();
        var output = new VirtualCameraOutput(device);

        output.Start();
        output.SubmitFrame(MakeFrame(4, 2));
        output.Dispose();
        output.Dispose();

        Assert.Equal(1, device.CloseCount);
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public void MethodsAfterDispose_ThrowObjectDisposedException()
    {
        var device = new FakeVirtualCameraDevice();
        var output = new VirtualCameraOutput(device);
        output.Dispose();

        Assert.Throws<ObjectDisposedException>(() => output.Start());
        Assert.Throws<ObjectDisposedException>(() => output.SubmitFrame(MakeFrame(4, 2)));
    }
}
