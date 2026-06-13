using System.Text.Json.Nodes;
using MyOs.Ipc;

namespace MyOs.Sdk;

public sealed class AppManagerClient
{
    private readonly string _socketPath;

    public AppManagerClient(string socketPath = IpcPaths.ServiceManagerSocket)
    {
        _socketPath = socketPath;
    }

    public async Task<IReadOnlyList<AppInfo>> ListAppsAsync(CancellationToken cancellationToken = default)
    {
        IpcResponse response = await IpcClient.SendAsync(_socketPath, "app.list", new JsonObject(), cancellationToken);
        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Error ?? "app.list failed");
        }

        List<AppInfo> apps = new();
        if (response.Data?["apps"] is not JsonArray appArray)
        {
            return apps;
        }

        foreach (JsonNode? item in appArray)
        {
            if (item is not JsonObject app)
            {
                continue;
            }

            apps.Add(new AppInfo(
                ReadString(app, "name"),
                ReadString(app, "id"),
                ReadString(app, "version"),
                ReadString(app, "kind"),
                ReadString(app, "bundle"),
                ReadString(app, "entry")));
        }

        return apps;
    }

    public async Task<AppRunResult> RunAppAsync(string name, CancellationToken cancellationToken = default)
    {
        JsonObject args = new()
        {
            ["name"] = name,
        };

        IpcResponse response = await IpcClient.SendAsync(_socketPath, "app.run", args, cancellationToken);
        JsonObject? data = response.Data as JsonObject;
        string resolvedName = ReadNullableString(data, "name") ?? name;
        int? pid = ReadNullableInt(data, "pid");
        string message = ReadNullableString(data, "message")
            ?? response.Error
            ?? (response.Ok ? $"{resolvedName} started" : "app launch failed");

        return new AppRunResult(response.Ok, resolvedName, pid, message);
    }

    private static string ReadString(JsonObject obj, string name) =>
        ReadNullableString(obj, name) ?? string.Empty;

    private static string? ReadNullableString(JsonObject? obj, string name) =>
        obj is not null && obj.TryGetPropertyValue(name, out JsonNode? node)
            ? node?.GetValue<string>()
            : null;

    private static int? ReadNullableInt(JsonObject? obj, string name) =>
        obj is not null && obj.TryGetPropertyValue(name, out JsonNode? node) && node is not null
            ? node.GetValue<int>()
            : null;
}
