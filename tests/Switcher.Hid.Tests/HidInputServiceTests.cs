using Switcher.Contracts;
using Switcher.Hid.Input;
using Switcher.Hid.Tests.Fakes;

namespace Switcher.Hid.Tests;

public sealed class HidInputServiceTests
{
    private static byte[] BuildInputBody(byte modulePresent, byte[] switchBytes, byte seq, byte[]? vrBytes = null)
    {
        var body = new byte[1 + ProtocolConstants.MaxModules + 2 * ProtocolConstants.MaxModules + 1];
        body[0] = modulePresent;
        switchBytes.CopyTo(body, 1);
        (vrBytes ?? new byte[2 * ProtocolConstants.MaxModules]).CopyTo(body, 1 + ProtocolConstants.MaxModules);
        body[^1] = seq;
        return body;
    }

    private static byte[] SwitchBytes(params (int ModuleIndex, byte Value)[] modules)
    {
        var bytes = new byte[ProtocolConstants.MaxModules];
        foreach (var (moduleIndex, value) in modules)
        {
            bytes[moduleIndex] = value;
        }

        return bytes;
    }

    [Fact]
    public void Start_OpensDeviceOnce()
    {
        var device = new FakeHidDevice();
        using var service = new HidInputService(device);

        service.Start();
        service.Start(); // idempotent

        Assert.Equal(1, device.OpenCount);
        service.Stop();
    }

    [Fact]
    public void Start_RaisesSwitchEdge_AfterBaselineReport()
    {
        var device = new FakeHidDevice();
        using var service = new HidInputService(device);
        var edges = new List<SwitchEdgeEvent>();
        using var received = new ManualResetEventSlim();
        service.SwitchEdge += e =>
        {
            edges.Add(e);
            received.Set();
        };

        service.Start();
        device.EnqueueInput(BuildInputBody(0b1, SwitchBytes(), seq: 0)); // baseline: no prior report to diff against
        device.EnqueueInput(BuildInputBody(0b1, SwitchBytes((0, 0b0001)), seq: 1)); // module 0, Pgm1Src1 pressed

        Assert.True(received.Wait(TimeSpan.FromSeconds(5)));
        service.Stop();

        var edge = Assert.Single(edges);
        Assert.Equal(0, edge.ModuleIndex);
        Assert.Equal(SwitchId.Pgm1Src1, edge.Switch);
        Assert.True(edge.IsRising);
    }

    [Fact]
    public void ModulePresenceChanged_PublishesFirstBitmapThenOnlyChanges()
    {
        var device = new FakeHidDevice();
        using var service = new HidInputService(device);
        var bitmaps = new List<byte>();
        using var received = new ManualResetEventSlim();
        service.ModulePresenceChanged += mask =>
        {
            bitmaps.Add(mask);
            if (bitmaps.Count == 2)
            {
                received.Set();
            }
        };

        service.Start();
        device.EnqueueInput(BuildInputBody(0b0000_0101, SwitchBytes(), seq: 0));  // modules 0 and 2 attached
        device.EnqueueInput(BuildInputBody(0b0000_0101, SwitchBytes(), seq: 1));  // unchanged: no republish
        device.EnqueueInput(BuildInputBody(0b0000_1101, SwitchBytes(), seq: 2));  // module 3 attached

        Assert.True(received.Wait(TimeSpan.FromSeconds(5)));
        service.Stop();

        Assert.Equal([0b0000_0101, 0b0000_1101], bitmaps);
    }

    [Fact]
    public void Start_RaisesVrChanged_WhenValueMovesPastDeadband()
    {
        var device = new FakeHidDevice();
        using var service = new HidInputService(device);
        var changes = new List<VrChangedEvent>();
        using var received = new ManualResetEventSlim();
        service.VrChanged += e =>
        {
            changes.Add(e);
            received.Set();
        };

        service.Start();
        device.EnqueueInput(BuildInputBody(0, SwitchBytes(), seq: 0)); // baseline is always reported

        Assert.True(received.Wait(TimeSpan.FromSeconds(5)));
        service.Stop();

        Assert.Contains(changes, c => c.ModuleIndex == 0 && c.Channel == VrChannel.Src1);
    }

    [Fact]
    public void Start_SeqGap_RaisesSequenceGapDetected()
    {
        var device = new FakeHidDevice();
        using var service = new HidInputService(device);
        using var gapDetected = new ManualResetEventSlim();
        service.SequenceGapDetected += () => gapDetected.Set();

        service.Start();
        device.EnqueueInput(BuildInputBody(0, SwitchBytes(), seq: 0));
        device.EnqueueInput(BuildInputBody(0, SwitchBytes(), seq: 5)); // skipped 1..4

        Assert.True(gapDetected.Wait(TimeSpan.FromSeconds(5)));
        service.Stop();
    }

    [Fact]
    public void Stop_ClosesDeviceAndJoinsReadThread()
    {
        var device = new FakeHidDevice();
        using var service = new HidInputService(device);

        service.Start();
        service.Stop();

        Assert.True(device.CloseCount >= 1);
    }

    [Fact]
    public void Dispose_DisposesUnderlyingDevice()
    {
        var device = new FakeHidDevice();
        var service = new HidInputService(device);
        service.Start();

        service.Dispose();

        Assert.Equal(1, device.DisposeCount);
    }
}
