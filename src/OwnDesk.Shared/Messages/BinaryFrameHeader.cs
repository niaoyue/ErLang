namespace OwnDesk.Shared.Messages;

public sealed record BinaryFrameHeader
{
    public string Type { get; init; } = OwnDeskMessageTypes.Frame;

    public required long Sequence { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required DateTimeOffset CapturedAtUtc { get; init; }

    public string Format { get; init; } = "jpeg";

    public required int ByteLength { get; init; }
}

