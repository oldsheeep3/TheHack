using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Switcher.Atem.Protocol;
using Switcher.Contracts;

namespace Switcher.Atem.Discovery;

/// <summary>
/// Finds ATEM switchers on the local network so the operator can pick one from a list instead of
/// typing an IP (docs/specs/pc-switcher-app.md §2.8). Blackmagic publishes no discovery protocol we
/// can lean on, so the sweep uses the one thing an ATEM reliably answers: the connection hello on UDP
/// <see cref="ProtocolConstants.AtemPort"/>. Any address that replies with a
/// <see cref="AtemPacketFlags.NewSessionId"/> packet is an ATEM; acking that reply makes the switcher
/// start its state dump, and the <c>_pin</c> block in it carries the model name shown in the list.
///
/// The sweep opens one socket for every candidate rather than one per address — a /24 is 254 hosts,
/// and 254 sockets would be both slow and hostile to the machine. It also never holds a session: it
/// stops replying once it has what it needs, and the switcher times the half-open session out on its
/// own, which keeps a scan from stealing the control connection <see cref="AtemController"/> owns.
/// </summary>
public sealed class AtemDiscoveryService
{
    /// <summary>Shown for a switcher that answered the hello but never got as far as its <c>_pin</c> block.</summary>
    private const string DefaultProductName = "ATEM";

    private readonly Func<IAtemProbeSocket> _socketFactory;
    private readonly ILogger _logger;

    public AtemDiscoveryService(ILogger<AtemDiscoveryService>? logger = null)
        : this(() => new UdpAtemProbeSocket(), logger ?? NullLogger<AtemDiscoveryService>.Instance)
    {
    }

    internal AtemDiscoveryService(Func<IAtemProbeSocket> socketFactory, ILogger logger)
    {
        _socketFactory = socketFactory;
        _logger = logger;
    }

    /// <summary>Sweeps every IPv4 subnet this PC is attached to.</summary>
    public Task<IReadOnlyList<AtemDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default) =>
        DiscoverAsync(AtemScanTargets.FromLocalSubnets(), AtemDiscoveryOptions.Default, cancellationToken);

    /// <summary>Checks a single address — the manual-entry path. Returns null when nothing answers.</summary>
    public async Task<AtemDeviceInfo?> ProbeAsync(string ip, CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(ip, out var address))
        {
            return null;
        }

        var found = await DiscoverAsync([address], AtemDiscoveryOptions.SingleHost, cancellationToken)
            .ConfigureAwait(false);
        return found.FirstOrDefault();
    }

    /// <summary>Probes each candidate address and returns whichever ones answered as an ATEM.</summary>
    public async Task<IReadOnlyList<AtemDeviceInfo>> DiscoverAsync(
        IReadOnlyList<IPAddress> candidates,
        AtemDiscoveryOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);

        if (candidates.Count == 0)
        {
            return [];
        }

        // Empty string = "answered, but has not told us its model yet".
        var responders = new ConcurrentDictionary<IPAddress, string>();
        var clock = Stopwatch.StartNew();
        var lastActivity = 0L;

        using var socket = _socketFactory();
        using var sweep = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var receiving = ReceiveLoopAsync();

        foreach (var candidate in candidates)
        {
            if (sweep.IsCancellationRequested)
            {
                break;
            }

            await socket.SendToAsync(
                AtemHandshake.HelloPacket,
                new IPEndPoint(candidate, ProtocolConstants.AtemPort),
                sweep.Token).ConfigureAwait(false);

            if (options.ProbeInterval > TimeSpan.Zero)
            {
                await DelayAsync(options.ProbeInterval, sweep.Token).ConfigureAwait(false);
            }
        }

        // Every hello is out; keep listening until the replies stop trickling in or the budget runs out.
        while (!sweep.IsCancellationRequested &&
               clock.Elapsed < options.Timeout &&
               clock.Elapsed - TimeSpan.FromTicks(Interlocked.Read(ref lastActivity)) < options.QuietPeriod)
        {
            await DelayAsync(TimeSpan.FromMilliseconds(50), sweep.Token).ConfigureAwait(false);
        }

        await sweep.CancelAsync().ConfigureAwait(false);
        await receiving.ConfigureAwait(false);

        _logger.LogInformation(
            "ATEM discovery swept {CandidateCount} addresses in {ElapsedMs} ms and found {FoundCount}.",
            candidates.Count, (int)clock.ElapsedMilliseconds, responders.Count);

        return
        [
            .. responders
                .OrderBy(entry => entry.Key.GetAddressBytes(), ByteSequenceComparer.Instance)
                .Select(entry => new AtemDeviceInfo(
                    entry.Key.ToString(),
                    string.IsNullOrEmpty(entry.Value) ? DefaultProductName : entry.Value))
        ];

        async Task ReceiveLoopAsync()
        {
            while (!sweep.IsCancellationRequested)
            {
                var received = await socket.ReceiveAsync(sweep.Token).ConfigureAwait(false);
                if (received is not { } message)
                {
                    return;
                }

                if (HandleReply(message.Data, message.From, responders, socket, sweep.Token))
                {
                    Interlocked.Exchange(ref lastActivity, clock.Elapsed.Ticks);
                }
            }
        }
    }

    /// <summary>Returns whether the datagram was recognisable as ATEM traffic.</summary>
    private static bool HandleReply(
        byte[] datagram,
        IPEndPoint from,
        ConcurrentDictionary<IPAddress, string> responders,
        IAtemProbeSocket socket,
        CancellationToken cancellationToken)
    {
        if (!AtemPacketHeader.TryParse(datagram, out var header))
        {
            return false;
        }

        var isHandshake = header.Flags.HasFlag(AtemPacketFlags.NewSessionId);
        if (!isHandshake && !responders.ContainsKey(from.Address))
        {
            // Traffic from something we never handshook with; not ours to interpret.
            return false;
        }

        responders.TryAdd(from.Address, string.Empty);

        foreach (var (name, payload) in AtemCommandReader.ReadPacket(datagram))
        {
            if (name == AtemCommandNames.ProductIdentifier &&
                AtemCommandReader.ReadProductIdentifier(payload) is { } product)
            {
                responders[from.Address] = product;
            }
        }

        // Ack so the switcher moves on to (and keeps sending) its state dump, which is where the model
        // name lives. Fire-and-forget: a failed ack only costs us the friendly name, not the find.
        if (isHandshake || header.Flags.HasFlag(AtemPacketFlags.AckRequest))
        {
            _ = SendAckAsync(socket, from, header.SessionId, header.PacketId, cancellationToken);
        }

        return true;
    }

    private static async Task SendAckAsync(
        IAtemProbeSocket socket,
        IPEndPoint destination,
        ushort sessionId,
        ushort packetId,
        CancellationToken cancellationToken)
    {
        var ack = new byte[AtemPacketHeader.Size];
        new AtemPacketHeader(AtemPacketFlags.AckReply, AtemPacketHeader.Size, sessionId, packetId, 0, 0)
            .WriteTo(ack);

        try
        {
            await socket.SendToAsync(ack, destination, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The sweep ended first; nothing left to do.
        }
    }

    /// <summary>A cancelled wait ends the sweep normally rather than throwing out of it.</summary>
    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class ByteSequenceComparer : IComparer<byte[]>
    {
        public static ByteSequenceComparer Instance { get; } = new();

        public int Compare(byte[]? x, byte[]? y) =>
            x is null || y is null ? 0 : x.AsSpan().SequenceCompareTo(y);
    }
}
