using System.Collections.Concurrent;
using Switcher.Atem.Transport;

namespace Switcher.Atem.Tests;

/// <summary>Records sent datagrams and lets tests inject inbound packets, standing in for real hardware.</summary>
internal sealed class FakeAtemUdpTransport : IAtemUdpTransport
{
    public ConcurrentQueue<byte[]> SentPackets { get; } = new();

    public string? ConnectedHost { get; private set; }

    public int ConnectedPort { get; private set; }

    public int ConnectCallCount { get; private set; }

    public bool Disposed { get; private set; }

    public event EventHandler<byte[]>? PacketReceived;

    public void Connect(string host, int port)
    {
        ConnectedHost = host;
        ConnectedPort = port;
        ConnectCallCount++;
    }

    public void Send(byte[] datagram) => SentPackets.Enqueue(datagram);

    public void Close()
    {
    }

    public void Raise(byte[] datagram) => PacketReceived?.Invoke(this, datagram);

    public void Dispose() => Disposed = true;
}
