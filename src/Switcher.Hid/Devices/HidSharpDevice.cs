using HidSharp;
using Switcher.Contracts;

namespace Switcher.Hid.Devices;

/// <summary>
/// Real <see cref="IHidDevice"/>: talks to the Pico 2W controller's vendor-defined USB-HID endpoint
/// via HidSharp (Windows/Linux/macOS HID I/O), mirroring
/// <see cref="Switcher.VirtualCam.Devices.SharedMemoryVirtualCameraDevice"/>'s "thin native adapter
/// behind a pure interface" shape. Defaults to the shared TinyUSB test VID/PID the firmware currently
/// enumerates as (firmware/pico2w-controller/src/usb_hid.c: <c>USB_VID</c>/<c>USB_PID</c> — replace
/// once a dedicated VID/PID is obtained).
/// </summary>
internal sealed class HidSharpDevice : IHidDevice
{
    private const int DefaultVendorId = 0xCafe;
    private const int DefaultProductId = 0x4011;

    private readonly int _vendorId;
    private readonly int _productId;
    private readonly string? _serialNumber;
    private readonly object _lock = new();

    private HidStream? _stream;
    private bool _disposed;

    public HidSharpDevice() : this(DefaultVendorId, DefaultProductId)
    {
    }

    public HidSharpDevice(int vendorId, int productId, string? serialNumber = null)
    {
        _vendorId = vendorId;
        _productId = productId;
        _serialNumber = serialNumber;
    }

    public void Open()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_stream is not null)
            {
                return;
            }

            var device = DeviceList.Local.GetHidDeviceOrNull(_vendorId, _productId, releaseNumberBcd: null, _serialNumber)
                ?? throw new InvalidOperationException(
                    $"No HID device found for VID=0x{_vendorId:X4} PID=0x{_productId:X4}" +
                    (_serialNumber is null ? "." : $" serial={_serialNumber}."));

            _stream = device.Open();
        }
    }

    public byte[]? ReadInputReport()
    {
        var stream = _stream ?? throw new InvalidOperationException("ReadInputReport was called before Open.");

        var buffer = new byte[stream.Device.GetMaxInputReportLength()];
        int read;
        try
        {
            read = stream.Read(buffer);
        }
        catch (IOException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }

        // First byte is the Report ID (ProtocolConstants.HidInputReportId) — this device only
        // enumerates one input report, so it is stripped unconditionally.
        return read <= 1 ? null : buffer[1..read];
    }

    public void WriteOutputReport(ReadOnlySpan<byte> reportBody)
    {
        var stream = _stream ?? throw new InvalidOperationException("WriteOutputReport was called before Open.");

        var buffer = new byte[reportBody.Length + 1];
        buffer[0] = ProtocolConstants.HidOutputReportId;
        reportBody.CopyTo(buffer.AsSpan(1));
        stream.Write(buffer);
    }

    public void Close()
    {
        lock (_lock)
        {
            _stream?.Dispose();
            _stream = null;
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

            _stream?.Dispose();
            _stream = null;
            _disposed = true;
        }
    }
}
