using Switcher.Contracts;
using Switcher.Hid.Devices;
using Switcher.Hid.Reports;

namespace Switcher.Hid;

/// <summary>
/// Sends computed backlight colors (see <see cref="Switcher.Hid.Backlight.BacklightCalculator"/>) to
/// the Pico 2W as HID output reports (docs/specs/00-system-overview.md §4.1/§4.5), one report per
/// module. Purely an I/O sink: the App layer owns tracking PGM/PVW state and module mapping and
/// decides when to recompute and call <see cref="Send"/>.
/// </summary>
public sealed class HidBacklightService : IDisposable
{
    private readonly IHidDevice _device;
    private readonly object _lock = new();

    private bool _started;
    private bool _disposed;

    public HidBacklightService() : this(new HidSharpDevice())
    {
    }

    internal HidBacklightService(IHidDevice device)
    {
        _device = device;
    }

    /// <summary>Opens the device. Safe to call again while already started (no-op).</summary>
    public void Start()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_started)
            {
                return;
            }

            _device.Open();
            _started = true;
        }
    }

    /// <summary>Serializes and writes one output report per entry.</summary>
    public void Send(IReadOnlyList<HidOutputReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_started)
            {
                throw new InvalidOperationException("Send was called before Start().");
            }

            foreach (var report in reports)
            {
                _device.WriteOutputReport(HidReportParser.SerializeOutput(report));
            }
        }
    }

    /// <summary>Closes the device. Safe to call again, or when never started (no-op).</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_started)
            {
                return;
            }

            _device.Close();
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
