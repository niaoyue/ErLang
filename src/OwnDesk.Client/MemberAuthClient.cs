using System.Net.Http.Json;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;

namespace OwnDesk.Client;

internal sealed class MemberAuthClient
{
    private static readonly HttpClient Http = new();

    public Task<AuthSessionDto> LoginAsync(
        ClientOrganization organization,
        string account,
        string password,
        CancellationToken cancellationToken)
    {
        return SendAsync(
            organization,
            "/api/login",
            new LoginMemberRequest
            {
                OrganizationToken = organization.Token,
                Username = account,
                Password = password
            },
            cancellationToken);
    }

    public Task<AuthSessionDto> RegisterAsync(
        ClientOrganization organization,
        string account,
        string password,
        CancellationToken cancellationToken)
    {
        return SendAsync(
            organization,
            "/api/register",
            new RegisterMemberRequest
            {
                OrganizationToken = organization.Token,
                Username = account,
                Password = password
            },
            cancellationToken);
    }

    private static async Task<AuthSessionDto> SendAsync<TRequest>(
        ClientOrganization organization,
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(organization.Server, path);
        using var response = await Http.PostAsJsonAsync(endpoint, request, JsonDefaults.Options, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ErrorMessageAsync(response, cancellationToken));
        }

        return await response.Content.ReadFromJsonAsync<AuthSessionDto>(JsonDefaults.Options, cancellationToken)
               ?? throw new InvalidOperationException("认证响应为空。");
    }

    private static Uri BuildEndpoint(string server, string path)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            throw new InvalidOperationException("请先填写组织的服务器 URL。");
        }

        if (!Uri.TryCreate($"{server.TrimEnd('/')}{path}", UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("组织的服务器 URL 无效。");
        }

        return endpoint;
    }

    private static async Task<string> ErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return "组织 Token、账号或密码无效。";
        }

        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorMessage>(JsonDefaults.Options, cancellationToken);
            if (!string.IsNullOrWhiteSpace(error?.Message))
            {
                return error.Message;
            }
        }
        catch
        {
        }

        return $"认证失败：HTTP {(int)response.StatusCode}。";
    }
}
