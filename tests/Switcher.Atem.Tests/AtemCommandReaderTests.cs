using System.Text;
using Switcher.Atem.Protocol;

namespace Switcher.Atem.Tests;

public class AtemCommandReaderTests
{
    [Fact]
    public void ReadPacket_ReturnsEveryCommandBlockInOrder()
    {
        var packet = BuildPacket(
            ("_pin", Ascii("ATEM Mini Pro", 44)),
            ("_ver", [0, 2, 0, 30]));

        var blocks = AtemCommandReader.ReadPacket(packet).ToList();

        Assert.Equal(["_pin", "_ver"], blocks.Select(b => b.Name));
        Assert.Equal<byte[]>([0, 2, 0, 30], blocks[1].Payload);
    }

    [Fact]
    public void ReadPacket_WithNothingBeyondTheHeader_ReturnsNoBlocks()
    {
        var header = new byte[AtemPacketHeader.Size];
        new AtemPacketHeader(AtemPacketFlags.AckReply, AtemPacketHeader.Size, 1, 0, 0, 0).WriteTo(header);

        Assert.Empty(AtemCommandReader.ReadPacket(header));
    }

    [Fact]
    public void ReadPacket_StopsAtABlockThatRunsPastTheEndOfTheDatagram()
    {
        var packet = BuildPacket(("_pin", Ascii("ATEM Mini", 12)), ("_top", [1, 2, 3, 4]));

        // A datagram cut short mid-block must not throw and must not invent a payload: the first,
        // complete block is still usable and the truncated one is dropped.
        var blocks = AtemCommandReader.ReadPacket(packet[..^3]).ToList();

        Assert.Equal(["_pin"], blocks.Select(b => b.Name));
    }

    [Fact]
    public void ReadPacket_WithAZeroLengthBlock_StopsInsteadOfLoopingForever()
    {
        var packet = new byte[AtemPacketHeader.Size + 8];
        new AtemPacketHeader(AtemPacketFlags.AckRequest, (ushort)packet.Length, 1, 0, 0, 0).WriteTo(packet);

        Assert.Empty(AtemCommandReader.ReadPacket(packet));
    }

    [Theory]
    [InlineData("ATEM Mini Pro")]
    [InlineData("ATEM Television Studio HD8")]
    public void ReadProductIdentifier_TrimsTheNulPaddingOffTheFixedWidthField(string productName)
    {
        Assert.Equal(productName, AtemCommandReader.ReadProductIdentifier(Ascii(productName, 44)));
    }

    [Fact]
    public void ReadProductIdentifier_WithAnEmptyField_ReturnsNull()
    {
        Assert.Null(AtemCommandReader.ReadProductIdentifier(new byte[44]));
    }

    private static byte[] Ascii(string text, int width)
    {
        var field = new byte[width];
        Encoding.ASCII.GetBytes(text, field);
        return field;
    }

    private static byte[] BuildPacket(params (string Name, byte[] Payload)[] blocks)
    {
        var body = blocks
            .SelectMany(b => AtemCommandSerializer.BuildCommandBlock(b.Name, b.Payload))
            .ToArray();
        var header = new AtemPacketHeader(AtemPacketFlags.AckRequest, 0, 1, 0, 0, 1);
        return AtemCommandSerializer.BuildPacket(header, body);
    }
}
