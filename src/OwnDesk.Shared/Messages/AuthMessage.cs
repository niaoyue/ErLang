namespace OwnDesk.Shared.Messages;

public sealed record AuthMessage
{
    public string Type { get; init; } = OwnDeskMessageTypes.Auth;

    public required string Account { get; init; }

    public required string Token { get; init; }

    public string? Password { get; init; }

    public string? SessionToken { get; init; }

    public string? DeviceId { get; init; }

    public string? DeviceName { get; init; }

    public string? SessionId { get; init; }
}
