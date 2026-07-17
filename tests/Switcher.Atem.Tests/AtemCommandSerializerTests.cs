using System.Text;
using Switcher.Atem.Protocol;

namespace Switcher.Atem.Tests;

public class AtemCommandSerializerTests
{
    [Fact]
    public void BuildInputCommandPayload_WritesMixEffectAndBigEndianSource()
    {
        var payload = AtemCommandSerializer.BuildInputCommandPayload(mixEffect: 2, source: 0x0007);

        Assert.Equal(new byte[] { 2, 0, 0, 7 }, payload);
    }

    [Fact]
    public void BuildMixEffectOnlyPayload_WritesMixEffectWithZeroPadding()
    {
        var payload = AtemCommandSerializer.BuildMixEffectOnlyPayload(mixEffect: 1);

        Assert.Equal(new byte[] { 1, 0, 0, 0 }, payload);
    }

    [Fact]
    public void BuildCommandBlock_PrependsLengthAndNameToThePayload()
    {
        var block = AtemCommandSerializer.BuildCommandBlock(AtemCommandNames.ProgramInput, new byte[] { 2, 0, 0, 7 });

        Assert.Equal(12, block.Length);
        Assert.Equal(new byte[] { 0x00, 0x0C }, block[..2]);
        Assert.Equal(new byte[] { 0x00, 0x00 }, block[2..4]);
        Assert.Equal("CPgI", Encoding.ASCII.GetString(block, 4, 4));
        Assert.Equal(new byte[] { 2, 0, 0, 7 }, block[8..]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("ABCDE")]
    public void BuildCommandBlock_RejectsNamesThatAreNotExactlyFourCharacters(string name)
    {
        Assert.Throws<ArgumentException>(() => AtemCommandSerializer.BuildCommandBlock(name, new byte[4]));
    }

    [Fact]
    public void BuildPacket_PrefixesTheCommandBlockWithAHeaderCarryingTheTotalLength()
    {
        var block = AtemCommandSerializer.BuildCommandBlock(AtemCommandNames.Cut, AtemCommandSerializer.BuildMixEffectOnlyPayload(0));
        var header = new AtemPacketHeader(AtemPacketFlags.AckRequest, TotalLength: 0, SessionId: 0x0042, AckId: 0, RetransmitFromId: 0, PacketId: 3);

        var packet = AtemCommandSerializer.BuildPacket(header, block);

        Assert.Equal(AtemPacketHeader.Size + block.Length, packet.Length);
        Assert.True(AtemPacketHeader.TryParse(packet, out var parsedHeader));
        Assert.Equal((ushort)packet.Length, parsedHeader.TotalLength);
        Assert.Equal(0x0042, parsedHeader.SessionId);
        Assert.Equal(block, packet[AtemPacketHeader.Size..]);
    }
}
