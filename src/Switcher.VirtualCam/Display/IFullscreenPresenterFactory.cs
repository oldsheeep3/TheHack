namespace Switcher.VirtualCam.Display;

/// <summary>
/// Creates independent full-screen presenters (<see cref="IHdmiFullscreenOutput"/>) that App
/// integration attaches to its own windows/monitors. The multiview full-screen window
/// (docs/specs/multiview-output-revision.md §2.4, App agent-A3-005's <c>MultiviewFullscreenWindow</c>)
/// obtains a presenter from this factory so it <b>reuses the exact same presentation stack as the HDMI
/// sink</b> — <see cref="HdmiFullscreenOutput"/> over <see cref="ISwapChainOutput"/> +
/// <see cref="ICursorVisibility"/> — rather than duplicating swap-chain/cursor logic.
/// </summary>
/// <remarks>
/// Responsibility boundary: this factory (and the presenter it returns) owns <b>presentation and the
/// cursor-visibility policy only</b>. Creating the operator window, obtaining its HWND, and placing it
/// on the target display are App integration's job (agent-A3-005); the App passes that HWND to
/// <see cref="IHdmiFullscreenOutput.Attach"/>. Multiview full-screen is a <b>separate system</b> from
/// <see cref="OutputRouter"/>'s sink assignments — the multiview composite is presented directly through
/// a presenter from this factory, not routed as a VCAM/HDMI/NDI sink.
/// </remarks>
public interface IFullscreenPresenterFactory
{
    /// <summary>Creates a new, unattached full-screen presenter instance. Each call returns an
    /// independent presenter (e.g. one for the HDMI sink and a separate one for the multiview
    /// full-screen window), so they can target different displays without interfering.</summary>
    IHdmiFullscreenOutput Create();
}
