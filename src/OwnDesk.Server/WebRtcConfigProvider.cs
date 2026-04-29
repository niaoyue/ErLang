using System.Text.Json;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;

namespace OwnDesk.Server;

internal sealed class WebRtcConfigProvider
{
    private static readonly char[] UrlSeparators = [',', ' ', '\t', '\r', '\n'];
    private readonly WebRtcConfigDto _config;

    public WebRtcConfigProvider(IConfiguration configuration)
    {
        var iceServers = ReadIceServers(configuration).ToArray();
        var relayConfigured = iceServers
            .SelectMany(server => server.Urls)
            .Any(IsTurnUrl);
        var requestedPolicy = NormalizeIceTransportPolicy(
            Environment.GetEnvironmentVariable("OWNDESK_WEBRTC_ICE_TRANSPORT_POLICY")
            ?? configuration["OwnDesk:IceTransportPolicy"]
            ?? configuration["OwnDesk:WebRtc:IceTransportPolicy"]
            ?? "all");
        _config = new WebRtcConfigDto
        {
            IceServers = iceServers,
            IceTransportPolicy = requestedPolicy == "relay" && relayConfigured ? "relay" : "all",
            RelayConfigured = relayConfigured
        };
    }

    public WebRtcConfigDto GetConfig() => _config;

    private static IEnumerable<WebRtcIceServerDto> ReadIceServers(IConfiguration configuration)
    {
        var environmentValue = Environment.GetEnvironmentVariable("OWNDESK_WEBRTC_ICE_SERVERS");
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return ReadEnvironmentIceServers(environmentValue);
        }

        var configured = ReadIceServers(configuration.GetSection("OwnDesk:IceServers")).ToArray();
        return configured.Length > 0
            ? configured
            : ReadIceServers(configuration.GetSection("OwnDesk:WebRtc:IceServers"));
    }

    private static IEnumerable<WebRtcIceServerDto> ReadIceServers(IConfigurationSection section)
    {
        foreach (var child in section.GetChildren())
        {
            var urls = ReadUrls(child).ToArray();
            if (urls.Length == 0)
            {
                continue;
            }

            yield return new WebRtcIceServerDto
            {
                Urls = urls,
                Username = EmptyToNull(child["Username"]),
                Credential = EmptyToNull(child["Credential"]),
                CredentialType = EmptyToNull(child["CredentialType"])
            };
        }
    }

    private static IEnumerable<string> ReadUrls(IConfigurationSection section)
    {
        var children = section.GetSection("Urls").GetChildren()
            .Select(value => value.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim());
        foreach (var child in children)
        {
            yield return child;
        }

        foreach (var value in SplitUrls(section["Urls"]))
        {
            yield return value;
        }

        foreach (var value in SplitUrls(section["Url"]))
        {
            yield return value;
        }
    }

    private static IEnumerable<string> SplitUrls(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(UrlSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IReadOnlyList<WebRtcIceServerDto> ReadEnvironmentIceServers(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal) ||
            trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            WebRtcIceServerDto[]? parsed = trimmed.StartsWith("[", StringComparison.Ordinal)
                ? JsonSerializer.Deserialize<WebRtcIceServerDto[]>(trimmed, JsonDefaults.Options)
                : [JsonSerializer.Deserialize<WebRtcIceServerDto>(trimmed, JsonDefaults.Options)!];

            return parsed?
                .Select(NormalizeIceServer)
                .Where(server => server is not null)
                .Cast<WebRtcIceServerDto>()
                .ToArray() ?? [];
        }

        return trimmed
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseDelimitedIceServer)
            .Where(server => server is not null)
            .Cast<WebRtcIceServerDto>()
            .ToArray();
    }

    private static WebRtcIceServerDto? ParseDelimitedIceServer(string value)
    {
        var parts = value.Split(';', 4, StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        return NormalizeIceServer(new WebRtcIceServerDto
        {
            Urls = SplitUrls(parts[0]).ToArray(),
            Username = parts.Length > 1 ? EmptyToNull(parts[1]) : null,
            Credential = parts.Length > 2 ? EmptyToNull(parts[2]) : null,
            CredentialType = parts.Length > 3 ? EmptyToNull(parts[3]) : null
        });
    }

    private static WebRtcIceServerDto? NormalizeIceServer(WebRtcIceServerDto? server)
    {
        if (server is null)
        {
            return null;
        }

        var urls = server.Urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (urls.Length == 0)
        {
            return null;
        }

        return server with
        {
            Urls = urls,
            Username = EmptyToNull(server.Username),
            Credential = EmptyToNull(server.Credential),
            CredentialType = EmptyToNull(server.CredentialType)
        };
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeIceTransportPolicy(string value)
    {
        return value.Trim().Equals("relay", StringComparison.OrdinalIgnoreCase)
            ? "relay"
            : "all";
    }

    private static bool IsTurnUrl(string url)
    {
        return url.StartsWith("turn:", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("turns:", StringComparison.OrdinalIgnoreCase);
    }
}
