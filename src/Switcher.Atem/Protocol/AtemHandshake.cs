namespace Switcher.Atem.Protocol;

/// <summary>
/// Constants for the ATEM UDP connection handshake, an analogue of a TCP SYN. The datagram below is
/// taken verbatim from the interoperability-tested constant in
/// https://github.com/SteffeyDev/atem-connection (src/lib/atemSocketChild.ts, COMMAND_CONNECT_HELLO).
/// Several bytes in this specific packet (notably offset 8-9, which carries 0x00,0x3a rather than the
/// "unused" zero that <see cref="AtemPacketHeader"/> writes there for every other packet) are
/// undocumented and fixed by the switcher firmware rather than derived from the general header
/// layout, so the known-good bytes are replicated as-is instead of being re-derived field by field.
/// Decoded: <see cref="AtemPacketFlags.NewSessionId"/> flag, 20-byte total length, session id
/// 0x53AB, 8-byte payload {0x01, 0,0,0,0,0,0,0}.
/// </summary>
public static class AtemHandshake
{
    public static readonly byte[] HelloPacket =
    [
        0x10, 0x14, 0x53, 0xab, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3a, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];
}
