using Switcher.Contracts;
using Switcher.VirtualCam.Display;

namespace Switcher.VirtualCam.Tests.Display;

/// <summary>
/// Verifies that the multiview full-screen path (docs/specs/multiview-output-revision.md §2.4) reuses
/// the HDMI presentation stack via <see cref="FullscreenPresenterFactory"/>: a presenter obtained from
/// the factory reflects cursor-hide / display_id / fullscreen through the same
/// <see cref="ISwapChainOutput"/> + <see cref="ICursorVisibility"/> seams as the HDMI sink, with no
/// duplicate presentation implementation.
/// </summary>
public sealed class MultiviewFullscreenPresenterTests
{
    private static FrameData MakeFrame(int width, int height, byte fill = 0x80) =>
        new(width, height, new byte[width * height * 4].Select(_ => fill).ToArray());

    private static (IFullscreenPresenterFactory Factory, FakeSwapChainOutput SwapChain, FakeCursorVisibility Cursor) MakeFactory()
    {
        var swapChain = new FakeSwapChainOutput();
        var cursor = new FakeCursorVisibility();
        var factory = new FullscreenPresenterFactory(() => new HdmiFullscreenOutput(swapChain, cursor));
        return (factory, swapChain, cursor);
    }

    [Fact]
    public void Create_ReturnsPresenterOfSameHdmiPresentationType()
    {
        var factory = new FullscreenPresenterFactory();

        using var presenter = factory.Create();

        Assert.IsType<HdmiFullscreenOutput>(presenter);
    }

    [Fact]
    public void Presenter_Attach_ReflectsDisplayIdHideCursorAndFullscreen()
    {
        var (factory, swapChain, cursor) = MakeFactory();
        var presenter = factory.Create();

        presenter.Attach(displayId: 3, windowHandle: 0x2222, hideCursor: true, fullscreen: true);

        var attach = Assert.Single(swapChain.AttachCalls);
        Assert.Equal((3, (nint)0x2222), attach);
        Assert.Equal(3, presenter.DisplayId);
        Assert.True(presenter.HideCursor);
        Assert.True(presenter.Fullscreen);
        Assert.True(presenter.IsAttached);
        Assert.Equal(1, cursor.HideCount);
    }

    [Fact]
    public void Presenter_Present_ForwardsMultiviewFrameToSwapChain()
    {
        var (factory, swapChain, _) = MakeFactory();
        var presenter = factory.Create();
        presenter.Attach(displayId: 1, windowHandle: 1, hideCursor: false, fullscreen: true);

        var frame = MakeFrame(8, 4);
        presenter.Present(frame);

        Assert.Same(frame, Assert.Single(swapChain.PresentCalls));
    }

    [Fact]
    public void Presenter_Detach_RestoresCursor()
    {
        var (factory, _, cursor) = MakeFactory();
        var presenter = factory.Create();
        presenter.Attach(displayId: 0, windowHandle: 1, hideCursor: true, fullscreen: true);

        presenter.Detach();

        Assert.False(presenter.IsAttached);
        Assert.Equal(1, cursor.ShowCount);
    }

    [Fact]
    public void Factory_CreatesIndependentPresenterInstances()
    {
        var factory = new FullscreenPresenterFactory(() => new HdmiFullscreenOutput(new FakeSwapChainOutput(), new FakeCursorVisibility()));

        using var first = factory.Create();
        using var second = factory.Create();

        Assert.NotSame(first, second);
    }
}
