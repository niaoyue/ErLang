using System.Net.Http.Json;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;

namespace OwnDesk.Agent;

internal static class WebRtcIceConfigClient
{
    private static readonly HttpClient Http = new();

    public static async Task<WebRtcIceServerDto[]> FetchAsync(
        AgentOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.PostAsJsonAsync(
                BuildEndpoint(options.Server, "/api/webrtc/config"),
                new AuthMessage
                {
                    Account = options.Account,
                    Token = options.Token,
                    Password = options.Password,
                    DeviceId = options.DeviceId,
                    DeviceName = options.DeviceName
                },
                JsonDefaults.Options,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "WebRTC ICE config endpoint missing on server; using host candidates."
                        : $"WebRTC ICE config skipped: HTTP {(int)response.StatusCode}; using host candidates.");
                return [];
            }

            var config = await response.Content.ReadFromJsonAsync<WebRtcConfigDto>(JsonDefaults.Options, cancellationToken);
            var iceServers = config?.IceServers ?? [];
            Console.WriteLine($"WebRTC ICE config loaded: {iceServers.Length} server(s).");
            return iceServers;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WebRTC ICE config skipped: {ex.Message}; using host candidates.");
            return [];
        }
    }

    private static Uri BuildEndpoint(string server, string path)
    {
        var baseUri = new Uri(
            server.EndsWith("/", StringComparison.Ordinal) ? server : $"{server}/",
            UriKind.Absolute);
        var builder = new UriBuilder(baseUri);
        var basePath = builder.Path.Trim('/');
        var endpoint = path.Trim('/');
        builder.Path = string.IsNullOrEmpty(basePath) ? endpoint : $"{basePath}/{endpoint}";
        builder.Query = string.Empty;
        return builder.Uri;
    }
}
