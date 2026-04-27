namespace OwnDesk.Shared.Messages;

public sealed record DeviceInfoDto
{
    public required string DeviceId { get; init; }

    public required string DeviceName { get; init; }

    public required DateTimeOffset ConnectedAtUtc { get; init; }

    public required DateTimeOffset LastSeenUtc { get; init; }

    public required int ScreenWidth { get; init; }

    public required int ScreenHeight { get; init; }

    public required bool Online { get; init; }
}
