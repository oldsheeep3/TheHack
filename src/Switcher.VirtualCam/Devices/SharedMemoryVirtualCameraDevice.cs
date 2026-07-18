using System.IO.MemoryMappedFiles;
using Switcher.VirtualCam.FrameConversion;

namespace Switcher.VirtualCam.Devices;

/// <summary>
/// Real <see cref="IVirtualCameraDevice"/>: publishes NV12 frames to the companion native DirectShow
/// source filter over a named shared-memory ring slot (see README.md, §"Virtual camera device", for
/// the wire format, filter registration, and required admin rights). Windows-only; on any other OS,
/// <see cref="Open"/> throws <see cref="PlatformNotSupportedException"/> (the type still compiles and
/// links everywhere, matching <c>Switcher.Media.Compositing.DirectX11Compositor</c>'s approach to
/// Windows-only native dependencies).
/// </summary>
/// <remarks>
/// The shared-memory/event names are parameterized (rather than fixed constants) so that two
/// instances (VCAM1/VCAM2) can coexist as distinct devices without colliding on the same named
/// region; see <see cref="DefaultMemoryMappedFileName"/>/<see cref="DefaultFrameReadyEventName"/> for
/// the single-camera default that matches the native filter's original registration.
/// </remarks>
internal sealed class SharedMemoryVirtualCameraDevice : IVirtualCameraDevice
{
    internal const string DefaultMemoryMappedFileName = "Local\\HybridSwitcherVCamFrame";
    internal const string DefaultFrameReadyEventName = "Local\\HybridSwitcherVCamFrameReady";

    // Header layout written before the NV12 payload: width (int32) + height (int32).
    private const int HeaderSizeBytes = 8;

    private readonly string _memoryMappedFileName;
    private readonly string _frameReadyEventName;
    private readonly object _lock = new();

    private MemoryMappedFile? _memoryMappedFile;
    private MemoryMappedViewAccessor? _viewAccessor;
    private EventWaitHandle? _frameReadyEvent;
    private int _width;
    private int _height;
    private bool _disposed;

    public SharedMemoryVirtualCameraDevice()
        : this(DefaultMemoryMappedFileName, DefaultFrameReadyEventName)
    {
    }

    public SharedMemoryVirtualCameraDevice(string memoryMappedFileName, string frameReadyEventName)
    {
        ArgumentException.ThrowIfNullOrEmpty(memoryMappedFileName);
        ArgumentException.ThrowIfNullOrEmpty(frameReadyEventName);

        _memoryMappedFileName = memoryMappedFileName;
        _frameReadyEventName = frameReadyEventName;
    }

    public void Open(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_memoryMappedFile is not null && _width == width && _height == height)
            {
                return;
            }

            CloseLocked();

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "The virtual camera device requires Windows (shared-memory IPC with the native DirectShow filter); see README.md.");
            }

            var bufferSize = HeaderSizeBytes + Nv12FrameConverter.GetRequiredBufferSize(width, height);
            _memoryMappedFile = MemoryMappedFile.CreateOrOpen(_memoryMappedFileName, bufferSize);
            _viewAccessor = _memoryMappedFile.CreateViewAccessor();
            _frameReadyEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _frameReadyEventName);
            _width = width;
            _height = height;
        }
    }

    public unsafe void PushFrame(ReadOnlySpan<byte> nv12Frame)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_viewAccessor is null)
            {
                throw new InvalidOperationException("PushFrame was called before Open.");
            }

            _viewAccessor.Write(0, _width);
            _viewAccessor.Write(4, _height);

            byte* basePointer = null;
            _viewAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePointer);
            try
            {
                var destination = new Span<byte>(basePointer + HeaderSizeBytes, nv12Frame.Length);
                nv12Frame.CopyTo(destination);
            }
            finally
            {
                _viewAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }

            _frameReadyEvent!.Set();
        }
    }

    public void Close()
    {
        lock (_lock)
        {
            CloseLocked();
        }
    }

    private void CloseLocked()
    {
        _frameReadyEvent?.Dispose();
        _frameReadyEvent = null;
        _viewAccessor?.Dispose();
        _viewAccessor = null;
        _memoryMappedFile?.Dispose();
        _memoryMappedFile = null;
        _width = 0;
        _height = 0;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            CloseLocked();
            _disposed = true;
        }
    }
}
