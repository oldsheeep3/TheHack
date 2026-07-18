using System.Reflection;
using System.Runtime.InteropServices;
using Switcher.Contracts;

namespace Switcher.VirtualCam.Ndi;

/// <summary>
/// Real <see cref="INdiSenderDevice"/>: publishes composited BGRA32 frames on the network via the NDI
/// SDK's <c>NDIlib_send_*</c> C API (P/Invoke). Like <see cref="Devices.SharedMemoryVirtualCameraDevice"/>
/// and <see cref="Display.Direct3DSwapChainOutput"/>, this type still compiles and links on any OS; it
/// only touches the native runtime when the NDI SDK is actually present. The SDK runtime is optional,
/// so <see cref="IsAvailable"/> probes for it once (via <see cref="NativeLibrary.TryLoad(string, out nint)"/>
/// across the platform's candidate library names) and, when it is missing, every operation is a no-op —
/// callers (<see cref="NdiOutput"/>) disable send-out gracefully rather than failing (see README.md,
/// §"NDI output"). Windows/Linux/macOS NDI runtimes are all supported; the SDK is a separate download.
/// </summary>
internal sealed partial class NdiSdkSenderDevice : INdiSenderDevice
{
    // The DllImport name below is virtual: the DllImportResolver installed in the static constructor
    // maps it to whichever platform-specific NDI runtime actually loaded (Processing.NDI.Lib.x64.dll on
    // Windows, libndi.so.* on Linux, libndi.dylib on macOS).
    private const string NdiLibrary = "Processing.NDI.Lib";

    // MAKE_FOURCC('B','G','R','A') — NDIlib_FourCC_video_type_BGRA (matches the compositor's BGRA32).
    private const int FourCcBgra = 'B' | ('G' << 8) | ('R' << 16) | ('A' << 24);

    // NDIlib_frame_format_type_progressive.
    private const int FrameFormatProgressive = 1;

    // NDIlib_send_timecode_synthesize — let the SDK synthesize timecodes from the send cadence.
    private const long SendTimecodeSynthesize = long.MaxValue;

    private const int BytesPerPixel = 4;

    private static readonly bool s_available = ProbeAvailability();
    private static readonly object s_initLock = new();
    private static bool s_initialized;

    private readonly object _lock = new();

    private nint _sender;
    private nint _namePtr;
    private int _width;
    private int _height;
    private bool _disposed;

    static NdiSdkSenderDevice() =>
        NativeLibrary.SetDllImportResolver(typeof(NdiSdkSenderDevice).Assembly, ResolveNdiLibrary);

    public bool IsAvailable => s_available;

    public void Open(string senderName, int width, int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senderName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // SDK absent: nothing to open. NdiOutput only reaches here when IsAvailable is true, but
            // guard anyway so a direct caller cannot trip a P/Invoke into a missing runtime.
            if (!s_available || !EnsureInitialized())
            {
                return;
            }

            CloseLocked();

            _namePtr = Marshal.StringToHGlobalAnsi(senderName);
            var settings = new NdiSendCreate
            {
                NdiName = _namePtr,
                Groups = 0,
                ClockVideo = 1,
                ClockAudio = 0,
            };

            unsafe
            {
                _sender = NDIlib_send_create(&settings);
            }

            if (_sender == 0)
            {
                // Creation failed (e.g. duplicate name): release the name buffer and stay closed.
                CloseLocked();
                return;
            }

            _width = width;
            _height = height;
        }
    }

    public void Send(FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sender == 0)
            {
                return;
            }

            unsafe
            {
                fixed (byte* pixels = frame.Pixels.Span)
                {
                    var video = new NdiVideoFrame
                    {
                        XRes = frame.Width,
                        YRes = frame.Height,
                        FourCC = FourCcBgra,
                        FrameRateN = 60,
                        FrameRateD = 1,
                        PictureAspectRatio = 0f,
                        FrameFormatType = FrameFormatProgressive,
                        Timecode = SendTimecodeSynthesize,
                        Data = (nint)pixels,
                        LineStrideInBytes = frame.Width * BytesPerPixel,
                        Metadata = 0,
                        Timestamp = 0,
                    };

                    NDIlib_send_send_video_v2(_sender, &video);
                }
            }
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
        if (_sender != 0)
        {
            NDIlib_send_destroy(_sender);
            _sender = 0;
        }

        if (_namePtr != 0)
        {
            Marshal.FreeHGlobal(_namePtr);
            _namePtr = 0;
        }

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

    private static bool EnsureInitialized()
    {
        if (s_initialized)
        {
            return true;
        }

        lock (s_initLock)
        {
            if (!s_initialized)
            {
                // NDIlib_initialize is process-global and idempotent; the SDK cleans up at process exit.
                s_initialized = NDIlib_initialize();
            }

            return s_initialized;
        }
    }

    private static bool ProbeAvailability()
    {
        foreach (var candidate in CandidateLibraryNames())
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                NativeLibrary.Free(handle);
                return true;
            }
        }

        return false;
    }

    private static nint ResolveNdiLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, NdiLibrary, StringComparison.Ordinal))
        {
            return 0;
        }

        foreach (var candidate in CandidateLibraryNames())
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return 0;
    }

    private static string[] CandidateLibraryNames()
    {
        if (OperatingSystem.IsWindows())
        {
            return ["Processing.NDI.Lib.x64.dll", "Processing.NDI.Lib.x86.dll"];
        }

        if (OperatingSystem.IsMacOS())
        {
            return ["libndi.dylib", "libndi.4.dylib"];
        }

        return ["libndi.so", "libndi.so.6", "libndi.so.5", "libndi.so.4"];
    }

    [LibraryImport(NdiLibrary)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool NDIlib_initialize();

    [LibraryImport(NdiLibrary)]
    private static unsafe partial nint NDIlib_send_create(NdiSendCreate* settings);

    [LibraryImport(NdiLibrary)]
    private static unsafe partial void NDIlib_send_send_video_v2(nint sender, NdiVideoFrame* video);

    [LibraryImport(NdiLibrary)]
    private static partial void NDIlib_send_destroy(nint sender);

    // Mirrors NDIlib_send_create_t (bools stored as bytes to keep the struct blittable for LibraryImport).
    [StructLayout(LayoutKind.Sequential)]
    private struct NdiSendCreate
    {
        public nint NdiName;
        public nint Groups;
        public byte ClockVideo;
        public byte ClockAudio;
    }

    // Mirrors NDIlib_video_frame_v2_t; sequential layout matches the C struct's natural alignment on
    // 64-bit targets (the SDK runtime is 64-bit).
    [StructLayout(LayoutKind.Sequential)]
    private struct NdiVideoFrame
    {
        public int XRes;
        public int YRes;
        public int FourCC;
        public int FrameRateN;
        public int FrameRateD;
        public float PictureAspectRatio;
        public int FrameFormatType;
        public long Timecode;
        public nint Data;
        public int LineStrideInBytes;
        public nint Metadata;
        public long Timestamp;
    }
}
