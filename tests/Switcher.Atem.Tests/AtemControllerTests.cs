using Microsoft.Extensions.Logging.Abstractions;
using Switcher.Atem.Protocol;
using Switcher.Contracts;

namespace Switcher.Atem.Tests;

public class AtemControllerTests
{
    [Fact]
    public void InitialState_IsDisconnected()
    {
        using var controller = new AtemController(ButtonCommandMapping.Empty, new FakeAtemUdpTransport(), NullLogger<AtemController>.Instance);

        Assert.Equal(AtemConnectionState.Disconnected, controller.State);
    }

    [Fact]
    public async Task Connect_TransitionsToConnectingAndSendsAHelloPacketToTheAtemPort()
    {
        var transport = new FakeAtemUdpTransport();
        using var controller = new AtemController(ButtonCommandMapping.Empty, transport, NullLogger<AtemController>.Instance);
        var states = new List<AtemConnectionState>();
        controller.ConnectionStateChanged += (_, state) => states.Add(state);

        controller.Connect("10.0.0.5");

        Assert.Equal(AtemConnectionState.Connecting, controller.State);
        Assert.Equal("10.0.0.5", transport.ConnectedHost);
        Assert.Equal(ProtocolConstants.AtemPort, transport.ConnectedPort);
        await WaitUntilAsync(() => transport.SentPackets.Any(p => p.SequenceEqual(AtemHandshake.HelloPacket)));
        Assert.Contains(AtemConnectionState.Connecting, states);
    }

    [Fact]
    public async Task ReceivingANewSessionIdPacket_CompletesTheHandshakeAndAcksIt()
    {
        var transport = new FakeAtemUdpTransport();
        using var controller = new AtemController(ButtonCommandMapping.Empty, transport, NullLogger<AtemController>.Instance);
        controller.Connect("10.0.0.5");
        await WaitUntilAsync(() => transport.SentPackets.Count > 0);

        transport.Raise(BuildHandshakeResponse(sessionId: 0xAAAA, packetId: 42));

        await WaitUntilAsync(() => controller.State == AtemConnectionState.Connected);
        var ack = transport.SentPackets.Last();
        Assert.True(AtemPacketHeader.TryParse(ack, out var header));
        Assert.Equal(AtemPacketFlags.AckReply, header.Flags);
        Assert.Equal(0xAAAA, header.SessionId);
        Assert.Equal(42, header.AckId);
    }

    [Fact]
    public async Task SendCommand_WhenConnectedAndMapped_SendsTheCorrespondingAtemCommand()
    {
        var transport = new FakeAtemUdpTransport();
        var mapping = new ButtonCommandMapping(new Dictionary<(string, int), AtemCommandMapping>
        {
            [("main", 1)] = new AtemCommandMapping(AtemAction.Cut, MixEffect: 0),
        });
        using var controller = new AtemController(mapping, transport, NullLogger<AtemController>.Instance);
        controller.Connect("10.0.0.5");
        await WaitUntilAsync(() => transport.SentPackets.Count > 0);
        transport.Raise(BuildHandshakeResponse(sessionId: 7, packetId: 1));
        await WaitUntilAsync(() => controller.State == AtemConnectionState.Connected);
        var packetsBeforeSend = transport.SentPackets.Count;

        controller.SendCommand(new ButtonEvent("main", 1, 100));

        await WaitUntilAsync(() => transport.SentPackets.Count > packetsBeforeSend);
        var sent = transport.SentPackets.Last();
        Assert.True(AtemPacketHeader.TryParse(sent, out var header));
        Assert.Equal(AtemPacketFlags.AckRequest, header.Flags);
        Assert.Equal(7, header.SessionId);
        var expectedBlock = AtemCommandSerializer.BuildCommandBlock(AtemCommandNames.Cut, AtemCommandSerializer.BuildMixEffectOnlyPayload(0));
        Assert.Equal(expectedBlock, sent[AtemPacketHeader.Size..]);
        Assert.Equal(0, controller.DroppedCommandCount);
    }

    [Fact]
    public void SendCommand_WhenNotConnected_DropsTheEventAndCountsIt()
    {
        var transport = new FakeAtemUdpTransport();
        var mapping = new ButtonCommandMapping(new Dictionary<(string, int), AtemCommandMapping>
        {
            [("main", 1)] = new AtemCommandMapping(AtemAction.Cut),
        });
        using var controller = new AtemController(mapping, transport, NullLogger<AtemController>.Instance);

        controller.SendCommand(new ButtonEvent("main", 1, 100));

        Assert.Equal(1, controller.DroppedCommandCount);
        Assert.Empty(transport.SentPackets);
    }

    [Fact]
    public async Task SendCommand_WhenConnectedButButtonIsUnmapped_DoesNotSendAndDoesNotCountAsDropped()
    {
        var transport = new FakeAtemUdpTransport();
        using var controller = new AtemController(ButtonCommandMapping.Empty, transport, NullLogger<AtemController>.Instance);
        controller.Connect("10.0.0.5");
        await WaitUntilAsync(() => transport.SentPackets.Count > 0);
        transport.Raise(BuildHandshakeResponse(sessionId: 1, packetId: 1));
        await WaitUntilAsync(() => controller.State == AtemConnectionState.Connected);
        var packetsBeforeSend = transport.SentPackets.Count;

        controller.SendCommand(new ButtonEvent("main", 99, 100));

        Assert.Equal(packetsBeforeSend, transport.SentPackets.Count);
        Assert.Equal(0, controller.DroppedCommandCount);
    }

    [Fact]
    public void Dispose_DisposesTheUnderlyingTransport()
    {
        var transport = new FakeAtemUdpTransport();
        var controller = new AtemController(ButtonCommandMapping.Empty, transport, NullLogger<AtemController>.Instance);

        controller.Dispose();

        Assert.True(transport.Disposed);
    }

    private static byte[] BuildHandshakeResponse(ushort sessionId, ushort packetId)
    {
        var header = new AtemPacketHeader(AtemPacketFlags.NewSessionId, AtemPacketHeader.Size, sessionId, 0, 0, packetId);
        var buffer = new byte[AtemPacketHeader.Size];
        header.WriteTo(buffer);
        return buffer;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Condition was not met within the timeout.");
    }
}
