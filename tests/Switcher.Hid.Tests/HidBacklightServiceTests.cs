using Switcher.Contracts;
using Switcher.Hid.Reports;
using Switcher.Hid.Tests.Fakes;

namespace Switcher.Hid.Tests;

public sealed class HidBacklightServiceTests
{
    private static HidOutputReport Report(byte moduleIndex) => new(
        moduleIndex,
        [
            new BacklightColor(1, 2, 3),
            new BacklightColor(4, 5, 6),
            new BacklightColor(7, 8, 9),
            new BacklightColor(10, 11, 12),
        ]);

    [Fact]
    public void Start_OpensDeviceOnce()
    {
        var device = new FakeHidDevice();
        using var service = new HidBacklightService(device);

        service.Start();
        service.Start(); // idempotent

        Assert.Equal(1, device.OpenCount);
    }

    [Fact]
    public void Send_BeforeStart_Throws()
    {
        var device = new FakeHidDevice();
        using var service = new HidBacklightService(device);

        Assert.Throws<InvalidOperationException>(() => service.Send([Report(0)]));
    }

    [Fact]
    public void Send_WritesOneSerializedReportPerEntry()
    {
        var device = new FakeHidDevice();
        using var service = new HidBacklightService(device);
        service.Start();

        service.Send([Report(0), Report(3)]);

        Assert.Equal(2, device.WrittenReports.Count);
        Assert.Equal(HidReportParser.SerializeOutput(Report(0)), device.WrittenReports[0]);
        Assert.Equal(HidReportParser.SerializeOutput(Report(3)), device.WrittenReports[1]);
    }

    [Fact]
    public void Stop_ClosesDevice()
    {
        var device = new FakeHidDevice();
        using var service = new HidBacklightService(device);
        service.Start();

        service.Stop();

        Assert.Equal(1, device.CloseCount);
    }

    [Fact]
    public void Dispose_DisposesUnderlyingDevice()
    {
        var device = new FakeHidDevice();
        var service = new HidBacklightService(device);
        service.Start();

        service.Dispose();

        Assert.Equal(1, device.DisposeCount);
    }
}
