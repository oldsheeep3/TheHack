using Switcher.Contracts;
using Switcher.VirtualCam.Ndi;

namespace Switcher.VirtualCam.Tests.Ndi;

public sealed class NdiOutputTests
{
    private static FrameData MakeFrame(int width, int height, byte fill = 0x80) =>
        new(width, height, new byte[width * height * 4].Select(_ => fill).ToArray());

    [Fact]
    public void SubmitFrame_BeforeStart_Throws()
    {
        var device = new FakeNdiSenderDevice();
        var output = new NdiOutput("SWITCHER PGM1", device);

        Assert.Throws<InvalidOperationException>(() => output.SubmitFrame(MakeFrame(4, 2)));
        Assert.Empty(device.OpenCalls);
    }

    [Fact]
    public void SubmitFrame_AfterStart_OpensSenderWithNameAndSendsFrame()
    {
        var device = new FakeNdiSenderDevice();
        var output = new NdiOutput("SWITCHER PGM1", device);

        output.Start();
        var frame = MakeFrame(4, 2);
        output.SubmitFrame(frame);

        var openCall = Assert.Single(device.OpenCalls);
        Assert.Equal(("SWITCHER PGM1", 4, 2), openCall);
        Assert.Same(frame, Assert.Single(device.SendCalls));
    }

    [Fact]
    public void SubmitFrame_SameResolutionTwice_OpensSenderOnlyOnce()
    {
        var device = new FakeNdiSenderDevice();
        var output = new NdiOutput("SWITCHER PGM1", device);

        output.Start();
        output.SubmitFrame(MakeFrame(4, 2));
        output.SubmitFrame(MakeFrame(4, 2));

        Assert.Single(device.OpenCalls);
        Assert.Equal(2, device.SendCalls.Count);
    }

    [Fact]
    public void SubmitFrame_ResolutionChanges_ReopensSender()
    {
        var device = new FakeNdiSenderDevice();
        var output = new NdiOutput("SWITCHER PGM1", device);

        output.Start();
        output.SubmitFrame(MakeFrame(4, 2));
        output.SubmitFrame(MakeFrame(8, 4));

        Assert.Equal([("SWITCHER PGM1", 4, 2), ("SWITCHER PGM1", 8, 4)], device.OpenCalls);
    }

    [Fact]
    public void SetSenderName_ReopensUnderNewNameOnNextFrame()
    {
        var device = new FakeNdiSenderDevice();
        var output = new NdiOutput("SWITCHER PGM1", device);

        output.Start();
        output.SubmitFrame(MakeFrame(4, 2));
        output.SetSenderName("CUSTOM NAME");
        output.SubmitFrame(MakeFrame(4, 2));

        Assert.Equal("CUSTOM NAME", output.SenderName);
        Assert.Equal([("SWITCHER PGM1", 4, 2), ("CUSTOM NAME", 4, 2)], device.OpenCalls);
    }

    [Fact]
    public void SetSenderName_EmptyOrWhitespace_Throws()
    {
        var output = new NdiOutput("SWITCHER PGM1", new FakeNdiSenderDevice());

        Assert.Throws<ArgumentException>(() => output.SetSenderName(""));
        Assert.Throws<ArgumentException>(() => output.SetSenderName("   "));
    }

    [Fact]
    public void SubmitFrame_SdkNotAvailable_IsNoOpAndPreservesState()
    {
        var device = new FakeNdiSenderDevice { IsAvailable = false };
        var output = new NdiOutput("SWITCHER PGM1", device);

        output.Start();
        output.SubmitFrame(MakeFrame(4, 2));
        output.SubmitFrame(MakeFrame(8, 4));

        Assert.Empty(device.OpenCalls);
        Assert.Empty(device.SendCalls);
        // State preserved: sender name is still tracked so a later SDK install would publish it.
        Assert.Equal("SWITCHER PGM1", output.SenderName);
    }

    [Fact]
    public void Stop_ClosesSenderAndIsIdempotent()
    {
        var device = new FakeNdiSenderDevice();
        var output = new NdiOutput("SWITCHER PGM1", device);

        output.Start();
        output.SubmitFrame(MakeFrame(4, 2));
        output.Stop();
        output.Stop();

        Assert.Equal(1, device.CloseCount);
    }

    [Fact]
    public void SubmitFrame_AfterStop_ThrowsUntilStartedAgainThenReopens()
    {
        var device = new FakeNdiSenderDevice();
        var output = new NdiOutput("SWITCHER PGM1", device);

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
        var device = new FakeNdiSenderDevice();
        var output = new NdiOutput("SWITCHER PGM1", device);

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
        var output = new NdiOutput("SWITCHER PGM1", new FakeNdiSenderDevice());
        output.Dispose();

        Assert.Throws<ObjectDisposedException>(() => output.Start());
        Assert.Throws<ObjectDisposedException>(() => output.SubmitFrame(MakeFrame(4, 2)));
    }
}
