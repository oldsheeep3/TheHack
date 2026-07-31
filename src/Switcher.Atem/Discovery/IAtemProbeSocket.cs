using System.Net;

namespace Switcher.Atem.Discovery;

/// <summary>
/// The one connectionless UDP socket <see cref="AtemDiscoveryService"/> sweeps a subnet with. Unlike
/// <see cref="Transport.IAtemUdpTransport"/> — which is bound to a single ATEM — a discovery sweep talks
/// to hundreds of addresses over one socket, so it needs send-to/receive-from rather than connected
/// send/receive. Abstracted so the sweep logic (hello fan-out, handshake ack, product-name pickup) is
/// unit testable without a network.
/// </summary>
public interface IAtemProbeSocket : IDisposable
{
    /// <summary>Sends a datagram to a specific address.</summary>
    Task SendToAsync(byte[] datagram, IPEndPoint destination, CancellationToken cancellationToken);

    /// <summary>
    /// Waits for the next inbound datagram. Returns null when the socket is closed or the wait is
    /// cancelled, which the sweep treats as "nothing more is coming" rather than as an error.
    /// </summary>
    Task<(byte[] Data, IPEndPoint From)?> ReceiveAsync(CancellationToken cancellationToken);
}
