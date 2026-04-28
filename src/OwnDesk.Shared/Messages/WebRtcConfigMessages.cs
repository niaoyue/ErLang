namespace OwnDesk.Shared.Messages;

public sealed record WebRtcConfigDto
{
    public WebRtcIceServerDto[] IceServers { get; init; } = [];
}

public sealed record WebRtcIceServerDto
{
    public string[] Urls { get; init; } = [];

    public string? Username { get; init; }

    public string? Credential { get; init; }

    public string? CredentialType { get; init; }
}
