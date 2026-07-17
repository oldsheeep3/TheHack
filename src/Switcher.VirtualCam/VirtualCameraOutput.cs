using Switcher.Contracts;
using Switcher.VirtualCam.Devices;
using Switcher.VirtualCam.FrameConversion;

namespace Switcher.VirtualCam;

/// <summary>
/// Implements <see cref="IVirtualCameraOutput"/>: converts each composited <see cref="FrameData"/>
/// (BGRA32) to NV12 (see <see cref="Nv12FrameConverter"/>) and hands it to the native virtual camera
/// device (see <see cref="Devices.IVirtualCameraDevice"/> and README.md). The device is opened lazily
/// on the first frame after <see cref="Start"/>, once the frame resolution is known, and re-opened if
/// the resolution changes mid-stream.
/// </summary>
public sealed class VirtualCameraOutput : IVirtualCameraOutput, IDisposable
{
    private readonly IVirtualCameraDevice _device;
    private readonly object _lock = new();

    private byte[]? _nv12Buffer;
    private int _openWidth;
    private int _openHeight;
    private bool _started;
    private bool _disposed;

    public VirtualCameraOutput() : this(new SharedMemoryVirtualCameraDevice())
    {
    }

    internal VirtualCameraOutput(IVirtualCameraDevice device)
    {
        _device = device;
    }

    /// <summary>Marks the output as active. Safe to call again while already started (no-op).</summary>
    public void Start()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _started = true;
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

            if (_nv12Buffer is null || frame.Width != _openWidth || frame.Height != _openHeight)
            {
                _device.Open(frame.Width, frame.Height);
                _nv12Buffer = new byte[Nv12FrameConverter.GetRequiredBufferSize(frame.Width, frame.Height)];
                _openWidth = frame.Width;
                _openHeight = frame.Height;
            }

            Nv12FrameConverter.ConvertBgraToNv12(frame, _nv12Buffer);
            _device.PushFrame(_nv12Buffer);
        }
    }

    /// <summary>Stops the output and releases the device. Safe to call again, or when never started
    /// (no-op).</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_started)
            {
                return;
            }

            _device.Close();
            _nv12Buffer = null;
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
