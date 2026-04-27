namespace OwnDesk.Shared.Messages;

public sealed record AgentHelloMessage
{
    public string Type { get; init; } = OwnDeskMessageTypes.AgentHello;

    public required string DeviceId { get; init; }

    public required string DeviceName { get; init; }

    public required int ScreenWidth { get; init; }

    public required int ScreenHeight { get; init; }

    public DateTimeOffset SentAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

