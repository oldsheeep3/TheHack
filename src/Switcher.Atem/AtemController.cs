using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Switcher.Atem.Protocol;
using Switcher.Atem.Transport;
using Switcher.Contracts;

namespace Switcher.Atem;

/// <summary>
/// ATEM remote-control client (docs/tasks/agent-A-003-atem-control.md): connects to an ATEM Mini over
/// the reverse-engineered UDP protocol (port <see cref="ProtocolConstants.AtemPort"/>), performs the
/// connection handshake with backoff reconnect, and translates mapped controller button presses into
/// ATEM commands (program/preview input, cut, auto). The App integration task routes controller input
/// into <see cref="SendCommand"/>; this type does not read raw button hardware itself.
///
/// Thread safety: connection state, session id and outgoing packet sequence are guarded by
/// <see cref="_sync"/>, held only for the in-memory bookkeeping around a send/receive, never across a
/// socket call. Command drop policy: while not <see cref="AtemConnectionState.Connected"/>,
/// <see cref="SendCommand"/> drops the event (counted in <see cref="DroppedCommandCount"/>) instead of
/// queueing it — replaying a queued switch command after a reconnect could apply a stale operator
/// decision to a program state that has since moved on, which is worse for a live broadcast than
/// simply missing the press.
/// </summary>
public sealed class AtemController : IAtemController, IDisposable
{
    private static readonly TimeSpan[] BackoffSchedule =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
    ];

    private readonly IAtemUdpTransport _transport;
    private readonly ILogger<AtemController> _logger;
    private readonly object _sync = new();

    private ButtonCommandMapping _mapping;
    private AtemConnectionState _state = AtemConnectionState.Disconnected;
    private ushort _sessionId;
    private ushort _nextPacketId = 1;
    private string? _targetIp;
    private string? _productName;
    private CancellationTokenSource? _handshakeCts;
    private int _droppedCommandCount;
    private bool _disposed;

    public AtemController(ButtonCommandMapping mapping, ILogger<AtemController> logger)
        : this(mapping, new UdpAtemTransport(), logger)
    {
    }

    internal AtemController(ButtonCommandMapping mapping, IAtemUdpTransport transport, ILogger<AtemController> logger)
    {
        _mapping = mapping;
        _transport = transport;
        _logger = logger;
        _transport.PacketReceived += OnPacketReceived;
    }

    /// <summary>Raised whenever the connection lifecycle transitions to a new state.</summary>
    public event EventHandler<AtemConnectionState>? ConnectionStateChanged;

    /// <summary>Raised once the connected switcher reports its model name in its post-handshake dump.</summary>
    public event EventHandler<string>? ProductNameChanged;

    public AtemConnectionState State
    {
        get { lock (_sync) { return _state; } }
    }

    /// <summary>Address most recently passed to <see cref="Connect"/>, or null if never connected.</summary>
    public string? TargetIp
    {
        get { lock (_sync) { return _targetIp; } }
    }

    /// <summary>Model name of the connected switcher ("ATEM Mini Pro"), once it has said so.</summary>
    public string? ProductName
    {
        get { lock (_sync) { return _productName; } }
    }

    /// <summary>Number of <see cref="SendCommand"/> calls dropped while not connected.</summary>
    public int DroppedCommandCount => Volatile.Read(ref _droppedCommandCount);

    /// <summary>Hot-swaps the button-to-command mapping table (e.g. after operator re-configuration).</summary>
    public void SetMapping(ButtonCommandMapping mapping)
    {
        lock (_sync)
        {
            _mapping = mapping;
        }
    }

    public void Connect(string ip)
    {
        CancellationTokenSource cts;
        bool changed;
        lock (_sync)
        {
            _targetIp = ip;
            _handshakeCts?.Cancel();
            _handshakeCts?.Dispose();
            cts = new CancellationTokenSource();
            _handshakeCts = cts;
            _sessionId = 0;
            _nextPacketId = 1;
            _productName = null;
            changed = TrySetStateLocked(AtemConnectionState.Connecting);
        }

        if (changed)
        {
            ConnectionStateChanged?.Invoke(this, AtemConnectionState.Connecting);
        }

        _transport.Connect(ip, ProtocolConstants.AtemPort);
        _ = HandshakeLoopAsync(cts.Token, ip);
    }

    public void SendCommand(ButtonEvent buttonEvent)
    {
        AtemCommandMapping? mapping;
        ushort sessionId = 0;
        ushort packetId = 0;
        bool wasConnected;

        lock (_sync)
        {
            wasConnected = _state == AtemConnectionState.Connected;
            mapping = wasConnected && _mapping.TryGetMapping(buttonEvent, out var resolved) ? resolved : null;

            if (mapping is not null)
            {
                sessionId = _sessionId;
                packetId = _nextPacketId;
                _nextPacketId = (ushort)((_nextPacketId + 1) % 0x8000);
            }
        }

        if (!wasConnected)
        {
            Interlocked.Increment(ref _droppedCommandCount);
            _logger.LogDebug(
                "Dropped button event {ControllerId}/{ButtonId}: ATEM controller is not connected.",
                buttonEvent.ControllerId, buttonEvent.ButtonId);
            return;
        }

        if (mapping is null)
        {
            _logger.LogDebug(
                "No ATEM mapping configured for {ControllerId}/{ButtonId}.",
                buttonEvent.ControllerId, buttonEvent.ButtonId);
            return;
        }

        SendPacket(BuildCommandPacket(mapping, sessionId, packetId));
    }

    /// <summary>
    /// Points the switcher's streaming output at <paramref name="request"/>'s URL and, when asked, puts
    /// it on air — the "let the ATEM dial this PC's SRT listener itself" path
    /// (docs/specs/pc-switcher-app.md §2.8). Returns false when no switcher is connected.
    /// <para>
    /// The two command layouts this sends (<c>CRSS</c>/<c>StrR</c>) are reverse-engineered and not
    /// verified against hardware in this repo, which is why nothing calls this implicitly: it runs only
    /// when the operator presses the button for it. A switcher that disagrees about the layout ignores
    /// the block, so the failure mode is "the ATEM did not start streaming", not a disturbed session.
    /// </para>
    /// </summary>
    public bool TrySendStreamingSetup(AtemStreamingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryReservePacket(out var sessionId, out var packetId))
        {
            _logger.LogWarning("Cannot configure ATEM streaming: no switcher is connected.");
            return false;
        }

        SendPacket(BuildBlockPacket(
            AtemCommandNames.SetStreamingService,
            AtemCommandSerializer.BuildStreamingServicePayload(request.ServiceName, request.Url, request.StreamKey),
            sessionId,
            packetId));

        _logger.LogInformation("Sent ATEM streaming destination {Url}.", request.Url);

        if (!request.Start)
        {
            return true;
        }

        if (!TryReservePacket(out sessionId, out packetId))
        {
            return false;
        }

        SendPacket(BuildBlockPacket(
            AtemCommandNames.SetStreamingState,
            AtemCommandSerializer.BuildStreamingStatePayload(streaming: true),
            sessionId,
            packetId));

        _logger.LogInformation("Asked the ATEM to start streaming.");
        return true;
    }

    /// <summary>
    /// Takes the next outgoing packet id, but only while connected. Returns false (reserving nothing)
    /// when there is no session, so callers drop rather than burn sequence numbers off air.
    /// </summary>
    private bool TryReservePacket(out ushort sessionId, out ushort packetId)
    {
        lock (_sync)
        {
            if (_state != AtemConnectionState.Connected)
            {
                sessionId = 0;
                packetId = 0;
                return false;
            }

            sessionId = _sessionId;
            packetId = _nextPacketId;
            _nextPacketId = (ushort)((_nextPacketId + 1) % 0x8000);
            return true;
        }
    }

    private static byte[] BuildBlockPacket(string commandName, ReadOnlySpan<byte> payload, ushort sessionId, ushort packetId)
    {
        var block = AtemCommandSerializer.BuildCommandBlock(commandName, payload);
        var header = new AtemPacketHeader(AtemPacketFlags.AckRequest, 0, sessionId, 0, 0, packetId);
        return AtemCommandSerializer.BuildPacket(header, block);
    }

    private static byte[] BuildCommandPacket(AtemCommandMapping mapping, ushort sessionId, ushort packetId)
    {
        var (name, payload) = mapping.Action switch
        {
            AtemAction.ProgramInput => (AtemCommandNames.ProgramInput, AtemCommandSerializer.BuildInputCommandPayload(mapping.MixEffect, mapping.Source)),
            AtemAction.PreviewInput => (AtemCommandNames.PreviewInput, AtemCommandSerializer.BuildInputCommandPayload(mapping.MixEffect, mapping.Source)),
            AtemAction.Cut => (AtemCommandNames.Cut, AtemCommandSerializer.BuildMixEffectOnlyPayload(mapping.MixEffect)),
            AtemAction.Auto => (AtemCommandNames.Auto, AtemCommandSerializer.BuildMixEffectOnlyPayload(mapping.MixEffect)),
            _ => throw new ArgumentOutOfRangeException(nameof(mapping), mapping.Action, "Unsupported ATEM action."),
        };

        return BuildBlockPacket(name, payload, sessionId, packetId);
    }

    private async Task HandshakeLoopAsync(CancellationToken cancellationToken, string ip)
    {
        for (var attempt = 0; !cancellationToken.IsCancellationRequested; attempt++)
        {
            bool stillHandshaking;
            lock (_sync)
            {
                stillHandshaking = _state != AtemConnectionState.Connected && _targetIp == ip;
            }

            if (!stillHandshaking)
            {
                return;
            }

            SendPacket(AtemHandshake.HelloPacket);

            var delay = BackoffSchedule[Math.Min(attempt, BackoffSchedule.Length - 1)];
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void OnPacketReceived(object? sender, byte[] data)
    {
        if (!AtemPacketHeader.TryParse(data, out var header))
        {
            return;
        }

        if (header.Flags.HasFlag(AtemPacketFlags.NewSessionId))
        {
            CompleteHandshake(header);
        }
        else if (header.Flags.HasFlag(AtemPacketFlags.AckRequest))
        {
            // A state packet from the switcher. It retransmits anything we leave unacknowledged and
            // eventually drops the session, so acking is what keeps a long show's connection alive.
            ushort sessionId;
            bool connected;
            lock (_sync)
            {
                sessionId = _sessionId;
                connected = _state == AtemConnectionState.Connected;
            }

            if (connected)
            {
                SendAck(sessionId, header.PacketId);
            }
        }

        ReadDeviceIdentity(data);
    }

    private void CompleteHandshake(AtemPacketHeader header)
    {
        ushort sessionId;
        ushort ackFor;
        bool changed;
        lock (_sync)
        {
            _sessionId = header.SessionId;
            sessionId = _sessionId;
            ackFor = header.PacketId;
            changed = TrySetStateLocked(AtemConnectionState.Connected);
            _handshakeCts?.Cancel();
        }

        if (changed)
        {
            ConnectionStateChanged?.Invoke(this, AtemConnectionState.Connected);
        }

        SendAck(sessionId, ackFor);
    }

    /// <summary>Picks the model name out of the switcher's state dump so the UI can name what it is
    /// talking to instead of showing a bare IP.</summary>
    private void ReadDeviceIdentity(byte[] data)
    {
        foreach (var (name, payload) in AtemCommandReader.ReadPacket(data))
        {
            if (name != AtemCommandNames.ProductIdentifier ||
                AtemCommandReader.ReadProductIdentifier(payload) is not { } product)
            {
                continue;
            }

            bool changed;
            lock (_sync)
            {
                changed = !string.Equals(_productName, product, StringComparison.Ordinal);
                _productName = product;
            }

            if (changed)
            {
                _logger.LogInformation("Connected ATEM identifies itself as {ProductName}.", product);
                ProductNameChanged?.Invoke(this, product);
            }

            return;
        }
    }

    private void SendAck(ushort sessionId, ushort ackedPacketId)
    {
        var header = new AtemPacketHeader(AtemPacketFlags.AckReply, AtemPacketHeader.Size, sessionId, ackedPacketId, 0, 0);
        var packet = new byte[AtemPacketHeader.Size];
        header.WriteTo(packet);
        SendPacket(packet);
    }

    private void SendPacket(byte[] packet)
    {
        try
        {
            _transport.Send(packet);
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to send ATEM packet.");
        }
    }

    /// <summary>Must be called while holding <see cref="_sync"/>. Returns whether the state actually changed.</summary>
    private bool TrySetStateLocked(AtemConnectionState state)
    {
        if (_state == state)
        {
            return false;
        }

        _state = state;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_sync)
        {
            _handshakeCts?.Cancel();
            _handshakeCts?.Dispose();
            _handshakeCts = null;
        }

        _transport.PacketReceived -= OnPacketReceived;
        _transport.Dispose();
    }
}
