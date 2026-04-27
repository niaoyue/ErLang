using System.Text.Json;

namespace OwnDesk.Client;

internal sealed record ClientSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string Server { get; init; } = string.Empty;

    public string Account { get; init; } = string.Empty;

    public string Token { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string DeviceId { get; init; } = Environment.MachineName;

    public string DeviceName { get; init; } = Environment.MachineName;

    public bool StartAgentOnLaunch { get; init; } = true;

    public bool EnableWebRtc { get; init; } = true;

    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OwnDesk",
            "client-settings.json");

    public static ClientSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return FromEnvironment(new ClientSettings());
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<ClientSettings>(json, JsonOptions) ?? new ClientSettings();
            return FromEnvironment(settings);
        }
        catch
        {
            return FromEnvironment(new ClientSettings());
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public ClientSettings Normalize()
    {
        return this with
        {
            Server = NormalizeServer(Server),
            Account = Account.Trim(),
            Token = Token.Trim(),
            Password = Password,
            DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? Environment.MachineName : DeviceId.Trim(),
            DeviceName = string.IsNullOrWhiteSpace(DeviceName) ? Environment.MachineName : DeviceName.Trim()
        };
    }

    private static ClientSettings FromEnvironment(ClientSettings settings)
    {
        return settings with
        {
            Server = ReadEnvironment("OWNDESK_SERVER", settings.Server),
            Account = ReadEnvironment("OWNDESK_ACCOUNT", settings.Account),
            Token = ReadEnvironment("OWNDESK_TOKEN", settings.Token),
            Password = ReadEnvironment("OWNDESK_PASSWORD", settings.Password),
            DeviceId = ReadEnvironment("OWNDESK_DEVICE_ID", settings.DeviceId),
            DeviceName = ReadEnvironment("OWNDESK_DEVICE_NAME", settings.DeviceName)
        };
    }

    private static string NormalizeServer(string server)
    {
        server = server.Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(server) ? "https://YOUR_OWNDESK_DOMAIN" : server;
    }

    private static string ReadEnvironment(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
