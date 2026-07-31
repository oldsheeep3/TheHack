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

    // CRSS field geometry (see BuildStreamingServicePayload).
    private const byte StreamingServiceMaskAllFields = 0b0000_0111;   // service name | url | key
    private const int StreamingServiceNameOffset = 1;
    private const int StreamingServiceNameSize = 64;
    private const int StreamingUrlOffset = StreamingServiceNameOffset + StreamingServiceNameSize;
    private const int StreamingUrlSize = 512;
    private const int StreamingKeyOffset = StreamingUrlOffset + StreamingUrlSize;
    private const int StreamingKeySize = 512;

    /// <summary>Total CRSS payload size: the 1089 bytes of fields, padded to a 16-byte boundary.</summary>
    public const int StreamingServicePayloadSize = 1104;

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

    /// <summary>
    /// Payload for CRSS (set streaming service): byte 0 is a field mask, then three fixed-width,
    /// NUL-padded ASCII fields — service name (64), URL (512), stream key (512) — in a block padded
    /// to <see cref="StreamingServicePayloadSize"/>. Layout from
    /// https://github.com/SteffeyDev/atem-connection (src/commands/Streaming/StreamingServiceCommand.ts).
    /// <para>
    /// Unlike the mix-effect commands, this one is not exercised against hardware in this repo's tests —
    /// it is only sent from the operator-initiated "configure the ATEM's streaming output" action, so a
    /// firmware that disagrees about the layout cannot disturb normal switching (the ATEM ignores a
    /// command block it does not recognise).
    /// </para>
    /// </summary>
    public static byte[] BuildStreamingServicePayload(string serviceName, string url, string key)
    {
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(key);

        var payload = new byte[StreamingServicePayloadSize];
        payload[0] = StreamingServiceMaskAllFields;
        WriteFixedAscii(serviceName, payload.AsSpan(StreamingServiceNameOffset, StreamingServiceNameSize));
        WriteFixedAscii(url, payload.AsSpan(StreamingUrlOffset, StreamingUrlSize));
        WriteFixedAscii(key, payload.AsSpan(StreamingKeyOffset, StreamingKeySize));
        return payload;
    }

    /// <summary>Payload for StrR (start/stop streaming): byte 0 the desired state, bytes 1-3 padding.</summary>
    public static byte[] BuildStreamingStatePayload(bool streaming) => [(byte)(streaming ? 1 : 0), 0, 0, 0];

    /// <summary>Copies ASCII text into a fixed-width field, truncating to fit and NUL-padding the rest.</summary>
    private static void WriteFixedAscii(string value, Span<byte> destination)
    {
        destination.Clear();

        // The field has to stay NUL-terminated, so the last byte is never written.
        var length = Math.Min(value.Length, destination.Length - 1);
        Encoding.ASCII.GetBytes(value.AsSpan(0, length), destination);
    }

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
