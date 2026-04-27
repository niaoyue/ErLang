using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace OwnDesk.Server;

internal static class WebSocketMessages
{
    private const int BufferSize = 32 * 1024;

    public static async Task<WebSocketMessage?> ReceiveAsync(
        WebSocket socket,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            using var stream = new MemoryStream();
            WebSocketMessageType? messageType = null;

            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                messageType ??= result.MessageType;
                stream.Write(buffer, 0, result.Count);
                if (stream.Length > maxBytes)
                {
                    throw new InvalidOperationException($"WebSocket message exceeded {maxBytes} bytes.");
                }

                if (result.EndOfMessage)
                {
                    return new WebSocketMessage(messageType.Value, stream.ToArray());
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static string AsText(WebSocketMessage message)
    {
        return Encoding.UTF8.GetString(message.Payload);
    }
}

