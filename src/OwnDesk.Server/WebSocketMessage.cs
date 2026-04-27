using System.Net.WebSockets;

namespace OwnDesk.Server;

internal sealed record WebSocketMessage(WebSocketMessageType MessageType, byte[] Payload)
{
    public bool IsText => MessageType == WebSocketMessageType.Text;

    public bool IsBinary => MessageType == WebSocketMessageType.Binary;
}

