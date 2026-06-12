using System.Text.Json;
using System.Text.Json.Nodes;

namespace MyOs.Ipc;

internal static class IpcProtocol
{
    public const int Version = 1;

    public static string WriteRequest(int id, string command, JsonObject? args)
    {
        JsonObject root = new()
        {
            ["version"] = Version,
            ["id"] = id,
            ["command"] = command,
            ["args"] = args ?? new JsonObject(),
        };

        return root.ToJsonString();
    }

    public static string WriteResponse(IpcResponse response)
    {
        JsonObject root = new()
        {
            ["version"] = Version,
            ["id"] = response.Id,
            ["ok"] = response.Ok,
        };

        if (response.Ok)
        {
            root["data"] = response.Data ?? new JsonObject();
        }
        else
        {
            root["error"] = response.Error ?? "unknown error";
        }

        return root.ToJsonString();
    }

    public static IpcRequest ReadRequest(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;

        int version = root.GetProperty("version").GetInt32();
        int id = root.GetProperty("id").GetInt32();
        string? command = root.GetProperty("command").GetString();
        JsonObject args = new();

        if (root.TryGetProperty("args", out JsonElement argsElement) &&
            JsonNode.Parse(argsElement.GetRawText()) is JsonObject parsedArgs)
        {
            args = parsedArgs;
        }

        if (version != Version)
        {
            throw new InvalidOperationException($"unsupported IPC version {version}");
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            throw new InvalidOperationException("missing IPC command");
        }

        return new IpcRequest(version, id, command, args);
    }

    public static IpcResponse ReadResponse(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;

        int version = root.GetProperty("version").GetInt32();
        int id = root.GetProperty("id").GetInt32();
        bool ok = root.GetProperty("ok").GetBoolean();

        if (version != Version)
        {
            throw new InvalidOperationException($"unsupported IPC version {version}");
        }

        if (!ok)
        {
            string error = root.TryGetProperty("error", out JsonElement errorElement)
                ? errorElement.GetString() ?? "unknown error"
                : "unknown error";
            return IpcResponse.Failure(id, error);
        }

        JsonNode? data = root.TryGetProperty("data", out JsonElement dataElement)
            ? JsonNode.Parse(dataElement.GetRawText())
            : new JsonObject();

        return IpcResponse.Success(id, data);
    }
}
