namespace Switcher.Atem.Protocol;

/// <summary>
/// Flag bits packed into the top 5 bits of the first big-endian uint16 of every ATEM UDP packet
/// header. The ATEM UDP protocol (port <see cref="Switcher.Contracts.ProtocolConstants.AtemPort"/>)
/// is undocumented by Blackmagic; values below are taken from the reverse-engineered, widely used
/// implementation at https://github.com/SteffeyDev/atem-connection
/// (src/lib/atemSocketChild.ts, enum PacketFlag).
/// </summary>
[Flags]
public enum AtemPacketFlags : byte
{
    None = 0x00,
    AckRequest = 0x01,
    NewSessionId = 0x02,
    IsRetransmit = 0x04,
    RetransmitRequest = 0x08,
    AckReply = 0x10,
}
