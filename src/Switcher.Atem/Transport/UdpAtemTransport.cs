using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Switcher.Atem.Transport;

/// <summary>
/// <see cref="IAtemUdpTransport"/> backed by a real <see cref="UdpClient"/>. Owns the socket and its
/// background receive loop; both are torn down together on <see cref="Close"/>/<see cref="Dispose"/>,
/// and re-created on <see cref="Connect"/> so a reconnect never leaks the previous socket or loop task.
/// </summary>
public sealed class UdpAtemTransport(ILogger<UdpAtemTransport>? logger = null) : IAtemUdpTransport
{
    private readonly object _sync = new();
    private UdpClient? _client;
    private CancellationTokenSource? _receiveCts;

    public event EventHandler<byte[]>? PacketReceived;

    public void Connect(string host, int port)
    {
        lock (_sync)
        {
            CloseInternal();

            var client = new UdpClient();
            client.Connect(host, port);
            _client = client;

            var cts = new CancellationTokenSource();
            _receiveCts = cts;
            _ = ReceiveLoopAsync(client, cts.Token);
        }
    }

    public void Send(byte[] datagram)
    {
        UdpClient? client;
        lock (_sync)
        {
            client = _client;
        }

        if (client is null)
        {
            throw new InvalidOperationException("ATEM transport is not connected.");
        }

        client.Send(datagram, datagram.Length);
    }

    public void Close()
    {
        lock (_sync)
        {
            CloseInternal();
        }
    }

    private void CloseInternal()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _receiveCts = null;

        _client?.Dispose();
        _client = null;
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                PacketReceived?.Invoke(this, result.Buffer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                logger?.LogWarning(ex, "ATEM UDP receive failed; receive loop continues.");
            }
        }
    }

    public void Dispose() => Close();
}
