using Switcher.Contracts;

namespace Switcher.VirtualCam.Display;

/// <summary>
/// Full-screen physical-display (HDMI/Type-C) output sink (<see cref="OutputSink.Hdmi"/>,
/// docs/specs/00-system-overview.md §4.2 / docs/specs/pc-switcher-app.md §2.3): presents composited
/// frames via an <see cref="ISwapChainOutput"/> and applies the <c>hide_cursor</c>/<c>fullscreen</c>
/// policy. App integration (agent-A2-006) owns the native window (HWND) and its placement on the
/// target monitor; this type owns presentation and the cursor-visibility policy only. See
/// <see cref="HdmiFullscreenOutput"/>.
/// </summary>
public interface IHdmiFullscreenOutput : IDisposable
{
    /// <summary>The display currently attached to, or <c>null</c> if not attached.</summary>
    int? DisplayId { get; }

    /// <summary>Whether the system cursor is hidden while attached.</summary>
    bool HideCursor { get; }

    /// <summary>Whether the current assignment requested full-screen presentation.</summary>
    bool Fullscreen { get; }

    /// <summary>Whether <see cref="Attach"/> has been called without a matching <see cref="Detach"/>.</summary>
    bool IsAttached { get; }

    /// <summary>Attaches to <paramref name="displayId"/> via <paramref name="windowHandle"/> (an HWND
    /// owned by App integration) and applies the cursor/fullscreen policy. Safe to call again to move
    /// to a different display or change policy.</summary>
    void Attach(int displayId, nint windowHandle, bool hideCursor, bool fullscreen);

    /// <summary>Presents one composited frame. Throws if not attached.</summary>
    void Present(FrameData frame);

    /// <summary>Detaches from the display, restores the cursor, and releases the swap chain. Safe to
    /// call multiple times, and when never attached.</summary>
    void Detach();
}
