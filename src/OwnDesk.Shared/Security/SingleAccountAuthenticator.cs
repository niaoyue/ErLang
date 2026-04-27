using System.Security.Cryptography;
using System.Text;

namespace OwnDesk.Shared.Security;

public sealed class SingleAccountAuthenticator
{
    private readonly string _account;
    private readonly string _token;

    public SingleAccountAuthenticator(string account, string token)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            throw new ArgumentException("Account cannot be empty.", nameof(account));
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token cannot be empty.", nameof(token));
        }

        _account = account;
        _token = token;
    }

    public string Account => _account;

    public bool IsAuthorized(string? account, string? token)
    {
        return FixedTimeEquals(account, _account) && FixedTimeEquals(token, _token);
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
}

