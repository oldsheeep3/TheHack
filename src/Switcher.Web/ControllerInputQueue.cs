using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Serializes controller input events received concurrently from multiple WebSocket connections
/// (e.g. "main" and "sub" controllers) through a single <see cref="Channel{T}"/> so that exactly one
/// event at a time is forwarded to <see cref="IControllerInputSink.Enqueue"/>, preventing races or
/// reordering on simultaneous button presses.
/// </summary>
public sealed class ControllerInputQueue : IAsyncDisposable
{
    private readonly Channel<ButtonEvent> _channel = Channel.CreateUnbounded<ButtonEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly IControllerInputSink _sink;
    private readonly ILogger<ControllerInputQueue> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;

    public ControllerInputQueue(IControllerInputSink sink, ILogger<ControllerInputQueue> logger)
    {
        _sink = sink;
        _logger = logger;
        _processingTask = Task.Run(() => ProcessAsync(_cts.Token));
    }

    /// <summary>Enqueues an event for serialized delivery. Returns false if the queue has been shut down.</summary>
    public bool TryEnqueue(ButtonEvent buttonEvent) => _channel.Writer.TryWrite(buttonEvent);

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var buttonEvent in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    _sink.Enqueue(buttonEvent);
                }
                catch (Exception ex)
                {
                    // A single misbehaving sink must not stop the consumer loop from serializing
                    // subsequent events for other controllers.
                    _logger.LogError(ex, "Controller input sink threw while handling event from {ControllerId}.", buttonEvent.ControllerId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        try
        {
            await _processingTask;
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
