using System.Collections.Concurrent;
using System.Net.WebSockets;
using OwnDesk.Shared.Messages;

namespace OwnDesk.Server;

internal sealed class DeviceRegistry
{
    private readonly ConcurrentDictionary<DeviceKey, DeviceSession> _devices = new();

    public DeviceSession RegisterAgent(string account, string deviceId, string deviceName, WebSocket socket)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new DeviceSession(
            account,
            deviceId,
            new SafeWebSocket(socket),
            new DeviceInfoDto
            {
                Account = account,
                DeviceId = deviceId,
                DeviceName = deviceName,
                ConnectedAtUtc = now,
                LastSeenUtc = now,
                ScreenWidth = 0,
                ScreenHeight = 0,
                Online = true
            });

        _devices.AddOrUpdate(
            session.Key,
            session,
            (_, existing) =>
            {
                existing.Abort();
                return session;
            });

        return session;
    }

    public IReadOnlyList<DeviceInfoDto> ListDevices(string account)
    {
        return _devices
            .Where(pair => pair.Key.Account == account)
            .Select(pair => pair.Value.Snapshot)
            .OrderBy(device => device.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool TryGetDevice(string account, string deviceId, out DeviceInfoDto? device)
    {
        if (_devices.TryGetValue(new DeviceKey(account, deviceId), out var session))
        {
            device = session.Snapshot;
            return true;
        }

        device = null;
        return false;
    }

    public void UpdateHello(DeviceSession session, string deviceName, int screenWidth, int screenHeight)
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
    }

    public void UnregisterAgent(DeviceSession session)
    {
        if (_devices.TryGetValue(session.Key, out var current) && ReferenceEquals(current, session))
        {
            _devices.TryRemove(session.Key, out _);
        }

        session.Abort();
    }

    public ViewerSession? AddViewer(string account, string deviceId, WebSocket socket)
    {
        if (!_devices.TryGetValue(new DeviceKey(account, deviceId), out var session))
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

    public async Task BroadcastToViewersAsync(DeviceSession session, string text, CancellationToken cancellationToken)
    {
        foreach (var viewer in session.Viewers.Values.ToArray())
        {
            if (!viewer.Connection.IsOpen)
            {
                RemoveViewer(viewer);
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

    public async Task<bool> SendToAgentAsync(string account, string deviceId, string text, CancellationToken cancellationToken)
    {
        if (!_devices.TryGetValue(new DeviceKey(account, deviceId), out var session) || !session.Agent.IsOpen)
        {
            return false;
        }

        await session.Agent.SendTextAsync(text, cancellationToken);
        return true;
    }

    private bool IsCurrent(DeviceSession session)
    {
        return _devices.TryGetValue(session.Key, out var current) && ReferenceEquals(current, session);
    }
}

internal readonly record struct DeviceKey(string Account, string DeviceId);

internal sealed class DeviceSession
{
    private readonly object _snapshotLock = new();
    private DeviceInfoDto _snapshot;

    public DeviceSession(string account, string deviceId, SafeWebSocket agent, DeviceInfoDto snapshot)
    {
        Account = account;
        DeviceId = deviceId;
        Agent = agent;
        _snapshot = snapshot;
    }

    public string Account { get; }

    public string DeviceId { get; }

    public DeviceKey Key => new(Account, DeviceId);

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

internal sealed record ViewerSession(Guid Id, SafeWebSocket Connection, DeviceSession Device);
