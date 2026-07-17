using Switcher.Contracts;

namespace Switcher.VirtualCam.Display;

/// <summary>
/// Thin output surface for showing the composited PGM full-screen on a physical display (HDMI /
/// Type-C, docs/specs/pc-switcher-app.md §2.3). App integration owns the native window and decides
/// which display a given instance is assigned to; this project only provides the presentation
/// surface, so it stays decoupled from window/monitor enumeration concerns.
/// </summary>
public interface ISwapChainOutput : IDisposable
{
    /// <summary>Binds the output to <paramref name="windowHandle"/> (an HWND) and goes full-screen on
    /// the monitor identified by <paramref name="displayIndex"/> (its <c>IDXGIOutput</c> adapter-output
    /// index). Safe to call again to move to a different display.</summary>
    void AttachToDisplay(int displayIndex, nint windowHandle);

    /// <summary>Presents one composited frame to the attached display. Throws if not attached.</summary>
    void Present(FrameData frame);

    /// <summary>Leaves full-screen and releases the swap chain. Safe to call multiple times, and when
    /// never attached.</summary>
    void Detach();
}
