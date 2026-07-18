using System.Runtime.InteropServices;

namespace Switcher.VirtualCam.Display;

/// <summary>
/// Real <see cref="ICursorVisibility"/>: wraps the Win32 <c>ShowCursor</c> display-counter API (each
/// call increments/decrements a per-thread show count; the cursor is only actually visible once the
/// count is non-negative), looping until the counter reaches the target sign so repeated Hide/Show
/// calls are idempotent regardless of the counter's starting value. Windows-only; see README.md.
/// </summary>
internal sealed partial class Win32CursorVisibility : ICursorVisibility
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool bShow);

    public void Hide()
    {
        RequirePlatform();
        while (ShowCursor(false) >= 0)
        {
        }
    }

    public void Show()
    {
        RequirePlatform();
        while (ShowCursor(true) < 0)
        {
        }
    }

    private static void RequirePlatform()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Cursor visibility control requires Windows (user32.dll ShowCursor); see README.md.");
        }
    }
}
