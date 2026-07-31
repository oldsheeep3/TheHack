using System.Net;
using System.Threading.Channels;
using Switcher.Atem.Discovery;
using Switcher.Atem.Protocol;

namespace Switcher.Atem.Tests;

/// <summary>
/// Stands in for the discovery sweep's UDP socket. Addresses registered with <see cref="AddDevice"/>
/// behave like an ATEM — they answer a hello with a NewSessionId packet and, once acked, send the
/// state dump carrying their <c>_pin</c> product name. Everything else stays silent, which is what a
/// sweep across a real subnet mostly hears.
/// </summary>
internal sealed class FakeAtemProbeSocket : IAtemProbeSocket
{
    private readonly Dictionary<string, (ushort SessionId, string? ProductName)> _devices = new(StringComparer.Ordinal);
    private readonly Channel<(byte[] Data, IPEndPoint From)> _inbound = Channel.CreateUnbounded<(byte[], IPEndPoint)>();

    public List<(byte[] Data, IPEndPoint To)> Sent { get; } = [];

    public bool Disposed { get; private set; }

    /// <summary>Registers an address that answers like an ATEM. A null product name models a switcher
    /// whose state dump never arrives before the sweep ends.</summary>
    public void AddDevice(string ip, ushort sessionId, string? productName) =>
        _devices[ip] = (sessionId, productName);

    public Task SendToAsync(byte[] datagram, IPEndPoint destination, CancellationToken cancellationToken)
    {
        Sent.Add((datagram, destination));

        if (!_devices.TryGetValue(destination.Address.ToString(), out var device))
        {
            return Task.CompletedTask;
        }

        if (datagram.SequenceEqual(AtemHandshake.HelloPacket))
        {
            Reply(destination, BuildHandshakeReply(device.SessionId));
        }
        else if (AtemPacketHeader.TryParse(datagram, out var header) &&
                 header.Flags.HasFlag(AtemPacketFlags.AckReply) &&
                 device.ProductName is { } productName)
        {
            Reply(destination, BuildStateDump(device.SessionId, productName));
        }

        return Task.CompletedTask;
    }

    public async Task<(byte[] Data, IPEndPoint From)?> ReceiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _inbound.Reader.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public void Dispose() => Disposed = true;

    private void Reply(IPEndPoint device, byte[] datagram) => _inbound.Writer.TryWrite((datagram, device));

    private static byte[] BuildHandshakeReply(ushort sessionId)
    {
        var packet = new byte[AtemPacketHeader.Size];
        new AtemPacketHeader(AtemPacketFlags.NewSessionId, AtemPacketHeader.Size, sessionId, 0, 0, 1).WriteTo(packet);
        return packet;
    }

    private static byte[] BuildStateDump(ushort sessionId, string productName)
    {
        var payload = new byte[44];
        System.Text.Encoding.ASCII.GetBytes(productName, payload);
        var block = AtemCommandSerializer.BuildCommandBlock(AtemCommandNames.ProductIdentifier, payload);
        var header = new AtemPacketHeader(AtemPacketFlags.AckRequest, 0, sessionId, 0, 0, 2);
        return AtemCommandSerializer.BuildPacket(header, block);
    }
}
