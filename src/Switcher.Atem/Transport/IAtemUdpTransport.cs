namespace Switcher.Atem.Transport;

/// <summary>
/// Abstraction over the UDP socket used to talk to an ATEM switcher, so <see cref="Switcher.Atem.AtemController"/>
/// can be unit tested (handshake, reconnect, command routing) without a real network endpoint or
/// ATEM hardware (docs/tasks/agent-A-003-atem-control.md step 4).
/// </summary>
public interface IAtemUdpTransport : IDisposable
{
    /// <summary>Raised on the receive loop's thread whenever a datagram arrives from the connected endpoint.</summary>
    event EventHandler<byte[]>? PacketReceived;

    /// <summary>(Re)binds the underlying socket to the given remote endpoint. Safe to call again to retarget.</summary>
    void Connect(string host, int port);

    /// <summary>Sends a raw datagram to the endpoint passed to <see cref="Connect"/>.</summary>
    void Send(byte[] datagram);

    /// <summary>Tears down the socket and receive loop; safe to call when not connected.</summary>
    void Close();
}
