using System.Text.Json;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;

namespace OwnDesk.Server;

internal sealed class JsonDeviceRecordStore : IDeviceRecordStore
{
    private static readonly JsonSerializerOptions StoreJsonOptions = new(JsonDefaults.Options)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _storePath;
    private DeviceStore _store;

    public JsonDeviceRecordStore(string storePath)
    {
        if (string.IsNullOrWhiteSpace(storePath))
        {
            throw new ArgumentException("Device store path cannot be empty.", nameof(storePath));
        }

        _storePath = storePath;
        _store = LoadStore(storePath);
    }

    public IReadOnlyList<DeviceInfoDto> Load(string organizationId)
    {
        lock (_gate)
        {
            if (!_store.Organizations.TryGetValue(organizationId, out var devices))
            {
                return [];
            }

            return devices.Values
                .Select(device => device with { Online = false })
                .ToArray();
        }
    }

    public void Upsert(string organizationId, DeviceInfoDto device)
    {
        lock (_gate)
        {
            if (!_store.Organizations.TryGetValue(organizationId, out var devices))
            {
                devices = new Dictionary<string, DeviceInfoDto>(StringComparer.OrdinalIgnoreCase);
                _store.Organizations[organizationId] = devices;
            }

            devices[device.DeviceId] = device with { Online = false };
            SaveStore();
        }
    }

    public void Remove(string organizationId, string deviceId)
    {
        lock (_gate)
        {
            if (!_store.Organizations.TryGetValue(organizationId, out var devices))
            {
                return;
            }

            if (devices.Remove(deviceId))
            {
                SaveStore();
            }
        }
    }

    private static DeviceStore LoadStore(string storePath)
    {
        try
        {
            if (!File.Exists(storePath))
            {
                return new DeviceStore();
            }

            var json = File.ReadAllText(storePath);
            return JsonSerializer.Deserialize<DeviceStore>(json, StoreJsonOptions) ?? new DeviceStore();
        }
        catch
        {
            return new DeviceStore();
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

    private sealed class DeviceStore
    {
        public Dictionary<string, Dictionary<string, DeviceInfoDto>> Organizations { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
