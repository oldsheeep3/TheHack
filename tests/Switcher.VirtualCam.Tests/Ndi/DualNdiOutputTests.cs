using Switcher.Contracts;
using Switcher.VirtualCam.Ndi;

namespace Switcher.VirtualCam.Tests.Ndi;

public sealed class DualNdiOutputTests
{
    private static FrameData MakeFrame(int width, int height, byte fill = 0x80) =>
        new(width, height, new byte[width * height * 4].Select(_ => fill).ToArray());

    private static (DualNdiOutput Dual, FakeNdiSenderDevice Device1, FakeNdiSenderDevice Device2) MakeDual()
    {
        var device1 = new FakeNdiSenderDevice();
        var device2 = new FakeNdiSenderDevice();
        var dual = new DualNdiOutput(
            new NdiOutput(DualNdiOutput.DefaultNdi1SenderName, device1),
            new NdiOutput(DualNdiOutput.DefaultNdi2SenderName, device2));
        return (dual, device1, device2);
    }

    [Fact]
    public void SubmitFrame_UsesDefaultSenderNamesPerSink()
    {
        var (dual, device1, device2) = MakeDual();
        dual.Start();

        dual.SubmitFrame(OutputSink.Ndi1, MakeFrame(4, 2));
        dual.SubmitFrame(OutputSink.Ndi2, MakeFrame(4, 2));

        Assert.Equal("SWITCHER PGM1", Assert.Single(device1.OpenCalls).Name);
        Assert.Equal("SWITCHER PGM2", Assert.Single(device2.OpenCalls).Name);
    }

    [Fact]
    public void SubmitFrame_RoutesToTheMatchingSenderOnly()
    {
        var (dual, device1, device2) = MakeDual();
        dual.Start();

        dual.SubmitFrame(OutputSink.Ndi1, MakeFrame(4, 2));

        Assert.Single(device1.SendCalls);
        Assert.Empty(device2.SendCalls);
        Assert.Empty(device2.OpenCalls);
    }

    [Fact]
    public void SetSenderName_ChangesOnlyTheTargetSink()
    {
        var (dual, device1, device2) = MakeDual();
        dual.Start();

        dual.SetSenderName(OutputSink.Ndi1, "CUSTOM PGM1");
        dual.SubmitFrame(OutputSink.Ndi1, MakeFrame(4, 2));
        dual.SubmitFrame(OutputSink.Ndi2, MakeFrame(4, 2));

        Assert.Equal("CUSTOM PGM1", Assert.Single(device1.OpenCalls).Name);
        Assert.Equal("SWITCHER PGM2", Assert.Single(device2.OpenCalls).Name);
    }

    [Fact]
    public void SubmitFrame_UnknownSink_Throws()
    {
        var (dual, _, _) = MakeDual();
        dual.Start();

        Assert.Throws<ArgumentOutOfRangeException>(() => dual.SubmitFrame(OutputSink.Hdmi, MakeFrame(4, 2)));
    }

    [Fact]
    public void SubmitFrame_OneSenderFails_OtherSenderUnaffected()
    {
        var (dual, device1, device2) = MakeDual();
        device1.ThrowOnOpen = true;
        dual.Start();

        Assert.Throws<InvalidOperationException>(() => dual.SubmitFrame(OutputSink.Ndi1, MakeFrame(4, 2)));
        dual.SubmitFrame(OutputSink.Ndi2, MakeFrame(4, 2));

        Assert.Single(device2.OpenCalls);
        Assert.Single(device2.SendCalls);
    }

    [Fact]
    public void SubmitFrame_OneSenderSdkAbsent_OtherSenderStillSends()
    {
        var (dual, device1, device2) = MakeDual();
        device1.IsAvailable = false;
        dual.Start();

        dual.SubmitFrame(OutputSink.Ndi1, MakeFrame(4, 2));
        dual.SubmitFrame(OutputSink.Ndi2, MakeFrame(4, 2));

        Assert.Empty(device1.SendCalls);
        Assert.Single(device2.SendCalls);
    }

    [Fact]
    public void Stop_StopsBothSendersAndIsIdempotent()
    {
        var (dual, device1, device2) = MakeDual();
        dual.Start();
        dual.SubmitFrame(OutputSink.Ndi1, MakeFrame(4, 2));
        dual.SubmitFrame(OutputSink.Ndi2, MakeFrame(4, 2));

        dual.Stop();
        dual.Stop();

        Assert.Equal(1, device1.CloseCount);
        Assert.Equal(1, device2.CloseCount);
    }

    [Fact]
    public void Dispose_DisposesBothSenders()
    {
        var (dual, device1, device2) = MakeDual();
        dual.Start();

        dual.Dispose();

        Assert.Equal(1, device1.DisposeCount);
        Assert.Equal(1, device2.DisposeCount);
    }
}
