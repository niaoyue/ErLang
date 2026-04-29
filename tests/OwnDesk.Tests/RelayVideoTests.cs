using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OwnDesk.Server;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;
using Xunit;

namespace OwnDesk.Tests;

public sealed class RelayVideoTests
{
    [Fact]
    public async Task DeviceRegistryAggregatesRelayVideoDemand()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var registry = new DeviceRegistry(new JsonDeviceRecordStore(storePath));
        var agentSocket = new RecordingWebSocket();
        var session = await registry.RegisterAgentAsync(
            "org-1",
            "pc-1",
            "PC 1",
            agentSocket,
            CancellationToken.None);

        var firstViewer = registry.AddViewer("org-1", "pc-1", new TestWebSocket());
        Assert.NotNull(firstViewer);
        await registry.SetViewerRelayVideoAsync(firstViewer, false, CancellationToken.None);
        AssertRelayVideo(agentSocket.SentText.Last(), enabled: false);

        var secondViewer = registry.AddViewer("org-1", "pc-1", new TestWebSocket());
        Assert.NotNull(secondViewer);
        await registry.SendRelayVideoDemandAsync(session, CancellationToken.None);
        AssertRelayVideo(agentSocket.SentText.Last(), enabled: true);

        await registry.SetViewerRelayVideoAsync(secondViewer, false, CancellationToken.None);
        AssertRelayVideo(agentSocket.SentText.Last(), enabled: false);
    }

    private static void AssertRelayVideo(string text, bool enabled)
    {
        using var document = JsonDocument.Parse(text);
        Assert.Equal(OwnDeskMessageTypes.RelayVideo, document.RootElement.GetProperty("type").GetString());
        Assert.Equal(enabled, document.RootElement.GetProperty("enabled").GetBoolean());
    }

    private class TestWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => _state;

        public override string? SubProtocol => null;

        public override void Abort()
        {
            _state = WebSocketState.Aborted;
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _state = WebSocketState.Closed;
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingWebSocket : TestWebSocket
    {
        public List<string> SentText { get; } = [];

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            if (messageType == WebSocketMessageType.Text)
            {
                SentText.Add(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            }

            return Task.CompletedTask;
        }
    }
}
