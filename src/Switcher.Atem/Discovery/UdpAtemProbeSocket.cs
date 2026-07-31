using System.Net;
using System.Net.Sockets;

namespace Switcher.Atem.Discovery;

/// <summary>
/// <see cref="IAtemProbeSocket"/> over a real <see cref="UdpClient"/> bound to an ephemeral local port.
/// A sweep sends to hundreds of unreachable addresses, and on Windows an ICMP "port unreachable" from
/// one of them surfaces on the *next* receive as <see cref="SocketException"/> 10054
/// (ConnectionReset) — which would abort a scan on the first host that is up but is not an ATEM. The
/// SIO_UDP_CONNRESET ioctl below turns that reporting off, and the receive loop swallows the remaining
/// socket errors anyway.
/// </summary>
public sealed class UdpAtemProbeSocket : IAtemProbeSocket
{
    private const int SioUdpConnreset = unchecked((int)0x9800000C);

    private readonly UdpClient _client;

    public UdpAtemProbeSocket()
    {
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));

        if (OperatingSystem.IsWindows())
        {
            try
            {
                _client.Client.IOControl(SioUdpConnreset, [0, 0, 0, 0], null);
            }
            catch (SocketException)
            {
                // Not fatal: without the ioctl a scan can end early on an ICMP reject, and the caller
                // still gets whatever was found before that.
            }
        }
    }

    public async Task SendToAsync(byte[] datagram, IPEndPoint destination, CancellationToken cancellationToken)
    {
        try
        {
            await _client.SendAsync(datagram, destination, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            // An unroutable candidate address fails synchronously; the rest of the sweep goes on.
        }
    }

    public async Task<(byte[] Data, IPEndPoint From)?> ReceiveAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                return (result.Buffer, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
            catch (SocketException)
            {
                // ICMP rejects from non-ATEM hosts land here; keep listening for the real replies.
            }
        }

        return null;
    }

    public void Dispose() => _client.Dispose();
}
