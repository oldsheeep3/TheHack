using System.Buffers.Binary;
using System.Text;

namespace Switcher.Atem.Protocol;

/// <summary>
/// Serializes the subset of ATEM mix-effect commands this client sends (program/preview input, cut,
/// auto) into command-block and full-packet byte layouts. Command payload layouts sourced from
/// https://github.com/SteffeyDev/atem-connection
/// (src/commands/MixEffects/{ProgramInputCommand,PreviewInputCommand,CutCommand,AutoTransitionCommand}.ts);
/// the command-block wrapper (length + reserved + 4-char name + payload) from
/// src/lib/packetBuilder.ts (PacketBuilder.addCommand).
/// </summary>
public static class AtemCommandSerializer
{
    private const int CommandBlockHeaderSize = 8;
    private const int InputPayloadSize = 4;

    /// <summary>Payload for CPgI/CPvI: byte 0 mix-effect index, byte 1 padding, bytes 2-3 source id (BE).</summary>
    public static byte[] BuildInputCommandPayload(byte mixEffect, ushort source)
    {
        var payload = new byte[InputPayloadSize];
        payload[0] = mixEffect;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), source);
        return payload;
    }

    /// <summary>Payload for DCut/DAut: byte 0 mix-effect index, bytes 1-3 padding.</summary>
    public static byte[] BuildMixEffectOnlyPayload(byte mixEffect) => [mixEffect, 0, 0, 0];

    /// <summary>Wraps a command payload in its block header: bytes 0-1 block length (header+payload),
    /// bytes 2-3 reserved (zero), bytes 4-7 the 4-char ASCII command name.</summary>
    public static byte[] BuildCommandBlock(string commandName, ReadOnlySpan<byte> payload)
    {
        if (commandName.Length != 4)
        {
            throw new ArgumentException("ATEM command names are exactly 4 ASCII characters.", nameof(commandName));
        }

        var block = new byte[CommandBlockHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(0, 2), (ushort)block.Length);
        Encoding.ASCII.GetBytes(commandName, block.AsSpan(4, 4));
        payload.CopyTo(block.AsSpan(CommandBlockHeaderSize));
        return block;
    }

    /// <summary>Prefixes a command block with a 12-byte packet header, filling in the header's total length.</summary>
    public static byte[] BuildPacket(AtemPacketHeader header, ReadOnlySpan<byte> commandBlock)
    {
        var totalLength = (ushort)(AtemPacketHeader.Size + commandBlock.Length);
        var packet = new byte[totalLength];
        (header with { TotalLength = totalLength }).WriteTo(packet);
        commandBlock.CopyTo(packet.AsSpan(AtemPacketHeader.Size));
        return packet;
    }
}
