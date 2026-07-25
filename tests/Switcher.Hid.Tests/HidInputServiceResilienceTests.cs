using Switcher.Hid.Devices;
using Switcher.Hid.Reports;

namespace Switcher.Hid.Tests;

/// <summary>
/// The read loop runs on its own background thread, where an escaping exception ends the process. Its
/// subscribers are App-side handlers that touch the engine, the disk and the network — a full disk while
/// a module is plugged in used to be enough to kill the switcher mid-show.
/// </summary>
public sealed class HidInputServiceResilienceTests
{
    [Fact]
    public void AThrowingSwitchHandlerDoesNotStopTheReadLoop()
    {
        // Two reports with a switch difference, so the second produces an edge.
        var device = new ScriptedHidDevice(
            Report(modulePresent: 0b1, pgm1Src1: false),
            Report(modulePresent: 0b1, pgm1Src1: true),
            Report(modulePresent: 0b1, pgm1Src1: false));

        using var service = new HidInputService(device);
        var failures = new List<Exception>();
        var edges = 0;

        service.HandlerFailed += failures.Add;
        service.SwitchEdge += _ =>
        {
            edges++;
            throw new InvalidOperationException("handler blew up");
        };

        service.Start();
        Assert.True(device.Drained.Wait(TimeSpan.FromSeconds(5)));
        service.Stop();

        // Both edges were delivered: the first throw did not end the loop.
        Assert.Equal(2, edges);
        Assert.Equal(2, failures.Count);
        Assert.All(failures, ex => Assert.IsType<InvalidOperationException>(ex));
    }

    [Fact]
    public void AThrowingPresenceHandlerDoesNotStopTheReadLoop()
    {
        var device = new ScriptedHidDevice(
            Report(modulePresent: 0b1, pgm1Src1: false),
            Report(modulePresent: 0b11, pgm1Src1: false));

        using var service = new HidInputService(device);
        var failures = 0;
        var presenceUpdates = 0;

        service.HandlerFailed += _ => failures++;
        service.ModulePresenceChanged += _ =>
        {
            presenceUpdates++;
            throw new UnauthorizedAccessException("config directory is read-only");
        };

        service.Start();
        Assert.True(device.Drained.Wait(TimeSpan.FromSeconds(5)));
        service.Stop();

        Assert.Equal(2, presenceUpdates);
        Assert.Equal(2, failures);
    }

    private static byte[] Report(byte modulePresent, bool pgm1Src1)
    {
        var body = new byte[HidReportParser.InputReportLength];
        body[0] = modulePresent;
        if (pgm1Src1)
        {
            body[1] = 0b0001;  // module 0, Pgm1Src1
        }

        return body;
    }

    /// <summary>Replays a fixed list of reports, then blocks so the loop stays alive until Stop.</summary>
    private sealed class ScriptedHidDevice(params byte[][] reports) : IHidDevice
    {
        private readonly ManualResetEventSlim _stopped = new(false);
        private int _index;

        public ManualResetEventSlim Drained { get; } = new(false);

        public void Open()
        {
        }

        public byte[]? ReadInputReport()
        {
            if (_index < reports.Length)
            {
                return reports[_index++];
            }

            Drained.Set();
            _stopped.Wait();
            return null;
        }

        public void WriteOutputReport(ReadOnlySpan<byte> reportBody)
        {
        }

        public void Close() => _stopped.Set();

        public void Dispose()
        {
            _stopped.Set();
            _stopped.Dispose();
            Drained.Dispose();
        }
    }
}
