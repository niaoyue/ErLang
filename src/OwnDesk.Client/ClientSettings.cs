using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OwnDesk.Client;

internal sealed record ClientSettings
{
    private const string DefaultOrganizationName = "默认组织";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public List<ClientOrganization> Organizations { get; init; } = [];

    public string SelectedOrganizationId { get; init; } = string.Empty;

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
                return FromEnvironment(new ClientSettings()).Normalize();
            }

            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<ClientSettings>(json, JsonOptions) ?? new ClientSettings();
            settings = MigrateLegacySettings(json, settings);
            return FromEnvironment(settings).Normalize();
        }
        catch
        {
            return FromEnvironment(new ClientSettings()).Normalize();
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(Normalize(), JsonOptions);
        File.WriteAllText(SettingsPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public ClientSettings Normalize()
    {
        var organizations = Organizations
            .Select((organization, index) => organization.Normalize(index))
            .ToList();
        if (organizations.Count == 0)
        {
            organizations.Add(ClientOrganization.Create(DefaultOrganizationName));
        }

        var selectedOrganizationId = organizations.Any(organization => organization.Id == SelectedOrganizationId)
            ? SelectedOrganizationId
            : organizations[0].Id;

        return this with
        {
            Organizations = organizations,
            SelectedOrganizationId = selectedOrganizationId,
            DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? Environment.MachineName : DeviceId.Trim(),
            DeviceName = string.IsNullOrWhiteSpace(DeviceName) ? Environment.MachineName : DeviceName.Trim()
        };
    }

    public ClientOrganization SelectedOrganization()
    {
        var settings = Normalize();
        return settings.Organizations.First(organization => organization.Id == settings.SelectedOrganizationId);
    }

    public ClientSettings WithSelectedOrganizationId(string organizationId)
    {
        return (this with { SelectedOrganizationId = organizationId }).Normalize();
    }

    public ClientSettings WithSelectedOrganization(ClientOrganization organization)
    {
        var normalized = Normalize();
        var organizations = normalized.Organizations
            .Select(existing => existing.Id == organization.Id ? organization : existing)
            .ToList();
        if (organizations.All(existing => existing.Id != organization.Id))
        {
            organizations.Add(organization);
        }

        return normalized with
        {
            Organizations = organizations,
            SelectedOrganizationId = organization.Id
        };
    }

    public ClientSettings AddOrganization(ClientOrganization organization)
    {
        var normalized = Normalize();
        var organizations = normalized.Organizations.Append(organization).ToList();
        return normalized with
        {
            Organizations = organizations,
            SelectedOrganizationId = organization.Id
        };
    }

    public ClientSettings RemoveOrganization(string organizationId)
    {
        var organizations = Normalize()
            .Organizations
            .Where(organization => organization.Id != organizationId)
            .ToList();
        if (organizations.Count == 0)
        {
            organizations.Add(ClientOrganization.Create(DefaultOrganizationName));
        }

        return this with
        {
            Organizations = organizations,
            SelectedOrganizationId = organizations[0].Id
        };
    }

    private static ClientSettings FromEnvironment(ClientSettings settings)
    {
        settings = settings.Normalize();
        var server = ReadEnvironment("OWNDESK_SERVER");
        var token = ReadEnvironment("OWNDESK_TOKEN");
        var account = ReadEnvironment("OWNDESK_ACCOUNT");
        var password = ReadEnvironment("OWNDESK_PASSWORD");
        var deviceId = ReadEnvironment("OWNDESK_DEVICE_ID");
        var deviceName = ReadEnvironment("OWNDESK_DEVICE_NAME");

        var hasOrganizationOverride =
            !string.IsNullOrWhiteSpace(server) ||
            !string.IsNullOrWhiteSpace(token) ||
            !string.IsNullOrWhiteSpace(account) ||
            !string.IsNullOrWhiteSpace(password);
        if (hasOrganizationOverride)
        {
            var selected = settings.SelectedOrganization();
            selected = selected with
            {
                Server = server ?? selected.Server,
                Token = token ?? selected.Token,
                Account = account ?? selected.Account,
                Password = password ?? selected.Password,
                SignedIn = !string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(password)
                    ? true
                    : selected.SignedIn
            };
            settings = settings.WithSelectedOrganization(selected);
        }

        return settings with
        {
            DeviceId = deviceId ?? settings.DeviceId,
            DeviceName = deviceName ?? settings.DeviceName
        };
    }

    private static string? ReadEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ClientSettings MigrateLegacySettings(string json, ClientSettings settings)
    {
        if (settings.Organizations.Count > 0)
        {
            return settings;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var server = ReadString(root, "server");
            var token = ReadString(root, "token");
            var account = ReadString(root, "account");
            var password = ReadString(root, "password");
            if (string.IsNullOrWhiteSpace(server) &&
                string.IsNullOrWhiteSpace(token) &&
                string.IsNullOrWhiteSpace(account) &&
                string.IsNullOrWhiteSpace(password))
            {
                return settings;
            }

            var organization = ClientOrganization.Create("默认组织") with
            {
                Server = server ?? string.Empty,
                Token = token ?? string.Empty,
                Account = account ?? string.Empty,
                Password = password ?? string.Empty,
                SignedIn = !string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(password)
            };
            return settings with
            {
                Organizations = [organization],
                SelectedOrganizationId = organization.Id
            };
        }
        catch (JsonException)
        {
            return settings;
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }
}

internal sealed record ClientOrganization
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = string.Empty;

    public string Server { get; init; } = string.Empty;

    public string Token { get; init; } = string.Empty;

    public string Account { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public bool SignedIn { get; init; }

    public string SessionToken { get; init; } = string.Empty;

    public DateTimeOffset? SessionExpiresAtUtc { get; init; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Server : Name;

    [JsonIgnore]
    public bool HasConnection => !string.IsNullOrWhiteSpace(Server) && !string.IsNullOrWhiteSpace(Token);

    [JsonIgnore]
    public bool HasSavedCredentials =>
        !string.IsNullOrWhiteSpace(Account) &&
        !string.IsNullOrWhiteSpace(Password);

    public static ClientOrganization Create(string name)
    {
        return new ClientOrganization
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name
        };
    }

    public ClientOrganization Normalize(int index)
    {
        var name = Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = index == 0 ? "默认组织" : $"组织 {index + 1}";
        }

        return this with
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim(),
            Name = name,
            Server = NormalizeServer(Server),
            Token = Token.Trim(),
            Account = Account.Trim(),
            Password = Password
        };
    }

    private static string NormalizeServer(string server)
    {
        return server.Trim().TrimEnd('/');
    }
}
