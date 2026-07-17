using System.Buffers.Binary;

namespace Switcher.Atem.Protocol;

/// <summary>
/// The fixed 12-byte header prefixing every ATEM UDP datagram. Field layout sourced from the
/// reverse-engineered implementation at https://github.com/SteffeyDev/atem-connection
/// (src/lib/atemSocketChild.ts, sendPacket/_receivePacket/_sendAck):
/// bytes 0-1 pack <see cref="Flags"/> (top 5 bits) with the total packet length in bytes, header
/// included (low 11 bits); bytes 2-3 session id; bytes 4-5 the packet id being acknowledged (only
/// meaningful when <see cref="AtemPacketFlags.AckReply"/> is set); bytes 6-7 the packet id to
/// retransmit from (only meaningful when <see cref="AtemPacketFlags.RetransmitRequest"/> is set);
/// bytes 8-9 unused; bytes 10-11 this packet's own sequence id.
/// </summary>
public readonly record struct AtemPacketHeader(
    AtemPacketFlags Flags,
    ushort TotalLength,
    ushort SessionId,
    ushort AckId,
    ushort RetransmitFromId,
    ushort PacketId)
{
    public const int Size = 12;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException($"Destination must be at least {Size} bytes.", nameof(destination));
        }

        var opcodeAndLength = (ushort)(((byte)Flags << 11) | (TotalLength & 0x07FF));
        BinaryPrimitives.WriteUInt16BigEndian(destination[..2], opcodeAndLength);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..4], SessionId);
        BinaryPrimitives.WriteUInt16BigEndian(destination[4..6], AckId);
        BinaryPrimitives.WriteUInt16BigEndian(destination[6..8], RetransmitFromId);
        BinaryPrimitives.WriteUInt16BigEndian(destination[8..10], 0);
        BinaryPrimitives.WriteUInt16BigEndian(destination[10..12], PacketId);
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out AtemPacketHeader header)
    {
        if (source.Length < Size)
        {
            header = default;
            return false;
        }

        var opcodeAndLength = BinaryPrimitives.ReadUInt16BigEndian(source[..2]);
        var flags = (AtemPacketFlags)(opcodeAndLength >> 11);
        var length = (ushort)(opcodeAndLength & 0x07FF);
        var sessionId = BinaryPrimitives.ReadUInt16BigEndian(source[2..4]);
        var ackId = BinaryPrimitives.ReadUInt16BigEndian(source[4..6]);
        var retransmitFromId = BinaryPrimitives.ReadUInt16BigEndian(source[6..8]);
        var packetId = BinaryPrimitives.ReadUInt16BigEndian(source[10..12]);

        header = new AtemPacketHeader(flags, length, sessionId, ackId, retransmitFromId, packetId);
        return true;
    }
}
