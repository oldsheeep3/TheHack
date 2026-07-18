using Switcher.Contracts;
using Switcher.VirtualCam.Display;

namespace Switcher.VirtualCam.Tests.Display;

public sealed class HdmiFullscreenOutputTests
{
    private static FrameData MakeFrame(int width, int height, byte fill = 0x80) =>
        new(width, height, new byte[width * height * 4].Select(_ => fill).ToArray());

    private static (HdmiFullscreenOutput Output, FakeSwapChainOutput SwapChain, FakeCursorVisibility Cursor) MakeOutput()
    {
        var swapChain = new FakeSwapChainOutput();
        var cursor = new FakeCursorVisibility();
        var output = new HdmiFullscreenOutput(swapChain, cursor);
        return (output, swapChain, cursor);
    }

    [Fact]
    public void Attach_ForwardsToSwapChainAndRecordsAssignment()
    {
        var (output, swapChain, _) = MakeOutput();

        output.Attach(displayId: 1, windowHandle: 0x1234, hideCursor: true, fullscreen: true);

        var call = Assert.Single(swapChain.AttachCalls);
        Assert.Equal((1, (nint)0x1234), call);
        Assert.Equal(1, output.DisplayId);
        Assert.True(output.IsAttached);
    }

    [Fact]
    public void Attach_HideCursorTrue_HidesCursor()
    {
        var (output, _, cursor) = MakeOutput();

        output.Attach(displayId: 0, windowHandle: 1, hideCursor: true, fullscreen: true);

        Assert.True(output.HideCursor);
        Assert.Equal(1, cursor.HideCount);
        Assert.Equal(0, cursor.ShowCount);
    }

    [Fact]
    public void Attach_HideCursorFalse_ShowsCursor()
    {
        var (output, _, cursor) = MakeOutput();

        output.Attach(displayId: 0, windowHandle: 1, hideCursor: false, fullscreen: true);

        Assert.False(output.HideCursor);
        Assert.Equal(0, cursor.HideCount);
        Assert.Equal(1, cursor.ShowCount);
    }

    [Fact]
    public void Attach_RecordsFullscreenFlag()
    {
        var (output, _, _) = MakeOutput();

        output.Attach(displayId: 0, windowHandle: 1, hideCursor: false, fullscreen: false);

        Assert.False(output.Fullscreen);
    }

    [Fact]
    public void Present_BeforeAttach_Throws()
    {
        var (output, _, _) = MakeOutput();

        Assert.Throws<InvalidOperationException>(() => output.Present(MakeFrame(4, 2)));
    }

    [Fact]
    public void Present_AfterAttach_ForwardsToSwapChain()
    {
        var (output, swapChain, _) = MakeOutput();
        output.Attach(displayId: 0, windowHandle: 1, hideCursor: false, fullscreen: true);

        var frame = MakeFrame(4, 2);
        output.Present(frame);

        Assert.Same(frame, Assert.Single(swapChain.PresentCalls));
    }

    [Fact]
    public void Detach_RestoresCursorAndClearsAttachedState()
    {
        var (output, swapChain, cursor) = MakeOutput();
        output.Attach(displayId: 2, windowHandle: 1, hideCursor: true, fullscreen: true);

        output.Detach();

        Assert.Equal(1, swapChain.DetachCount);
        Assert.Equal(1, cursor.ShowCount);
        Assert.False(output.IsAttached);
        Assert.Null(output.DisplayId);
    }

    [Fact]
    public void Detach_WithoutAttach_DoesNotThrowOrTouchSwapChain()
    {
        var (output, swapChain, _) = MakeOutput();

        output.Detach();

        Assert.Equal(0, swapChain.DetachCount);
    }

    [Fact]
    public void Dispose_DetachesAndDisposesSwapChainAndIsIdempotent()
    {
        var (output, swapChain, cursor) = MakeOutput();
        output.Attach(displayId: 0, windowHandle: 1, hideCursor: true, fullscreen: true);

        output.Dispose();
        output.Dispose();

        Assert.Equal(1, swapChain.DetachCount);
        Assert.Equal(1, swapChain.DisposeCount);
        Assert.Equal(1, cursor.ShowCount);
    }

    [Fact]
    public void MethodsAfterDispose_ThrowObjectDisposedException()
    {
        var (output, _, _) = MakeOutput();
        output.Dispose();

        Assert.Throws<ObjectDisposedException>(() => output.Attach(0, 1, false, true));
        Assert.Throws<ObjectDisposedException>(() => output.Present(MakeFrame(4, 2)));
    }
}
