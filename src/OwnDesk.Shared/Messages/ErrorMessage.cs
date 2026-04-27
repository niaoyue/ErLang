namespace OwnDesk.Shared.Messages;

public sealed record ErrorMessage
{
    public string Type { get; init; } = OwnDeskMessageTypes.Error;

    public required string Message { get; init; }
}

