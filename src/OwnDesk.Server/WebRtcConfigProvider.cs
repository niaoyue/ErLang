using OwnDesk.Shared.Messages;

namespace OwnDesk.Server;

internal sealed class WebRtcConfigProvider
{
    private static readonly char[] UrlSeparators = [',', ' ', '\t', '\r', '\n'];
    private readonly WebRtcConfigDto _config;

    public WebRtcConfigProvider(IConfiguration configuration)
    {
        _config = new WebRtcConfigDto
        {
            IceServers = ReadIceServers(configuration.GetSection("OwnDesk:IceServers")).ToArray()
        };
    }

    public WebRtcConfigDto GetConfig() => _config;

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

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
