using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Switcher.Atem.Protocol;
using Switcher.Contracts;

namespace Switcher.Atem.Tests;

/// <summary>
/// Covers the two things the controller learned for the ATEM picker: naming the switcher it is talking
/// to (from the <c>_pin</c> block of the state dump) and pointing its streaming output at this PC.
/// </summary>
public class AtemStreamingAndIdentityTests
{
    [Fact]
    public async Task StateDump_NamesTheConnectedSwitcher()
    {
        var (controller, transport) = await ConnectedControllerAsync();
        using var _ = controller;
        var names = new List<string>();
        controller.ProductNameChanged += (_, name) => names.Add(name);

        transport.Raise(BuildProductIdentifierPacket(sessionId: 7, "ATEM Mini Pro"));

        await WaitUntilAsync(() => controller.ProductName is not null);
        Assert.Equal("ATEM Mini Pro", controller.ProductName);
        Assert.Equal(["ATEM Mini Pro"], names);
    }

    [Fact]
    public async Task StateDump_ReportsTheSameNameOnlyOnce()
    {
        var (controller, transport) = await ConnectedControllerAsync();
        using var _ = controller;
        var raised = 0;
        controller.ProductNameChanged += (_, _) => raised++;

        // The switcher repeats its state dump after a reconnect; the UI should not churn on that.
        transport.Raise(BuildProductIdentifierPacket(sessionId: 7, "ATEM Mini"));
        await WaitUntilAsync(() => raised == 1);
        transport.Raise(BuildProductIdentifierPacket(sessionId: 7, "ATEM Mini"));
        await Task.Delay(50);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Connect_ForgetsThePreviousSwitchersName()
    {
        var (controller, transport) = await ConnectedControllerAsync();
        using var _ = controller;
        transport.Raise(BuildProductIdentifierPacket(sessionId: 7, "ATEM Mini Pro"));
        await WaitUntilAsync(() => controller.ProductName is not null);

        controller.Connect("10.0.0.99");

        // Otherwise the chip would keep naming a switcher this app is no longer talking to.
        Assert.Null(controller.ProductName);
        Assert.Equal("10.0.0.99", controller.TargetIp);
    }

    [Fact]
    public async Task StatePacket_IsAcknowledgedSoTheSwitcherStopsRetransmitting()
    {
        var (controller, transport) = await ConnectedControllerAsync();
        using var _ = controller;
        var before = transport.SentPackets.Count;

        transport.Raise(BuildProductIdentifierPacket(sessionId: 7, "ATEM Mini", packetId: 91));

        await WaitUntilAsync(() => transport.SentPackets.Count > before);
        var ack = transport.SentPackets.Last();
        Assert.True(AtemPacketHeader.TryParse(ack, out var header));
        Assert.Equal(AtemPacketFlags.AckReply, header.Flags);
        Assert.Equal(91, header.AckId);
        Assert.Equal(7, header.SessionId);
    }

    [Fact]
    public async Task TrySendStreamingSetup_SendsTheDestinationThenTheStartCommand()
    {
        var (controller, transport) = await ConnectedControllerAsync();
        using var _ = controller;
        transport.SentPackets.Clear();

        var sent = controller.TrySendStreamingSetup(new AtemStreamingRequest("srt://192.168.1.50:9000"));

        Assert.True(sent);
        var packets = transport.SentPackets.ToList();
        Assert.Equal(2, packets.Count);
        Assert.Equal(AtemCommandNames.SetStreamingService, CommandNameOf(packets[0]));
        Assert.Equal(AtemCommandNames.SetStreamingState, CommandNameOf(packets[1]));

        var payload = PayloadOf(packets[0]);
        Assert.Equal(AtemCommandSerializer.StreamingServicePayloadSize, payload.Length);
        Assert.Equal("srt://192.168.1.50:9000", ReadField(payload, offset: 65, length: 512));
        Assert.Equal("Switcher SRT", ReadField(payload, offset: 1, length: 64));
        Assert.Equal(1, PayloadOf(packets[1])[0]);
    }

    [Fact]
    public async Task TrySendStreamingSetup_WithoutStart_OnlySendsTheDestination()
    {
        var (controller, transport) = await ConnectedControllerAsync();
        using var _ = controller;
        transport.SentPackets.Clear();

        controller.TrySendStreamingSetup(new AtemStreamingRequest("srt://192.168.1.50:9000", Start: false));

        Assert.Equal(AtemCommandNames.SetStreamingService, CommandNameOf(Assert.Single(transport.SentPackets)));
    }

    [Fact]
    public void TrySendStreamingSetup_WhenNotConnected_SendsNothingAndSaysSo()
    {
        var transport = new FakeAtemUdpTransport();
        using var controller = new AtemController(ButtonCommandMapping.Empty, transport, NullLogger<AtemController>.Instance);

        Assert.False(controller.TrySendStreamingSetup(new AtemStreamingRequest("srt://192.168.1.50:9000")));
        Assert.Empty(transport.SentPackets);
    }

    [Fact]
    public async Task TrySendStreamingSetup_UsesFreshPacketIdsSoTheSwitcherDoesNotTreatThemAsRetransmits()
    {
        var (controller, transport) = await ConnectedControllerAsync();
        using var _ = controller;
        transport.SentPackets.Clear();

        controller.TrySendStreamingSetup(new AtemStreamingRequest("srt://192.168.1.50:9000"));

        var ids = transport.SentPackets
            .Select(p => { AtemPacketHeader.TryParse(p, out var header); return header.PacketId; })
            .ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void BuildStreamingServicePayload_TruncatesAnOverlongUrlAndKeepsItNulTerminated()
    {
        var payload = AtemCommandSerializer.BuildStreamingServicePayload("svc", new string('x', 900), string.Empty);

        // 512-byte field, so 511 characters plus the terminator - a longer URL is cut, never overflowed
        // into the stream key that follows it.
        Assert.Equal(new string('x', 511), ReadField(payload, offset: 65, length: 512));
        Assert.Equal(0, payload[65 + 511]);
    }

    private static async Task<(AtemController Controller, FakeAtemUdpTransport Transport)> ConnectedControllerAsync()
    {
        var transport = new FakeAtemUdpTransport();
        var controller = new AtemController(ButtonCommandMapping.Empty, transport, NullLogger<AtemController>.Instance);
        controller.Connect("10.0.0.5");
        await WaitUntilAsync(() => transport.SentPackets.Count > 0);

        var handshake = new byte[AtemPacketHeader.Size];
        new AtemPacketHeader(AtemPacketFlags.NewSessionId, AtemPacketHeader.Size, 7, 0, 0, 1).WriteTo(handshake);
        transport.Raise(handshake);
        await WaitUntilAsync(() => controller.State == AtemConnectionState.Connected);

        return (controller, transport);
    }

    private static byte[] BuildProductIdentifierPacket(ushort sessionId, string productName, ushort packetId = 2)
    {
        var field = new byte[44];
        Encoding.ASCII.GetBytes(productName, field);
        var block = AtemCommandSerializer.BuildCommandBlock(AtemCommandNames.ProductIdentifier, field);
        var header = new AtemPacketHeader(AtemPacketFlags.AckRequest, 0, sessionId, 0, 0, packetId);
        return AtemCommandSerializer.BuildPacket(header, block);
    }

    private static string CommandNameOf(byte[] packet) =>
        Encoding.ASCII.GetString(packet, AtemPacketHeader.Size + 4, 4);

    private static byte[] PayloadOf(byte[] packet) => packet[(AtemPacketHeader.Size + 8)..];

    private static string ReadField(byte[] payload, int offset, int length)
    {
        var span = payload.AsSpan(offset, length);
        var end = span.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end >= 0 ? span[..end] : span);
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
