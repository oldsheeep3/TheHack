using Switcher.Contracts;

namespace Switcher.VirtualCam.Ndi;

/// <summary>
/// Implements <see cref="INdiOutput"/>: hands each composited <see cref="FrameData"/> (BGRA32, the same
/// format the compositor renders) to the native NDI Sender (see <see cref="INdiSenderDevice"/> and
/// README.md). Unlike <see cref="VirtualCameraOutput"/>, no NV12 conversion happens here — NDI sends
/// BGRA directly, so this is a separate, copy-free path from the VCAM sink. Mirroring
/// <see cref="VirtualCameraOutput"/>'s fault-tolerant pattern, the sender is opened lazily on the first
/// frame after <see cref="Start"/> (once the resolution is known) and re-opened if the resolution or
/// sender name changes mid-stream. When the NDI SDK is not detected (<see cref="INdiSenderDevice.IsAvailable"/>
/// is <c>false</c>), <see cref="SubmitFrame"/> is a no-op that preserves state, so a missing SDK never
/// throws into <see cref="OutputRouter"/>'s fan-out and never disrupts the other sinks.
/// </summary>
public sealed class NdiOutput : INdiOutput, IDisposable
{
    private readonly INdiSenderDevice _device;
    private readonly object _lock = new();

    private string _senderName;
    private string? _openSenderName;
    private int _openWidth;
    private int _openHeight;
    private bool _open;
    private bool _started;
    private bool _disposed;

    public NdiOutput(string senderName) : this(senderName, new NdiSdkSenderDevice())
    {
    }

    internal NdiOutput(string senderName, INdiSenderDevice device)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senderName);
        _senderName = senderName;
        _device = device;
    }

    public string SenderName
    {
        get
        {
            lock (_lock)
            {
                return _senderName;
            }
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _started = true;
        }
    }

    public void SetSenderName(string senderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senderName);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _senderName = senderName;
            // The sender is re-opened under the new name on the next frame (see SubmitFrame's
            // _openSenderName comparison), matching the lazy-open/re-open resilience pattern.
        }
    }

    public void SubmitFrame(FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_started)
            {
                throw new InvalidOperationException("SubmitFrame was called before Start().");
            }

            // NDI SDK not installed: disable send-out gracefully (no-op) and keep state intact, so a
            // missing SDK never surfaces as a failure to the router's per-sink fan-out.
            if (!_device.IsAvailable)
            {
                return;
            }

            if (!_open
                || frame.Width != _openWidth
                || frame.Height != _openHeight
                || !string.Equals(_senderName, _openSenderName, StringComparison.Ordinal))
            {
                _device.Open(_senderName, frame.Width, frame.Height);
                _open = true;
                _openSenderName = _senderName;
                _openWidth = frame.Width;
                _openHeight = frame.Height;
            }

            _device.Send(frame);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_started)
            {
                return;
            }

            _device.Close();
            _open = false;
            _openSenderName = null;
            _openWidth = 0;
            _openHeight = 0;
            _started = false;
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

            _device.Close();
            _device.Dispose();
            _disposed = true;
        }
    }
}
