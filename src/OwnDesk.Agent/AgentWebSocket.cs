using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace OwnDesk.Agent;

internal static class AgentWebSocket
{
    private const int BufferSize = 16 * 1024;

    public static async Task SendTextAsync(WebSocket socket, string text, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
    }

    public static async Task SendBinaryAsync(WebSocket socket, byte[] payload, CancellationToken cancellationToken)
    {
        await socket.SendAsync(payload, WebSocketMessageType.Binary, true, cancellationToken);
    }

    public static async Task<string?> ReceiveStringAsync(
        WebSocket socket,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            using var stream = new MemoryStream();

            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    if (result.EndOfMessage)
                    {
                        return null;
                    }

                    continue;
                }

                stream.Write(buffer, 0, result.Count);
                if (stream.Length > maxBytes)
                {
                    throw new InvalidOperationException($"WebSocket message exceeded {maxBytes} bytes.");
                }

                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(stream.ToArray());
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
