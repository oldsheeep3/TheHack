using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Switcher.Atem.Discovery;
using Switcher.Atem.Protocol;
using Switcher.Contracts;

namespace Switcher.Atem.Tests;

public class AtemDiscoveryServiceTests
{
    /// <summary>Short enough to keep the suite quick, long enough that the fake's replies land first.</summary>
    private static readonly AtemDiscoveryOptions FastSweep = new(
        ProbeInterval: TimeSpan.Zero,
        Timeout: TimeSpan.FromSeconds(3),
        QuietPeriod: TimeSpan.FromMilliseconds(200));

    [Fact]
    public async Task DiscoverAsync_ReturnsOnlyTheAddressesThatAnsweredLikeAnAtem()
    {
        var socket = new FakeAtemProbeSocket();
        socket.AddDevice("10.0.0.7", sessionId: 0x1234, productName: "ATEM Mini Pro");
        var service = new AtemDiscoveryService(() => socket, NullLogger.Instance);

        var found = await service.DiscoverAsync(Candidates("10.0.0.5", "10.0.0.7", "10.0.0.9"), FastSweep);

        var device = Assert.Single(found);
        Assert.Equal("10.0.0.7", device.Ip);
        Assert.Equal("ATEM Mini Pro", device.Name);
    }

    [Fact]
    public async Task DiscoverAsync_ProbesEveryCandidateWithTheHelloPacket()
    {
        var socket = new FakeAtemProbeSocket();
        var service = new AtemDiscoveryService(() => socket, NullLogger.Instance);

        await service.DiscoverAsync(Candidates("10.0.0.5", "10.0.0.6"), FastSweep);

        var probed = socket.Sent
            .Where(s => s.Data.SequenceEqual(AtemHandshake.HelloPacket))
            .Select(s => s.To.Address.ToString())
            .ToList();
        Assert.Equal(["10.0.0.5", "10.0.0.6"], probed);
        Assert.All(socket.Sent, s => Assert.Equal(ProtocolConstants.AtemPort, s.To.Port));
    }

    [Fact]
    public async Task DiscoverAsync_AcksTheHandshakeSoTheSwitcherSendsItsStateDump()
    {
        var socket = new FakeAtemProbeSocket();
        socket.AddDevice("10.0.0.7", sessionId: 0xABCD, productName: "ATEM Mini Extreme");
        var service = new AtemDiscoveryService(() => socket, NullLogger.Instance);

        await service.DiscoverAsync(Candidates("10.0.0.7"), FastSweep);

        var ack = socket.Sent
            .Select(s => s.Data)
            .Where(data => AtemPacketHeader.TryParse(data, out var header) &&
                           header.Flags.HasFlag(AtemPacketFlags.AckReply))
            .ToList();
        Assert.NotEmpty(ack);
        Assert.True(AtemPacketHeader.TryParse(ack[0], out var acked));
        Assert.Equal(0xABCD, acked.SessionId);
    }

    [Fact]
    public async Task DiscoverAsync_WhenTheSwitcherNeverNamesItself_StillReportsItUnderAGenericName()
    {
        var socket = new FakeAtemProbeSocket();
        socket.AddDevice("10.0.0.7", sessionId: 1, productName: null);
        var service = new AtemDiscoveryService(() => socket, NullLogger.Instance);

        var found = await service.DiscoverAsync(Candidates("10.0.0.7"), FastSweep);

        // Being findable matters more than being named: the operator can still select it by address.
        Assert.Equal("ATEM", Assert.Single(found).Name);
    }

    [Fact]
    public async Task DiscoverAsync_WithNoCandidates_ReturnsEmptyWithoutOpeningASocket()
    {
        var opened = false;
        var service = new AtemDiscoveryService(
            () => { opened = true; return new FakeAtemProbeSocket(); },
            NullLogger.Instance);

        var found = await service.DiscoverAsync([], FastSweep);

        Assert.Empty(found);
        Assert.False(opened);
    }

    [Fact]
    public async Task DiscoverAsync_SortsResultsByAddressSoTheListDoesNotReorderBetweenScans()
    {
        var socket = new FakeAtemProbeSocket();
        socket.AddDevice("10.0.0.20", sessionId: 1, productName: "B");
        socket.AddDevice("10.0.0.3", sessionId: 2, productName: "A");
        var service = new AtemDiscoveryService(() => socket, NullLogger.Instance);

        var found = await service.DiscoverAsync(Candidates("10.0.0.20", "10.0.0.3"), FastSweep);

        Assert.Equal(["10.0.0.3", "10.0.0.20"], found.Select(d => d.Ip));
    }

    [Fact]
    public async Task ProbeAsync_WithSomethingThatIsNotAnAddress_ReturnsNullWithoutScanning()
    {
        var service = new AtemDiscoveryService(
            () => throw new InvalidOperationException("must not open a socket"),
            NullLogger.Instance);

        Assert.Null(await service.ProbeAsync("not-an-ip"));
    }

    [Fact]
    public async Task DiscoverAsync_DisposesItsSocket()
    {
        var socket = new FakeAtemProbeSocket();
        var service = new AtemDiscoveryService(() => socket, NullLogger.Instance);

        await service.DiscoverAsync(Candidates("10.0.0.5"), FastSweep);

        Assert.True(socket.Disposed);
    }

    private static IReadOnlyList<IPAddress> Candidates(params string[] addresses) =>
        [.. addresses.Select(IPAddress.Parse)];
}
