using Switcher.Contracts;

namespace Switcher.VirtualCam.Display;

/// <summary>
/// Default <see cref="IHdmiFullscreenOutput"/>: delegates presentation to an <see cref="ISwapChainOutput"/>
/// (<see cref="Direct3DSwapChainOutput"/> by default) and cursor visibility to an
/// <see cref="ICursorVisibility"/> (<see cref="Win32CursorVisibility"/> by default). Cursor hide/show
/// is applied on <see cref="Attach"/>/<see cref="Detach"/> so the policy tracks the attached state.
/// </summary>
public sealed class HdmiFullscreenOutput : IHdmiFullscreenOutput
{
    private readonly ISwapChainOutput _swapChain;
    private readonly ICursorVisibility _cursor;
    private readonly object _lock = new();

    private bool _disposed;

    public HdmiFullscreenOutput() : this(new Direct3DSwapChainOutput(), new Win32CursorVisibility())
    {
    }

    internal HdmiFullscreenOutput(ISwapChainOutput swapChain, ICursorVisibility cursor)
    {
        _swapChain = swapChain;
        _cursor = cursor;
    }

    public int? DisplayId { get; private set; }

    public bool HideCursor { get; private set; }

    public bool Fullscreen { get; private set; }

    public bool IsAttached { get; private set; }

    public void Attach(int displayId, nint windowHandle, bool hideCursor, bool fullscreen)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _swapChain.AttachToDisplay(displayId, windowHandle);

            DisplayId = displayId;
            HideCursor = hideCursor;
            Fullscreen = fullscreen;
            IsAttached = true;

            ApplyCursorPolicyLocked();
        }
    }

    public void Present(FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsAttached)
            {
                throw new InvalidOperationException("Present was called before Attach.");
            }

            _swapChain.Present(frame);
        }
    }

    public void Detach()
    {
        lock (_lock)
        {
            if (!IsAttached)
            {
                return;
            }

            _swapChain.Detach();
            _cursor.Show();
            DisplayId = null;
            IsAttached = false;
        }
    }

    private void ApplyCursorPolicyLocked()
    {
        if (HideCursor)
        {
            _cursor.Hide();
        }
        else
        {
            _cursor.Show();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (IsAttached)
            {
                _swapChain.Detach();
                _cursor.Show();
                IsAttached = false;
            }

            _swapChain.Dispose();
            _disposed = true;
        }
    }
}
