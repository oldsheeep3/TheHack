using Switcher.Atem.Protocol;

namespace Switcher.Atem.Tests;

public class AtemPacketHeaderTests
{
    [Fact]
    public void WriteTo_PacksFlagsAndLengthIntoTheFirstTwoBytes()
    {
        var header = new AtemPacketHeader(AtemPacketFlags.AckRequest, TotalLength: 16, SessionId: 0x1234, AckId: 1, RetransmitFromId: 2, PacketId: 5);
        var buffer = new byte[AtemPacketHeader.Size];

        header.WriteTo(buffer);

        Assert.Equal(
            new byte[] { 0x08, 0x10, 0x12, 0x34, 0x00, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x05 },
            buffer);
    }

    [Fact]
    public void WriteTo_ThenTryParse_RoundTripsAllFields()
    {
        var header = new AtemPacketHeader(
            AtemPacketFlags.AckReply | AtemPacketFlags.IsRetransmit,
            TotalLength: 42,
            SessionId: 0xBEEF,
            AckId: 7,
            RetransmitFromId: 3,
            PacketId: 9);
        var buffer = new byte[AtemPacketHeader.Size];
        header.WriteTo(buffer);

        var parsed = AtemPacketHeader.TryParse(buffer, out var result);

        Assert.True(parsed);
        Assert.Equal(header, result);
    }

    [Fact]
    public void TryParse_WhenBufferShorterThanHeader_ReturnsFalse()
    {
        var parsed = AtemPacketHeader.TryParse(new byte[11], out _);

        Assert.False(parsed);
    }

    [Fact]
    public void TryParse_DecodesTheKnownGoodHelloPacket()
    {
        var parsed = AtemPacketHeader.TryParse(AtemHandshake.HelloPacket, out var header);

        Assert.True(parsed);
        Assert.Equal(AtemPacketFlags.NewSessionId, header.Flags);
        Assert.Equal(20, header.TotalLength);
        Assert.Equal(0x53ab, header.SessionId);
        Assert.Equal(0, header.PacketId);
    }
}
