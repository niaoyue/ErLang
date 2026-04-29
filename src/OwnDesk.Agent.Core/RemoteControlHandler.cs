using System.Text.Json;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;

namespace OwnDesk.Agent;

internal sealed class RemoteControlHandler
{
    private const int MaxTextInputChars = 1024;
    private const int MaxTextInputCharsPerWindow = 4096;
    private static readonly TimeSpan TextInputWindow = TimeSpan.FromSeconds(5);

    private readonly InputController _inputController;
    private readonly StreamQualityController _qualityController;
    private readonly Queue<(DateTimeOffset At, int Count)> _recentTextInputs = new();
    private readonly object _inputRateGate = new();
    private int _relayVideoEnabled = 1;

    public RemoteControlHandler(InputController inputController, StreamQualityController qualityController)
    {
        _inputController = inputController;
        _qualityController = qualityController;
    }

    public void UpdateFrameSize(int width, int height)
    {
        _inputController.UpdateFrameSize(width, height);
    }

    public bool RelayVideoEnabled => Volatile.Read(ref _relayVideoEnabled) == 1;

    public void HandleJson(string text)
    {
        using var document = JsonDocument.Parse(text);
        if (!document.RootElement.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        var type = typeElement.GetString();
        if (type == OwnDeskMessageTypes.Input)
        {
            var command = JsonSerializer.Deserialize<InputCommand>(text, JsonDefaults.Options);
            if (command is not null && IsAllowedInput(command))
            {
                _inputController.Apply(command);
            }
        }
        else if (type == OwnDeskMessageTypes.StreamQuality)
        {
            var message = JsonSerializer.Deserialize<StreamQualityMessage>(text, JsonDefaults.Options);
            if (message is not null && _qualityController.TryApplyProfile(message.Profile, out var settings))
            {
                Console.WriteLine(
                    $"Stream quality changed: {settings.Profile} {settings.FramesPerSecond}fps JPEG={settings.JpegQuality} max={settings.MaxWidth}x{settings.MaxHeight} WebRTC={settings.WebRtcBitrateKbps}kbps");
            }
        }
        else if (type == OwnDeskMessageTypes.RelayVideo)
        {
            var enabled = !document.RootElement.TryGetProperty("enabled", out var enabledElement) ||
                          enabledElement.ValueKind != JsonValueKind.False;
            Volatile.Write(ref _relayVideoEnabled, enabled ? 1 : 0);
            Console.WriteLine(enabled ? "Relay JPEG stream resumed." : "Relay JPEG stream paused.");
        }
    }

    private bool IsAllowedInput(InputCommand command)
    {
        return command.Event switch
        {
            "mouseMove" or "mouseDown" or "mouseUp" or "mouseClick" or "wheel" => HasFiniteCoordinates(command),
            "keyDown" or "keyUp" => command.KeyCode is >= 0 and <= 255,
            "text" => IsAllowedTextInput(command.Text),
            _ => false
        };
    }

    private static bool HasFiniteCoordinates(InputCommand command)
    {
        return double.IsFinite(command.X) && double.IsFinite(command.Y);
    }

    private bool IsAllowedTextInput(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxTextInputChars)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        lock (_inputRateGate)
        {
            while (_recentTextInputs.Count > 0 && now - _recentTextInputs.Peek().At > TextInputWindow)
            {
                _recentTextInputs.Dequeue();
            }

            var current = _recentTextInputs.Sum(item => item.Count);
            if (current + text.Length > MaxTextInputCharsPerWindow)
            {
                return false;
            }

            _recentTextInputs.Enqueue((now, text.Length));
            return true;
        }
    }
}
