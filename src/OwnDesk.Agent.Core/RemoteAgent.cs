using System.Net.WebSockets;
using System.Text.Json;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;
using OwnDesk.Shared.Transport;

namespace OwnDesk.Agent;

internal sealed class RemoteAgent
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private const int MaxControlMessageBytes = 256 * 1024;

    private readonly AgentOptions _options;
    private readonly ScreenCaptureService _screenCapture;
    private readonly InputController _inputController;
    private readonly StreamQualityController _qualityController;
    private readonly RemoteControlHandler _controlHandler;
    private readonly WebRtcMediaState _webRtcMediaState = new();

    public RemoteAgent(
        AgentOptions options,
        ScreenCaptureService screenCapture,
        InputController inputController,
        StreamQualityController qualityController)
    {
        _options = options;
        _screenCapture = screenCapture;
        _inputController = inputController;
        _qualityController = qualityController;
        _controlHandler = new RemoteControlHandler(inputController, qualityController);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var relayTask = RunRelayAsync(cancellationToken);
        if (!_options.EnableWebRtc)
        {
            await relayTask;
            return;
        }

        var webRtcConfig = await WebRtcIceConfigClient.FetchAsync(_options, cancellationToken);
        var webRtcTask = new WebRtcAgentSignalingClient(
            _options,
            _screenCapture,
            _qualityController,
            _controlHandler,
            _webRtcMediaState,
            webRtcConfig).RunAsync(cancellationToken);
        await Task.WhenAll(relayTask, webRtcTask);
    }

    private async Task RunRelayAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection error: {ex.Message}");
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"Reconnecting in {ReconnectDelay.TotalSeconds:n0}s...");
                await Task.Delay(ReconnectDelay, cancellationToken);
            }
        }
    }

    private async Task RunConnectionAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket
        {
            Options =
            {
                KeepAliveInterval = TimeSpan.FromSeconds(20)
            }
        };

        var uri = EndpointUris.BuildWebSocketUri(
            _options.Server,
            "/ws/agent",
            new Dictionary<string, string>());

        Console.WriteLine($"Connecting to {uri}...");
        await socket.ConnectAsync(uri, cancellationToken);
        await SendAuthAsync(socket, cancellationToken);
        Console.WriteLine("Connected.");

        await SendHelloAsync(socket, cancellationToken);

        using var connectionStopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var captureTask = CaptureLoopAsync(socket, connectionStopped.Token);
        var receiveTask = ReceiveLoopAsync(socket, connectionStopped.Token);

        await Task.WhenAny(captureTask, receiveTask);
        await connectionStopped.CancelAsync();

        await ObserveAsync(captureTask);
        await ObserveAsync(receiveTask);
    }

    private async Task SendHelloAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var size = _screenCapture.GetPrimaryScreenSize();
        var hello = new AgentHelloMessage
        {
            DeviceId = _options.DeviceId,
            DeviceName = _options.DeviceName,
            ScreenWidth = size.Width,
            ScreenHeight = size.Height,
            SentAtUtc = DateTimeOffset.UtcNow
        };

        await AgentWebSocket.SendTextAsync(
            socket,
            JsonSerializer.Serialize(hello, JsonDefaults.Options),
            cancellationToken);
    }

    private async Task CaptureLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var sequence = 0L;
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var settings = _qualityController.Current;
            if (_webRtcMediaState.HasActiveVideo)
            {
                await Task.Delay(settings.FrameDelay, cancellationToken);
                continue;
            }

            var frame = _screenCapture.CaptureJpeg(settings.JpegQuality, settings.MaxWidth, settings.MaxHeight);
            _inputController.UpdateFrameSize(frame.Width, frame.Height);
            var header = new BinaryFrameHeader
            {
                Sequence = ++sequence,
                Width = frame.Width,
                Height = frame.Height,
                CapturedAtUtc = DateTimeOffset.UtcNow,
                ByteLength = frame.JpegBytes.Length
            };

            await AgentWebSocket.SendBinaryAsync(socket, BinaryFrameCodec.Encode(header, frame.JpegBytes), cancellationToken);

            await Task.Delay(settings.FrameDelay, cancellationToken);
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var text = await AgentWebSocket.ReceiveStringAsync(socket, MaxControlMessageBytes, cancellationToken);
            if (text is null)
            {
                break;
            }

            _controlHandler.HandleJson(text);
        }
    }

    private async Task SendAuthAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        await AgentWebSocket.SendTextAsync(
            socket,
            JsonSerializer.Serialize(
                new AuthMessage
                {
                    Account = _options.Account,
                    Token = _options.Token,
                    Password = _options.Password,
                    DeviceId = _options.DeviceId,
                    DeviceName = _options.DeviceName
                },
                JsonDefaults.Options),
            cancellationToken);
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
