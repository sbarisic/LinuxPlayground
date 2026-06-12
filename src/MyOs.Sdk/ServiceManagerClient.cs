using System.Text.Json.Nodes;
using MyOs.Ipc;

namespace MyOs.Sdk;

public sealed class ServiceManagerClient
{
    private readonly string _socketPath;

    public ServiceManagerClient(string socketPath = IpcPaths.ServiceManagerSocket)
    {
        _socketPath = socketPath;
    }

    public async Task<IReadOnlyList<ServiceInfo>> ListServicesAsync(CancellationToken cancellationToken = default)
    {
        IpcResponse response = await IpcClient.SendAsync(_socketPath, "service.list", new JsonObject(), cancellationToken);
        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Error ?? "service.list failed");
        }

        List<ServiceInfo> services = new();
        if (response.Data?["services"] is not JsonArray serviceArray)
        {
            return services;
        }

        foreach (JsonNode? item in serviceArray)
        {
            if (item is not JsonObject service)
            {
                continue;
            }

            services.Add(new ServiceInfo(
                ReadString(service, "name"),
                ReadString(service, "exec"),
                ReadString(service, "state"),
                ReadNullableInt(service, "pid"),
                ReadString(service, "restart"),
                ReadBool(service, "critical"),
                ReadNullableString(service, "lastExit")));
        }

        return services;
    }

    public Task<ServiceOperationResult> StartServiceAsync(string name, CancellationToken cancellationToken = default) =>
        SendServiceOperationAsync("service.start", name, cancellationToken);

    public Task<ServiceOperationResult> StopServiceAsync(string name, CancellationToken cancellationToken = default) =>
        SendServiceOperationAsync("service.stop", name, cancellationToken);

    public Task<ServiceOperationResult> RestartServiceAsync(string name, CancellationToken cancellationToken = default) =>
        SendServiceOperationAsync("service.restart", name, cancellationToken);

    private async Task<ServiceOperationResult> SendServiceOperationAsync(
        string command,
        string name,
        CancellationToken cancellationToken)
    {
        JsonObject args = new()
        {
            ["name"] = name,
        };

        IpcResponse response = await IpcClient.SendAsync(_socketPath, command, args, cancellationToken);
        string message = ReadNullableString(response.Data as JsonObject, "message")
            ?? response.Error
            ?? (response.Ok ? "ok" : "operation failed");

        return new ServiceOperationResult(response.Ok, message);
    }

    private static string ReadString(JsonObject obj, string name) =>
        ReadNullableString(obj, name) ?? string.Empty;

    private static string? ReadNullableString(JsonObject? obj, string name) =>
        obj is not null && obj.TryGetPropertyValue(name, out JsonNode? node)
            ? node?.GetValue<string>()
            : null;

    private static int? ReadNullableInt(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out JsonNode? node) && node is not null
            ? node.GetValue<int>()
            : null;

    private static bool ReadBool(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out JsonNode? node) && node is not null && node.GetValue<bool>();
}
