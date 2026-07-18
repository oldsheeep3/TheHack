using System.Collections.Concurrent;
using Switcher.Hid.Devices;

namespace Switcher.Hid.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IHidDevice"/> for testing <see cref="HidInputService"/>/
/// <see cref="HidBacklightService"/> without real HID hardware. Input report bodies are fed via
/// <see cref="EnqueueInput"/>; <see cref="ReadInputReport"/> blocks until one is available (mirroring
/// a real blocking HID read), and <see cref="Close"/> unblocks any pending read by returning null.
/// </summary>
internal sealed class FakeHidDevice : IHidDevice
{
    private readonly BlockingCollection<byte[]?> _pendingReads = [];

    public List<byte[]> WrittenReports { get; } = [];
    public int OpenCount { get; private set; }
    public int CloseCount { get; private set; }
    public int DisposeCount { get; private set; }

    public void EnqueueInput(byte[] reportBody) => _pendingReads.Add(reportBody);

    public void Open() => OpenCount++;

    public byte[]? ReadInputReport() => _pendingReads.Take();

    public void WriteOutputReport(ReadOnlySpan<byte> reportBody)
    {
        lock (WrittenReports)
        {
            WrittenReports.Add(reportBody.ToArray());
        }
    }

    public void Close()
    {
        CloseCount++;
        _pendingReads.Add(null);
    }

    public void Dispose()
    {
        DisposeCount++;
        Close();
    }
}
