using OwnDesk.Shared.Messages;

namespace OwnDesk.Server;

internal interface IDeviceRecordStore
{
    IReadOnlyList<DeviceInfoDto> Load(string organizationId);

    void Upsert(string organizationId, DeviceInfoDto device);

    void Remove(string organizationId, string deviceId);
}
