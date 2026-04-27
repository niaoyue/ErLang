namespace OwnDesk.Shared.Messages;

public sealed record RegisterMemberRequest
{
    public required string OrganizationToken { get; init; }

    public required string Username { get; init; }

    public required string Password { get; init; }
}

public sealed record LoginMemberRequest
{
    public required string OrganizationToken { get; init; }

    public required string Username { get; init; }

    public required string Password { get; init; }
}

public sealed record AuthSessionDto
{
    public required string Username { get; init; }

    public required string SessionToken { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
