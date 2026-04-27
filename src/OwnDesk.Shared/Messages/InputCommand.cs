namespace OwnDesk.Shared.Messages;

public sealed record InputCommand
{
    public string Type { get; init; } = OwnDeskMessageTypes.Input;

    public required string Event { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public string? Button { get; init; }

    public string? Key { get; init; }

    public int? KeyCode { get; init; }

    public string? Text { get; init; }

    public double? DeltaY { get; init; }
}
