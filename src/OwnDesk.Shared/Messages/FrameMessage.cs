namespace OwnDesk.Shared.Messages;

public sealed record FrameMessage
{
    public string Type { get; init; } = OwnDeskMessageTypes.Frame;

    public required long Sequence { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required DateTimeOffset CapturedAtUtc { get; init; }

    public required string ImageBase64 { get; init; }
}

