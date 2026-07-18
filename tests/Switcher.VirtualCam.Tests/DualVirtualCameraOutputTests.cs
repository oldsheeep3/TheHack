using Switcher.Contracts;

namespace Switcher.VirtualCam.Tests;

public sealed class DualVirtualCameraOutputTests
{
    private static FrameData MakeFrame(int width, int height, byte fill = 0x80) =>
        new(width, height, new byte[width * height * 4].Select(_ => fill).ToArray());

    private static (DualVirtualCameraOutput Dual, FakeVirtualCameraDevice Device1, FakeVirtualCameraDevice Device2) MakeDual()
    {
        var device1 = new FakeVirtualCameraDevice();
        var device2 = new FakeVirtualCameraDevice();
        var dual = new DualVirtualCameraOutput(new VirtualCameraOutput(device1), new VirtualCameraOutput(device2));
        return (dual, device1, device2);
    }

    [Fact]
    public void Start_StartsBothSinks()
    {
        var (dual, device1, device2) = MakeDual();

        dual.Start();
        dual.SubmitFrame(OutputSink.Vcam1, MakeFrame(4, 2));
        dual.SubmitFrame(OutputSink.Vcam2, MakeFrame(4, 2));

        Assert.Single(device1.OpenCalls);
        Assert.Single(device2.OpenCalls);
    }

    [Fact]
    public void SubmitFrame_RoutesToTheMatchingDeviceOnly()
    {
        var (dual, device1, device2) = MakeDual();
        dual.Start();

        dual.SubmitFrame(OutputSink.Vcam1, MakeFrame(4, 2));

        Assert.Single(device1.OpenCalls);
        Assert.Single(device1.PushFrameCalls);
        Assert.Empty(device2.OpenCalls);
        Assert.Empty(device2.PushFrameCalls);
    }

    [Fact]
    public void SubmitFrame_BothSinksAreIndependent()
    {
        var (dual, device1, device2) = MakeDual();
        dual.Start();

        dual.SubmitFrame(OutputSink.Vcam1, MakeFrame(4, 2));
        dual.SubmitFrame(OutputSink.Vcam2, MakeFrame(8, 4));

        Assert.Equal((4, 2), Assert.Single(device1.OpenCalls));
        Assert.Equal((8, 4), Assert.Single(device2.OpenCalls));
    }

    [Fact]
    public void SubmitFrame_UnknownSink_Throws()
    {
        var (dual, _, _) = MakeDual();
        dual.Start();

        Assert.Throws<ArgumentOutOfRangeException>(() => dual.SubmitFrame(OutputSink.Hdmi, MakeFrame(4, 2)));
    }

    [Fact]
    public void Stop_StopsBothSinksAndIsIdempotent()
    {
        var (dual, device1, device2) = MakeDual();
        dual.Start();
        dual.SubmitFrame(OutputSink.Vcam1, MakeFrame(4, 2));
        dual.SubmitFrame(OutputSink.Vcam2, MakeFrame(4, 2));

        dual.Stop();
        dual.Stop();

        Assert.Equal(1, device1.CloseCount);
        Assert.Equal(1, device2.CloseCount);
    }

    [Fact]
    public void Dispose_DisposesBothDevices()
    {
        var (dual, device1, device2) = MakeDual();
        dual.Start();

        dual.Dispose();

        Assert.Equal(1, device1.DisposeCount);
        Assert.Equal(1, device2.DisposeCount);
    }

    [Fact]
    public void SubmitFrame_OneDeviceFails_OtherDeviceUnaffected()
    {
        var (dual, device1, device2) = MakeDual();
        device1.ThrowOnOpen = true;
        dual.Start();

        Assert.Throws<InvalidOperationException>(() => dual.SubmitFrame(OutputSink.Vcam1, MakeFrame(4, 2)));
        dual.SubmitFrame(OutputSink.Vcam2, MakeFrame(4, 2));

        Assert.Single(device2.OpenCalls);
        Assert.Single(device2.PushFrameCalls);
    }
}
