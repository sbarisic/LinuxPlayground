using System.Text.Json.Nodes;

namespace MyOs.Ipc;

public sealed class IpcResponse
{
    private IpcResponse(int id, bool ok, JsonNode? data, string? error)
    {
        Id = id;
        Ok = ok;
        Data = data;
        Error = error;
    }

    public int Id { get; }

    public bool Ok { get; }

    public JsonNode? Data { get; }

    public string? Error { get; }

    public static IpcResponse Success(int id, JsonNode? data = null) => new(id, true, data, null);

    public static IpcResponse Failure(int id, string error) => new(id, false, null, error);
}
