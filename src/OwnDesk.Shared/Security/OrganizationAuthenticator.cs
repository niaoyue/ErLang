using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OwnDesk.Shared.Messages;

namespace OwnDesk.Shared.Security;

public sealed class OrganizationAuthenticator
{
    private const int PasswordIterations = 100_000;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions StoreJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _organizationToken;
    private readonly string _organizationId;
    private readonly string _storePath;
    private readonly object _storeGate = new();
    private readonly ConcurrentDictionary<string, MemberSession> _sessions = new(StringComparer.Ordinal);
    private AuthStore _store;

    public OrganizationAuthenticator(string organizationToken, string storePath)
    {
        if (string.IsNullOrWhiteSpace(organizationToken))
        {
            throw new ArgumentException("Organization token cannot be empty.", nameof(organizationToken));
        }

        if (string.IsNullOrWhiteSpace(storePath))
        {
            throw new ArgumentException("Auth store path cannot be empty.", nameof(storePath));
        }

        _organizationToken = organizationToken;
        _organizationId = CreateOrganizationId(organizationToken);
        _storePath = storePath;
        _store = LoadStore(storePath);
    }

    public string OrganizationId => _organizationId;

    public AuthenticatedMember? Authenticate(AuthMessage message)
    {
        if (message.Type != OwnDeskMessageTypes.Auth || !IsOrganizationToken(message.Token))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(message.SessionToken) &&
            TryAuthenticateSession(message.Account, message.SessionToken, out var sessionMember))
        {
            return sessionMember;
        }

        return AuthenticateCredentials(message.Token, message.Account, message.Password);
    }

    public AuthenticatedMember? AuthenticateCredentials(string? organizationToken, string? username, string? password)
    {
        if (!IsOrganizationToken(organizationToken) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var normalized = NormalizeUsername(username);
        MemberRecord? member;
        lock (_storeGate)
        {
            member = GetOrganizationMembers().GetValueOrDefault(normalized);
        }

        if (member is null || !VerifyPassword(password, member))
        {
            return null;
        }

        return new AuthenticatedMember(_organizationId, member.Username);
    }

    public AuthSessionDto Register(RegisterMemberRequest request)
    {
        EnsureOrganizationToken(request.OrganizationToken);
        var username = ValidateUsername(request.Username);
        var password = ValidatePassword(request.Password);
        var normalized = NormalizeUsername(username);

        lock (_storeGate)
        {
            var members = GetOrganizationMembers();
            if (members.ContainsKey(normalized))
            {
                throw new InvalidOperationException("Username is already registered.");
            }

            members[normalized] = CreateMember(username, password);
            SaveStore();
        }

        return CreateSession(username);
    }

    public AuthSessionDto Login(LoginMemberRequest request)
    {
        var member = AuthenticateCredentials(request.OrganizationToken, request.Username, request.Password);
        if (member is null)
        {
            throw new UnauthorizedAccessException("Invalid organization token, username, or password.");
        }

        return CreateSession(member.Username);
    }

    private bool TryAuthenticateSession(string? username, string sessionToken, out AuthenticatedMember? member)
    {
        member = null;
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        if (!_sessions.TryGetValue(sessionToken, out var session))
        {
            return false;
        }

        if (session.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(sessionToken, out _);
            return false;
        }

        if (session.OrganizationId != _organizationId ||
            !FixedTimeEquals(NormalizeUsername(username), NormalizeUsername(session.Username)))
        {
            return false;
        }

        member = new AuthenticatedMember(session.OrganizationId, session.Username);
        return true;
    }

    private AuthSessionDto CreateSession(string username)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var sessionToken = Convert.ToBase64String(tokenBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(SessionLifetime);
        _sessions[sessionToken] = new MemberSession(_organizationId, username, expiresAtUtc);

        return new AuthSessionDto
        {
            Username = username,
            SessionToken = sessionToken,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    private void EnsureOrganizationToken(string? organizationToken)
    {
        if (!IsOrganizationToken(organizationToken))
        {
            throw new UnauthorizedAccessException("Invalid organization token.");
        }
    }

    private bool IsOrganizationToken(string? organizationToken)
    {
        return FixedTimeEquals(organizationToken, _organizationToken);
    }

    private Dictionary<string, MemberRecord> GetOrganizationMembers()
    {
        if (!_store.Organizations.TryGetValue(_organizationId, out var members))
        {
            members = new Dictionary<string, MemberRecord>(StringComparer.OrdinalIgnoreCase);
            _store.Organizations[_organizationId] = members;
        }

        return members;
    }

    private static MemberRecord CreateMember(string username, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(password, salt, PasswordIterations);
        return new MemberRecord
        {
            Username = username,
            Salt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(hash),
            Iterations = PasswordIterations,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static bool VerifyPassword(string password, MemberRecord member)
    {
        var salt = Convert.FromBase64String(member.Salt);
        var expectedHash = Convert.FromBase64String(member.PasswordHash);
        var actualHash = HashPassword(password, salt, member.Iterations);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static byte[] HashPassword(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
    }

    private static string ValidateUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username is required.");
        }

        username = username.Trim();
        if (username.Length is < 3 or > 64)
        {
            throw new ArgumentException("Username must be 3-64 characters.");
        }

        foreach (var character in username)
        {
            if (!char.IsLetterOrDigit(character) && character is not '.' and not '_' and not '-' and not '@')
            {
                throw new ArgumentException("Username can only contain letters, digits, '.', '_', '-', or '@'.");
            }
        }

        return username;
    }

    private static string ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password is required.");
        }

        if (password.Length < 8)
        {
            throw new ArgumentException("Password must be at least 8 characters.");
        }

        return password;
    }

    private static string NormalizeUsername(string username)
    {
        return username.Trim().ToLowerInvariant();
    }

    private static string CreateOrganizationId(string organizationToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(organizationToken));
        return $"org_{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }

    private static bool FixedTimeEquals(string? left, string right)
    {
        if (left is null)
        {
            return false;
        }

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static AuthStore LoadStore(string storePath)
    {
        try
        {
            if (!File.Exists(storePath))
            {
                return new AuthStore();
            }

            var json = File.ReadAllText(storePath);
            return JsonSerializer.Deserialize<AuthStore>(json, StoreJsonOptions) ?? new AuthStore();
        }
        catch
        {
            return new AuthStore();
        }
    }

    private void SaveStore()
    {
        var directory = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_storePath, JsonSerializer.Serialize(_store, StoreJsonOptions));
    }

    private sealed record MemberSession(string OrganizationId, string Username, DateTimeOffset ExpiresAtUtc);

    private sealed class AuthStore
    {
        public Dictionary<string, Dictionary<string, MemberRecord>> Organizations { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class MemberRecord
    {
        public string Username { get; set; } = string.Empty;

        public string Salt { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;

        public int Iterations { get; set; }

        public DateTimeOffset CreatedAtUtc { get; set; }
    }
}

public sealed record AuthenticatedMember(string OrganizationId, string Username);
