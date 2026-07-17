using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Broadcasts the current PGM/PVW tally state as UDP JSON to <c>255.255.255.255:9999</c>
/// (docs/specs/00-system-overview.md §4.3): immediately on every <see cref="Publish"/>, plus a
/// redundant resend ~4 times/second in case a datagram is lost.
/// </summary>
/// <remarks>
/// Broadcast delivery depends on OS/NIC broadcast support and local firewall rules; a send failure
/// is logged and otherwise ignored rather than propagated, since it must never take down the caller
/// (typically the compositor engine reporting a PGM/PVW change).
/// </remarks>
public sealed class TallyBroadcaster : ITallyBroadcaster, IAsyncDisposable
{
    private static readonly TimeSpan RedundantSendInterval = TimeSpan.FromMilliseconds(250); // ~4/sec

    private readonly UdpClient _udpClient;
    private readonly IPEndPoint _broadcastEndpoint;
    private readonly ILogger<TallyBroadcaster> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _redundantSendTask;
    private readonly object _stateLock = new();
    private TallyState _lastState = new(Array.Empty<int>(), Array.Empty<int>());

    public TallyBroadcaster(ILogger<TallyBroadcaster> logger)
        : this(logger, ProtocolConstants.TallyBroadcastAddress, ProtocolConstants.TallyBroadcastPort)
    {
    }

    internal TallyBroadcaster(ILogger<TallyBroadcaster> logger, string broadcastAddress, int broadcastPort)
    {
        _logger = logger;
        _broadcastEndpoint = new IPEndPoint(IPAddress.Parse(broadcastAddress), broadcastPort);

        _udpClient = new UdpClient();
        _udpClient.EnableBroadcast = true;

        _redundantSendTask = Task.Run(() => RedundantSendLoopAsync(_cts.Token));
    }

    public void Publish(TallyState state)
    {
        lock (_stateLock)
        {
            _lastState = state;
        }

        Send(state);
    }

    internal static byte[] SerializePayload(TallyState state) =>
        JsonSerializer.SerializeToUtf8Bytes(state, ProtocolJsonOptions.Default);

    private void Send(TallyState state)
    {
        try
        {
            var payload = SerializePayload(state);
            _udpClient.Send(payload, payload.Length, _broadcastEndpoint);
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast tally state; check NIC/firewall broadcast settings.");
        }
        catch (ObjectDisposedException)
        {
            // Disposed concurrently with a publish/redundant tick; nothing to do.
        }
    }

    private async Task RedundantSendLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RedundantSendInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                TallyState state;
                lock (_stateLock)
                {
                    state = _lastState;
                }

                Send(state);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _redundantSendTask;
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        finally
        {
            _cts.Dispose();
            _udpClient.Dispose();
        }
    }
}
