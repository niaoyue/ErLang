using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using OwnDesk.Shared;
using OwnDesk.Shared.Messages;

namespace OwnDesk.Server;

internal sealed class DeviceRegistry
{
    private readonly ConcurrentDictionary<DeviceKey, DeviceSession> _onlineDevices = new();
    private readonly ConcurrentDictionary<DeviceKey, DeviceInfoDto> _deviceRecords = new();
    private readonly ConcurrentDictionary<string, bool> _loadedOrganizations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, DeviceWatcherSession> _watchers = new();
    private readonly IDeviceRecordStore _store;

    public DeviceRegistry(IDeviceRecordStore store)
    {
        _store = store;
    }

    public async Task<DeviceSession> RegisterAgentAsync(
        string organizationId,
        string deviceId,
        string deviceName,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        EnsureOrganizationLoaded(organizationId);
        var now = DateTimeOffset.UtcNow;
        var key = new DeviceKey(organizationId, deviceId);
        var snapshot = _deviceRecords.AddOrUpdate(
            key,
            _ => new DeviceInfoDto
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                ConnectedAtUtc = now,
                LastSeenUtc = now,
                ScreenWidth = 0,
                ScreenHeight = 0,
                Online = true
            },
            (_, existing) => existing with
            {
                DeviceName = deviceName,
                ConnectedAtUtc = now,
                LastSeenUtc = now,
                Online = true
            });
        var session = new DeviceSession(
            organizationId,
            deviceId,
            new SafeWebSocket(socket),
            snapshot);
        _store.Upsert(organizationId, snapshot);

        _onlineDevices.AddOrUpdate(
            session.Key,
            session,
            (_, existing) =>
            {
                existing.Abort();
                return session;
            });

        await NotifyDeviceListChangedAsync(organizationId, cancellationToken);
        return session;
    }

    public IReadOnlyList<DeviceInfoDto> ListDevices(string organizationId)
    {
        EnsureOrganizationLoaded(organizationId);
        return _deviceRecords
            .Where(pair => pair.Key.OrganizationId == organizationId)
            .Select(pair => pair.Value)
            .OrderByDescending(device => device.Online)
            .ThenBy(device => device.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool TryGetDevice(string organizationId, string deviceId, out DeviceInfoDto? device)
    {
        if (_onlineDevices.TryGetValue(new DeviceKey(organizationId, deviceId), out var session))
        {
            device = session.Snapshot;
            return true;
        }

        device = null;
        return false;
    }

    public async Task UpdateHelloAsync(
        DeviceSession session,
        string deviceName,
        int screenWidth,
        int screenHeight,
        CancellationToken cancellationToken)
    {
        if (!IsCurrent(session))
        {
            return;
        }

        session.Update(device => device with
        {
            DeviceName = deviceName,
            ScreenWidth = screenWidth,
            ScreenHeight = screenHeight,
            LastSeenUtc = DateTimeOffset.UtcNow
        });
        _deviceRecords[session.Key] = session.Snapshot;
        _store.Upsert(session.OrganizationId, session.Snapshot);
        await NotifyDeviceListChangedAsync(session.OrganizationId, cancellationToken);
    }

    public void UpdateFrame(DeviceSession session, int screenWidth, int screenHeight)
    {
        if (!IsCurrent(session))
        {
            return;
        }

        session.Update(device => device with
        {
            ScreenWidth = screenWidth,
            ScreenHeight = screenHeight,
            LastSeenUtc = DateTimeOffset.UtcNow
        });
        _deviceRecords[session.Key] = session.Snapshot;
    }

    public async Task UnregisterAgentAsync(DeviceSession session, CancellationToken cancellationToken)
    {
        if (_onlineDevices.TryGetValue(session.Key, out var current) && ReferenceEquals(current, session))
        {
            _onlineDevices.TryRemove(session.Key, out _);
            session.Update(device => device with
            {
                Online = false,
                LastSeenUtc = DateTimeOffset.UtcNow
            });
            _deviceRecords[session.Key] = session.Snapshot;
            _store.Upsert(session.OrganizationId, session.Snapshot);
        }

        session.Abort();
        await NotifyDeviceListChangedAsync(session.OrganizationId, cancellationToken);
    }

    public async Task<bool> RemoveDeviceAsync(string organizationId, string deviceId, CancellationToken cancellationToken)
    {
        EnsureOrganizationLoaded(organizationId);
        var key = new DeviceKey(organizationId, deviceId);
        var removed = _deviceRecords.TryRemove(key, out _);
        if (_onlineDevices.TryRemove(key, out var session))
        {
            session.Abort();
            removed = true;
        }

        if (removed)
        {
            _store.Remove(organizationId, deviceId);
            await NotifyDeviceListChangedAsync(organizationId, cancellationToken);
        }

        return removed;
    }

    public ViewerSession? AddViewer(string organizationId, string deviceId, WebSocket socket)
    {
        if (!_onlineDevices.TryGetValue(new DeviceKey(organizationId, deviceId), out var session))
        {
            return null;
        }

        var viewer = new ViewerSession(Guid.NewGuid(), new SafeWebSocket(socket), session);
        session.Viewers[viewer.Id] = viewer;
        return viewer;
    }

    public void RemoveViewer(ViewerSession viewer)
    {
        viewer.Device.Viewers.TryRemove(viewer.Id, out _);
    }

    public async Task SetViewerRelayVideoAsync(ViewerSession viewer, bool enabled, CancellationToken cancellationToken)
    {
        viewer.RelayVideoEnabled = enabled;
        await SendRelayVideoDemandAsync(viewer.Device, cancellationToken);
    }

    public async Task SendRelayVideoDemandAsync(DeviceSession session, CancellationToken cancellationToken)
    {
        if (!IsCurrent(session) || !session.Agent.IsOpen)
        {
            return;
        }

        var enabled = session.Viewers.Values.Any(viewer => viewer.Connection.IsOpen && viewer.RelayVideoEnabled);
        var message = JsonSerializer.Serialize(
            new
            {
                type = OwnDeskMessageTypes.RelayVideo,
                enabled
            },
            JsonDefaults.Options);
        try
        {
            await session.Agent.SendTextAsync(message, cancellationToken);
        }
        catch (WebSocketException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public async Task BroadcastToViewersAsync(DeviceSession session, string text, CancellationToken cancellationToken)
    {
        foreach (var viewer in session.Viewers.Values.ToArray())
        {
            if (!viewer.Connection.IsOpen)
            {
                RemoveViewer(viewer);
                continue;
            }

            if (!viewer.RelayVideoEnabled)
            {
                continue;
            }

            try
            {
                await viewer.Connection.SendTextAsync(text, cancellationToken);
            }
            catch (WebSocketException)
            {
                RemoveViewer(viewer);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }
    }

    public async Task BroadcastBinaryToViewersAsync(DeviceSession session, byte[] payload, CancellationToken cancellationToken)
    {
        foreach (var viewer in session.Viewers.Values.ToArray())
        {
            if (!viewer.Connection.IsOpen)
            {
                RemoveViewer(viewer);
                continue;
            }

            if (!viewer.RelayVideoEnabled)
            {
                continue;
            }

            try
            {
                await viewer.Connection.SendBinaryAsync(payload, cancellationToken);
            }
            catch (WebSocketException)
            {
                RemoveViewer(viewer);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }
    }

    public async Task<bool> SendToAgentAsync(string organizationId, string deviceId, string text, CancellationToken cancellationToken)
    {
        if (!_onlineDevices.TryGetValue(new DeviceKey(organizationId, deviceId), out var session) || !session.Agent.IsOpen)
        {
            return false;
        }

        await session.Agent.SendTextAsync(text, cancellationToken);
        return true;
    }

    public DeviceWatcherSession AddWatcher(string organizationId, WebSocket socket)
    {
        var watcher = new DeviceWatcherSession(Guid.NewGuid(), organizationId, new SafeWebSocket(socket));
        _watchers[watcher.Id] = watcher;
        return watcher;
    }

    public void RemoveWatcher(DeviceWatcherSession watcher)
    {
        _watchers.TryRemove(watcher.Id, out _);
    }

    public async Task NotifyDeviceListChangedAsync(string organizationId, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Serialize(
            new
            {
                type = OwnDeskMessageTypes.DeviceListChanged
            },
            JsonDefaults.Options);
        foreach (var watcher in _watchers.Values.Where(watcher => watcher.OrganizationId == organizationId).ToArray())
        {
            if (!watcher.Connection.IsOpen)
            {
                RemoveWatcher(watcher);
                continue;
            }

            try
            {
                using var sendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await watcher.Connection.SendTextAsync(message, sendTimeout.Token);
            }
            catch (WebSocketException)
            {
                RemoveWatcher(watcher);
            }
            catch (OperationCanceledException)
            {
                RemoveWatcher(watcher);
            }
        }
    }

    private bool IsCurrent(DeviceSession session)
    {
        return _onlineDevices.TryGetValue(session.Key, out var current) && ReferenceEquals(current, session);
    }

    private void EnsureOrganizationLoaded(string organizationId)
    {
        if (!_loadedOrganizations.TryAdd(organizationId, true))
        {
            return;
        }

        foreach (var device in _store.Load(organizationId))
        {
            _deviceRecords[new DeviceKey(organizationId, device.DeviceId)] = device with { Online = false };
        }
    }
}

internal readonly record struct DeviceKey(string OrganizationId, string DeviceId);

internal sealed class DeviceSession
{
    private readonly object _snapshotLock = new();
    private DeviceInfoDto _snapshot;

    public DeviceSession(string organizationId, string deviceId, SafeWebSocket agent, DeviceInfoDto snapshot)
    {
        OrganizationId = organizationId;
        DeviceId = deviceId;
        Agent = agent;
        _snapshot = snapshot;
    }

    public string OrganizationId { get; }

    public string DeviceId { get; }

    public DeviceKey Key => new(OrganizationId, DeviceId);

    public SafeWebSocket Agent { get; }

    public ConcurrentDictionary<Guid, ViewerSession> Viewers { get; } = new();

    public DeviceInfoDto Snapshot
    {
        get
        {
            lock (_snapshotLock)
            {
                return _snapshot;
            }
        }
    }

    public void Update(Func<DeviceInfoDto, DeviceInfoDto> update)
    {
        lock (_snapshotLock)
        {
            _snapshot = update(_snapshot);
        }
    }

    public void Abort()
    {
        Agent.Abort();

        foreach (var viewer in Viewers.Values)
        {
            viewer.Connection.Abort();
        }
    }
}

internal sealed class ViewerSession(Guid id, SafeWebSocket connection, DeviceSession device)
{
    public Guid Id { get; } = id;

    public SafeWebSocket Connection { get; } = connection;

    public DeviceSession Device { get; } = device;

    public bool RelayVideoEnabled { get; set; } = true;
}

internal sealed record DeviceWatcherSession(Guid Id, string OrganizationId, SafeWebSocket Connection);
