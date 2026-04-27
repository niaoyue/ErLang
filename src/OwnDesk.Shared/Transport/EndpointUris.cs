using System.Text;

namespace OwnDesk.Shared.Transport;

public static class EndpointUris
{
    public static Uri BuildWebSocketUri(
        string serverBaseUri,
        string endpointPath,
        IReadOnlyDictionary<string, string> query)
    {
        if (string.IsNullOrWhiteSpace(serverBaseUri))
        {
            throw new ArgumentException("Server base URI cannot be empty.", nameof(serverBaseUri));
        }

        if (string.IsNullOrWhiteSpace(endpointPath))
        {
            throw new ArgumentException("Endpoint path cannot be empty.", nameof(endpointPath));
        }

        var baseUri = new Uri(
            serverBaseUri.EndsWith("/", StringComparison.Ordinal) ? serverBaseUri : $"{serverBaseUri}/",
            UriKind.Absolute);

        var builder = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme switch
            {
                "http" => "ws",
                "https" => "wss",
                "ws" => "ws",
                "wss" => "wss",
                _ => throw new ArgumentException($"Unsupported server URI scheme: {baseUri.Scheme}.", nameof(serverBaseUri))
            }
        };

        var basePath = builder.Path.Trim('/');
        var endpoint = endpointPath.Trim('/');
        builder.Path = string.IsNullOrEmpty(basePath) ? endpoint : $"{basePath}/{endpoint}";
        builder.Query = BuildQuery(query);
        return builder.Uri;
    }

    private static string BuildQuery(IReadOnlyDictionary<string, string> query)
    {
        var builder = new StringBuilder();

        foreach (var (key, value) in query)
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder
                .Append(Uri.EscapeDataString(key))
                .Append('=')
                .Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }
}
