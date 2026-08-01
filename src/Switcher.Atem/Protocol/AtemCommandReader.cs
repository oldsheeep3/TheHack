using System.Buffers.Binary;
using System.Text;

namespace Switcher.Atem.Protocol;

/// <summary>
/// Walks the command blocks packed into a received ATEM datagram. Every packet after the handshake
/// carries zero or more blocks laid out exactly as <see cref="AtemCommandSerializer.BuildCommandBlock"/>
/// writes them — bytes 0-1 block length (header included), bytes 2-3 reserved, bytes 4-7 the 4-char
/// name, then the payload.
///
/// A malformed or truncated block stops the walk rather than throwing: these bytes come off the wire
/// from an undocumented protocol, and one bad packet must not take down the receive loop.
/// </summary>
public static class AtemCommandReader
{
    private const int CommandBlockHeaderSize = 8;

    /// <summary>Enumerates the (name, payload) pairs in a full datagram, header included.</summary>
    public static IEnumerable<(string Name, byte[] Payload)> ReadPacket(byte[] datagram)
    {
        ArgumentNullException.ThrowIfNull(datagram);

        if (datagram.Length <= AtemPacketHeader.Size)
        {
            yield break;
        }

        var offset = AtemPacketHeader.Size;
        while (offset + CommandBlockHeaderSize <= datagram.Length)
        {
            var blockLength = BinaryPrimitives.ReadUInt16BigEndian(datagram.AsSpan(offset, 2));

            // A zero/short length would loop forever, and a length past the end means the packet was
            // truncated; either way there is nothing more to trust in this datagram.
            if (blockLength < CommandBlockHeaderSize || offset + blockLength > datagram.Length)
            {
                yield break;
            }

            var name = Encoding.ASCII.GetString(datagram, offset + 4, 4);
            var payload = datagram[(offset + CommandBlockHeaderSize)..(offset + blockLength)];
            yield return (name, payload);

            offset += blockLength;
        }
    }

    /// <summary>
    /// Reads the product name out of a <c>_pin</c> payload: a fixed-width, NUL-padded ASCII field.
    /// Returns null when the payload is empty or holds nothing printable.
    /// </summary>
    public static string? ReadProductIdentifier(ReadOnlySpan<byte> payload)
    {
        var end = payload.IndexOf((byte)0);
        var text = Encoding.ASCII.GetString(end >= 0 ? payload[..end] : payload).Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
