namespace Switcher.VirtualCam.Display;

/// <summary>
/// Abstraction over the OS mouse-cursor visibility toggle, so <see cref="HdmiFullscreenOutput"/>'s
/// <c>hide_cursor</c> policy (docs/specs/00-system-overview.md §4.2) can be unit tested without the
/// Windows-only <c>user32.dll</c> call. See <see cref="Win32CursorVisibility"/>.
/// </summary>
internal interface ICursorVisibility
{
    /// <summary>Hides the system cursor. Safe to call again while already hidden.</summary>
    void Hide();

    /// <summary>Shows the system cursor. Safe to call again while already shown.</summary>
    void Show();
}
