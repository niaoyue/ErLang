using System.Net.WebSockets;
using System.Text;

namespace OwnDesk.Server;

internal sealed class SafeWebSocket
{
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    public SafeWebSocket(WebSocket socket)
    {
        Socket = socket;
    }

    public WebSocket Socket { get; }

    public bool IsOpen => Socket.State == WebSocketState.Open;

    public async Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        if (!IsOpen)
        {
            return;
        }

        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsOpen)
            {
                return;
            }

            var payload = Encoding.UTF8.GetBytes(text);
            await Socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public async Task SendBinaryAsync(byte[] payload, CancellationToken cancellationToken)
    {
        if (!IsOpen)
        {
            return;
        }

        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsOpen)
            {
                return;
            }

            await Socket.SendAsync(payload, WebSocketMessageType.Binary, true, cancellationToken);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public void Abort()
    {
        try
        {
            Socket.Abort();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
