namespace Switcher.VirtualCam.Display;

/// <summary>
/// Default <see cref="IFullscreenPresenterFactory"/>: hands out fresh <see cref="HdmiFullscreenOutput"/>
/// instances (each over its own <see cref="Direct3DSwapChainOutput"/> + <see cref="Win32CursorVisibility"/>).
/// This is the single reuse point that lets both the HDMI output sink and the multiview full-screen
/// window (docs/specs/multiview-output-revision.md §2.4) share one presentation implementation instead
/// of duplicating swap-chain/cursor code.
/// </summary>
public sealed class FullscreenPresenterFactory : IFullscreenPresenterFactory
{
    private readonly Func<IHdmiFullscreenOutput> _create;

    public FullscreenPresenterFactory() => _create = static () => new HdmiFullscreenOutput();

    // Test seam: lets a test inject a presenter built over fake ISwapChainOutput/ICursorVisibility so
    // the multiview full-screen path can be verified (cursor hide/display_id/fullscreen) OS-independently.
    internal FullscreenPresenterFactory(Func<IHdmiFullscreenOutput> create) => _create = create;

    public IHdmiFullscreenOutput Create() => _create();
}
