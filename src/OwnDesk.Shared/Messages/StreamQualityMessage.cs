namespace OwnDesk.Shared.Messages;

public sealed record StreamQualityMessage
{
    public string Type { get; init; } = OwnDeskMessageTypes.StreamQuality;

    public required string Profile { get; init; }
}
