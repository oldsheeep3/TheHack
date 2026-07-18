using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

/// <summary>
/// Accepts the <c>/ws</c> controller-input WebSocket connection and feeds decoded
/// <see cref="ButtonEvent"/>s into the shared <see cref="ControllerInputQueue"/>.
/// </summary>
/// <remarks>
/// Superseded by the HID input path (agent-A2-004-hid-io); this WebSocket route is retained purely
/// for the optional wireless controller fallback (docs/specs/pc-switcher-app.md §2.6) and is not the
/// primary controller input path going forward.
/// </remarks>
internal static class ControllerWebSocketEndpoint
{
    private const int ReceiveBufferSize = 4 * 1024;
    private const int MaxMessageBytes = 16 * 1024;

    public static async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var queue = context.RequestServices.GetRequiredService<ControllerInputQueue>();
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Switcher.Web.ControllerWebSocket");

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await ReceiveLoopAsync(socket, queue, logger, context.RequestAborted);
    }

    private static async Task ReceiveLoopAsync(
        WebSocket socket,
        ControllerInputQueue queue,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferSize];
        using var messageStream = new MemoryStream();

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                messageStream.SetLength(0);
                var result = await ReceiveMessageAsync(socket, buffer, messageStream, cancellationToken);
                if (result is null)
                {
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    ProcessMessage(messageStream.ToArray(), queue, logger);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the connection/host is shutting down.
        }
        catch (WebSocketException ex)
        {
            logger.LogInformation(ex, "Controller WebSocket connection closed unexpectedly.");
        }
    }

    private static async Task<WebSocketReceiveResult?> ReceiveMessageAsync(
        WebSocket socket,
        byte[] buffer,
        MemoryStream messageStream,
        CancellationToken cancellationToken)
    {
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, statusDescription: null, cancellationToken);
                return null;
            }

            if (messageStream.Length + result.Count > MaxMessageBytes)
            {
                await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "message too large", cancellationToken);
                return null;
            }

            messageStream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return result;
    }

    private static void ProcessMessage(byte[] payload, ControllerInputQueue queue, ILogger logger)
    {
        WsEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<WsEnvelope>(payload, ProtocolJsonOptions.Default);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse controller input WebSocket message.");
            return;
        }

        if (envelope?.Data is null || string.IsNullOrWhiteSpace(envelope.Data.ControllerId))
        {
            logger.LogWarning("Ignoring malformed controller input envelope.");
            return;
        }

        if (!queue.TryEnqueue(envelope.Data))
        {
            logger.LogWarning("Controller input queue rejected event from {ControllerId} (queue closed).", envelope.Data.ControllerId);
        }
    }
}
