using System.Text.Json.Nodes;
using MyOs.Ipc;

namespace MyOs.Sdk;

public sealed class DeviceManagerClient
{
    private readonly string _socketPath;

    public DeviceManagerClient(string socketPath = IpcPaths.DeviceManagerSocket)
    {
        _socketPath = socketPath;
    }

    public async Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        IpcResponse response = await IpcClient.SendAsync(_socketPath, "device.list", new JsonObject(), cancellationToken);
        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Error ?? "device.list failed");
        }

        List<DeviceInfo> devices = new();
        if (response.Data?["devices"] is not JsonArray deviceArray)
        {
            return devices;
        }

        foreach (JsonNode? item in deviceArray)
        {
            if (item is not JsonObject device)
            {
                continue;
            }

            devices.Add(new DeviceInfo(
                ReadString(device, "name"),
                ReadString(device, "class"),
                ReadString(device, "sysPath"),
                ReadNullableString(device, "dev")));
        }

        return devices;
    }

    private static string ReadString(JsonObject obj, string name) =>
        ReadNullableString(obj, name) ?? string.Empty;

    private static string? ReadNullableString(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out JsonNode? node)
            ? node?.GetValue<string>()
            : null;
}
